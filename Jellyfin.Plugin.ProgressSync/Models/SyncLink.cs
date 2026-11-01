using System;

namespace Jellyfin.Plugin.ProgressSync.Models;

/// <summary>
/// A persistent link that keeps progress in sync between two users for one series, movie or collection.
/// </summary>
/// <param name="Id">The link id.</param>
/// <param name="CreatorUserId">The user who created the link.</param>
/// <param name="PartnerUserId">The other user.</param>
/// <param name="RootItemId">The series, (primary) movie or collection the link covers.</param>
/// <param name="Mode">How progress flows.</param>
/// <param name="CreatedUtc">When the link was created.</param>
public sealed record SyncLink(
    Guid Id,
    Guid CreatorUserId,
    Guid PartnerUserId,
    Guid RootItemId,
    LinkMode Mode,
    DateTime CreatedUtc);
