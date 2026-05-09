using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

const string MapsEndpointTemplate = "https://repo.totalbs.dev/api/v1/maps?page={0}";
const int MaxSearchPages = 50;
const string BundleKey = "android2021";
const string BundleFileName = "bundleAndroid2021.vivify";
const string BeatSaverUrlTemplate = "https://beatsaver.com/maps/{0}";
const string StateFileName = "checked-maps.json";
const string HasBundleReportFileName = "maps-with-bundleAndroid2021-vivify.txt";
const string MissingBundleReportFileName = "maps-without-bundleAndroid2021-vivify.txt";

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

    var vivifyMaps = await FetchTotalBsMapsAsync(httpClient);
    Console.WriteLine($"Found {vivifyMaps.Count} maps in the TotalBS API.");

    var mapsCheckedThisRun = 0;

    foreach (var map in vivifyMaps.Values)
    {
        if (checkedMaps.TryGetValue(map.Id, out var previousState) &&
            string.Equals(previousState.Hash, map.Hash, StringComparison.OrdinalIgnoreCase) &&
            previousState.HasBundle.HasValue &&
            previousState.HasBundle.Value == map.HasAndroid2021Bundle)
        {
            continue;
        }

        mapsCheckedThisRun++;
        Console.WriteLine($"Checking {map.Id} ({map.Name})...");

        var hasBundleFile = map.HasAndroid2021Bundle;
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
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Failed to complete Vivify map check: {ex.Message}");
    Environment.ExitCode = 1;
}

static async Task<Dictionary<string, TotalBsMap>> FetchTotalBsMapsAsync(HttpClient httpClient)
{
    var mapsById = new Dictionary<string, TotalBsMap>(StringComparer.OrdinalIgnoreCase);

    for (var page = 0; page < MaxSearchPages; page++)
    {
        using var response = await httpClient.GetAsync(string.Format(CultureInfo.InvariantCulture, MapsEndpointTemplate, page));
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var document = await JsonDocument.ParseAsync(stream);
        var root = document.RootElement;

        var docs = GetMaps(root).ToList();
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

        if (IsLastPage(root, page, docs.Count))
        {
            break;
        }
    }

    return mapsById;
}

static IEnumerable<JsonElement> GetMaps(JsonElement root)
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

static bool IsLastPage(JsonElement root, int page, int pageItemCount)
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
            return (currentPage + 1) * pageSize.Value >= totalCount.Value;
        }

        if (pageSize is > 0)
        {
            return pageItemCount < pageSize.Value;
        }
    }

    return false;
}

static bool TryParseMap(JsonElement doc, out TotalBsMap? map)
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

    var uploaderName = string.Empty;
    if (doc.TryGetProperty("author", out var uploader) && uploader.ValueKind == JsonValueKind.Object)
    {
        uploaderName = GetString(uploader, "displayName") ?? GetString(uploader, "username") ?? string.Empty;
    }

    var authors = string.IsNullOrWhiteSpace(uploaderName) ? "Unknown" : uploaderName;
    var beatSaverUrl = string.Format(CultureInfo.InvariantCulture, BeatSaverUrlTemplate, id);

    if (!TryGetLatestVersion(doc, out var hash, out var hasBundle) || string.IsNullOrWhiteSpace(hash))
    {
        return false;
    }

    map = new TotalBsMap(id, name, beatSaverUrl, authors, hash, hasBundle);
    return true;
}

static bool TryGetLatestVersion(JsonElement doc, out string? hash, out bool hasBundle)
{
    hash = null;
    hasBundle = false;

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

        var createdAtString = GetStringFromAny(version, "modifiedDate", "createdDate", "createdAt");
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
    hasBundle = HasAndroid2021Bundle(selectedVersion.Value);
    return true;
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

static bool? GetBooleanFromAny(JsonElement element, string propertyName, string alternatePropertyName)
{
    if (element.TryGetProperty(propertyName, out var property) || element.TryGetProperty(alternatePropertyName, out property))
    {
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

internal sealed record TotalBsMap(
    string Id,
    string Name,
    string BeatSaverUrl,
    string Authors,
    string Hash,
    bool HasAndroid2021Bundle);

internal sealed record MapCheckState(string Hash, bool? HasBundle);
