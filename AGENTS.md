# AGENTS.md

## Cursor Cloud specific instructions

### Overview

This is a .NET 8 WPF desktop application (Windows-only) — a modular plugin host shell. It targets `net8.0-windows` with `<UseWPF>true</UseWPF>` and `<EnableWindowsTargeting>true</EnableWindowsTargeting>`.

All projects use `EnableWindowsTargeting`, so **restore and build work on Linux** despite being a Windows app. The GUI (`dotnet run`) requires a Windows desktop with .NET 8 runtime.

### Build commands

See `README.md` for full details. Quick reference:

- **Restore**: `dotnet restore WpfPluginHost.sln`
- **Build (Debug)**: `dotnet build WpfPluginHost.sln -c Debug`
- **Lint/format check**: `dotnet format --verify-no-changes WpfPluginHost.sln`

### Key caveats

- **No automated test projects exist** in this solution. There are no xUnit/NUnit/MSTest references.
- **No CI/CD or git hooks** are configured.
- WPF GUI cannot be launched on Linux. Build and format checking are the primary verification steps on Linux.
- The `plugins/` directory at the repo root is generated during build (each plugin project copies its output there via MSBuild `CopyPluginToRuntimeFolder` target). It is not gitignored, so build artifacts may appear as untracked files.
- `Plugin.SampleA` depends on `Microsoft.Office.Interop.Excel` (COM) — requires Excel on Windows at runtime.
- `Plugin.PostgreCompare` depends on PostgreSQL at runtime (default: `localhost:5880`, user `cisdb_unisys`, db `cisdb`).
- `Plugin.PixelCompare` has NuGet vulnerability warnings for `SixLabors.ImageSharp 3.1.5` — these are pre-existing.
- Pre-existing whitespace formatting issues are flagged by `dotnet format --verify-no-changes`; these are in the upstream code.
