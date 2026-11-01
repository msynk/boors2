# Boors

Boors is an analysis workstation for the Iranian stock market (TSE/IFB). It collects market data, disclosures and
macro data, versions them in a raw cache, and shows them through dashboards, a market watch with TSETMC-compatible
filters, symbol workspaces, screeners and a data-health page.

[`winui/`](winui/)
C# / .NET 10, WinUI 3 (Windows App SDK), ASP.NET Core minimal API, DuckDB.
A native Windows desktop app with the market-data library in-process, plus a lean node server.

Run the desktop app: double-click [`run-desktop.bat`](run-desktop.bat) (needs the .NET 10 SDK). It builds the app in
Release and starts it; arguments go to the app, e.g. `run-desktop.bat --lang en --page reports`.

[`docs/development-plan.md`](docs/development-plan.md): the product plan, data sources, data model, analytics and roadmap. Its stack sections describe the `boilerplate/` implementation.

[`reports/`](reports/) and [`research_notes/`](research_notes/): the feasibility research.
