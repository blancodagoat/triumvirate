# Triumvirate

Launcher/front-desk GUI for the three sibling tools — Memento (`../blancoshot`),
DejaVu (`../dejavu`), Recite (`../recite`). Runs them as separate processes:
enable/disable, install/update from GitHub releases, and edits each tool's
`%APPDATA%\<Name>\config.json` directly. Early stage (0.1.x).

C# / .NET 10 WinForms, win-x64, zero NuGet dependencies. Reuses `Theme.cs` and
`DarkMenuRenderer.cs` copied from the siblings — port fixes across.

## Build

```
dotnet publish src/Triumvirate/Triumvirate.csproj -c Release -p:SelfContained=false -o publish/framework-dependent
dotnet publish src/Triumvirate/Triumvirate.csproj -c Release -o publish/self-contained
```

## Tests

No test framework. `tests/Triumvirate.Tests` is a plain exe with asserts; exit 0 = pass,
headless. Production files come in via explicit `<Compile Include>` in the test csproj.

```
dotnet run --project tests/Triumvirate.Tests
```

## Gotchas

- The config.json schemas of the three tools are a contract — Triumvirate reads/writes
  them, so any schema change here must match what the tools actually parse (and vice
  versa).
- Unlike the siblings, no scoop manifest and no winget step in CI yet.
- Icons are generated: `python3 tools/make-icons.py` rewrites `assets/` — never hand-edit.
