# Architecture

```
/windows          .NET 10 solution (KitBuilder.slnx)
  src/KitBuilder.Core    domain, OKLab color math, kit engine — no UI, no I/O
  src/KitBuilder.Data    catalog, color index, Steam prices, inventory, SQLite cache, DI
  tests/…                xUnit; shared fixtures copied from /shared/test-fixtures
  tools/EndpointProbe    real-call verification of every endpoint (run from a home connection)
  tools/Cli              developer CLI: price refresh and kit building in the terminal
  (src/KitBuilder.App    WinUI 3 app — phase 3)
/android          Kotlin/Compose port — phase 5
/data-pipeline    color index generator (Python, OKLab k-means)
/shared           design tokens, test fixtures, legal texts
/docs             this folder
```

## Data flow

1. `CatalogService` downloads ByMykel `skins_not_grouped.json` (ETag, at most daily) and the
   `color-index.json` asset of the newest `data-*` release (located by `DataReleaseLocator`);
   both work offline from cache.
2. `SteamMarketPriceProvider` keeps a SQLite price table fresh in the background (paginated search,
   resumable) and fetches single items on demand (`priceoverview`). All Steam traffic goes through
   `SteamThrottlingHandler` (serialized, ≥ 3.5 s apart, backoff on 429).
3. `SteamInventoryClient` loads a public inventory (paginated, 10-minute limit per profile, cached).
4. `CandidateFactory` joins catalog + prices + colors (+ owned items) into `KitCandidate`s.
5. `KitEngine.Build` runs off the UI thread (`Task.Run`) with a `CancellationToken`.

Prices are integer cents of the display currency (BRL). Infeasibility is returned as codes
(`InfeasibilityReason`, `Suggestion`) so the UI composes localized text (pt-BR now, en later).

## Privacy and limits

No credentials, cookies or tokens are ever requested or stored. Only public endpoints are called.
No telemetry. Caches live in `%LOCALAPPDATA%\KitBuilder`.
