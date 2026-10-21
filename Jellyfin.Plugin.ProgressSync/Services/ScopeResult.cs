using System.Collections.Generic;
using MediaBrowser.Controller.Entities;

namespace Jellyfin.Plugin.ProgressSync.Services;

/// <summary>
/// Items selected for a copy.
/// </summary>
/// <param name="Items">Items visible to both users.</param>
/// <param name="Skipped">Number of candidates dropped because one of the users cannot access them.</param>
internal sealed record ScopeResult(IReadOnlyList<BaseItem> Items, int Skipped);
