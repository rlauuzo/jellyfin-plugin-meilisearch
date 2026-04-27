using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Entities;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Persistence;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Querying;
using Meilisearch;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Meilisearch;

public class MeilisearchRepositoryDecorator(
    IItemRepository inner,
    MeilisearchClientHolder clientHolder,
    ILogger<MeilisearchRepositoryDecorator> logger
) : IItemRepository
{
    private sealed record SearchResultData(IReadOnlyList<Guid> OrderedIds, int TotalCount);

    public QueryResult<BaseItem> GetItems(InternalItemsQuery filter)
    {
        var searchResult = RunSearch(filter);
        if (ShouldFallbackToInnerSearch(searchResult))
            return inner.GetItems(filter);

        var materialized = MaterializeItems(filter, searchResult!);
        return new QueryResult<BaseItem>(
            filter.StartIndex,
            searchResult!.TotalCount,
            materialized);
    }

    private bool TryPrepareSearch(InternalItemsQuery filter, out string searchTerm, out IReadOnlyList<string> types)
    {
        searchTerm = filter.SearchTerm ?? string.Empty;
        types = [];
        if (string.IsNullOrWhiteSpace(searchTerm) || !clientHolder.Ok || clientHolder.Index is null)
            return false;

        types = TypeHelper.BuildTypeNames(filter.IncludeItemTypes, filter.ExcludeItemTypes);
        return types.Count > 0;
    }

    private SearchResultData? RunSearch(InternalItemsQuery filter)
    {
        if (!TryPrepareSearch(filter, out var searchTerm, out var types))
            return null;

        var hitsPerPage = Math.Max(filter.Limit is > 0 ? filter.Limit.Value : 30, 1);
        var page = ((filter.StartIndex ?? 0) / hitsPerPage) + 1;

        // Jellyfin calls into this repository synchronously. Running the external async search
        // on a thread-pool thread reduces sync-over-async deadlock risk at this boundary.
        return Task.Run(() => ExecuteSearchAsync(searchTerm, types, page, hitsPerPage)).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Lightweight search that only retrieves the total count without parsing hit GUIDs.
    /// Used by <see cref="GetCount"/> to avoid the overhead of a full search.
    /// </summary>
    private int? RunCountSearch(InternalItemsQuery filter)
    {
        if (!TryPrepareSearch(filter, out var searchTerm, out var types))
            return null;

        return Task.Run(() => ExecuteSearchAsync(searchTerm, types, page: 1, hitsPerPage: 1)).GetAwaiter().GetResult()?.TotalCount;
    }

    private async Task<SearchResultData?> ExecuteSearchAsync(string searchTerm, IReadOnlyList<string> types, int page, int hitsPerPage)
    {
        var index = clientHolder.Index;
        if (!clientHolder.Ok || index is null)
            return null;

        try
        {
            var matchingStrategy = Plugin.Instance?.Configuration.MatchingStrategy ?? "last";
            var typeFilter = TypeHelper.BuildTypeFilter(types);
            var result = await index.SearchAsync<MeilisearchItem>(
                searchTerm,
                new SearchQuery
                {
                    Filter = typeFilter,
                    Page = page,
                    HitsPerPage = hitsPerPage,
                    MatchingStrategy = matchingStrategy,
                }
            ).ConfigureAwait(false);

            List<Guid> ids = new(result.Hits.Count);
            foreach (var hit in result.Hits)
            {
                if (Guid.TryParse(hit.Guid, out var id))
                    ids.Add(id);
                else
                    logger.LogWarning("Skipping Meilisearch hit with invalid GUID '{Guid}'", hit.Guid);
            }

            var totalCount = result is PaginatedSearchResult<MeilisearchItem> paginatedResult
                ? paginatedResult.TotalHits
                : ids.Count;
            return new SearchResultData(ids, totalCount);
        }
        catch (MeilisearchCommunicationError e)
        {
            logger.LogError(e, "Meilisearch communication error");
            clientHolder.Unset();
            return null;
        }
    }

    private IReadOnlyList<BaseItem> MaterializeItems(InternalItemsQuery filter, SearchResultData searchResult)
        => MaterializedQueryHelper.Execute(filter, searchResult.OrderedIds, inner.GetItemList);

    private static bool ShouldFallbackToInnerSearch(SearchResultData? searchResult)
        => searchResult is null || searchResult.OrderedIds.Count == 0;

    public Task ReattachUserDataAsync(BaseItem item, CancellationToken cancellationToken) => inner.ReattachUserDataAsync(item, cancellationToken);
    public void DeleteItem(IReadOnlyList<Guid> ids) => inner.DeleteItem(ids);
    public void SaveItems(IReadOnlyList<BaseItem> items, CancellationToken ct) => inner.SaveItems(items, ct);
    public void SaveImages(BaseItem item) => inner.SaveImages(item);
    public BaseItem? RetrieveItem(Guid id) => inner.RetrieveItem(id);
    public int GetCount(InternalItemsQuery filter)
    {
        var count = RunCountSearch(filter);
        return count is null or 0
            ? inner.GetCount(filter)
            : count.Value;
    }
    public ItemCounts GetItemCounts(InternalItemsQuery filter) => inner.GetItemCounts(filter);
    public IReadOnlyList<Guid> GetItemIdsList(InternalItemsQuery filter)
    {
        var searchResult = RunSearch(filter);
        return ShouldFallbackToInnerSearch(searchResult)
            ? inner.GetItemIdsList(filter)
            : searchResult!.OrderedIds;
    }
    public IReadOnlyList<BaseItem> GetItemList(InternalItemsQuery filter)
    {
        var searchResult = RunSearch(filter);
        return ShouldFallbackToInnerSearch(searchResult)
            ? inner.GetItemList(filter)
            : MaterializeItems(filter, searchResult!);
    }
    public IReadOnlyList<BaseItem> GetLatestItemList(InternalItemsQuery filter, CollectionType ct) => inner.GetLatestItemList(filter, ct);
    public IReadOnlyList<string> GetNextUpSeriesKeys(InternalItemsQuery filter, DateTime dateCutoff) => inner.GetNextUpSeriesKeys(filter, dateCutoff);
    public QueryResult<(BaseItem Item, ItemCounts ItemCounts)> GetGenres(InternalItemsQuery filter) => inner.GetGenres(filter);
    public QueryResult<(BaseItem Item, ItemCounts ItemCounts)> GetMusicGenres(InternalItemsQuery filter) => inner.GetMusicGenres(filter);
    public QueryResult<(BaseItem Item, ItemCounts ItemCounts)> GetStudios(InternalItemsQuery filter) => inner.GetStudios(filter);
    public QueryResult<(BaseItem Item, ItemCounts ItemCounts)> GetArtists(InternalItemsQuery filter) => inner.GetArtists(filter);
    public QueryResult<(BaseItem Item, ItemCounts ItemCounts)> GetAlbumArtists(InternalItemsQuery filter) => inner.GetAlbumArtists(filter);
    public QueryResult<(BaseItem Item, ItemCounts ItemCounts)> GetAllArtists(InternalItemsQuery filter) => inner.GetAllArtists(filter);
    public IReadOnlyList<string> GetMusicGenreNames() => inner.GetMusicGenreNames();
    public IReadOnlyList<string> GetStudioNames() => inner.GetStudioNames();
    public IReadOnlyList<string> GetGenreNames() => inner.GetGenreNames();
    public IReadOnlyList<string> GetAllArtistNames() => inner.GetAllArtistNames();
    public IReadOnlyDictionary<string, MusicArtist[]> FindArtists(IReadOnlyList<string> artistNames) => inner.FindArtists(artistNames);
    public void UpdateInheritedValues() => inner.UpdateInheritedValues();
    public Task<bool> ItemExistsAsync(Guid id) => inner.ItemExistsAsync(id);
    public bool GetIsPlayed(User user, Guid id, bool recursive) => inner.GetIsPlayed(user, id, recursive);
}
