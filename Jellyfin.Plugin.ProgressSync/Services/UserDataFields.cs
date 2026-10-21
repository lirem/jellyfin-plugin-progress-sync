using MediaBrowser.Controller.Entities;

namespace Jellyfin.Plugin.ProgressSync.Services;

/// <summary>
/// The single place that defines which user data fields count as watch progress.
/// </summary>
internal static class UserDataFields
{
    /// <summary>
    /// Compares the progress fields of two user data records.
    /// </summary>
    /// <param name="a">First record.</param>
    /// <param name="b">Second record.</param>
    /// <returns><c>true</c> if position, played state, play count and last played date are equal.</returns>
    public static bool ProgressEquals(UserItemData a, UserItemData b)
    {
        return a.PlaybackPositionTicks == b.PlaybackPositionTicks
            && a.Played == b.Played
            && a.PlayCount == b.PlayCount
            && a.LastPlayedDate == b.LastPlayedDate;
    }

    /// <summary>
    /// Creates a new record with the target's non-progress fields and the source's progress fields.
    /// Neither argument is modified (they may be instances owned by the server's user data cache).
    /// </summary>
    /// <param name="target">The record to keep favorite, rating and stream choices from.</param>
    /// <param name="source">The record to take progress from.</param>
    /// <returns>A new record.</returns>
    public static UserItemData CloneWithProgressFrom(UserItemData target, UserItemData source)
    {
        // Likes is deliberately not assigned: it is derived from Rating and its setter overwrites Rating.
        return new UserItemData
        {
            Key = target.Key,
            Rating = target.Rating,
            IsFavorite = target.IsFavorite,
            AudioStreamIndex = target.AudioStreamIndex,
            SubtitleStreamIndex = target.SubtitleStreamIndex,
            PlaybackPositionTicks = source.PlaybackPositionTicks,
            Played = source.Played,
            PlayCount = source.PlayCount,
            LastPlayedDate = source.LastPlayedDate
        };
    }
}
