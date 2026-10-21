using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using Jellyfin.Plugin.ProgressSync.Models;
using MediaBrowser.Common.Configuration;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.ProgressSync.Services;

/// <summary>
/// Persists links in <c>plugins/configurations/Jellyfin.Plugin.ProgressSync/links.json</c>.
/// Reads are lock-free via <see cref="Snapshot"/>; writes are serialized and atomic on disk.
/// </summary>
public sealed class LinkStore
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly Lock _writeLock = new();
    private readonly ILogger<LinkStore> _logger;
    private readonly string _path;
    private volatile LinkSnapshot _snapshot;
    private bool _readOnly;

    /// <summary>
    /// Initializes a new instance of the <see cref="LinkStore"/> class.
    /// </summary>
    /// <param name="applicationPaths">Instance of the <see cref="IApplicationPaths"/> interface.</param>
    /// <param name="logger">Instance of the <see cref="ILogger{LinkStore}"/> interface.</param>
    public LinkStore(IApplicationPaths applicationPaths, ILogger<LinkStore> logger)
        : this(Path.Combine(applicationPaths.PluginConfigurationsPath, "Jellyfin.Plugin.ProgressSync", "links.json"), logger)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="LinkStore"/> class with an explicit file path.
    /// </summary>
    /// <param name="path">The links file.</param>
    /// <param name="logger">The logger.</param>
    internal LinkStore(string path, ILogger<LinkStore> logger)
    {
        _path = path;
        _logger = logger;
        _snapshot = new LinkSnapshot(Load());
    }

    /// <summary>
    /// Gets the current immutable set of links.
    /// </summary>
    public LinkSnapshot Snapshot => _snapshot;

    /// <summary>
    /// Creates a link, or changes the mode of the existing link with the same creator, partner and root.
    /// </summary>
    /// <param name="creatorUserId">The creating user.</param>
    /// <param name="partnerUserId">The partner user.</param>
    /// <param name="rootItemId">The series or movie id.</param>
    /// <param name="mode">How progress flows.</param>
    /// <param name="isStale">Links to drop while writing (for example links of deleted users).</param>
    /// <returns>The stored link.</returns>
    public SyncLink Upsert(Guid creatorUserId, Guid partnerUserId, Guid rootItemId, LinkMode mode, Func<SyncLink, bool> isStale)
    {
        lock (_writeLock)
        {
            var links = _snapshot.All.Where(l => !isStale(l)).ToList();
            var index = links.FindIndex(l => l.CreatorUserId.Equals(creatorUserId)
                && l.PartnerUserId.Equals(partnerUserId)
                && l.RootItemId.Equals(rootItemId));
            SyncLink link;
            if (index >= 0)
            {
                link = links[index] with { Mode = mode };
                links[index] = link;
            }
            else
            {
                link = new SyncLink(Guid.NewGuid(), creatorUserId, partnerUserId, rootItemId, mode, DateTime.UtcNow);
                links.Add(link);
            }

            Commit(links);
            return link;
        }
    }

    /// <summary>
    /// Removes a link.
    /// </summary>
    /// <param name="linkId">The link id.</param>
    /// <param name="isStale">Links to drop while writing (for example links of deleted users).</param>
    /// <returns><c>true</c> if the link existed.</returns>
    public bool Remove(Guid linkId, Func<SyncLink, bool> isStale)
    {
        lock (_writeLock)
        {
            var existed = _snapshot.Find(linkId) is not null;
            var links = _snapshot.All.Where(l => !isStale(l) && !l.Id.Equals(linkId)).ToList();
            if (links.Count != _snapshot.All.Count)
            {
                Commit(links);
            }

            return existed;
        }
    }

    private void Commit(List<SyncLink> links)
    {
        if (_readOnly)
        {
            throw new InvalidOperationException("Links are read-only because the links file could not be read at startup. Restart the server.");
        }

        Persist(links);
        _snapshot = new LinkSnapshot(links);
    }

    private void Persist(IReadOnlyList<SyncLink> links)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var tempPath = _path + ".tmp";
        using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(stream, new LinkFile { Version = 1, Links = links }, _jsonOptions);
            stream.Flush(true);
        }

        if (File.Exists(_path))
        {
            File.Replace(tempPath, _path, _path + ".bak");
        }
        else
        {
            File.Move(tempPath, _path);
        }
    }

    private List<SyncLink> Load()
    {
        if (File.Exists(_path))
        {
            try
            {
                return Read(_path);
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "Progress Sync: links file {Path} is corrupt, trying the backup", _path);
            }
            catch (Exception ex)
            {
                // The file exists but cannot be read (locked, permissions). Never overwrite it.
                _readOnly = true;
                _logger.LogCritical(ex, "Progress Sync: cannot read links file {Path}; links are inactive and changes are disabled until the server is restarted", _path);
                return [];
            }
        }

        var backupPath = _path + ".bak";
        if (File.Exists(backupPath))
        {
            try
            {
                var links = Read(backupPath);
                _logger.LogWarning("Progress Sync: links restored from backup {Path}", backupPath);
                return links;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Progress Sync: could not read links backup {Path}", backupPath);
            }
        }

        return [];
    }

    private static List<SyncLink> Read(string path)
    {
        using var stream = File.OpenRead(path);
        var file = JsonSerializer.Deserialize<LinkFile>(stream, _jsonOptions) ?? throw new JsonException("Empty links file.");
        return (file.Links ?? []).Where(l => l is not null).DistinctBy(l => l.Id).ToList();
    }

    private sealed class LinkFile
    {
        public int Version { get; init; }

        public IReadOnlyList<SyncLink>? Links { get; init; }
    }
}
