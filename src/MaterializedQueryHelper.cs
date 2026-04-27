using MediaBrowser.Controller.Entities;
using MediaBrowser.Model.Querying;

namespace Jellyfin.Plugin.Meilisearch;

/// <summary>
/// Temporarily mutates an <see cref="InternalItemsQuery"/> to materialize Meilisearch results
/// through the inner Jellyfin repository, then restores the original state.
/// <para>
/// Thread-safety: This class assumes single-threaded access per filter instance, which matches
/// Jellyfin's current usage pattern. The save-mutate-restore window between the property
/// assignments and the <c>finally</c> block is NOT thread-safe — if the same filter were ever
/// accessed concurrently, a data race would occur. Do not share filter instances across threads.
/// </para>
/// </summary>
internal static class MaterializedQueryHelper
{
    internal static IReadOnlyList<BaseItem> Execute(
        InternalItemsQuery filter,
        IReadOnlyList<Guid> orderedIds,
        Func<InternalItemsQuery, IReadOnlyList<BaseItem>> materializer)
    {
        var originalStartIndex = filter.StartIndex;
        var originalLimit = filter.Limit;
        var originalSearchTerm = filter.SearchTerm;
        var originalItemIds = filter.ItemIds;

        try
        {
            filter.StartIndex = null;
            filter.Limit = null;
            filter.SearchTerm = null;
            filter.ItemIds = [.. orderedIds];

            var items = materializer(filter);
            Dictionary<Guid, BaseItem> itemsById = new(items.Count);
            foreach (var item in items)
                itemsById[item.Id] = item;

            List<BaseItem> orderedItems = new(orderedIds.Count);
            foreach (var id in orderedIds)
            {
                if (itemsById.TryGetValue(id, out var item))
                    orderedItems.Add(item);
            }

            return orderedItems;
        }
        finally
        {
            filter.StartIndex = originalStartIndex;
            filter.Limit = originalLimit;
            filter.SearchTerm = originalSearchTerm;
            filter.ItemIds = originalItemIds;
        }
    }
}
