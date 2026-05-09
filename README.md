# vivifycoveragecheck

Console app that scans BeatSaver for maps requiring `Vivify`, tracks the latest version hash for each map across runs, uses LunarRepo (TotalBS) bundle metadata when available, falls back to checking BeatSaver downloads for `bundleAndroid2021.vivify`, and prints the overall bundle coverage percentage across all checked maps.

## Run

```bash
dotnet run --project /home/runner/work/vivifycoveragecheck/vivifycoveragecheck/vivifycoveragecheck.csproj
```

On each run, the app writes files next to the built executable:

- `checked-maps.json` (map id -> `{ hash, hasBundle }`)
- `maps-with-bundleAndroid2021-vivify.txt`
- `maps-without-bundleAndroid2021-vivify.txt`
