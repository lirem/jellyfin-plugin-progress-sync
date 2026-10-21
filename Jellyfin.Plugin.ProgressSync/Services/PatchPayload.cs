namespace Jellyfin.Plugin.ProgressSync.Services;

/// <summary>
/// The payload File Transformation passes to <see cref="IndexTransformer.Transform"/>.
/// </summary>
public sealed class PatchPayload
{
    /// <summary>
    /// Gets or sets the file contents.
    /// </summary>
    public string? Contents { get; set; }
}
