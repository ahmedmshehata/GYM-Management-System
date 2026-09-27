# GymPro

A rewrite of the legacy JASystem / DoctorGYM desktop app: members, plans, subscriptions and check-in, with
**multiple user accounts**, an **append-only audit log**, and an **importer for the old `db_at.db` schema**.
There is also a separate **branding tool** that sets the app's title, logo and colour.

Stack: .NET 10 · WPF · EF Core 10 · SQLite · CommunityToolkit.Mvvm. There are no commercial dependencies (the old app used DevExpress 16.1).

## Solution layout

| Project | What it is |
|---|---|
| `src/GymPro.Core` | Entities, business rules (`MembershipRules`), roles/permissions, PBKDF2 password hashing, options, `BrandingWriter`, theme catalog + WCAG colour maths (`Theming/`) |
| `src/GymPro.Data` | `GymDbContext` (audit + concurrency), migrations, services, `LegacyImporter` |
| `src/GymPro.App` | The WPF app (`GymPro.exe`). `Theming/Controls.xaml` + `ThemeManager` provide the themes |
| `src/GymPro.Branding` | WPF tool: logo + title + colour → `appsettings.json` |
| `tests/GymPro.Tests` | xUnit v3: rules, audit, multi-user conflicts, lockout, import of the real legacy DBs, branding merge, theme contrast (WCAG AA) |

## Build and run

```bash
dotnet build
dotnet test
dotnet run --project src/GymPro.App
```

On first run the app creates the database, applies migrations and asks you to create the **administrator**.
There is no default password.

## Configuration: `appsettings.json` (next to `GymPro.exe`)

```json
{
  "Branding": { "AppTitle": "GymPro", "LogoPath": null, "LogoBase64": null, "AccentColor": "#1E88E5", "DefaultTheme": "fluent-light" },
  "Database": { "Path": "%ProgramData%\\GymPro\\gym.db", "BusyTimeoutSeconds": 10, "JournalMode": "WAL" },
  "Security": { "MinPasswordLength": 8, "MaxFailedLogins": 5, "LockoutMinutes": 15 }
}
```

* `Database:Path` expands environment variables. Relative paths are resolved from the app folder. The default
  `%ProgramData%` location is shared by every Windows account on the PC.
* `Branding` is normally written by the branding tool (below). `LogoBase64` takes precedence over `LogoPath`.

## Themes and UI

Pick a theme from **Theme** in the header. It applies instantly (no restart), is saved to **your user account** and follows
you to any PC. The login screen uses the last theme used on that PC. `Branding:DefaultTheme` is the starting theme.

| Id | Theme | Character |
|---|---|---|
| `fluent-light` / `fluent-dark` | Modern (Fluent) | Windows 11 look: soft corners, subtle depth |
| `office-colorful` / `office-black` | Office | Square, compact, coloured title band |
| `material-light` / `material-dark` | Material Design | Filled inputs with underline, elevation, roomier spacing |
| `pro-blue` | Pro Blue (DevExpress-style) | Navy chrome, dense grids for data-heavy work |
| `high-contrast` | Accessibility | Black/white/yellow, thick borders; ignores the brand colour |

* **Brand colour**: "Use the gym's brand colour" swaps each theme's accent for `Branding:AccentColor`. Button text colour is
  picked automatically (black or white). If the brand colour is too pale to see, focus rings and indicators are darkened
  just enough to stay visible.
* **Accessibility is tested**: `ThemeTests` checks every theme with 6 brand colours (including pale yellow, near-black
  and neon green) for WCAG AA. That covers text 4.5:1, status badges 4.5:1, focus rings and input borders 3:1.
* The Windows title bar follows the theme: dark mode, and on Windows 11 the caption colour too.
* No commercial UI library. The "DevExpress-style" and "Office" themes are look-alikes built on plain WPF.

**Front-desk UX**

* A navigation rail replaces the tabs, and it collapses to icons (☰).
* **Check-in**:
  * Today's numbers are shown at the top: check-ins, active members, expiring within 7 days, balances due.
  * Type a card number and press Enter: one match opens the member directly, then Ctrl+Enter checks them in.
  * Status badges: green Active, amber Expiring soon (≤ 7 days or ≤ 2 sessions), red Expired / No sessions / Cancelled, blue Checked in today.
  * A "Balance due" badge on the member; initials avatar when there's no photo.
* Success messages appear as toasts at the bottom right. Only errors and confirmations still use dialogs.
* Keyboard shortcuts:
  * `F2` goes to check-in.
  * `Ctrl+1…6` switch pages.
  * `Ctrl+N` / `Ctrl+S` add and save members.
* Empty states explain what to do next.

## Members, photos and plans

* **Member list**: loads 100 at a time and fetches the next page as you scroll to the bottom ("Showing 100 of 4,582 ·
  scroll for more"). Every member is reachable without loading thousands of rows at once. Searching by a number puts
  that exact card number first.
* **List or photo cards**: toggle at the top of the list (remembered per PC). Both use small thumbnails (160 px),
  never the full photo. Thumbnails for imported or older photos are created in the background after sign-in.
* **Photos**: *Camera…* captures from a webcam, with a camera picker when several are attached (Windows camera API,
  no extra library). You can also use *From file…*. Photos are saved as a 600 px JPEG. Members without a photo show the
  gym logo (`Branding`), or the GymPro logo.
* **Optimize photos** (Admin, *Import & maintenance*): shrinks all stored photos to 600 px, creates thumbnails and
  compacts the database. A full backup (`gym-backup-before-photo-optimize-*.db`) is written next to the database
  first. On the real imported data this took 51 s: 4,582 photos, **941 MB → 202 MB**, all record counts unchanged.
* **Pausable plans**: tick *Can be paused* and set the max days. On a member's subscription, *Pause* stops check-ins
  and freezes the remaining days. *Resume* moves the end date later by the paused days. A pause that runs past the
  allowance ends by itself, so a forgotten resume can't freeze a membership forever.
* **Session packs**: *Expire after the valid period* (the old behaviour) or untick it for packs that never expire and
  just count down per session across any months ("No expiry").

## Application icon

`src/GymPro.App/Assets/GymPro.ico` (16–256 px) and `app-logo.png` are generated by `tools/make-icon.ps1`: a tilted
white dumbbell with orange plates on a deep-blue tile. It's tilted because an upright dumbbell reads as the letter
"H" at 16 px. Preview: `docs/icon-preview.png`.

## Multi-user

| Role | Can |
|---|---|
| Reception | Check-in, add/edit members, sell subscriptions, take payments |
| Manager | + manage plans, cancel subscriptions, view the audit log |
| Admin | + manage users, import legacy data |

* Passwords use PBKDF2-SHA256 (210k iterations) with a per-user salt. The account locks after
  `MaxFailedLogins` failures for `LockoutMinutes` minutes. Users an admin creates or resets must change their password at next sign-in.
* The last active admin can't be demoted or deactivated, and you can't deactivate yourself.
* **Concurrent editing**: every row has a `Version` concurrency token. If two users edit the same member, the second
  save fails with "another user changed this record" instead of silently overwriting.
* **Races**: unique indexes back the rules, so they hold even when two PCs act at the same instant. One check-in per subscription per day. Member numbers are unique and retried automatically.
* **Several PCs**: SQLite is a file database. It works well on one PC with many Windows users. For several PCs over a
  network share, set `"JournalMode": "DELETE"` (WAL is unsafe on network shares) and keep it to a handful of
  front-desk PCs. If you outgrow that, the next step is a small server (ASP.NET Core API + PostgreSQL); the services in
  `GymPro.Data` are the seam for that move.

## Audit log

* Every insert, update and delete through EF Core writes an `AuditLogs` row in the **same transaction**: time (UTC),
  user, PC name, record type and id, and JSON details (`{"Phone":{"old":"0100","new":"0111"}}`). If the change
  rolls back, so does its audit entry.
* Login, failed login (including lockout), logout, password change and import are logged as events.
* Photo blobs and password hashes are never written to the log; they are marked `[NotAudited]` and logged as `***` / `changed`.
* **Append-only**: code that modifies an audit row throws, and SQLite triggers reject `UPDATE` / `DELETE` on
  `AuditLogs` even from raw SQL.
* Managers and admins browse it in the **Audit log** tab, filtered by date, user, record type, action or text.

## Importing the old database

Open **Import legacy data** (Admin) and pick the old `db_at.db`. The old file is opened **read-only**.

| Legacy | New |
|---|---|
| `roles` (`role_type` M/C) | `Plans`. `C` becomes a session pack (`role_count` sessions; the old code hardcoded 12), `M` becomes time-based (`role_count` months) |
| `members` | `Members`. The old `ID` is kept as the card number when it's free. `Hieght`/`Wieght` map to height and weight (the old app showed height as weight) |
| `members.ImagePath` | `MemberPhotos`. Handles both Base64-in-DB (JASystem_ImageToDB) and file paths; looks next to the .db and in `Images\` |
| `subscribe` | `Subscriptions`. Price = `subscribe_pay` + `subscribe_rmind` (paid + remaining) |
| `Attendance` | `Attendances`. Duplicate same-day check-ins are skipped |

* **Re-runnable**: rows are matched by `LegacyId`, so importing twice adds nothing.
* **Skips are explained**: the report lists every reason with a count. On the real data all 2,184 skips were
  *same-day duplicate check-ins*: the old app never blocked double clicks, and it stored only the date, so they can't be separate visits.
* Everything runs in one transaction. Row-level auditing is turned off during the import, and one `Import` audit entry records the
  source path, its SHA-256 and the summary.
* Access `.accdb` files are **not** read directly (that needs the ACE OLEDB driver). Use the SQLite `db_at.db`
  from the same install.

## Branding tool (`GymPro.Branding.exe`)

1. Enter the **title** (Arabic works) and **accent colour**, then pick a **logo** (PNG/JPG/ICO/BMP). The logo is
   scaled to fit 512 px and saved as PNG, which keeps transparency.
2. Choose how to store the logo:
   * **Copy file**: writes `Branding\logo.png` next to the app and sets `"LogoPath": "Branding/logo.png"`
   * **Embed**: puts it in `"LogoBase64"`, so appsettings.json is self-contained
3. Pick the target `appsettings.json`. When run from the repo it finds `src/GymPro.App/appsettings.json`
   automatically. Press **Apply to app**.

It shows a live preview of the login screen and the exact JSON. Only the `Branding` section is replaced; the rest of the file is kept,
and the previous file is kept as `appsettings.json.bak`. Restart GymPro to see the change.

## Release package

```powershell
pwsh tools/publish.ps1                       # version from Directory.Build.props
pwsh tools/publish.ps1 -Version 1.1.0        # stamp a specific version into the exe and zip name
pwsh tools/publish.ps1 -FrameworkDependent   # much smaller, but PCs need the .NET 10 Desktop Runtime
pwsh tools/publish.ps1 -SkipTests
```

It runs the tests, then publishes both programs self-contained and single-file for `win-x64`, and zips the result:

| Output (`publish\`) | |
|---|---|
| `GymPro\GymPro.exe` | The gym app (~72 MB, no .NET install needed) |
| `GymPro\GymPro.Branding.exe` | Branding tool (~62 MB) |
| `GymProppsettings.json` | Settings; keep it next to `GymPro.exe` |
| `GymPro-<version>-win-x64.zip` + `.sha256` | What you copy to the gym PCs |

Install: unzip anywhere (e.g. `C:\GymPro`) and run `GymPro.exe`. It needs Windows 10 1809+ or Windows 11, 64-bit. Database
migrations run automatically on first start.

## Schema changes

```bash
dotnet ef migrations add <Name> -p src/GymPro.Data -s src/GymPro.Data -o Migrations
```

Migrations are applied automatically at app start.
