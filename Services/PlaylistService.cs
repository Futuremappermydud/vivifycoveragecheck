using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

internal sealed class PlaylistService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public BeatSaverPlaylist BuildWithBundle(
        Dictionary<string, BeatSaverMap> vivifyMaps,
        Dictionary<string, MapCheckState> checkedMaps)
    {
        return Build(
            vivifyMaps,
            checkedMaps,
            hasBundle: true,
            AppConfig.WithBundlePlaylistTitle,
            AppConfig.WithBundlePlaylistDescription);
    }

    public BeatSaverPlaylist BuildMissingBundle(
        Dictionary<string, BeatSaverMap> vivifyMaps,
        Dictionary<string, MapCheckState> checkedMaps)
    {
        return Build(
            vivifyMaps,
            checkedMaps,
            hasBundle: false,
            AppConfig.MissingBundlePlaylistTitle,
            AppConfig.MissingBundlePlaylistDescription);
    }

    public async Task SaveAsync(string playlistFilePath, BeatSaverPlaylist playlist)
    {
        await using var stream = File.Create(playlistFilePath);
        await JsonSerializer.SerializeAsync(stream, playlist, SerializerOptions);
    }

    private static BeatSaverPlaylist Build(
        Dictionary<string, BeatSaverMap> vivifyMaps,
        Dictionary<string, MapCheckState> checkedMaps,
        bool hasBundle,
        string title,
        string description)
    {
        var songs = vivifyMaps.Values
            .Where(map =>
                checkedMaps.TryGetValue(map.Id, out var state) &&
                state.HasBundle == hasBundle &&
                string.Equals(state.Hash, map.Hash, StringComparison.OrdinalIgnoreCase))
            .OrderBy(map => map.Name, StringComparer.OrdinalIgnoreCase)
            .Select(map => new BeatSaverPlaylistSong(map.Id, map.Hash, map.Name))
            .ToList();

        var customData = new Dictionary<string, object?>
        {
            ["bundleFile"] = AppConfig.BundleFileName,
            ["hasBundle"] = hasBundle,
            ["generatedAt"] = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
        };

        return new BeatSaverPlaylist(
            title,
            AppConfig.PlaylistAuthor,
            description,
            AppConfig.PlaylistImage,
            customData,
            songs);
    }
}
