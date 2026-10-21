using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data;
using Jellyfin.Database.Implementations.Enums;
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

            var links = _store.Snapshot.ForRoot(rootId);
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
}
