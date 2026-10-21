using System;

namespace Jellyfin.Plugin.ProgressSync.Models;

/// <summary>
/// Request body for creating or updating a link.
/// </summary>
/// <param name="PartnerUserId">The user to link with.</param>
/// <param name="Mode">How progress flows; required.</param>
public sealed record CreateLinkRequest(Guid PartnerUserId, LinkMode? Mode);
