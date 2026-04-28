using MediaBrowser.Common.Configuration;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Meilisearch;

/**
 * Following code is somewhat copy-pasted or adapted from Jellysearch.
 */
public class DbIndexer(
    IApplicationPaths applicationPaths,
    MeilisearchClientHolder clientHolder,
    ILogger<DbIndexer> logger) : Indexer(clientHolder, logger)
{
    protected override async Task<IReadOnlyList<MeilisearchItem>> GetItems(IReadOnlySet<string> includedTypes)
    {
        var dbPath = Path.Combine(applicationPaths.DataPath, "jellyfin.db");
        Status["Database"] = dbPath;
        logger.LogInformation("Indexing items from database: {DB}", dbPath);

        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadOnly
        }.ToString());
        await connection.OpenAsync().ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        var types = includedTypes.Select((t, i) => (t, i)).ToList();
        command.CommandText = $"""
            SELECT
                Id, Type, ParentId, CommunityRating,
                Name, Overview, ProductionYear, Genres,
                Studios, Tags, IsFolder, CriticRating,
                OriginalTitle, SeriesName, Artists,
                AlbumArtists, Path, Tagline, SortName
            FROM
                BaseItems
            WHERE Type IN ({string.Join(", ", types.Select(x => $"@type{x.i}"))})
            """;
        foreach (var (t, i) in types)
            command.Parameters.AddWithValue($"@type{i}", t);

        await using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);

        // Resolve column ordinals once by name to avoid fragile magic-number indexing.
        var colId = reader.GetOrdinal("Id");
        var colType = reader.GetOrdinal("Type");
        var colParentId = reader.GetOrdinal("ParentId");
        var colCommunityRating = reader.GetOrdinal("CommunityRating");
        var colName = reader.GetOrdinal("Name");
        var colOverview = reader.GetOrdinal("Overview");
        var colProductionYear = reader.GetOrdinal("ProductionYear");
        var colGenres = reader.GetOrdinal("Genres");
        var colStudios = reader.GetOrdinal("Studios");
        var colTags = reader.GetOrdinal("Tags");
        var colIsFolder = reader.GetOrdinal("IsFolder");
        var colCriticRating = reader.GetOrdinal("CriticRating");
        var colOriginalTitle = reader.GetOrdinal("OriginalTitle");
        var colSeriesName = reader.GetOrdinal("SeriesName");
        var colArtists = reader.GetOrdinal("Artists");
        var colAlbumArtists = reader.GetOrdinal("AlbumArtists");
        var colPath = reader.GetOrdinal("Path");
        var colTagline = reader.GetOrdinal("Tagline");
        var colSortName = reader.GetOrdinal("SortName");

        var items = new List<MeilisearchItem>();
        while (await reader.ReadAsync().ConfigureAwait(false))
        {
            var path = !reader.IsDBNull(colPath) ? reader.GetString(colPath) : null;
            if (path?.StartsWith('%') == true) path = null;
            items.Add(new MeilisearchItem(
                reader.GetGuid(colId).ToString(),
                !reader.IsDBNull(colType) ? reader.GetString(colType) : null,
                !reader.IsDBNull(colParentId) ? reader.GetString(colParentId) : null,
                CommunityRating: !reader.IsDBNull(colCommunityRating) ? reader.GetDouble(colCommunityRating) : null,
                Name: !reader.IsDBNull(colName) ? reader.GetString(colName) : null,
                Overview: !reader.IsDBNull(colOverview) ? reader.GetString(colOverview) : null,
                ProductionYear: !reader.IsDBNull(colProductionYear) ? reader.GetInt32(colProductionYear) : null,
                Genres: !reader.IsDBNull(colGenres) ? reader.GetString(colGenres).Split('|') : null,
                Studios: !reader.IsDBNull(colStudios) ? reader.GetString(colStudios).Split('|') : null,
                Tags: !reader.IsDBNull(colTags) ? reader.GetString(colTags).Split('|') : null,
                IsFolder: !reader.IsDBNull(colIsFolder) ? reader.GetBoolean(colIsFolder) : null,
                CriticRating: !reader.IsDBNull(colCriticRating) ? reader.GetDouble(colCriticRating) : null,
                OriginalTitle: !reader.IsDBNull(colOriginalTitle) ? reader.GetString(colOriginalTitle) : null,
                SeriesName: !reader.IsDBNull(colSeriesName) ? reader.GetString(colSeriesName) : null,
                Artists: !reader.IsDBNull(colArtists) ? reader.GetString(colArtists).Split('|') : null,
                AlbumArtists: !reader.IsDBNull(colAlbumArtists) ? reader.GetString(colAlbumArtists).Split('|') : null,
                Path: path,
                Tagline: !reader.IsDBNull(colTagline) ? reader.GetString(colTagline) : null,
                SortName: !reader.IsDBNull(colSortName) ? reader.GetString(colSortName) : null
            ));
        }

        return items;
    }
}
