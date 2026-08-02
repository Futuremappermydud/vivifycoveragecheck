using System.IO.Compression;

internal sealed class BundleChecker(HttpClient httpClient)
{
    public async Task<bool> MapContainsBundleFileAsync(string downloadUrl)
    {
        using var response = await httpClient.GetAsync(downloadUrl);
        response.EnsureSuccessStatusCode();

        await using var zipStream = await response.Content.ReadAsStreamAsync();
        using var zipArchive = new ZipArchive(zipStream, ZipArchiveMode.Read, leaveOpen: false);

        return zipArchive.Entries.Any(entry =>
            !string.IsNullOrWhiteSpace(entry.FullName) &&
            string.Equals(
                Path.GetFileName(entry.FullName),
                AppConfig.BundleFileName,
                StringComparison.OrdinalIgnoreCase));
    }
}
