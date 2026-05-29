# samples\Demo

A fully synthetic six-office floor plan used as the README walkthrough
sample (and as a regression check for the pipeline).

| File | Purpose |
|---|---|
| `map.png` | 2400×1600 px synthetic floor plan: 3 offices over a corridor over 3 offices |
| `assignments.csv` | 8 fictional people across 3 teams; office 106 left vacant |
| `teams.json` | Pastel hex colors for the 3 teams |
| `calibration.json` | Hand-curated golden calibration (skips the `calibrate` pass) |
| `calibration.json.reviewed` | Sentinel marking the golden as already reviewed |

Run the pipeline (from inside this folder):

```
OfficeMapMaker validate labels --calibration calibration.json --assignments assignments.csv
OfficeMapMaker validate fill   --map map.png --calibration calibration.json
OfficeMapMaker layout          --map map.png --calibration calibration.json --assignments assignments.csv
OfficeMapMaker layout confirm  --layout layout.json
OfficeMapMaker build           --map map.png --calibration calibration.json --assignments assignments.csv --layout layout.json --teams teams.json
OfficeMapMaker build confirm   --composite composite.png
OfficeMapMaker tile            --composite composite.png --out tiles
```

All generated artifacts (`layout.json`, `composite.png`, `tiles\`,
`calibration_*` review files, `*.manifest.json`, `*.reviewed` sentinels
written by your run) are deliberately ignored by `.gitignore`; the
committed contents are just the four inputs plus the golden calibration
sentinel.
