using Jellyfin.Plugin.SubtitleExtract.Events;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Events;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.SubtitleExtract;

/// <summary>
/// Registers the plugin's services with the service collection.
/// </summary>
public class SubtitleExtractServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddSingleton<SubtitleExtractionService>();
        serviceCollection.AddScoped<IEventConsumer<SubtitleExtractionFailedEventArgs>, SubtitleExtractionFailedLogger>();
    }
}
