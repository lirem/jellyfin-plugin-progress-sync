using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.ProgressSync.Configuration;

/// <summary>
/// Plugin configuration. Progress Sync has no settings; the type exists because the dashboard
/// plugin pages expect a configurable plugin.
/// </summary>
public sealed class PluginConfiguration : BasePluginConfiguration
{
}
