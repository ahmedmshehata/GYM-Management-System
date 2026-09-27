# GymPro: developer notes

How to publish a new version. For building, running and architecture, see [README.md](README.md).

## How releases work

Pushing a **tag** named `v<version>` (for example `v1.2.0`) makes the [CI workflow](.github/workflows/ci.yml) do three things:

1. Build and run all tests on a clean Windows machine.
2. Run `tools/publish.ps1 -Version <version>` to build the self-contained `GymPro-<version>-win-x64.zip` and its `.sha256` checksum.
3. Create a **GitHub Release** named `GymPro v<version>` with both files attached and auto-generated notes (the commits since the last release).

Normal pushes to `main` only build and test. Nothing is released until you push a tag.

The version number inside `GymPro.exe` comes from the tag. For local builds it comes from `<Version>` in `Directory.Build.props`, so keep the two the same.

## First downloadable release

The "download" badge in the README shows "no releases" until you publish one. Tag the current version and push the tag:

```bash
git tag -a v1.0.0 -m "GymPro 1.0.0"
```

```bash
git push origin v1.0.0
```

GitHub then builds `GymPro-1.0.0-win-x64.zip` with its checksum and publishes it on the [Releases](https://github.com/ahmedmshehata/GYM-Management-System/releases) page. The release step hadn't run yet when this was written (earlier runs skipped it), so check the first run under **Actions**.

## Releasing a new version

Run all commands from the repository root (`C:\UserData\Code\GYM\GymPro`), or add `-C <path>` to each `git` command.

1. **Pick the version number** ([semantic versioning](https://semver.org)):

   | Change | Example | Bump |
   |---|---|---|
   | Bug fixes only | 1.2.0 → 1.2.1 | patch |
   | New features, existing data still works | 1.2.1 → 1.3.0 | minor |
   | Breaking change (e.g. needs a manual data step) | 1.3.0 → 2.0.0 | major |
   | Test build for a few PCs | 1.3.0-beta.1 | pre-release |

2. **Update the version** in `Directory.Build.props`:

   ```xml
   <Version>1.3.0</Version>
   ```

3. **Check locally** that tests pass and the package builds:

   ```powershell
   pwsh tools/publish.ps1
   ```

4. **Commit and push** the version change:

   ```bash
   git commit -am "Release 1.3.0"
   ```

   ```bash
   git push
   ```

5. **Tag and push the tag.** This starts the release:

   ```bash
   git tag -a v1.3.0 -m "GymPro 1.3.0"
   ```

   ```bash
   git push origin v1.3.0
   ```

6. **Watch it** under the repo's **Actions** tab (about 5–10 minutes). When it's green, the new zip is on the **Releases** page. You can edit the release notes there to write them for gym staff: what's new and anything they need to do.

### Pre-releases

Tags with a suffix, such as `v1.3.0-beta.1` or `v2.0.0-rc.1`, are published as **pre-releases**. They're downloadable, but they don't become "Latest" and don't change the download badge. Use them to try a build on one front-desk PC first.

## Fixing a mistake

**Tagged the wrong commit, and the release hasn't been created yet** (or you've deleted it on GitHub):

```bash
git tag -d v1.3.0
```

```bash
git push origin :refs/tags/v1.3.0
```

Then tag the right commit and push the tag again.

**A release is already out and people may have downloaded it:** don't reuse the version number. Fix the problem and release `1.3.1`.

**Release job failed:** open the failed run under **Actions**. After fixing the cause, delete the tag as above and push it again. Or, if the code didn't need to change, use **Re-run jobs**.

## Checklist before tagging

- [ ] `pwsh tools/publish.ps1` succeeds locally (all tests pass)
- [ ] `<Version>` in `Directory.Build.props` matches the tag
- [ ] New database changes have an EF migration committed (`src/GymPro.Data/Migrations`)
- [ ] The README is updated for new features
