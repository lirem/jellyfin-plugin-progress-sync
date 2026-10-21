using System;

namespace Jellyfin.Plugin.ProgressSync.Models;

/// <summary>
/// A persistent link that keeps progress in sync between two users for one series or movie.
/// </summary>
/// <param name="Id">The link id.</param>
/// <param name="CreatorUserId">The user who created the link.</param>
/// <param name="PartnerUserId">The other user.</param>
/// <param name="RootItemId">The series or (primary) movie the link covers.</param>
/// <param name="Mode">How progress flows.</param>
/// <param name="CreatedUtc">When the link was created.</param>
public sealed record SyncLink(
    Guid Id,
    Guid CreatorUserId,
    Guid PartnerUserId,
    Guid RootItemId,
    LinkMode Mode,
    DateTime CreatedUtc);
