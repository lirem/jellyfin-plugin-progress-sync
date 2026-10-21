namespace Jellyfin.Plugin.ProgressSync.Models;

/// <summary>
/// Response for link creation.
/// </summary>
/// <param name="Link">The created or updated link.</param>
/// <param name="InitialCopy">Result of the initial partner-to-caller copy, if it succeeded.</param>
/// <param name="InitialCopyError">Error message if the initial copy failed; the link is kept.</param>
public sealed record CreateLinkResponse(LinkDto Link, CopyResult? InitialCopy, string? InitialCopyError);
