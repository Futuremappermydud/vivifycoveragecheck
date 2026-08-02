using System.Globalization;
using System.Text.Json;

internal sealed class BeatSaverClient(HttpClient httpClient)
{
    public async Task<Dictionary<string, BeatSaverMap>> FetchVivifyMapsAsync()
    {
        var mapsById = new Dictionary<string, BeatSaverMap>(StringComparer.OrdinalIgnoreCase);

        for (var page = 0; page < AppConfig.MaxBeatSaverPages; page++)
        {
            using var response = await httpClient.GetAsync(
                string.Format(CultureInfo.InvariantCulture, AppConfig.BeatSaverSearchEndpointTemplate, page));
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

                mapsById.TryAdd(map.Id, map);
            }

            if (IsLastPage(root, page))
            {
                break;
            }
        }

        return mapsById;
    }

    private static IEnumerable<JsonElement> GetDocs(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in root.EnumerateArray())
            {
                yield return item;
            }

            yield break;
        }

        if (root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty("docs", out var docs) &&
            docs.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in docs.EnumerateArray())
            {
                yield return item;
            }
        }
    }

    private static bool IsLastPage(JsonElement root, int page)
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

        if (root.TryGetProperty("info", out var info) &&
            info.ValueKind == JsonValueKind.Object &&
            info.TryGetProperty("pages", out var pagesElement) &&
            pagesElement.TryGetInt32(out var pages))
        {
            return page >= pages - 1;
        }

        return false;
    }

    private static bool TryParseMap(JsonElement doc, out BeatSaverMap? map)
    {
        map = null;

        if (doc.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        var id = doc.GetString("id");
        if (string.IsNullOrWhiteSpace(id))
        {
            return false;
        }

        var name = doc.GetString("name") ?? id;

        var levelAuthorName = string.Empty;
        if (doc.TryGetProperty("metadata", out var metadata) && metadata.ValueKind == JsonValueKind.Object)
        {
            levelAuthorName = metadata.GetString("levelAuthorName") ?? string.Empty;
        }

        var uploaderName = string.Empty;
        if (doc.TryGetProperty("uploader", out var uploader) && uploader.ValueKind == JsonValueKind.Object)
        {
            uploaderName = uploader.GetString("name") ?? uploader.GetString("username") ?? string.Empty;
        }

        var authors = BuildAuthors(levelAuthorName, uploaderName);
        var beatSaverUrl = doc.GetString("url") ??
            string.Format(CultureInfo.InvariantCulture, AppConfig.BeatSaverUrlTemplate, id);

        if (!TryGetLatestVersion(doc, out var hash, out var downloadUrl) || string.IsNullOrWhiteSpace(hash))
        {
            return false;
        }

        map = new BeatSaverMap(id, name, beatSaverUrl, authors, hash, downloadUrl);
        return true;
    }

    private static string BuildAuthors(string levelAuthorName, string uploaderName)
    {
        if (string.IsNullOrWhiteSpace(levelAuthorName) && string.IsNullOrWhiteSpace(uploaderName))
        {
            return "Unknown";
        }

        if (string.IsNullOrWhiteSpace(levelAuthorName))
        {
            return uploaderName;
        }

        if (string.IsNullOrWhiteSpace(uploaderName) ||
            string.Equals(levelAuthorName, uploaderName, StringComparison.OrdinalIgnoreCase))
        {
            return levelAuthorName;
        }

        return $"{levelAuthorName} (uploader: {uploaderName})";
    }

    private static bool TryGetLatestVersion(JsonElement doc, out string? hash, out string? downloadUrl)
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

            var createdAtString = version.GetString("createdAt");
            if (DateTimeOffset.TryParse(
                    createdAtString,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal,
                    out var createdAt))
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

        hash = selectedVersion.Value.GetString("hash");
        downloadUrl = selectedVersion.Value.GetStringFromAny("downloadURL", "downloadUrl");
        return true;
    }
}
