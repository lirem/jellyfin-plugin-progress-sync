using System;

namespace Jellyfin.Plugin.ProgressSync.Models;

/// <summary>
/// A link as seen by the calling user.
/// </summary>
/// <param name="Id">The link id.</param>
/// <param name="PartnerUserId">The other user, from the caller's perspective.</param>
/// <param name="PartnerName">The other user's name.</param>
/// <param name="Mode">How progress flows.</param>
/// <param name="IsCreator">Whether the caller created the link.</param>
/// <param name="CanRemove">Whether the caller may remove the link.</param>
/// <param name="CreatedUtc">When the link was created.</param>
public sealed record LinkDto(
    Guid Id,
    Guid PartnerUserId,
    string PartnerName,
    LinkMode Mode,
    bool IsCreator,
    bool CanRemove,
    DateTime CreatedUtc);
