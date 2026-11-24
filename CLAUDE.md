# GymPro: context for Claude Code

Gym membership, check-in and payments desktop app for Windows. It's a rewrite of a legacy JASystem / DoctorGYM WPF +
Access/SQLite app. Owner: Ahmed Shehata. Repo: https://github.com/ahmedmshehata/GYM-Management-System (public, MIT).
User-facing docs: `README.md`. Releases and white-label how-to: `README_DEV.md`.

## Stack and layout

.NET 10 (`global.json`), C# latest, WPF, EF Core 10 + SQLite, CommunityToolkit.Mvvm, xUnit v3.
`Directory.Build.props` turns on `TreatWarningsAsErrors` and the analyzers: **builds must have 0 warnings**.
Package versions live only in `Directory.Packages.props` (Central Package Management).
`SQLitePCLRaw.lib.e_sqlite3` is pinned to 2.1.13 to fix GHSA-2m69-gcr7-jv3q.

| Project | Role |
|---|---|
| `src/GymPro.Core` | Entities, rules (`MembershipRules`: pause, no-expiry packs, check-in blocks), permissions, PBKDF2, options, theming (`ThemeCatalog`/`ThemeResolver`/`ColorMath`), white-label model (`Branding/BrandProfile`, `BrandPack`, `IcoFile`) |
| `src/GymPro.Data` | `GymDbContext` (append-only audit log in the same transaction, `Version` concurrency token), migrations, services, `LegacyImporter`, `PhotoMaintenanceService` |
| `src/GymPro.App` | The WPF app (`GymPro.exe`): MVVM, 8 themes via `Theming/Controls.xaml` + `ThemeManager`, webcam (`CameraWindow`, Windows.Media.Capture, TFM `net10.0-windows10.0.19041.0`) |
| `src/GymPro.Branding` | Old end-user branding tool (title/logo/colour). Kept as is |
| `src/GymPro.BrandStudio` | `BrandStudio.exe`: white-label configurator (brand packs, live preview, apply, build) |
| `tests/GymPro.Tests` | 172 tests: rules, audit, concurrency, import (synthetic `LegacyFixture`), themes WCAG, brand packs, guard test |

## Commands

```bash
dotnet build                     # must stay at 0 warnings
dotnet test tests/GymPro.Tests
pwsh tools/publish.ps1           # release zip in publish\ (self-contained single-file)
pwsh tools/publish.ps1 -Brand brands/sample   # white-label build (own exe name, file properties, icon)
dotnet ef migrations add <Name> -p src/GymPro.Data -s src/GymPro.Data -o Migrations
```

Releases are tag-driven: push a `v1.2.3` tag and CI builds and publishes a GitHub Release (see `README_DEV.md`).

## Rules the code relies on

- **No hard-coded product name in user-visible app text.** Use `Branding.Current.ProductName` / `ShortName`
  (`src/GymPro.App/Infrastructure/Branding.cs`) or `BrandDefaults` (Core). `WhiteLabelGuardTests` enforces this.
- Paths come from the brand: `Database:Path` uses `{DataFolder}`; logs and `ui.json` live under
  `%LOCALAPPDATA%\<DataFolderName>`. The default data folder is `GymPro`, so existing installs stay put.
- **Never start with an empty DB when data is elsewhere** (`DataServiceCollectionExtensions.FindDataLeftBehind`).
- The audit log is append-only (code check + SQLite triggers). Bulk/system writes set `db.AuditEnabled = false`
  and write one summary event.
- Theme colours are tokens resolved by `ThemeResolver`. Don't hard-code colours in XAML; a new palette must pass `ThemeTests`.
- The About → Licences notice must always stay (MIT requirement), even when "Powered by" is off.
- Branded builds use `--artifacts-path artifacts\brand-<exe>`, so they never touch the normal `obj`.

## Live data on the owner's PC (don't modify without asking)

- `C:\ProgramData\GymPro\gym.db`: the real database (~4,600 members), photos already optimised (~210 MB).
- `C:\ProgramData\GymPro\gym-backup-before-photo-optimize-*.db` (~980 MB): can be deleted once the owner is happy.
- For tests, copy with SQLite's backup API (consistent even if the app is open), never edit the live file.
- The admin password is known only to the owner. Ask for it when needed and never write it to a file.

## Windows tooling gotchas (learned the hard way)

- Git Bash heredocs containing non-ASCII characters (→, …, Arabic) can break bash parsing. Write larger edit scripts
  to a file in the scratchpad and run them with `python`.
- In Python string literals, `\a`, `\b` and similar become control characters in edited files (this broke a `.csproj`
  and `publish.ps1`). Use raw strings, or edit with the Edit tool.
- The Python on this PC is the Microsoft Store build: its writes under `%LOCALAPPDATA%` are silently redirected
  (sandboxed). Write such files with PowerShell instead.
- PowerShell variable names are case-insensitive (`$login` overwrites a `-Login` switch; `$t` shadows `$T`).
- To render SVG to PNG, use headless Edge (`msedge --headless=new --screenshot=... --force-device-scale-factor=N`).
  Playwright, cairosvg and rsvg are not installed.
- UI testing: drive the WPF apps with UI Automation from PowerShell (AutomationIds such as `UserNameBox`,
  `PasswordBox`, `OkButton`, `SearchBox`, `MemberGrid`, `ThemeButton`). Capture windows with `PrintWindow`.
  Win32 MessageBoxes aren't top-level to UIA; send `{ENTER}`.
- Drives are nearly full (C: ~98%, E: ~100% used). Clean `bin/obj/artifacts/publish` rather than copying them.

## Open items / ideas

- Memory use of the app with the member list open was ~420 MB, which seems high. It hasn't been profiled yet.
- Out of scope so far: UI translation/RTL, per-reseller code signing.
- `tools/CreateImg.py` is an untracked file left by the owner (PowerShell pasted into a .py). Don't commit it without asking.
