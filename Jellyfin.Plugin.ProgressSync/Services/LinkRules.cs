using System;
using System.Collections.Generic;
using Jellyfin.Plugin.ProgressSync.Models;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;

namespace Jellyfin.Plugin.ProgressSync.Services;

/// <summary>
/// Visibility, permission and data-flow rules for links.
/// </summary>
internal static class LinkRules
{
    /// <summary>
    /// Follow links are visible to their creator only; mirror links to both participants.
    /// </summary>
    /// <param name="link">The link.</param>
    /// <param name="userId">The user asking.</param>
    /// <returns><c>true</c> if the user may see the link.</returns>
    public static bool IsVisibleTo(SyncLink link, Guid userId)
    {
        return link.CreatorUserId.Equals(userId)
            || (link.Mode == LinkMode.Mirror && link.PartnerUserId.Equals(userId));
    }

    /// <summary>
    /// Whoever can see a link can remove it.
    /// </summary>
    /// <param name="link">The link.</param>
    /// <param name="userId">The user asking.</param>
    /// <returns><c>true</c> if the user may remove the link.</returns>
    public static bool CanRemove(SyncLink link, Guid userId) => IsVisibleTo(link, userId);

    /// <summary>
    /// Users that receive a change made by <paramref name="from"/>. Follow links copy partner to
    /// creator; mirror links copy both ways. May contain duplicates.
    /// </summary>
    /// <param name="links">Links on one root item.</param>
    /// <param name="from">The user whose progress changed.</param>
    /// <returns>Target user ids.</returns>
    public static IEnumerable<Guid> TargetsOf(IReadOnlyList<SyncLink> links, Guid from)
    {
        foreach (var link in links)
        {
            if (link.PartnerUserId.Equals(from))
            {
                yield return link.CreatorUserId;
            }
            else if (link.Mode == LinkMode.Mirror && link.CreatorUserId.Equals(from))
            {
                yield return link.PartnerUserId;
            }
        }
    }

    /// <summary>
    /// The item a link is attached to: the series for episodes, seasons and series; the primary
    /// version for movies.
    /// </summary>
    /// <param name="item">Any item.</param>
    /// <returns>The root id, or <c>null</c> if the item cannot be linked.</returns>
    public static Guid? ResolveRoot(BaseItem item)
    {
        return item switch
        {
            Episode episode => NonEmpty(episode.SeriesId),
            Season season => NonEmpty(season.SeriesId),
            Series series => series.Id,
            Movie movie => NonEmpty(movie.PrimaryVersionId) ?? movie.Id,
            _ => null
        };
    }

    private static Guid? NonEmpty(Guid? id) => id is { } value && !value.Equals(Guid.Empty) ? value : null;
}
