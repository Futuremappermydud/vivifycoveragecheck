internal static class CoverageStats
{
    public static (int WithBundle, int Total, int Unknown) Calculate(
        Dictionary<string, MapCheckState> checkedMaps)
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
}
