internal static class VivifyCoverageApp
{
    public static async Task RunAsync()
    {
        var baseDirectory = AppContext.BaseDirectory;
        var stateFilePath = Path.Combine(baseDirectory, AppConfig.StateFileName);
        var hasBundleReportPath = Path.Combine(baseDirectory, AppConfig.HasBundleReportFileName);
        var missingBundleReportPath = Path.Combine(baseDirectory, AppConfig.MissingBundleReportFileName);
        var withBundlePlaylistPath = Path.Combine(baseDirectory, AppConfig.WithBundlePlaylistFileName);
        var missingBundlePlaylistPath = Path.Combine(baseDirectory, AppConfig.MissingBundlePlaylistFileName);

        var checkedMapsStore = new CheckedMapsStore();
        var playlistService = new PlaylistService();
        var reportService = new ReportService();
        var checkedMaps = await checkedMapsStore.LoadAsync(stateFilePath);

        using var httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(AppConfig.UserAgent);

        var beatSaverClient = new BeatSaverClient(httpClient);
        var lunarRepoClient = new LunarRepoClient(httpClient);
        var bundleChecker = new BundleChecker(httpClient);

        var lunarRepoBundleHashes = await lunarRepoClient.FetchBundleHashesAsync();
        Console.WriteLine($"Found {lunarRepoBundleHashes.Count} maps with LunarRepo bundle data.");

        var vivifyMaps = await beatSaverClient.FetchVivifyMapsAsync();
        Console.WriteLine($"Found {vivifyMaps.Count} BeatSaver maps with the Vivify requirement.");

        var mapsCheckedThisRun = 0;

        foreach (var map in vivifyMaps.Values)
        {
            var hasLunarRepoBundle = LunarRepoClient.HasBundle(lunarRepoBundleHashes, map.Id, map.Hash);
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
                hasBundleFile = await bundleChecker.MapContainsBundleFileAsync(map.DownloadUrl);
            }

            checkedMaps[map.Id] = new MapCheckState(map.Hash, hasBundleFile);
        }

        var (hasBundleLines, missingBundleLines) = reportService.Build(vivifyMaps, checkedMaps);
        await reportService.SaveAsync(hasBundleReportPath, hasBundleLines);
        await reportService.SaveAsync(missingBundleReportPath, missingBundleLines);
        await checkedMapsStore.SaveAsync(stateFilePath, checkedMaps);

        var withBundlePlaylist = playlistService.BuildWithBundle(vivifyMaps, checkedMaps);
        var missingBundlePlaylist = playlistService.BuildMissingBundle(vivifyMaps, checkedMaps);
        await playlistService.SaveAsync(withBundlePlaylistPath, withBundlePlaylist);
        await playlistService.SaveAsync(missingBundlePlaylistPath, missingBundlePlaylist);

        WriteCoverageSummary(
            checkedMaps,
            mapsCheckedThisRun,
            hasBundleReportPath,
            missingBundleReportPath,
            stateFilePath,
            withBundlePlaylistPath,
            missingBundlePlaylistPath);
    }

    private static void WriteCoverageSummary(
        Dictionary<string, MapCheckState> checkedMaps,
        int mapsCheckedThisRun,
        string hasBundleReportPath,
        string missingBundleReportPath,
        string stateFilePath,
        string withBundlePlaylistPath,
        string missingBundlePlaylistPath)
    {
        var (withBundle, totalChecked, unknownBundle) = CoverageStats.Calculate(checkedMaps);
        if (totalChecked > 0)
        {
            var coveragePercent = (double)withBundle / totalChecked * 100;
            Console.WriteLine(
                $"Coverage: {withBundle}/{totalChecked} ({coveragePercent:0.00}%) checked maps include {AppConfig.BundleFileName}.");
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
        Console.WriteLine($"Wrote: {withBundlePlaylistPath}");
        Console.WriteLine($"Wrote: {missingBundlePlaylistPath}");
    }
}
