# Prompt: white-label configurator for GymPro

> Paste everything below the line into Claude Code (or another coding agent) opened at the repository root.
> It describes the current code precisely, so the agent can start without rediscovering it.

---

## Role and goal

You are a senior .NET / WPF engineer working in this repository (GymPro: a .NET 10 WPF + SQLite gym management app,
`GymPro.slnx`). Turn the existing branding tool (`src/GymPro.Branding`) into a **white-label configurator**. A reseller
must be able to ship this app as **their own product**: their name, logo, icon, colours, support details, data folder
and `.exe`. An end user should see **no trace of "GymPro"** except the legally required licence notice.

Work in small, reviewable steps. After each step, `dotnet build` (0 warnings, because `TreatWarningsAsErrors` is on)
and `dotnet test` must pass. Ask me before making any change that would break existing installations.

## What exists today (read these first)

| Area | File | Notes |
|---|---|---|
| Branding settings | `src/GymPro.Core/Configuration/AppOptions.cs` → `BrandingOptions` | `AppTitle`, `LogoPath`, `LogoBase64`, `AccentColor`, `DefaultTheme`; read from `appsettings.json` → `"Branding"` |
| Writing settings | `src/GymPro.Core/Configuration/BrandingWriter.cs` | Validates, merges only the `Branding` section, keeps `appsettings.json.bak`, atomic write. Covered by `tests/GymPro.Tests/BrandingWriterTests.cs` |
| Runtime branding | `src/GymPro.App/Infrastructure/Branding.cs` | Resolves title, logo and `PlaceholderLogo` (falls back to `Assets/app-logo.png`) |
| Themes | `src/GymPro.Core/Theming/*` | 8 themes. `ThemeResolver.Resolve(theme, brandAccent)` derives every colour. `ThemeTests` enforce WCAG AA for every theme × brand colour |
| Current tool | `src/GymPro.Branding/MainWindow.xaml(.cs)` | Title, accent, logo (PNG, max 512 px), copy-or-embed, default theme, target `appsettings.json`, live login preview |
| Icon | `tools/make-icon.ps1`, `src/GymPro.App/Assets/GymPro.ico` | `ApplicationIcon` is compiled into the exe |
| Release | `tools/publish.ps1`, `.github/workflows/ci.yml`, `README_DEV.md` | Self-contained single-file `GymPro.exe` + `GymPro.Branding.exe` + `appsettings.json`, zipped |
| Product metadata | `Directory.Build.props` | `Product`, `Company`, `Authors`, `Copyright`, `Description`, `Version` (shown in exe Properties) |

**User-visible "GymPro" that must become brandable** (find any others with
`grep -rn '"[^"]*GymPro' src --include=*.cs --include=*.xaml`, excluding namespaces):

- `App.xaml.cs`: start-up failure dialog text and caption, unhandled-error caption, log folder `%LOCALAPPDATA%\GymPro\logs`
- `Infrastructure/UiHelpers.cs`: `Dialogs.Info` caption
- `Infrastructure/Branding.cs`: fallback title `"GymPro"`
- `Theming/ThemeManager.cs`: `%LOCALAPPDATA%\GymPro\ui.json`
- `ViewModels/AdminViewModels.cs`: "Other PCs should close GymPro…" (photo optimize confirmation)
- `Views/ImportView.xaml`: "Back up the GymPro database first."
- `Core/Configuration/AppOptions.cs`: default DB path `%ProgramData%\GymPro\gym.db`, default `AppTitle`
- `PhotoMaintenanceService`: backup file name `gym-backup-before-photo-optimize-*.db` (fine), audit text
- The branding tool's own window title and labels ("GymPro Branding Tool", "GymPro appsettings.json", "Restart GymPro…")
- Build output: `GymPro.exe`, file properties, embedded icon, zip name `GymPro-<ver>-win-x64.zip`

Keep **code identifiers** (namespaces, project names, `GymPro.*` assemblies) unchanged. White-labelling is about what
users see, not a rename of the codebase.

## Requirements

### 1. Brand profile (single source of truth): `GymPro.Core`

Add `BrandProfile` (plus a validator and JSON (de)serialiser) with `"schemaVersion": 1`:

| Field | Purpose | Rules |
|---|---|---|
| `productName` | Shown everywhere ("Iron Club Manager") | required, ≤ 60 chars |
| `shortName` | Window captions, dialogs ("Iron Club") | required, ≤ 24 chars |
| `companyName`, `copyright`, `description` | Exe file properties, About | optional |
| `websiteUrl`, `supportEmail`, `supportPhone` | About and error dialogs ("Contact support") | URL/email validated |
| `logo` | PNG/JPG, normalised ≤ 512 px | stored as a file in the brand pack |
| `appIcon` | `.ico` (16–256 px) | if missing, **generate** it from the logo, rendered per size like `tools/make-icon.ps1` |
| `accentColor`, `defaultTheme`, `allowedThemes` | Colours. `allowedThemes` limits the theme picker (empty = all) | hex colour; theme ids from `ThemeCatalog` |
| `dataFolderName` | `%ProgramData%\<x>` and `%LOCALAPPDATA%\<x>` | safe folder name; **default `"GymPro"`** so existing installs keep working |
| `exeName` | Output file name without `.exe` | safe file name |
| `loginWelcome` | Optional subtitle on the login screen | ≤ 80 chars |
| `showPoweredBy` | "Powered by GymPro" line in About | default `true` |

`BrandingOptions` in `appsettings.json` stays **backward compatible**: an existing file with only
`AppTitle/LogoPath/LogoBase64/AccentColor/DefaultTheme` must load exactly as today. Map old fields to the new model.
Don't break `BrandingWriterTests`.

**Brand pack** = a folder or `.brandpack` zip containing `brand.json`, `logo.png` and `app.ico`. It must be
diffable and committable, e.g. `brands/iron-club/`.

### 2. Runtime white-labelling (no rebuild): `GymPro.App`

- Introduce an `IBrandInfo` service (singleton, built from options + pack) and replace **every** user-visible "GymPro"
  listed above: window titles, all `MessageBox` captions, dialog texts, toasts, the import/maintenance page, log and
  `ui.json` folders, default DB folder.
- **Never move existing data automatically.** If `dataFolderName` changes and the old folder has a database, show a
  clear message explaining what to do instead of silently creating an empty database.
- Window/taskbar icon at runtime: use the brand icon for all windows (the exe's embedded icon is the build-time part).
- Add an **About** dialog (header menu): product name, version, company, website, support email/phone,
  and **Licences**: the MIT notice for GymPro plus third-party notices. This is **always shown**, even when
  `showPoweredBy` is false, because the MIT licence requires keeping the copyright notice.
- The theme picker only lists `allowedThemes`. If the saved user theme isn't allowed, fall back to `defaultTheme`.

### 3. Build-time white-labelling: `tools/publish.ps1`

- New parameter `-Brand <path to brand pack>`. It must:
  - set `AssemblyName` (exe name), `Product`, `Company`, `Copyright`, `Description` and `ApplicationIcon` via MSBuild
    properties (`-p:`). Don't edit project files on disk.
  - write the brand's `Branding` section and assets into the output `appsettings.json` / `Branding\` folder
  - name the zip `<exeName>-<version>-<rid>.zip` (+ `.sha256`)
  - **not** ship the configurator inside a branded package unless `-IncludeConfigurator` is passed
- Without `-Brand`, output stays byte-for-byte compatible with today's `GymPro` package.
- Document it in `README_DEV.md` ("Building a branded package").

### 4. The configurator app (evolve `src/GymPro.Branding`)

Rename the *window* to a neutral **"Brand Studio"**. Keep the project and assembly name unless I agree otherwise.
Tabs:

1. **Identity**: names, company, website, support, login welcome, "Powered by" toggle
2. **Logo & icon**: pick logo; preview the generated icon at 16/24/32/48/256 on light and dark backgrounds,
   with a warning if it's illegible at 16 px; or import an existing `.ico`
3. **Colours & themes**: accent picker, default theme, allowed themes. Live preview of the login screen and a
   mini main window using the **real** `ThemeResolver` tokens. Show WCAG contrast results (reuse `ColorMath`), and
   explain how the app auto-corrects a pale brand colour (e.g. it darkens focus rings).
4. **Data & files**: data folder name, exe name. Warn when changing either would affect an existing installation.
5. **Output**:
   - *Apply to an installed app*: merge into its `appsettings.json` with a `.bak` (existing `BrandingWriter` behaviour)
   - *Save / open brand pack*: folder or `.brandpack`
   - *Build branded package*: run `tools/publish.ps1 -Brand …` with live log output; only shown when started from a
     source checkout (the .NET SDK is present)

UX rules: the same validation as the Core validator, inline errors next to fields, nothing written until the user
presses an explicit button, and every write keeps a backup.

### 5. Tests (xUnit v3, in `tests/GymPro.Tests`)

- `BrandProfile` validation (each rule), JSON round-trip, old-`appsettings.json` compatibility
- Brand pack save/load (folder and zip), icon generation produces every size
- **Guard test**: no user-visible literal `"GymPro"` remains in `src/GymPro.App` XAML/C# string literals (allow-list
  namespaces, pack URIs and the licence text), so new hard-coded names fail CI
- `ThemeTests` keep passing for every allowed-themes subset
- `publish.ps1 -Brand` smoke test, if practical, as a separate script step in CI rather than a unit test

## Constraints

- .NET 10, C# latest, nullable on, warnings as errors, Central Package Management. **No paid or copyleft
  dependencies**: plain WPF, `System.Text.Json`, `System.IO.Compression` and WPF imaging are enough.
- Keep MVVM style (CommunityToolkit.Mvvm) and the existing token-based theming. Don't hard-code colours.
- Don't commit customer logos or data. Put a sample brand under `brands/sample/` with a generated placeholder logo.
- Windows code signing per brand is **out of scope**. Note in `README_DEV.md` that each reseller needs their own
  certificate.
- Translating the UI into other languages is **out of scope**. Keep all text in one place so it's easy to add later.

## Definition of done

1. Building with `-Brand brands/sample` produces `<exeName>.exe` with the sample icon and file properties, and
   no "GymPro" visible in any window, dialog, folder name or file property (except the About → Licences notice).
2. Building without `-Brand` produces today's package, and an existing installation upgrades with no visible change.
3. Brand Studio can create a brand pack from scratch, preview it, apply it to an installed app and build a package.
4. `dotnet build` shows 0 warnings, all tests pass (old and new), and CI is green.
5. `README.md` (short section) and `README_DEV.md` (full how-to) are updated, and the work lands as small commits with
   clear messages.

Start by listing your plan (steps + files you'll touch) and any assumptions you need me to confirm. Wait for my
go-ahead before writing code.
