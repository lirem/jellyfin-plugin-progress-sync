namespace Jellyfin.Plugin.ProgressSync.Models;

/// <summary>
/// How progress flows over a link.
/// </summary>
public enum LinkMode
{
    /// <summary>
    /// One-way: the partner's progress is copied to the creator.
    /// </summary>
    Follow,

    /// <summary>
    /// Two-way: progress of either user is copied to the other.
    /// </summary>
    Mirror
}
