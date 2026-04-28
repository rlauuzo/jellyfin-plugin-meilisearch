using System.Globalization;
using Meilisearch;
using Microsoft.Extensions.Logging;
using Index = Meilisearch.Index;

namespace Jellyfin.Plugin.Meilisearch;

public abstract class Indexer(MeilisearchClientHolder clientHolder, ILogger<Indexer> logger)
{
    private readonly SemaphoreSlim _indexLock = new(1, 1);

    public Dictionary<string, string> Status { get; } = new();

    public async Task Index()
    {
        if (!await _indexLock.WaitAsync(0).ConfigureAwait(false))
        {
            logger.LogWarning("Indexing is already in progress, skipping");
            return;
        }

        try
        {
            var task = clientHolder.Call(IndexInternal);
            if (task == null)
            {
                logger.LogWarning("Meilisearch is not configured, skipping index update");
                return;
            }

            await task.ConfigureAwait(false);
        }
        finally
        {
            _indexLock.Release();
        }
    }

    private async Task IndexInternal(MeilisearchClient meilisearchClient, Index index)
    {
        var items = await GetItems(TypeHelper.TypeFullNames);

        if (items.Count <= 0)
        {
            logger.LogInformation("No items to index");
            return;
        }

        await index.AddDocumentsInBatchesAsync(items, batchSize: 5000, primaryKey: "guid");
        logger.LogInformation("Upload {COUNT} items to Meilisearch", items.Count);
        Status["Items"] = items.Count.ToString();
        Status["LastIndexed"] = DateTime.Now.ToString(CultureInfo.CurrentCulture);
    }

    protected abstract Task<IReadOnlyList<MeilisearchItem>> GetItems(IReadOnlySet<string> includedTypes);
}
