namespace Jellyfin.Plugin.ProgressSync.Services;

/// <summary>
/// State of the web client integration.
/// </summary>
public enum InjectionState
{
    /// <summary>
    /// Registration has not run yet.
    /// </summary>
    Pending,

    /// <summary>
    /// The client script is injected through File Transformation.
    /// </summary>
    Registered,

    /// <summary>
    /// The File Transformation plugin is not installed or not enabled.
    /// </summary>
    FileTransformationMissing,

    /// <summary>
    /// Registration failed; see <see cref="WebInjectionStatus.Message"/>.
    /// </summary>
    Failed
}
