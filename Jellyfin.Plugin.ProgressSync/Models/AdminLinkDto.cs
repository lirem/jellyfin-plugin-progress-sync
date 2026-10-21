using System;

namespace Jellyfin.Plugin.ProgressSync.Models;

/// <summary>
/// A link as shown on the admin page.
/// </summary>
/// <param name="Id">The link id.</param>
/// <param name="CreatorName">The creator's name.</param>
/// <param name="PartnerName">The partner's name.</param>
/// <param name="ItemName">The linked series or movie name.</param>
/// <param name="RootItemId">The linked series or movie id.</param>
/// <param name="Mode">How progress flows.</param>
/// <param name="CreatedUtc">When the link was created.</param>
public sealed record AdminLinkDto(
    Guid Id,
    string CreatorName,
    string PartnerName,
    string ItemName,
    Guid RootItemId,
    LinkMode Mode,
    DateTime CreatedUtc);
