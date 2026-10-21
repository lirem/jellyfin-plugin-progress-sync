using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.ProgressSync.Models;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.ProgressSync.Services;

/// <summary>
/// Copies watch progress between users.
/// </summary>
public sealed class ProgressCopier
{
    private readonly ILibraryManager _libraryManager;
    private readonly IUserDataManager _userDataManager;
    private readonly ILogger<ProgressCopier> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProgressCopier"/> class.
    /// </summary>
    /// <param name="libraryManager">Instance of the <see cref="ILibraryManager"/> interface.</param>
    /// <param name="userDataManager">Instance of the <see cref="IUserDataManager"/> interface.</param>
    /// <param name="logger">Instance of the <see cref="ILogger{ProgressCopier}"/> interface.</param>
    public ProgressCopier(ILibraryManager libraryManager, IUserDataManager userDataManager, ILogger<ProgressCopier> logger)
    {
        _libraryManager = libraryManager;
        _userDataManager = userDataManager;
        _logger = logger;
    }

    /// <summary>
    /// Copies progress from <paramref name="source"/> to <paramref name="target"/> for every playable
    /// item under <paramref name="opened"/> that both users can access, and logs the operation.
    /// </summary>
    /// <param name="source">The user to copy from.</param>
    /// <param name="target">The user to copy to.</param>
    /// <param name="opened">A movie, episode, season or series.</param>
    /// <param name="reason">The save reason reported to the server.</param>
    /// <returns>The copy result.</returns>
    public CopyResult CopyScope(User source, User target, BaseItem opened, UserDataSaveReason reason)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(opened);

        var scope = FilterVisible(EnumerateLeaves(opened, target), target, source);
        var copied = CopyItems(source, target, scope.Items, reason);
        var result = new CopyResult(copied.Updated, scope.Skipped, scope.Items.Count + scope.Skipped);

        _logger.LogInformation(
            "Progress Sync: {TargetUser} copied progress from {SourceUser} for {ItemName} ({ItemId}): {Updated} updated, {Skipped} skipped, {Total} total",
            target.Username,
            source.Username,
            opened.Name,
            opened.Id,
            result.Updated,
            result.Skipped,
            result.Total);

        return result;
    }

    /// <summary>
    /// Lists the playable items (all versions) under an opened item, enumerated as <paramref name="target"/>.
    /// </summary>
    /// <param name="opened">A movie, episode, season or series.</param>
    /// <param name="target">The user whose library view is used.</param>
    /// <returns>Distinct, non-virtual video items.</returns>
    /// <exception cref="ArgumentException">The item type is not supported.</exception>
    internal static IReadOnlyList<BaseItem> EnumerateLeaves(BaseItem opened, User target)
    {
        IEnumerable<Video> leaves = opened switch
        {
            Movie movie => movie.GetAllVersions(),
            Episode episode => episode.GetAllVersions(),
            Season season => season.GetEpisodes(target, new DtoOptions(true), false)
                .OfType<Episode>()
                .SelectMany(e => e.GetAllVersions()),
            Series series => series.GetEpisodes(target, new DtoOptions(true), false)
                .OfType<Episode>()
                .SelectMany(e => e.GetAllVersions()),
            _ => throw new ArgumentException("Unsupported item type.", nameof(opened))
        };

        return leaves
            .Where(v => !v.IsVirtualItem)
            .DistinctBy(v => v.Id)
            .ToList<BaseItem>();
    }

    /// <summary>
    /// Keeps only items both users can access (library access and parental rules). Items the source
    /// cannot access are skipped rather than overwritten with empty progress.
    /// </summary>
    /// <param name="leaves">Candidate items.</param>
    /// <param name="target">The user to copy to.</param>
    /// <param name="source">The user to copy from.</param>
    /// <returns>The server's cached item instances visible to both users, and the skipped count.</returns>
    internal ScopeResult FilterVisible(IReadOnlyList<BaseItem> leaves, User target, User source)
    {
        var items = new List<BaseItem>(leaves.Count);
        foreach (var leaf in leaves)
        {
            var targetItem = _libraryManager.GetItemById<BaseItem>(leaf.Id, target);
            if (targetItem is not null && _libraryManager.GetItemById<BaseItem>(leaf.Id, source) is not null)
            {
                items.Add(targetItem);
            }
        }

        return new ScopeResult(items, leaves.Count - items.Count);
    }

    /// <summary>
    /// Copies progress for already filtered items.
    /// </summary>
    /// <param name="source">The user to copy from.</param>
    /// <param name="target">The user to copy to.</param>
    /// <param name="items">Items visible to both users (the server's cached instances).</param>
    /// <param name="reason">The save reason reported to the server.</param>
    /// <returns>The number of updated items; <see cref="CopyResult.Skipped"/> is always 0.</returns>
    internal CopyResult CopyItems(User source, User target, IReadOnlyList<BaseItem> items, UserDataSaveReason reason)
    {
        if (items.Count == 0)
        {
            return new CopyResult(0, 0, 0);
        }

        var sourceData = _userDataManager.GetUserDataBatch(items, source);
        var targetData = _userDataManager.GetUserDataBatch(items, target);
        var updated = 0;
        foreach (var item in items)
        {
            var from = sourceData.GetValueOrDefault(item.Id) ?? EmptyData();
            var current = targetData.GetValueOrDefault(item.Id) ?? EmptyData();
            if (Write(target, item, from, current, reason, source.Id))
            {
                updated++;
            }
        }

        return new CopyResult(updated, 0, items.Count);
    }

    /// <summary>
    /// Copies already known progress to one user for one item. Used while propagating over links.
    /// </summary>
    /// <param name="target">The user to copy to.</param>
    /// <param name="targetItem">The item, as returned by <c>GetItemById</c> for the target.</param>
    /// <param name="sourceData">The progress to copy.</param>
    /// <param name="reason">The save reason reported to the server.</param>
    /// <returns><c>true</c> if the target's progress changed.</returns>
    internal bool CopyOne(User target, BaseItem targetItem, UserItemData sourceData, UserDataSaveReason reason)
    {
        var current = _userDataManager.GetUserDataBatch([targetItem], target).GetValueOrDefault(targetItem.Id) ?? EmptyData();
        return Write(target, targetItem, sourceData, current, reason, null);
    }

    private static UserItemData EmptyData() => new() { Key = string.Empty };

    private bool Write(User target, BaseItem item, UserItemData from, UserItemData current, UserDataSaveReason reason, Guid? sourceUserId)
    {
        if (UserDataFields.ProgressEquals(from, current))
        {
            return false;
        }

        var data = UserDataFields.CloneWithProgressFrom(current, from);

        // A top-level copy starts its own context so the resulting UserDataSaved event propagates over
        // the target's links but never back to the source or target. Propagation writes reuse the
        // active context. No await may happen while the context is active (it is thread-bound).
        using var context = PropagationContext.Current is null
            ? PropagationContext.Begin(sourceUserId is { } sourceId ? [target.Id, sourceId] : [target.Id])
            : null;
        _userDataManager.SaveUserData(target, item, data, reason, CancellationToken.None);
        return true;
    }
}
