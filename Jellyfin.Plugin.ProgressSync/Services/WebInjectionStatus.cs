namespace Jellyfin.Plugin.ProgressSync.Services;

/// <summary>
/// Holds the outcome of the web client integration for the admin page.
/// </summary>
public sealed class WebInjectionStatus
{
    /// <summary>
    /// Gets or sets the state.
    /// </summary>
    public InjectionState State { get; set; } = InjectionState.Pending;

    /// <summary>
    /// Gets or sets details for <see cref="InjectionState.Failed"/>.
    /// </summary>
    public string? Message { get; set; }
}
