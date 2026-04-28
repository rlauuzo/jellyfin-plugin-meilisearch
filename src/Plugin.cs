using System.Globalization;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Meilisearch;

// ReSharper disable once ClassNeverInstantiated.Global
public class Plugin : BasePlugin<Config>, IHasWebPages
{
    private readonly MeilisearchClientHolder _clientHolder;
    private readonly ILogger<Plugin> _logger;
    private readonly SemaphoreSlim _updateLock = new(1, 1);

    public Plugin(
        IApplicationPaths applicationPaths,
        IXmlSerializer xmlSerializer,
        ILogger<Plugin> logger,
        MeilisearchClientHolder clientHolder,
        Indexer indexer,
        IHostApplicationLifetime hostApplicationLifetime
    ) : base(
        applicationPaths,
        xmlSerializer)
    {
        _logger = logger;
        _clientHolder = clientHolder;
        Indexer = indexer;
        Instance = this;

        hostApplicationLifetime.ApplicationStarted.Register(() => { _ = TryCreateMeilisearchClient(false); });
    }

    public Indexer Indexer { get; }

    public override string Name => "Meilisearch";
    public override Guid Id => Guid.Parse("974395db-b31d-46a2-bc86-ef9aa5ac04dd");
    public static Plugin? Instance { get; private set; }


    public IEnumerable<PluginPageInfo> GetPages()
    {
        return
        [
            new PluginPageInfo
            {
                Name = Name,
                EmbeddedResourcePath = string.Format(CultureInfo.InvariantCulture, "{0}.config.html",
                    GetType().Namespace)
            }
        ];
    }

    public override void UpdateConfiguration(BasePluginConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var config = (Config)configuration;
        var skipReload = Configuration.Url == config.Url && Configuration.ApiKey == config.ApiKey;

        Configuration = config;
        SaveConfiguration(Configuration);
        ConfigurationChanged?.Invoke(this, configuration);
        if (!skipReload)
        {
            _logger.LogInformation("Configuration changed, reloading meilisearch...");
            // Fire-and-forget: avoid blocking the UI thread with .Wait().
            // TryCreateMeilisearchClient is guarded by _updateLock to prevent concurrent runs.
            _ = TryCreateMeilisearchClient(join: false);
        }
    }

    public async Task TryCreateMeilisearchClient(bool join = true)
    {
        if (!await _updateLock.WaitAsync(join ? Timeout.Infinite : 0).ConfigureAwait(false))
        {
            _logger.LogWarning("Meilisearch client configuration is still updating, skipping");
            return;
        }

        try
        {
            await CreateMeilisearchClientCoreAsync().ConfigureAwait(false);
        }
        finally
        {
            _updateLock.Release();
        }
    }

    private async Task CreateMeilisearchClientCoreAsync()
    {
        await _clientHolder.Set(Configuration).ConfigureAwait(false);
        await Indexer.Index().ConfigureAwait(false);
    }
}
