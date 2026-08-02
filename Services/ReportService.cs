internal sealed class ReportService
{
    public (IReadOnlyList<string> WithBundle, IReadOnlyList<string> MissingBundle) Build(
        Dictionary<string, BeatSaverMap> vivifyMaps,
        Dictionary<string, MapCheckState> checkedMaps)
    {
        var withBundle = new List<string>();
        var missingBundle = new List<string>();

        foreach (var map in vivifyMaps.Values
                     .OrderBy(map => map.Name, StringComparer.OrdinalIgnoreCase))
        {
            if (!checkedMaps.TryGetValue(map.Id, out var state) ||
                !string.Equals(state.Hash, map.Hash, StringComparison.OrdinalIgnoreCase) ||
                state.HasBundle is null)
            {
                continue;
            }

            var line = $"{map.BeatSaverUrl} | {map.Name} | {map.Authors}";
            if (state.HasBundle.Value)
            {
                withBundle.Add(line);
            }
            else
            {
                missingBundle.Add(line);
            }
        }

        return (withBundle, missingBundle);
    }

    public async Task SaveAsync(string path, IEnumerable<string> lines)
    {
        await File.WriteAllLinesAsync(path, lines);
    }
}
