# AGENTS.md — Boors WinUI

Conventions for anyone (human or agent) changing code under `winui/`. The README explains what the projects do;
this file says how to work on them.

## 1. Boundaries

- **Everything lives under `winui/` and is self-contained.** No `ProjectReference`, `Import`, linked file, path or
  runtime lookup may point outside this folder. `Directory.Build.props`, `Directory.Packages.props`, `global.json`
  and `nuget.config` here deliberately stop MSBuild/NuGet/SDK resolution from walking up into the repository.
- Code that came from `boilerplate/` (Boors.Market, its CLI and tests) was **copied**; change the copy here. If a fix
  also belongs upstream, say so in the PR — do not edit `boilerplate/` from this tree.
- The raw-cache format (`raw/{source}/{endpoint}/{yyyy}/{MM}/{dd}/{time}-{hash}-{node}.json.gz` + `.meta.json`) and
  the DuckDB schema version are shared with the other implementations. Do not change them without a migration plan
  in the README and a matching change in every implementation.

## 2. Stack and versions

- C# 14, .NET 10 (`global.json` pins SDK 10.0.100 with `latestFeature` roll-forward).
- Windows App SDK 2.5.x (WinUI 3), unpackaged and self-contained, `net10.0-windows10.0.19041.0`, x64, min Windows 10 1809.
- CommunityToolkit.Mvvm, Microsoft.Extensions.Hosting, ScottPlot.WinUI 5, DuckDB.NET, Jint, Polly (via Http.Resilience).
- Package versions are central (`Directory.Packages.props`). Add a package there, not in a csproj. Keep the
  market-data versions in step with the boilerplate unless a newer stable release is required.

## 3. Code rules

- **Nullable everywhere**, file-scoped namespaces, implicit usings; no warnings introduced (`dotnet build -c Release`
  must stay clean, analyzers from `.editorconfig` included).
- **Async for all I/O.** Never block the UI thread: store and analytics calls go through `IMarketApi`, whose local
  implementation runs on the thread pool. No `.Result`/`.Wait()` on the UI thread (the one startup join in
  `App.OnLaunched` waits for the host build that runs in parallel with window creation — keep it that way).
- **Single writer.** Anything that writes `market.duckdb` (collection, reload, sync) is a job on `MarketJobQueue`.
  Reads never queue. Do not open a second `MarketStore` on the same file.
- **Structured logging** with message templates (`logger.LogInformation("Job {JobId} finished in {Seconds:0.0} s", …)`),
  never string interpolation inside the template. The desktop writes CLEF JSON lines to `%LOCALAPPDATA%\Boors\logs`.
- **Exceptions:** API refusals are `MarketApiException(status, message)`; Boors.Node turns them into RFC 9457
  problem details. View models turn exceptions into a sentence (`PageViewModel.Describe`), never a crash dialog.

## 4. Desktop (WinUI) rules

- **MVVM:** view models derive from `PageViewModel`/`ObservableObject` and use `[ObservableProperty] public partial`
  properties (MVVM Toolkit 8.4 partial-property generators; no field-based properties). Page view models are DI
  singletons so a page shows its last state instantly.
- **`x:Bind` only** (compiled bindings; `Mode=OneWay` where values change). No `{Binding}`. For bool→Visibility of a
  *function* result, call a function that returns `Visibility` (`Ui.Show`, `Ui.ShowText`) — the XAML compiler's
  implicit cast of function results is buggy.
- **Strings:** English text is the key: `{l:L K='Market watch'}` in XAML, `Loc.T("Market watch")` / `Loc.F(...)` in
  code. Add the Persian text to `Infrastructure/FaStrings.cs` in the same change. Missing keys are logged at exit.
- **Numbers and dates only through `Fmt`** (Persian/Latin digits, Jalali under Persian, LRE…PDF embedding so signs
  and arrows never reorder in RTL). Every up/down value carries ▲/▼ and a sign — colour is never the only cue.
  Exports (CSV) are ASCII/invariant (`TableColumn.Export`).
- **Colours only through `Palette`** (validated up/down pairs per theme and preset, status colours reserved for
  health). Code-built UI must not read `Application.Current.Resources[...]` for theme brushes — that answers with the
  *app* theme, not the user's choice; use `Palette.TextPrimary/TextSecondary/...`.
- **RTL:** the shell sets `FlowDirection` from the language. Charts, sparklines, breadth bars and numeric cells are
  forced left-to-right. Words never go inside a ScottPlot canvas (no Persian shaping there) — put them in XAML.
- **Virtualize lists:** use `DataTable` (ItemsRepeater, recycled rows, frozen first column, background sorting) or
  `ItemsRepeater`; never a non-virtualized `ItemsControl` for data.
- **Accessibility:** icon buttons get `AutomationProperties.Name`; grid headers are buttons (keyboard and UI
  Automation can sort).

## 5. Tests and verification

- Tests are offline: `Boors.Market.Tests` uses captured golden files; `Boors.Market.Services.Tests` uses in-memory
  DuckDB, fake HTTP handlers and `FakeTimeProvider`. Never add a test that calls a live source.
- Run `dotnet test` in `src/Boors.Market.Tests` and `src/Boors.Market.Services.Tests` (Microsoft.Testing.Platform).
- Development data: copy (never move) a `.boors-data` folder into `winui/.boors-data` (git-ignored) and point the
  apps at it with `--data` / `BOORS_DATA`. DuckDB allows one process per file: stop the desktop before running the
  node or the CLI on the same folder.
- UI changes: launch the desktop against the copied data and look at it (dark+fa, light+fa, dark+en). The
  `--lang/--theme/--page/--size/--scroll` switches pin an appearance for screenshots; drive it with UI Automation,
  not injected mouse/keyboard input. Screenshots go to `winui/.screenshots/` (git-ignored).
- Cap long commands (`timeout <s> …`) and stop any process you start.
