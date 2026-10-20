# Milestone 1.5 upstream integration

Pre-merge review (2026-10-21): origin/main 6ce4b42 includes Milestone 1 1f9cd0f.
Upstream main is 6045eb0; develop is 6b7c1ce. Integrate 1fdd432, b2ab9f3,
87fe136 (PR #73 merge), 3372cdc and 6b7c1ce using a merge preserving ancestry.

Resolution approach: keep the NextPlus BookmarkService implementation, including
its AppData namespace, process-wide gate, input/output copies and atomic file
replacement. Apply upstream template-first deletion lookup inside that gate;
retain empty-timestamp rejection and existing missing-file behavior. Accept the
controller catch-all routes and frontend deletion handling from upstream.

Compatibility review identified removal of BMP loading, invisible site markers
without game graphics, forced UTF-8 replacing legacy decoding, and a test that
requires an installed game. Restore small fallbacks and use temporary synthetic
assets / injected file resolution for portable map regression tests. Do not add
game discovery features or bundle game files. Original checkout and audit docs
remain untouched in their existing branch.

## Final integration and verification

One merge conflict occurred in BookmarkService.cs. Retained Milestone 1 and
added upstream template-first deletion inside SyncRoot. Regression coverage
includes two worlds sharing a timestamp with both present and missing exports.
The default remains %APPDATA%/LegendsViewer-NextPlus/bookmarks.json; process-wide
synchronization, defensive copies and atomic replacement are preserved.

Compatibility changes beyond upstream:
- WorldMapImageGenerator.cs: injectable graphics lookup, Premium -> BMP ->
  generated-region fallback, legacy direct BMP input, diagnostics for invalid
  optional maps, cache clearing and bitmap disposal.
- Map.vue: visible interactive site-marker fallback if game graphics are absent.
- ExportEncoding.cs, XMLParser.cs, HistoryParser.cs, SitesAndPopsParser.cs:
  accept Premium UTF-8 and retain legacy CP437 XML / Windows-1252 text decoding.
  UTF-8 detection streams through the file once before parsing; it does not
  allocate the entire export in memory, but adds one sequential read.
- PremiumWorldMapTests.cs: replace installation-dependent assertion with
  controlled temporary directories. MapCompatibilityTests.cs uses synthetic
  sprite sheets and an injected resolver, requiring no installed game.
- BookmarkServiceTests.cs, ExportEncodingTests.cs, PopulationChartTests.cs,
  SitesAndPopulationsParserTests.cs and mapCompatibility.test.ts add regressions.

Commands and results:
- dotnet build LegendsViewer.sln: passed. Final incremental build: 0 warnings,
  0 errors. Initial fresh build: three existing CS8632 nullable-context warnings
  in LegendsViewer.Backend.Benchmarks/XmlParser.Benchmarks.cs (lines 12, 14, 15).
  An intermediate overlapping test run caused one transient MSB3026 copy retry;
  the sequential final build completed without that warning.
- dotnet test LegendsViewer.Backend.Tests/LegendsViewer.Backend.Tests.csproj
  --no-build --no-restore: 855 passed, 0 failed, 0 skipped.
- Focused filter FullyQualifiedName~Map|FullyQualifiedName~Parser|
  FullyQualifiedName~Population|FullyQualifiedName~Battle: 48/48 passed.
- Filter FullyQualifiedName~BookmarkServiceTests, repeated five times: each
  33/33 passed (includes concurrency, snapshot isolation and persistence tests).
- npm run test:run: 24/24 passed in two files.
- Vite warns about a chunk larger than 500 kB. npm reports 19 dependency
  vulnerabilities (2 moderate, 17 high), plus glob deprecation and esbuild
  install-script policy notices. Dependency upgrades are outside this merge.
- git diff --check: passed.

A temporary console harness loaded the read-only local region2-00184-01-01
export through BookmarkController.ParseWorldXml with an isolated temporary
bookmark file. The harness used DF_INSTALL_DIR for this local validation only;
no game assets or exports were modified or committed. Results:
- World Anethadefini Cadeni (accented i in the export), 65 x 65.
- 799 sites and 799 marker DTOs; 752 markers contain population data.
- 127 war overlays, 415 battle markers; 11,459 attacking race-squad entries.
- Civilized population 165,596; 752 populated site charts and 447 entity charts.
- Premium PNG 650 x 650, 258,690 bytes; no Premium fallback diagnostics.
- Injected missing-graphics resolver produced a distinct generated-region PNG,
  9,159 bytes, without renaming or modifying any game inputs.
- Focused-site map decoded successfully; local game icon endpoint resolved.
- Bookmark reloaded from the temporary JSON file successfully.

This is automated model/controller/rendering verification, not a browser visual
sign-off. Live Leaflet zoom, popup behavior, overlay toggles and chart appearance
still require manual visual testing. region3 was available but was not loaded.
Optional missing/malformed CSV, missing graphics, real synthetic BMP decoding
and fallback-marker visibility are covered by portable regression fixtures.

No copyrighted game assets are bundled. The original checkout remains on
fix/bookmark-persistence with its pre-existing docs/ and tsconfig build cache.
Only integration changes and this record are included; no later milestone work.
