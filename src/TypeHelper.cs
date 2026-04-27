using System.Collections.Frozen;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Channels;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.LiveTv;
using MediaBrowser.Controller.Playlists;

namespace Jellyfin.Plugin.Meilisearch;

public static class TypeHelper
{
    public static IReadOnlyDictionary<string, string> JellyfinTypeMap { get; } = new Dictionary<string, string>()
    {
        { "AggregateFolder", typeof(AggregateFolder).FullName! },
        { "Audio", typeof(Audio).FullName! },
        { "AudioBook", typeof(AudioBook).FullName! },
        { "BasePluginFolder", typeof(BasePluginFolder).FullName! },
        { "Book", typeof(Book).FullName! },
        { "BoxSet", typeof(BoxSet).FullName! },
        { "Channel", typeof(Channel).FullName! },
        { "CollectionFolder", typeof(CollectionFolder).FullName! },
        { "Episode", typeof(Episode).FullName! },
        { "Folder", typeof(Folder).FullName! },
        { "Genre", typeof(Genre).FullName! },
        { "Movie", typeof(Movie).FullName! },
        { "LiveTvChannel", typeof(LiveTvChannel).FullName! },
        { "LiveTvProgram", typeof(LiveTvProgram).FullName! },
        { "MusicAlbum", typeof(MusicAlbum).FullName! },
        { "MusicArtist", typeof(MusicArtist).FullName! },
        { "MusicGenre", typeof(MusicGenre).FullName! },
        { "MusicVideo", typeof(MusicVideo).FullName! },
        { "Person", typeof(Person).FullName! },
        { "Photo", typeof(Photo).FullName! },
        { "PhotoAlbum", typeof(PhotoAlbum).FullName! },
        { "Playlist", typeof(Playlist).FullName! },
        { "PlaylistsFolder", "Emby.Server.Implementations.Playlists.PlaylistsFolder" },
        { "Season", typeof(Season).FullName! },
        { "Series", typeof(Series).FullName! },
        { "Studio", typeof(Studio).FullName! },
        { "Trailer", typeof(Trailer).FullName! },
        { "TvChannel", typeof(LiveTvChannel).FullName! },
        { "TvProgram", typeof(LiveTvProgram).FullName! },
        { "UserRootFolder", typeof(UserRootFolder).FullName! },
        { "UserView", typeof(UserView).FullName! },
        { "Video", typeof(Video).FullName! },
        { "Year", typeof(Year).FullName! }
    }.ToFrozenDictionary();

    private static FrozenDictionary<BaseItemKind, string> BaseItemKindTypeMap { get; } = JellyfinTypeMap
        .Where(static entry => Enum.TryParse<BaseItemKind>(entry.Key, out _))
        .ToFrozenDictionary(
            static entry => Enum.Parse<BaseItemKind>(entry.Key),
            static entry => entry.Value);

    public static FrozenSet<string> TypeFullNames { get; } = JellyfinTypeMap.Values.ToFrozenSet();

    /// <summary>Pre-computed list of all type full names, returned when no include/exclude filters are applied.</summary>
    private static IReadOnlyList<string> AllTypeFullNames { get; } = [.. TypeFullNames];

    /// <summary>Pre-computed Meilisearch filter string for the unfiltered case (all types).</summary>
    private static string AllTypesFilter { get; } = string.Join(" OR ", TypeFullNames.Select(t => $"type = \"{t}\""));

    internal static string BuildTypeFilter(IReadOnlyList<string> types)
        => ReferenceEquals(types, AllTypeFullNames)
            ? AllTypesFilter
            : string.Join(" OR ", types.Select(t => $"type = \"{t}\""));

    public static IEnumerable<string> MapTypeKeys(IEnumerable<BaseItemKind> keys) =>
        keys.Select(k => BaseItemKindTypeMap.TryGetValue(k, out var v) ? v : null).OfType<string>();

    internal static IReadOnlyList<string> BuildTypeNames(BaseItemKind[]? includeItemTypes, BaseItemKind[]? excludeItemTypes)
    {
        if (includeItemTypes is not { Length: > 0 } && excludeItemTypes is not { Length: > 0 })
            return AllTypeFullNames;

        List<string> types = includeItemTypes is { Length: > 0 }
            ? [.. MapTypeKeys(includeItemTypes)]
            : [.. TypeFullNames];

        if (excludeItemTypes is { Length: > 0 })
        {
            var excludeNames = MapTypeKeys(excludeItemTypes).ToHashSet();
            types.RemoveAll(excludeNames.Contains);
        }

        return types;
    }
}
