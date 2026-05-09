using System.Globalization;
using System.IO.Compression;
using System.Text.Json;

const string SearchEndpointTemplate = "https://api.beatsaver.com/search/text/{0}?sortOrder=Latest&vivify=true&q=";
const int MaxSearchPages = 5;
const string VivifyRequirement = "Vivify";
const string StateFileName = "checked-maps.json";
const string HasBundleReportFileName = "maps-with-bundleAndroid2021-vivify.txt";
const string MissingBundleReportFileName = "maps-without-bundleAndroid2021-vivify.txt";

try
{
    var baseDirectory = AppContext.BaseDirectory;
    var stateFilePath = Path.Combine(baseDirectory, StateFileName);
    var hasBundleReportPath = Path.Combine(baseDirectory, HasBundleReportFileName);
    var missingBundleReportPath = Path.Combine(baseDirectory, MissingBundleReportFileName);

    var checkedHashes = await LoadCheckedHashesAsync(stateFilePath);
    var hasBundleLines = new List<string>();
    var missingBundleLines = new List<string>();

    using var httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
    httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("vivifycoveragecheck/1.0");

    var vivifyMaps = await FetchVivifyMapsAsync(httpClient);
    Console.WriteLine($"Found {vivifyMaps.Count} maps with '{VivifyRequirement}' requirement.");

    var mapsCheckedThisRun = 0;

    foreach (var map in vivifyMaps.Values)
    {
        if (checkedHashes.TryGetValue(map.Id, out var previousHash) && string.Equals(previousHash, map.Hash, StringComparison.OrdinalIgnoreCase))
        {
            continue;
        }

        mapsCheckedThisRun++;
        Console.WriteLine($"Checking {map.Id} ({map.Name})...");

        var hasBundleFile = await MapContainsBundleFileAsync(httpClient, map.DownloadUrl);
        var line = $"{map.BeatSaverUrl} | {map.Name} | {map.Authors}";

        if (hasBundleFile)
        {
            hasBundleLines.Add(line);
        }
        else
        {
            missingBundleLines.Add(line);
        }

        checkedHashes[map.Id] = map.Hash;
    }

    await File.WriteAllLinesAsync(hasBundleReportPath, hasBundleLines);
    await File.WriteAllLinesAsync(missingBundleReportPath, missingBundleLines);
    await SaveCheckedHashesAsync(stateFilePath, checkedHashes);

    Console.WriteLine($"Checked {mapsCheckedThisRun} new/updated maps.");
    Console.WriteLine($"Wrote: {hasBundleReportPath}");
    Console.WriteLine($"Wrote: {missingBundleReportPath}");
    Console.WriteLine($"Wrote: {stateFilePath}");
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Failed to complete Vivify map check: {ex.Message}");
    Environment.ExitCode = 1;
}

static async Task<Dictionary<string, BeatSaverMap>> FetchVivifyMapsAsync(HttpClient httpClient)
{
    var mapsById = new Dictionary<string, BeatSaverMap>(StringComparer.OrdinalIgnoreCase);

    for (var page = 0; page < MaxSearchPages; page++)
    {
        using var response = await httpClient.GetAsync(string.Format(CultureInfo.InvariantCulture, SearchEndpointTemplate, page));
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var document = await JsonDocument.ParseAsync(stream);
        var root = document.RootElement;

        var docs = GetDocs(root).ToList();
        if (docs.Count == 0)
        {
            break;
        }

        foreach (var doc in docs)
        {
            if (!TryParseMap(doc, out var map) || map is null)
            {
                continue;
            }

            if (!mapsById.ContainsKey(map.Id))
            {
                mapsById[map.Id] = map;
            }
        }

        if (IsLastPage(root, page))
        {
            break;
        }
    }

    return mapsById;
}

static IEnumerable<JsonElement> GetDocs(JsonElement root)
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

static bool IsLastPage(JsonElement root, int page)
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

static bool TryParseMap(JsonElement doc, out BeatSaverMap? map)
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
    var beatSaverUrl = GetString(doc, "url") ?? $"https://beatsaver.com/maps/{id}";

    if (!TryGetLatestVersion(doc, out var hash, out var downloadUrl) || string.IsNullOrWhiteSpace(hash) || string.IsNullOrWhiteSpace(downloadUrl))
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

static bool TryGetLatestVersion(JsonElement doc, out string? hash, out string? downloadUrl)
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
    downloadUrl = GetString(selectedVersion.Value, "downloadURL") ?? GetString(selectedVersion.Value, "downloadUrl");
    return true;
}

static async Task<bool> MapContainsBundleFileAsync(HttpClient httpClient, string downloadUrl)
{
    using var response = await httpClient.GetAsync(downloadUrl);
    response.EnsureSuccessStatusCode();

    await using var zipStream = await response.Content.ReadAsStreamAsync();
    using var zipArchive = new ZipArchive(zipStream, ZipArchiveMode.Read, leaveOpen: false);

    return zipArchive.Entries.Any(entry =>
        !string.IsNullOrWhiteSpace(entry.FullName) &&
        string.Equals(Path.GetFileName(entry.FullName), "bundleAndroid2021.vivify", StringComparison.OrdinalIgnoreCase));
}

static async Task<Dictionary<string, string>> LoadCheckedHashesAsync(string stateFilePath)
{
    if (!File.Exists(stateFilePath))
    {
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    await using var stream = File.OpenRead(stateFilePath);
    var data = await JsonSerializer.DeserializeAsync<Dictionary<string, string>>(stream);
    return data is null
        ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        : new Dictionary<string, string>(data, StringComparer.OrdinalIgnoreCase);
}

static async Task SaveCheckedHashesAsync(string stateFilePath, Dictionary<string, string> checkedHashes)
{
    await using var stream = File.Create(stateFilePath);
    await JsonSerializer.SerializeAsync(stream, checkedHashes, new JsonSerializerOptions { WriteIndented = true });
}

static string? GetString(JsonElement element, string propertyName)
{
    if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.String)
    {
        return null;
    }

    return property.GetString();
}

internal sealed record BeatSaverMap(
    string Id,
    string Name,
    string BeatSaverUrl,
    string Authors,
    string Hash,
    string DownloadUrl);
