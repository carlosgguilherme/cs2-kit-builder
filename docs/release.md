# Releases

## Data releases (color index)

Workflow `.github/workflows/data-index.yml` runs weekly (Monday 06:17 UTC) and on demand. It builds
`color-index.json` with `/data-pipeline` and publishes it as an asset of a release tagged
`data-YYYYMMDD` (marked as pre-release so it never becomes the app's "latest" release).
The app finds the newest non-draft `data-*` release with a `color-index.json` asset through the public
GitHub REST API (`DataReleaseLocator`, no token, once per day, last known URL kept offline) and
downloads it with ETag revalidation. First release: `data-20261109` (2,126 skins, ~0.9 MB).

**Repository visibility:** the repository is public (since 2026-11-06), so release assets are
downloadable without a token (verified).

## Windows app (phase 4)

Velopack (`Setup.exe`, portable, deltas) published by `release-windows.yml` on `v*` tags. Without a
code-signing certificate, Windows SmartScreen shows a warning on install; the pipeline will accept an
optional certificate secret.

## Android app (phase 5)

Signed APK in the same GitHub release. Note from the scope (to be re-checked in phase 5): since
2026-09-30, certified Android devices in Brazil require apps installed outside the Play Store to be
registered by a verified developer; the plan is a limited-distribution (hobbyist) account in the
Android Developer Console and registering the package name.
