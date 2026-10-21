using Jellyfin.Plugin.ProgressSync.Services;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.ProgressSync;

/// <summary>
/// Registers the plugin's services with the server's dependency injection container.
/// </summary>
public sealed class PluginServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddSingleton<LinkStore>();
        serviceCollection.AddSingleton<ProgressCopier>();
        serviceCollection.AddSingleton<WebInjectionStatus>();
        serviceCollection.AddHostedService<LinkSyncService>();
        serviceCollection.AddHostedService<WebInjectionService>();
    }
}
