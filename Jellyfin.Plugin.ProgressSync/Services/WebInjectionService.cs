using System;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.ProgressSync.Services;

/// <summary>
/// Registers <see cref="IndexTransformer"/> with the File Transformation plugin at startup.
/// </summary>
public sealed class WebInjectionService : IHostedService
{
    /// <summary>
    /// File Transformation's repository, shown to admins when the plugin is missing.
    /// </summary>
    internal const string FileTransformationRepository = "https://www.iamparadox.dev/jellyfin/plugins/manifest.json";

    private const string FileTransformationAssembly = "Jellyfin.Plugin.FileTransformation";
    private const string PluginInterfaceType = "Jellyfin.Plugin.FileTransformation.PluginInterface";
    private static readonly Guid _transformationId = new("ea72ed2e-2a79-4273-9031-764e6a297fbb");

    private readonly WebInjectionStatus _status;
    private readonly ILogger<WebInjectionService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="WebInjectionService"/> class.
    /// </summary>
    /// <param name="status">The shared integration status.</param>
    /// <param name="logger">Instance of the <see cref="ILogger{WebInjectionService}"/> interface.</param>
    public WebInjectionService(WebInjectionStatus status, ILogger<WebInjectionService> logger)
    {
        _status = status;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        // Hosted services start after all plugins are constructed, so File Transformation's
        // plugin instance (used by RegisterTransformation) exists by now.
        try
        {
            Register();
        }
        catch (Exception ex)
        {
            var cause = ex is TargetInvocationException { InnerException: { } inner } ? inner : ex;
            _status.State = InjectionState.Failed;
            _status.Message = cause.Message;
            _logger.LogError(cause, "Progress Sync: registering with File Transformation failed");
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private void Register()
    {
        var assembly = AssemblyLoadContext.All
            .SelectMany(context => context.Assemblies)
            .FirstOrDefault(a => string.Equals(a.GetName().Name, FileTransformationAssembly, StringComparison.Ordinal));
        if (assembly is null)
        {
            _status.State = InjectionState.FileTransformationMissing;
            _logger.LogWarning(
                "Progress Sync: File Transformation plugin not found, the 'Sync progress' button will not appear. Install it from {Repository}",
                FileTransformationRepository);
            return;
        }

        var register = assembly.GetType(PluginInterfaceType)?.GetMethod("RegisterTransformation", BindingFlags.Public | BindingFlags.Static);
        var payloadType = register?.GetParameters().SingleOrDefault()?.ParameterType;

        // The payload type is File Transformation's own Newtonsoft JObject. Building it through that
        // type avoids referencing or shipping Newtonsoft.Json (a second copy would be a different type).
        var parse = payloadType?.GetMethod("Parse", BindingFlags.Public | BindingFlags.Static, [typeof(string)]);
        if (register is null || parse is null)
        {
            _status.State = InjectionState.Failed;
            _status.Message = "Incompatible File Transformation version.";
            _logger.LogError("Progress Sync: incompatible File Transformation version, the 'Sync progress' button will not appear");
            return;
        }

        var json = JsonSerializer.Serialize(new
        {
            id = _transformationId,

            // Must be the exact string other consumers use: File Transformation runs one pipeline
            // per request and prefers an exact key over regex keys.
            fileNamePattern = "index.html",
            callbackAssembly = typeof(IndexTransformer).Assembly.FullName,
            callbackClass = typeof(IndexTransformer).FullName,
            callbackMethod = nameof(IndexTransformer.Transform)
        });
        register.Invoke(null, [parse.Invoke(null, [json])]);

        _status.State = InjectionState.Registered;
        _status.Message = null;
        _logger.LogInformation("Progress Sync: client script registered with File Transformation");
    }
}
