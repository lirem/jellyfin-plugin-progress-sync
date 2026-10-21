using System.Collections.Generic;
using Jellyfin.Plugin.ProgressSync.Services;

namespace Jellyfin.Plugin.ProgressSync.Models;

/// <summary>
/// Data for the admin page.
/// </summary>
/// <param name="FileTransformation">State of the web client integration.</param>
/// <param name="Message">Details when the integration is not active.</param>
/// <param name="Links">All links on the server.</param>
public sealed record AdminOverviewDto(
    InjectionState FileTransformation,
    string? Message,
    IReadOnlyList<AdminLinkDto> Links);
