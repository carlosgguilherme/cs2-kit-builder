# Synthetic samples

These files follow the documented/observed response formats but were **not**
recorded from Steam (Steam blocked the cloud machine where they were written).
Replace them with real recordings from `windows/tools/KitBuilder.EndpointProbe`
and keep the tests passing (see docs/data-sources.md).

`github-releases-mixed.json` is derived from the real `recorded/github-releases.2026-11-12.json` (same fields) with extra releases: an app release, a draft, an older data release and one without the color index.
