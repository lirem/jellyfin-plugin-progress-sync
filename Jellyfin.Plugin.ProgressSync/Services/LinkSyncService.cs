using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Plugin.ProgressSync.Models;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.ProgressSync.Services;

/// <summary>
/// Propagates progress changes over links as they are saved.
/// </summary>
public sealed class LinkSyncService : IHostedService
{
    private readonly IUserDataManager _userDataManager;
    private readonly IUserManager _userManager;
    private readonly ILibraryManager _libraryManager;
    private readonly LinkStore _store;
    private readonly ProgressCopier _copier;
    private readonly ILogger<LinkSyncService> _logger;
    private volatile CollectionRootsCache? _collectionRoots;

    /// <summary>
    /// Initializes a new instance of the <see cref="LinkSyncService"/> class.
    /// </summary>
    /// <param name="userDataManager">Instance of the <see cref="IUserDataManager"/> interface.</param>
    /// <param name="userManager">Instance of the <see cref="IUserManager"/> interface.</param>
    /// <param name="libraryManager">Instance of the <see cref="ILibraryManager"/> interface.</param>
    /// <param name="store">The link store.</param>
    /// <param name="copier">The progress copier.</param>
    /// <param name="logger">Instance of the <see cref="ILogger{LinkSyncService}"/> interface.</param>
    public LinkSyncService(
        IUserDataManager userDataManager,
        IUserManager userManager,
        ILibraryManager libraryManager,
        LinkStore store,
        ProgressCopier copier,
        ILogger<LinkSyncService> logger)
    {
        _userDataManager = userDataManager;
        _userManager = userManager;
        _libraryManager = libraryManager;
        _store = store;
        _copier = copier;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _userDataManager.UserDataSaved += OnUserDataSaved;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _userDataManager.UserDataSaved -= OnUserDataSaved;
        return Task.CompletedTask;
    }

    private void OnUserDataSaved(object? sender, UserDataSaveEventArgs e)
    {
        try
        {
            // Ratings are not progress. NFO imports save partial data on temporary provider items
            // (only Played set) and would wipe partners' progress during a library scan.
            if (e.SaveReason is UserDataSaveReason.UpdateUserRating or UserDataSaveReason.Import)
            {
                return;
            }

            if (e.Item is not (Episode or Movie) || LinkRules.ResolveRoot(e.Item) is not { } rootId)
            {
                return;
            }

            var snapshot = _store.Snapshot;
            if (snapshot.All.Count == 0)
            {
                return;
            }

            var links = LinksFor(snapshot, rootId, e.Item);
            if (links.Count == 0)
            {
                return;
            }

            // An organic change (playback, mark played, ...) starts a context; our own writes re-enter
            // here on the same thread with the context active and fan out to not-yet-reached users.
            var active = PropagationContext.Current;
            using var owned = active is null ? PropagationContext.Begin(e.UserId) : null;
            var context = active ?? owned!;

            var targets = LinkRules.TargetsOf(links, e.UserId)
                .Where(t => !context.Reached.Contains(t))
                .Distinct()
                .ToList();
            context.Reached.UnionWith(targets);

            foreach (var targetId in targets)
            {
                Propagate(targetId, e);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Progress Sync: failed to propagate progress for item {ItemId}", e.Item?.Id);
        }
    }

    private List<SyncLink> LinksFor(LinkSnapshot snapshot, Guid rootId, BaseItem item)
    {
        var links = new List<SyncLink>(snapshot.ForRoot(rootId));
        try
        {
            // Membership is checked on every save, so items added to a linked collection sync from then on.
            foreach (var collectionId in CollectionRoots(snapshot))
            {
                if (_libraryManager.GetItemById(collectionId) is BoxSet boxSet && LinkRules.IsInCollection(boxSet, item))
                {
                    links.AddRange(snapshot.ForRoot(collectionId));
                }
            }
        }
        catch (Exception ex)
        {
            // A failing collection lookup must not stop the item's own links.
            _logger.LogError(ex, "Progress Sync: failed to check collections for item {ItemId}", item.Id);
        }

        return links;
    }

    private Guid[] CollectionRoots(LinkSnapshot snapshot)
    {
        // Worked out when the links change (once per snapshot) instead of looking up every root on each
        // playback progress save. A root that could not be found then is rechecked after the next change.
        var cached = _collectionRoots;
        if (ReferenceEquals(cached?.Snapshot, snapshot))
        {
            return cached.Roots;
        }

        var roots = snapshot.All
            .Select(l => l.RootItemId)
            .Distinct()
            .Where(id => _libraryManager.GetItemById(id) is BoxSet)
            .ToArray();
        _collectionRoots = new CollectionRootsCache(snapshot, roots);
        return roots;
    }

    private void Propagate(Guid targetUserId, UserDataSaveEventArgs e)
    {
        try
        {
            var user = _userManager.GetUserById(targetUserId);
            if (user is null || user.HasPermission(PermissionKind.IsDisabled))
            {
                return;
            }

            var item = _libraryManager.GetItemById<BaseItem>(e.Item.Id, user);
            if (item is null)
            {
                return;
            }

            _copier.CopyOne(user, item, e.UserData, e.SaveReason);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Progress Sync: failed to copy progress of item {ItemId} to user {UserId}", e.Item.Id, targetUserId);
        }
    }

    private sealed record CollectionRootsCache(LinkSnapshot Snapshot, Guid[] Roots);
}
