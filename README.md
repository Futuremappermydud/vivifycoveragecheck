# vivifycoveragecheck

Console app that scans BeatSaver maps for the `Vivify` requirement, only checks new/updated map hashes across runs, and reports whether each new/updated map archive contains `bundleAndroid2021.vivify`.

## Run

```bash
dotnet run --project /home/runner/work/vivifycoveragecheck/vivifycoveragecheck/vivifycoveragecheck.csproj
```

On each run, the app writes files next to the built executable:

- `checked-maps.json` (map id -> last checked hash)
- `maps-with-bundleAndroid2021-vivify.txt`
- `maps-without-bundleAndroid2021-vivify.txt`
