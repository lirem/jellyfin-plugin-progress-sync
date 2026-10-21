using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.ProgressSync.Models;

namespace Jellyfin.Plugin.ProgressSync.Services;

/// <summary>
/// Immutable view of all links, indexed for lock-free reads on the playback hot path.
/// </summary>
public sealed class LinkSnapshot
{
    private readonly FrozenDictionary<Guid, SyncLink[]> _byRoot;
    private readonly FrozenDictionary<Guid, SyncLink> _byId;

    /// <summary>
    /// Initializes a new instance of the <see cref="LinkSnapshot"/> class.
    /// </summary>
    /// <param name="links">The links; ids must be unique.</param>
    internal LinkSnapshot(IReadOnlyList<SyncLink> links)
    {
        All = links;
        _byRoot = links.GroupBy(l => l.RootItemId).ToFrozenDictionary(g => g.Key, g => g.ToArray());
        _byId = links.ToFrozenDictionary(l => l.Id);
    }

    /// <summary>
    /// Gets all links.
    /// </summary>
    public IReadOnlyList<SyncLink> All { get; }

    /// <summary>
    /// Gets the links attached to a root item.
    /// </summary>
    /// <param name="rootItemId">The series or movie id.</param>
    /// <returns>The links; empty if none.</returns>
    public IReadOnlyList<SyncLink> ForRoot(Guid rootItemId) => _byRoot.TryGetValue(rootItemId, out var links) ? links : [];

    /// <summary>
    /// Finds a link by id.
    /// </summary>
    /// <param name="linkId">The link id.</param>
    /// <returns>The link, or <c>null</c>.</returns>
    public SyncLink? Find(Guid linkId) => _byId.GetValueOrDefault(linkId);
}
