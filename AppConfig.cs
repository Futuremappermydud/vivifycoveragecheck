internal static class AppConfig
{
    public const string BeatSaverSearchEndpointTemplate =
        "https://api.beatsaver.com/search/text/{0}?sortOrder=Latest&vivify=true&q=";
    public const int MaxBeatSaverPages = 8;

    public const string LunarRepoMapsEndpointTemplate = "https://repo.totalbs.dev/api/v1/maps?page={0}";
    public const int MaxLunarRepoPages = 50;

    public const string BundleKey = "android2021";
    public const string BundleFileName = "bundleAndroid2021.vivify";
    public const string BeatSaverUrlTemplate = "https://beatsaver.com/maps/{0}";

    public const string StateFileName = "checked-maps.json";
    public const string HasBundleReportFileName = "maps-with-bundleAndroid2021-vivify.txt";
    public const string MissingBundleReportFileName = "maps-without-bundleAndroid2021-vivify.txt";

    public const string PlaylistAuthor = "vivifycoveragecheck";
    public const string PlaylistImage = "";

    public const string WithBundlePlaylistFileName = "vivify-bundleAndroid2021.playlist.json";
    public const string WithBundlePlaylistTitle = "Vivify Android 2021 Bundle";
    public const string WithBundlePlaylistDescription =
        "BeatSaver maps requiring Vivify that include bundleAndroid2021.vivify.";

    public const string MissingBundlePlaylistFileName = "vivify-missing-bundleAndroid2021.playlist.json";
    public const string MissingBundlePlaylistTitle = "Vivify Missing Android 2021 Bundle";
    public const string MissingBundlePlaylistDescription =
        "BeatSaver maps requiring Vivify that are missing bundleAndroid2021.vivify.";

    public const string UserAgent = "vivifycoveragecheck/1.0";
}
