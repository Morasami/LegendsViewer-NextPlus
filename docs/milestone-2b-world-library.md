# Milestone 2B — Export Discovery and World Library

Implemented on `feat/world-library-discovery`, based on merged PR #3 (`8d132d3117eb94c9dead669b9a57e949752838c9`). Local main was fast-forwarded safely in the original checkout. The inherited TypeScript build-info change is excluded from delivery. Existing audit documents, installation selection and game/export files were preserved.

## Architecture and identity

`WorldLibraryService` is a singleton discovery/index service, separate from `IWorld` and `BookmarkService`. It stores filesystem discovery metadata, never the parsed historical model. The Vue/Pinia library groups snapshots by the service's world identifier and loads their exact main XML paths through the existing bookmark loading API and parser.

Companions use exact, case-sensitive standard suffixes: `-legends.xml`, `-legends_plus.xml`, `-world_history.txt`, `-world_sites_and_pops.txt`, `-world_map.csv`, and optional `-world_map.bmp`. Prefixes and directories must match exactly. Substring replacement and wildcard companion matching are avoided. Nonempty undated prefixes remain usable but are kept separate; malformed suffixes are ignored. Reliable dates use the five-digit game year and the game's 12 months of 28 days.

Snapshot IDs are SHA-256 hashes of the normalized directory and exact export prefix. Windows paths are case-normalized. World IDs include directory, region and a fingerprint of world names plus complete landmass metadata from a bounded extended-XML header. Only up to 64 KiB is read when an extended export is new or its size/modification time changes; DTDs and external entities are prohibited. Missing, truncated or ambiguous identity evidence keeps snapshots separate. Matching names alone never merge worlds, and identical filenames in different directories remain distinct. IDs are source-location identities; moving a file intentionally does not silently reassign its history.

Known bookmark metadata is reused only for an exact source path and timestamp. Bookmark IDs retain their legacy format unless different directories collide, in which case a directory hash distinguishes them. Existing bookmark persistence locking, detached copies and atomic replacement remain intact.

## Discovery roots and bounds

- Explicit additional export folders, persisted independently of the game installation.
- The Milestone 2A selected installation, including an explicitly selected unavailable installation so its problem is visible.
- Existing `exports`, `export`, `legends` and `user/exports` folders directly under that installation.
- A previous preferred export directory, when present, and directories referenced by imported bookmarks.

Roots are deduplicated. There is no recursive traversal of saves, mods, drives or arbitrary descendants. Drive-root scans and directory/file reparse points are rejected. Scanning checks at most 64 roots and 10,000 directory entries per root, with warnings when a limit is reached. Individual inaccessible roots/files produce diagnostics rather than terminating discovery. Cancellation does not publish a partial index. All operations on game/export roots are read-only.

Routine change detection uses file size and UTC modification time. Main XML contents are not read during discovery. A measured warm scan of this installation's three exports in one root took **88.3 ms**; this is a local observation, not a general performance guarantee.

## UI and diagnostics

Explore Worlds now opens with the World Library. Expandable world rows show names, region discriminator, alternative names, known thumbnails/dimensions, snapshot counts, latest dates and loaded/unavailable states. Unparsed exports have a placeholder. Each snapshot has an explicit Load/Reload action, Explore navigation and an expandable six-companion diagnostic list. Snapshots sort newest first when dates are reliable.

The export-directory dialog lists root sources and warnings, accepts absolute paths, browses server folders, and adds/removes additional directories. Rescan is explicit and also runs when the library screen opens. The original bookmark cards and manual XML selector remain available. Manual browsing now uses a query endpoint to preserve literal percent characters, Unicode and spaces, starts from the configured installation when no previous folder exists, and shows actionable access errors.

Main XML is required. Missing extended XML, history text, population text, CSV or BMP is optional and explained without marking the whole export invalid. CSV diagnostics explicitly state that installed valid graphics are also required. Existing Premium/BMP/generated-region rendering and site sprites remain in use.

Visual fixes include responsive bookmark images/actions, a bounded manual dialog, a narrow-screen navigation toggle, a header that avoids overlapping controls, and compact/scrollable narrow-map layer controls. Desktop map behavior is retained.

## Persistence, API and loading

- `%APPDATA%\LegendsViewer-NextPlus\settings.json`: backward-compatible version 1 settings with additive `ExportDirectories`; atomic compound updates preserve the installation fields.
- `%APPDATA%\LegendsViewer-NextPlus\world-library.json`: version 1 JSON containing snapshots, IDs, original directories/prefixes/dates, companion paths/access states/size/mtime, identity evidence, optional names/dimensions/thumbnails, last-seen timestamps and validation/failure status.
- Same-directory temporary writes are flushed before atomic publication. The previous valid generation is retained as `.bak`. Missing/corrupt/unsupported indexes recover a valid backup where possible or rebuild through discovery with a warning. Index reads have a 32 MiB safety limit. Disappeared exports remain in the index as unavailable.
- Scans do not rewrite `bookmarks.json`. User-requested successful world loads continue to update bookmarks normally, including adding region3 during browser acceptance.
- `GET /api/world-library`, `POST /api/world-library/refresh`, and `PUT /api/world-library/directories` provide the minimal library API. Mutations require `X-NextPlus-Settings: 1` and use the existing localhost host/origin/fetch-site middleware. Folder selection reuses `/api/game-settings/directories`; manual selection uses `/api/FileSystem/browse?path=...`.
- Existing loading endpoints are shared by manual, bookmark and library flows. A process-wide load gate rejects overlapping loads with HTTP 409. Success is shown only after backend parsing succeeds. Failures clear partial world/map state and retain diagnostic metadata. The existing parser is not cancellable mid-load; scan cancellation is supported and tested.

The generated OpenAPI TypeScript schema was regenerated with the existing npm script. No dependencies, database or separate parser were introduced.

Browser switching exposed an existing HistoryParser delimiter bug with living rulers whose birth date closes before the reign field. The parser now searches for the closing delimiter after the reign field. Two synthetic regression cases cover living and deceased rulers.

## Automated validation

| Check | Result |
| --- | --- |
| Focused discovery/bookmarks/settings/map compatibility | **78 passed, 0 failed** |
| Complete backend test project | **894 passed, 0 failed, 0 skipped** |
| Frontend Vitest | **43 passed, 0 failed** |
| .NET solution build | **Succeeded; final incremental build 0 warnings / 0 errors** |
| Frontend production build | **Succeeded** |
| Diff whitespace check | **Passed** |

The 26 new backend cases cover complete/partial sets, BMP, dates, ambiguous/different worlds, directory collisions, invalid names, Unicode/percent paths, missing/locked files, removed directories, settings/index restart persistence, backup/corrupt-index recovery, nonrecursive scans, prohibited XML entities/truncated headers, exact manual companion selection, parser failure, overlapping loads, scan cancellation and the history delimiter regression. Twelve new frontend cases cover grouping/order, diagnostics/error state, directory management, literal path browsing, selected-installation defaults, exact snapshot loading, success confirmation and network-failure cleanup. Fixtures require no installed game. No existing tests were weakened or removed.

Earlier rebuilds showed the three existing benchmark CS8632 nullable warnings; these files were not changed. The frontend still reports its existing Rollup chunk-size advisory. The project's npm-install target also reported the existing esbuild install-script advisory. No new dependency was added.

## Actual browser acceptance

Chrome was launched and driven using the existing local Playwright installation against **http://localhost:15422**, using the built production frontend. Screenshots were opened and inspected, including corrected captures after layout/animation issues were found. The development app needed normal user AppData access outside the workspace-only sandbox; the restricted launch correctly reported its inability to save the index.

Desktop viewport: **1440 × 1000**. Narrow viewport: **390 × 844**, including a touch-enabled Chromium context for compact map controls.

Verified automatic discovery of all three real installation exports; expansion and six-file diagnostics; actual folder browsing; adding an empty temporary export folder, preserving it across page reload, then removing it; rescanning; region2 loading; overview charts/populations; Premium map/site icons/zoom/warfare layer controls; explicit region2 → region3 → region2 switching; original manual region2 import; original bookmark Reload; and desktop/narrow navigation and layouts. The configured game installation remained unchanged, and temporary additional folders were removed from settings.

The main acceptance report records no page errors, console errors, failed requests or HTTP errors. Supplementary checks repeat manual import/reload and verify the final narrow map controls.

Local, ignored evidence (not committed; includes user/game imagery):

- `artifacts/browser-2b/01-library.png`
- `artifacts/browser-2b/02-expanded-snapshots.png`
- `artifacts/browser-2b/03-companion-status.png`
- `artifacts/browser-2b/04-directory-configuration.png`
- `artifacts/browser-2b/05-loaded-library.png`
- `artifacts/browser-2b/06-loaded-overview.png`
- `artifacts/browser-2b/07-premium-map.png`
- `artifacts/browser-2b/08-narrow-library.png`
- `artifacts/browser-2b/09-narrow-directories.png`
- `artifacts/browser-2b/10-manual-selector.png`
- `artifacts/browser-2b/11-narrow-map.png`
- `artifacts/browser-2b/12-overview-viewport.png`
- `artifacts/browser-2b/13-narrow-map-controls.png`
- `artifacts/browser-2b/browser-report.json`, `supplement-report.json`, `final-visual-report.json`

The reusable script is `scripts/browser-world-library.cjs`. Run it with the local app running, using `NEXTPLUS_PLAYWRIGHT_MODULE` to point to an existing Playwright package if it is not available through normal module resolution. `NEXTPLUS_BROWSER_OUTPUT` changes the ignored screenshot/report directory, `NEXTPLUS_CHROME_PATH` selects an installed Chrome executable, and optional `NEXTPLUS_TEST_EXPORT_DIRECTORY` enables add/remove persistence checks using an existing empty fixture folder. No global installation or permanent Playwright dependency is required.

## Limits and deferred scope

The real installation currently provides one date per export group; multiple-date grouping/chronological ordering were verified with synthetic backend/frontend fixtures, while browser switching used the real region2 and region3 snapshots. Ambiguous identities, including region aliases or incomplete headers, deliberately remain separate. Renamed/moved sources need the new location added and retain unavailable history at the old location. There is no filesystem watcher or automatic relocation/merge workflow.

Loading still performs the existing full parse into the single active world; there is no parsed-world cache, save/world.dat reader, SQLite layer, snapshot comparison, DFHack change, AI historian, relationship graph or portrait reconstruction. Those future requirements remain outside Milestone 2B.
