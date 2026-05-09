using System.Globalization;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;

const string BeatSaverSearchEndpointTemplate = "https://api.beatsaver.com/search/text/{0}?sortOrder=Latest&vivify=true&q=";
const int MaxBeatSaverPages = 5;
const string LunarRepoMapsEndpointTemplate = "https://repo.totalbs.dev/api/v1/maps?page={0}";
const int MaxLunarRepoPages = 50;
const string BundleKey = "android2021";
const string BundleFileName = "bundleAndroid2021.vivify";
const string BeatSaverUrlTemplate = "https://beatsaver.com/maps/{0}";
const string StateFileName = "checked-maps.json";
const string HasBundleReportFileName = "maps-with-bundleAndroid2021-vivify.txt";
const string MissingBundleReportFileName = "maps-without-bundleAndroid2021-vivify.txt";
const string PlaylistFileName = "vivify-bundleAndroid2021.playlist.json";
const string PlaylistTitle = "Vivify Android 2021 Bundle";
const string PlaylistAuthor = "vivifycoveragecheck";
const string PlaylistDescription = "BeatSaver maps requiring Vivify that include bundleAndroid2021.vivify.";
const string PlaylistImage = "";

try
{
    var baseDirectory = AppContext.BaseDirectory;
    var stateFilePath = Path.Combine(baseDirectory, StateFileName);
    var hasBundleReportPath = Path.Combine(baseDirectory, HasBundleReportFileName);
    var missingBundleReportPath = Path.Combine(baseDirectory, MissingBundleReportFileName);

    var checkedMaps = await LoadCheckedMapsAsync(stateFilePath);
    var hasBundleLines = new List<string>();
    var missingBundleLines = new List<string>();

    using var httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
    httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("vivifycoveragecheck/1.0");

    var lunarRepoBundleHashes = await FetchLunarRepoBundleHashesAsync(httpClient);
    Console.WriteLine($"Found {lunarRepoBundleHashes.Count} maps with LunarRepo bundle data.");

    var vivifyMaps = await FetchBeatSaverVivifyMapsAsync(httpClient);
    Console.WriteLine($"Found {vivifyMaps.Count} BeatSaver maps with the Vivify requirement.");

    var mapsCheckedThisRun = 0;

    foreach (var map in vivifyMaps.Values)
    {
        var hasLunarRepoBundle = HasLunarRepoBundle(lunarRepoBundleHashes, map.Id, map.Hash);
        if (checkedMaps.TryGetValue(map.Id, out var previousState) &&
            string.Equals(previousState.Hash, map.Hash, StringComparison.OrdinalIgnoreCase) &&
            previousState.HasBundle.HasValue)
        {
            var shouldCheckForNewBundle = !previousState.HasBundle.Value && hasLunarRepoBundle;
            if (!shouldCheckForNewBundle)
            {
                continue;
            }
        }

        mapsCheckedThisRun++;
        Console.WriteLine($"Checking {map.Id} ({map.Name})...");

        var hasBundleFile = hasLunarRepoBundle;
        if (!hasBundleFile && !string.IsNullOrWhiteSpace(map.DownloadUrl))
        {
            hasBundleFile = await MapContainsBundleFileAsync(httpClient, map.DownloadUrl);
        }
        var line = $"{map.BeatSaverUrl} | {map.Name} | {map.Authors}";

        if (hasBundleFile)
        {
            hasBundleLines.Add(line);
        }
        else
        {
            missingBundleLines.Add(line);
        }

        checkedMaps[map.Id] = new MapCheckState(map.Hash, hasBundleFile);
    }

    await File.WriteAllLinesAsync(hasBundleReportPath, hasBundleLines);
    await File.WriteAllLinesAsync(missingBundleReportPath, missingBundleLines);
    await SaveCheckedMapsAsync(stateFilePath, checkedMaps);

    var playlistPath = Path.Combine(baseDirectory, PlaylistFileName);
    var playlist = BuildPlaylist(vivifyMaps, checkedMaps);
    await SavePlaylistAsync(playlistPath, playlist);

    var (withBundle, totalChecked, unknownBundle) = GetCoverageStats(checkedMaps);
    if (totalChecked > 0)
    {
        var coveragePercent = (double)withBundle / totalChecked * 100;
        Console.WriteLine($"Coverage: {withBundle}/{totalChecked} ({coveragePercent:0.00}%) checked maps include {BundleFileName}.");
    }
    else
    {
        Console.WriteLine("Coverage: no checked maps yet.");
    }

    if (unknownBundle > 0)
    {
        Console.WriteLine($"Coverage excludes {unknownBundle} maps without stored bundle results.");
    }

    Console.WriteLine($"Checked {mapsCheckedThisRun} new/updated maps.");
    Console.WriteLine($"Wrote: {hasBundleReportPath}");
    Console.WriteLine($"Wrote: {missingBundleReportPath}");
    Console.WriteLine($"Wrote: {stateFilePath}");
    Console.WriteLine($"Wrote: {playlistPath}");
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Failed to complete Vivify map check: {ex.Message}");
    Environment.ExitCode = 1;
}

static async Task<Dictionary<string, BeatSaverMap>> FetchBeatSaverVivifyMapsAsync(HttpClient httpClient)
{
    var mapsById = new Dictionary<string, BeatSaverMap>(StringComparer.OrdinalIgnoreCase);

    for (var page = 0; page < MaxBeatSaverPages; page++)
    {
        using var response = await httpClient.GetAsync(string.Format(CultureInfo.InvariantCulture, BeatSaverSearchEndpointTemplate, page));
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var document = await JsonDocument.ParseAsync(stream);
        var root = document.RootElement;

        var docs = GetBeatSaverDocs(root).ToList();
        if (docs.Count == 0)
        {
            break;
        }

        foreach (var doc in docs)
        {
            if (!TryParseBeatSaverMap(doc, out var map) || map is null)
            {
                continue;
            }

            if (!mapsById.ContainsKey(map.Id))
            {
                mapsById[map.Id] = map;
            }
        }

        if (IsBeatSaverLastPage(root, page))
        {
            break;
        }
    }

    return mapsById;
}

static IEnumerable<JsonElement> GetBeatSaverDocs(JsonElement root)
{
    if (root.ValueKind == JsonValueKind.Array)
    {
        foreach (var item in root.EnumerateArray())
        {
            yield return item;
        }

        yield break;
    }

    if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("docs", out var docs) && docs.ValueKind == JsonValueKind.Array)
    {
        foreach (var item in docs.EnumerateArray())
        {
            yield return item;
        }
    }
}

static bool IsBeatSaverLastPage(JsonElement root, int page)
{
    if (root.ValueKind != JsonValueKind.Object)
    {
        return false;
    }

    if (root.TryGetProperty("lastPage", out var lastPage))
    {
        return lastPage.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.Number => lastPage.TryGetInt32(out var lastPageNumber) && page >= lastPageNumber,
            _ => false,
        };
    }

    if (root.TryGetProperty("info", out var info) && info.ValueKind == JsonValueKind.Object && info.TryGetProperty("pages", out var pagesElement) && pagesElement.TryGetInt32(out var pages))
    {
        return page >= pages - 1;
    }

    return false;
}

static bool TryParseBeatSaverMap(JsonElement doc, out BeatSaverMap? map)
{
    map = null;

    if (doc.ValueKind != JsonValueKind.Object)
    {
        return false;
    }

    var id = GetString(doc, "id");
    if (string.IsNullOrWhiteSpace(id))
    {
        return false;
    }

    var name = GetString(doc, "name") ?? id;

    var levelAuthorName = string.Empty;
    if (doc.TryGetProperty("metadata", out var metadata) && metadata.ValueKind == JsonValueKind.Object)
    {
        levelAuthorName = GetString(metadata, "levelAuthorName") ?? string.Empty;
    }

    var uploaderName = string.Empty;
    if (doc.TryGetProperty("uploader", out var uploader) && uploader.ValueKind == JsonValueKind.Object)
    {
        uploaderName = GetString(uploader, "name") ?? GetString(uploader, "username") ?? string.Empty;
    }

    var authors = BuildAuthors(levelAuthorName, uploaderName);
    var beatSaverUrl = GetString(doc, "url") ?? string.Format(CultureInfo.InvariantCulture, BeatSaverUrlTemplate, id);

    if (!TryGetLatestBeatSaverVersion(doc, out var hash, out var downloadUrl) ||
        string.IsNullOrWhiteSpace(hash))
    {
        return false;
    }

    map = new BeatSaverMap(id, name, beatSaverUrl, authors, hash, downloadUrl);
    return true;
}

static string BuildAuthors(string levelAuthorName, string uploaderName)
{
    if (string.IsNullOrWhiteSpace(levelAuthorName) && string.IsNullOrWhiteSpace(uploaderName))
    {
        return "Unknown";
    }

    if (string.IsNullOrWhiteSpace(levelAuthorName))
    {
        return uploaderName;
    }

    if (string.IsNullOrWhiteSpace(uploaderName) || string.Equals(levelAuthorName, uploaderName, StringComparison.OrdinalIgnoreCase))
    {
        return levelAuthorName;
    }

    return $"{levelAuthorName} (uploader: {uploaderName})";
}

static bool TryGetLatestBeatSaverVersion(JsonElement doc, out string? hash, out string? downloadUrl)
{
    hash = null;
    downloadUrl = null;

    if (!doc.TryGetProperty("versions", out var versions) || versions.ValueKind != JsonValueKind.Array)
    {
        return false;
    }

    JsonElement? selectedVersion = null;
    DateTimeOffset? selectedCreatedAt = null;

    foreach (var version in versions.EnumerateArray())
    {
        if (version.ValueKind != JsonValueKind.Object)
        {
            continue;
        }

        var createdAtString = GetString(version, "createdAt");
        if (DateTimeOffset.TryParse(createdAtString, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var createdAt))
        {
            if (selectedCreatedAt is null || createdAt > selectedCreatedAt.Value)
            {
                selectedCreatedAt = createdAt;
                selectedVersion = version;
            }
        }
        else if (selectedVersion is null)
        {
            selectedVersion = version;
        }
    }

    if (selectedVersion is null)
    {
        return false;
    }

    hash = GetString(selectedVersion.Value, "hash");
    downloadUrl = GetStringFromAny(selectedVersion.Value, "downloadURL", "downloadUrl");
    return true;
}

static async Task<Dictionary<string, HashSet<string>>> FetchLunarRepoBundleHashesAsync(HttpClient httpClient)
{
    var bundleHashesByMapId = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

    for (var page = 0; page < MaxLunarRepoPages; page++)
    {
        using var response = await httpClient.GetAsync(string.Format(CultureInfo.InvariantCulture, LunarRepoMapsEndpointTemplate, page));
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var document = await JsonDocument.ParseAsync(stream);
        var root = document.RootElement;

        var docs = GetLunarRepoMaps(root).ToList();
        if (docs.Count == 0)
        {
            break;
        }

        foreach (var doc in docs)
        {
            if (doc.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var id = GetString(doc, "id");
            if (string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            if (!doc.TryGetProperty("versions", out var versions) || versions.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var version in versions.EnumerateArray())
            {
                if (version.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var hash = GetString(version, "hash");
                if (string.IsNullOrWhiteSpace(hash) || !HasAndroid2021Bundle(version))
                {
                    continue;
                }

                if (!bundleHashesByMapId.TryGetValue(id, out var hashSet))
                {
                    hashSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    bundleHashesByMapId[id] = hashSet;
                }

                hashSet.Add(hash);
            }
        }

        if (IsLunarRepoLastPage(root, page, docs.Count))
        {
            break;
        }
    }

    return bundleHashesByMapId;
}

static IEnumerable<JsonElement> GetLunarRepoMaps(JsonElement root)
{
    if (root.ValueKind == JsonValueKind.Array)
    {
        foreach (var item in root.EnumerateArray())
        {
            yield return item;
        }

        yield break;
    }

    if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("data", out var docs) && docs.ValueKind == JsonValueKind.Array)
    {
        foreach (var item in docs.EnumerateArray())
        {
            yield return item;
        }
    }
}

static bool IsLunarRepoLastPage(JsonElement root, int page, int pageItemCount)
{
    if (root.ValueKind != JsonValueKind.Object)
    {
        return pageItemCount == 0;
    }

    if (root.TryGetProperty("pagination", out var pagination) && pagination.ValueKind == JsonValueKind.Object)
    {
        var currentPage = GetInt32(pagination, "page") ?? page;
        var pageSize = GetInt32(pagination, "pageSize");
        var totalCount = GetInt32(pagination, "totalCount");

        if (pageSize is > 0 && totalCount is >= 0)
        {
            var nextPageStart = (long)(currentPage + 1) * (long)pageSize.Value;
            return nextPageStart >= totalCount.Value;
        }

        if (pageSize is > 0)
        {
            return pageItemCount < pageSize.Value;
        }
    }

    return false;
}

static bool HasLunarRepoBundle(Dictionary<string, HashSet<string>> bundleHashesByMapId, string mapId, string hash)
{
    return bundleHashesByMapId.TryGetValue(mapId, out var hashes) && hashes.Contains(hash);
}

static bool HasAndroid2021Bundle(JsonElement version)
{
    if (version.TryGetProperty("bundles", out var bundles) && bundles.ValueKind == JsonValueKind.Object)
    {
        if (bundles.TryGetProperty(BundleKey, out var bundle) && bundle.ValueKind == JsonValueKind.Object)
        {
            var downloadUrl = GetStringFromAny(bundle, "downloadUrl", "downloadURL");
            if (!string.IsNullOrWhiteSpace(downloadUrl))
            {
                return true;
            }

            var status = GetString(bundle, "status");
            return !string.IsNullOrWhiteSpace(status) &&
                   !string.Equals(status, "unavailable", StringComparison.OrdinalIgnoreCase);
        }
    }

    return false;
}

static async Task<bool> MapContainsBundleFileAsync(HttpClient httpClient, string downloadUrl)
{
    using var response = await httpClient.GetAsync(downloadUrl);
    response.EnsureSuccessStatusCode();

    await using var zipStream = await response.Content.ReadAsStreamAsync();
    using var zipArchive = new ZipArchive(zipStream, ZipArchiveMode.Read, leaveOpen: false);

    return zipArchive.Entries.Any(entry =>
        !string.IsNullOrWhiteSpace(entry.FullName) &&
        string.Equals(Path.GetFileName(entry.FullName), BundleFileName, StringComparison.OrdinalIgnoreCase));
}

static async Task<Dictionary<string, MapCheckState>> LoadCheckedMapsAsync(string stateFilePath)
{
    if (!File.Exists(stateFilePath))
    {
        return new Dictionary<string, MapCheckState>(StringComparer.OrdinalIgnoreCase);
    }

    await using var stream = File.OpenRead(stateFilePath);
    using var document = await JsonDocument.ParseAsync(stream);

    if (document.RootElement.ValueKind != JsonValueKind.Object)
    {
        return new Dictionary<string, MapCheckState>(StringComparer.OrdinalIgnoreCase);
    }

    var data = new Dictionary<string, MapCheckState>(StringComparer.OrdinalIgnoreCase);

    foreach (var property in document.RootElement.EnumerateObject())
    {
        if (property.Value.ValueKind == JsonValueKind.String)
        {
            var hash = property.Value.GetString();
            if (!string.IsNullOrWhiteSpace(hash))
            {
                data[property.Name] = new MapCheckState(hash, null);
            }

            continue;
        }

        if (property.Value.ValueKind != JsonValueKind.Object)
        {
            continue;
        }

        var hashValue = GetStringFromAny(property.Value, "hash", "Hash");
        if (string.IsNullOrWhiteSpace(hashValue))
        {
            continue;
        }

        var hasBundle = GetBooleanFromAny(property.Value, "hasBundle", "HasBundle");
        data[property.Name] = new MapCheckState(hashValue, hasBundle);
    }

    return data;
}

static async Task SaveCheckedMapsAsync(string stateFilePath, Dictionary<string, MapCheckState> checkedMaps)
{
    await using var stream = File.Create(stateFilePath);
    var options = new JsonSerializerOptions
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
    await JsonSerializer.SerializeAsync(stream, checkedMaps, options);
}

static async Task SavePlaylistAsync(string playlistFilePath, BeatSaverPlaylist playlist)
{
    await using var stream = File.Create(playlistFilePath);
    var options = new JsonSerializerOptions
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
    await JsonSerializer.SerializeAsync(stream, playlist, options);
}

static BeatSaverPlaylist BuildPlaylist(Dictionary<string, BeatSaverMap> vivifyMaps, Dictionary<string, MapCheckState> checkedMaps)
{
    var songs = vivifyMaps.Values
        .OrderBy(map => map.Name, StringComparer.OrdinalIgnoreCase)
        .Where(map =>
            checkedMaps.TryGetValue(map.Id, out var state) &&
            state.HasBundle is true &&
            string.Equals(state.Hash, map.Hash, StringComparison.OrdinalIgnoreCase))
        .Select(map => new BeatSaverPlaylistSong(map.Id, map.Hash, map.Name))
        .ToList();

    var customData = new Dictionary<string, object?>
    {
        ["bundleFile"] = BundleFileName,
        ["generatedAt"] = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
    };

    return new BeatSaverPlaylist(
        PlaylistTitle,
        PlaylistAuthor,
        PlaylistDescription,
        PlaylistImage,
        customData,
        songs);
}

static string? GetString(JsonElement element, string propertyName)
{
    if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.String)
    {
        return null;
    }

    return property.GetString();
}

static string? GetStringFromAny(JsonElement element, params string[] propertyNames)
{
    foreach (var propertyName in propertyNames)
    {
        var value = GetString(element, propertyName);
        if (!string.IsNullOrWhiteSpace(value))
        {
            return value;
        }
    }

    return null;
}

static bool? GetBooleanFromAny(JsonElement element, params string[] propertyNames)
{
    foreach (var propertyName in propertyNames)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            continue;
        }

        if (property.ValueKind == JsonValueKind.True || property.ValueKind == JsonValueKind.False)
        {
            return property.GetBoolean();
        }
    }

    return null;
}

static int? GetInt32(JsonElement element, string propertyName)
{
    if (element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var value))
    {
        return value;
    }

    return null;
}

static (int WithBundle, int Total, int Unknown) GetCoverageStats(Dictionary<string, MapCheckState> checkedMaps)
{
    var withBundle = 0;
    var withoutBundle = 0;
    var unknown = 0;

    foreach (var state in checkedMaps.Values)
    {
        if (state.HasBundle is null)
        {
            unknown++;
            continue;
        }

        if (state.HasBundle.Value)
        {
            withBundle++;
        }
        else
        {
            withoutBundle++;
        }
    }

    return (withBundle, withBundle + withoutBundle, unknown);
}

internal sealed record BeatSaverMap(
    string Id,
    string Name,
    string BeatSaverUrl,
    string Authors,
    string Hash,
    string? DownloadUrl);

internal sealed record MapCheckState(string Hash, bool? HasBundle);

internal sealed record BeatSaverPlaylist(
    string PlaylistTitle,
    string PlaylistAuthor,
    string PlaylistDescription,
    string Image,
    Dictionary<string, object?> CustomData,
    List<BeatSaverPlaylistSong> Songs);

internal sealed record BeatSaverPlaylistSong(
    string Key,
    string Hash,
    string SongName);
