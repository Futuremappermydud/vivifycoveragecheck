# vivifycoveragecheck

Console app that scans the TotalBS maps API, tracks the latest version hash for each map across runs, reports whether each new/updated map has an `android2021` bundle (`bundleAndroid2021.vivify`), and prints the overall bundle coverage percentage across all checked maps.

## Run

```bash
dotnet run --project /home/runner/work/vivifycoveragecheck/vivifycoveragecheck/vivifycoveragecheck.csproj
```

On each run, the app writes files next to the built executable:

- `checked-maps.json` (map id -> `{ hash, hasBundle }`)
- `maps-with-bundleAndroid2021-vivify.txt`
- `maps-without-bundleAndroid2021-vivify.txt`
