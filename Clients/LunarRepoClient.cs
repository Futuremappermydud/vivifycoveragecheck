using System.Globalization;
using System.Text.Json;

internal sealed class LunarRepoClient(HttpClient httpClient)
{
    public async Task<Dictionary<string, HashSet<string>>> FetchBundleHashesAsync()
    {
        var bundleHashesByMapId = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        for (var page = 0; page < AppConfig.MaxLunarRepoPages; page++)
        {
            using var response = await httpClient.GetAsync(
                string.Format(CultureInfo.InvariantCulture, AppConfig.LunarRepoMapsEndpointTemplate, page));
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
                if (doc.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var id = doc.GetString("id");
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

                    var hash = version.GetString("hash");
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

            if (IsLastPage(root, page, docs.Count))
            {
                break;
            }
        }

        return bundleHashesByMapId;
    }

    public static bool HasBundle(
        Dictionary<string, HashSet<string>> bundleHashesByMapId,
        string mapId,
        string hash)
    {
        return bundleHashesByMapId.TryGetValue(mapId, out var hashes) && hashes.Contains(hash);
    }

    private static IEnumerable<JsonElement> GetMaps(JsonElement root)
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
            root.TryGetProperty("data", out var docs) &&
            docs.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in docs.EnumerateArray())
            {
                yield return item;
            }
        }
    }

    private static bool IsLastPage(JsonElement root, int page, int pageItemCount)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return pageItemCount == 0;
        }

        if (root.TryGetProperty("pagination", out var pagination) && pagination.ValueKind == JsonValueKind.Object)
        {
            var currentPage = pagination.GetInt32("page") ?? page;
            var pageSize = pagination.GetInt32("pageSize");
            var totalCount = pagination.GetInt32("totalCount");

            if (pageSize is > 0 && totalCount is >= 0)
            {
                var nextPageStart = (long)(currentPage + 1) * pageSize.Value;
                return nextPageStart >= totalCount.Value;
            }

            if (pageSize is > 0)
            {
                return pageItemCount < pageSize.Value;
            }
        }

        return false;
    }

    private static bool HasAndroid2021Bundle(JsonElement version)
    {
        if (!version.TryGetProperty("bundles", out var bundles) || bundles.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (!bundles.TryGetProperty(AppConfig.BundleKey, out var bundle) || bundle.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        var downloadUrl = bundle.GetStringFromAny("downloadUrl", "downloadURL");
        if (!string.IsNullOrWhiteSpace(downloadUrl))
        {
            return true;
        }

        var status = bundle.GetString("status");
        return !string.IsNullOrWhiteSpace(status) &&
               !string.Equals(status, "unavailable", StringComparison.OrdinalIgnoreCase);
    }
}
