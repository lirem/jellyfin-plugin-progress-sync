namespace Jellyfin.Plugin.ProgressSync.Models;

/// <summary>
/// Outcome of a progress copy.
/// </summary>
/// <param name="Updated">Number of items whose progress changed.</param>
/// <param name="Skipped">Number of items skipped because the source user cannot access them.</param>
/// <param name="Total">Number of items in scope, including skipped ones.</param>
public sealed record CopyResult(int Updated, int Skipped, int Total);
