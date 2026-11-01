# Golden files

Real responses captured on 2026-10-17 (25 Mehr 1405, after the close) from:

- `cdn.tsetmc.com/api/...` for فولاد (insCode 46348559193224090) and TEDPIX (32097828799138957)
- `search.codal.ir/api/search/v1/categories` and `.../v2/q?Symbol=فولاد`

They are trimmed to keep the repository small. Kept rows have the same keys, values, key order and string padding
the source sent, but the files were re-serialized compactly, so number spelling can differ (e.g. `4410.00` became
`4410.0`). The raw cache under `.boors-data/raw/` holds the untouched originals.

| File | Kept |
|---|---|
| `tsetmc/closing-price-daily-folad.json` | newest 6 rows, oldest 2 rows, and every row in 2025-06-01..2025-07-05 and 2026-02-15..2026-06-01 (both war closures) |
| `tsetmc/client-type-history-folad.json` | newest 30 rows |
| `tsetmc/index-b2-tedpix.json` | first 3, last 3, and the two closure windows above |
| `tsetmc/static-data.json` | all 9 PaperType rows and the first 5 IndustrialGroup rows |
| `tsetmc/market-watch-sample.json` | first 3 rows of the shares market watch plus the فولاد row |
| `tsetmc/instruments-history-in-day-sample.json` | `GetInstrmentsHistoryInDay/20261021`, captured 2026-10-27 with curl (not through the raw cache): first 3 rows plus the فولاد row of 2,216 |
| `codal/search-folad.json` | first 5 letters |
| everything else | complete |

When a source changes its format, capture a new response with the CLI (the raw cache keeps the original under
`.boors-data/raw/...`), add it next to the old file with a date suffix, and keep both tests.

## Codal monthly activity report (Phase 4 input)

`codal/monthly-activity-1605314.datasource.json.gz` is the `var datasource = {...};` object embedded in
`Decision.aspx` for فولاد's ن-۳۰ letter (TracingNo 1605314, period ending 1405/06/31), gzip-compressed and otherwise
untouched. Template "گزارش فعالیت ماهانه تولیدی - V4". Its table codes (BuyRawMaterial 3464, ProductionAndSales 1197,
Energy 3195, ProductMonthlyActivityDesc1 1199, SourceUsesCurrency 3640) differ from codes reported elsewhere, so the
parser must match tables by `aliasName`, not by numeric code.
