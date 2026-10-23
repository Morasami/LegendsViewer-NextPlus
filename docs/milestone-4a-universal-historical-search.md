# Milestone 4A: universal historical search

Universal Historical Search is a deterministic, fully offline search over the currently published world. It does not use an AI model, an API key, embeddings, an external search server, or a second XML parse. The implementation derives one immutable in-memory index from the resolved historical graph and publishes it as part of the same world generation used by the existing request-lease architecture.

## Architecture

`WorldSessionStore` owns a `Generation` containing the `World`, map, search index and a monotonically increasing generation ID. `WorldSessionStore.Publish` builds the index from that exact world before swapping the active generation. Requests lease the world, map and index together. An old lease therefore keeps a complete, consistent old generation while a replacement index is being built or after a new world has been published.

`HistoricalSearchIndex` contains:

- a compact document table for figures, entities, sites, geographic places, artifacts, wars, battles, other event collections, individual events and creature definitions;
- token postings for exact and token-prefix matching;
- normalized names for bounded infix/partial-name matching;
- deterministic category, year and pagination filters;
- a ranking function with stable tie-breakers.

Named records keep their display name, canonical type, a short factual description, an optional year/date label, a stable key and an existing detail URL. Individual events do not retain their rendered prose in the index. Event rendering is used once during index construction to derive search tokens, and only the bounded result page is rendered again for display. This avoids storing hundreds of thousands of duplicate event previews.

Event indexing is split into up to six independently built chunks for large worlds and merged in original event order. The merge preserves deterministic ordinals and ranking ties while reducing initial build time on multi-core systems.

## Searchable records

| API category | Records | Detail destination |
| --- | --- | --- |
| `Figure` | Historical figures, including named legendary creatures | `/hf/{id}` |
| `Entity` | Civilizations and other historical entities | `/entity/{id}` |
| `Site` | Settlements and sites | `/site/{id}` |
| `Place` | Regions, underground regions, landmasses, rivers, mountain peaks, structures, constructions | Existing per-type page |
| `Artifact` | Artifacts | `/artifact/{id}` |
| `War` | Wars | `/war/{id}` |
| `Battle` | Battles | `/battle/{id}` |
| `EventCollection` | Duels, raids, conquerings, insurrections, persecutions, purges, coups, rampages, abductions, thefts, processions, performances, journeys, competitions, ceremonies, occasions | Existing per-type page |
| `Event` | Individual historical events | No dedicated event page; opens the parent chronicle where one exists, otherwise clearly marked non-navigable |
| `Creature` | Creature definitions with reliable IDs and names | No dedicated page; clearly marked non-navigable |

Records with blank names are omitted rather than inventing titles. A site or entity's "first recorded" year comes from existing resolved owner/site history. Figure years use birth year, falling back to death year. Artifact years use the resolved creation event. Records without a reliable year are excluded when a year filter is active.

## Query behavior

- Case-insensitive and accent-folded Unicode matching.
- Whitespace and punctuation are normalized to separators, so `Urist-McLegend`, `urist mclegend` and `Urist  McLegend` behave consistently.
- Exact name matches rank before name prefixes, name-word matches, partial names, description/token matches and event-text matches.
- Exact event type labels rank below exact named records because an event's label is a type, not a unique proper name.
- Duplicate names produce separate results with distinct stable keys such as `figure:123`. The UI displays the key.
- Category filters and inclusive `fromYear`/`toYear` filters are applied before ranking and pagination.
- Pages are bounded at 100 results; a broad result set is capped at 25,000 matching candidates and reports `truncated`.
- Empty queries return an explicit empty state rather than every record. Invalid pages, years, categories, query length or punctuation-only text return HTTP 400 validation errors.
- A world with no indexed records still returns the available/unavailable category counts.

## API

### `GET /api/Search`

Query parameters:

| Parameter | Meaning |
| --- | --- |
| `q` | Search text, maximum 200 characters |
| `categories` | Comma-separated category names; case-insensitive; omit for all |
| `fromYear`, `toYear` | Inclusive year bounds from -100,000 to 100,000 |
| `page` | 1-based page, 1 to 100,000 |
| `pageSize` | 1 to 100, default 25 |

The response is `HistoricalSearchResponse`: query, world/generation identity, page metadata, total count, truncation state, index build statistics, available and matched category counts, unavailable categories, message/warning, and typed result items. Each result includes its stable key, category, optional integer ID, name, type, description, year/date label, URL, navigability, match kind and rank.

### `GET /api/Search/metadata`

Returns the active world/generation identity, index build time, record count, estimated retained bytes, category counts and unavailable categories. Both endpoints are read-only and use the existing local-origin/security middleware. No dependency or separate server was added.

## Frontend

- A prominent app-bar search field is available on desktop while a world is loaded; narrow layouts use a search icon. The navigation drawer also exposes **Historical Search**.
- `/search` is a dedicated result view with a debounced (300 ms) search field, category chips with counts, year filters, page-size selection and pagination.
- Results display category, type, name, identifier, date, preview, match explanation and a link to the existing page where available.
- The Pinia store aborts superseded requests and ignores responses from an older request version.
- The URL carries the query and filters, so browser back/forward and direct links work.
- Loading, empty-query, no-match, unavailable-category, truncated-result and API-error states are explicit.
- Desktop and 390-pixel layouts were checked for horizontal overflow and clipped controls.

## Index lifecycle and cache integration

- The Milestone 3 cache format is unchanged.
- Cached worlds restore the graph and build the search index from that graph. The XML parser is not invoked.
- Indexes are published atomically with their world. A switch never mixes documents from two worlds or snapshots.
- Old indexes become collectible after the final lease on their generation is released.
- No additional XML parsing occurs during search requests.
- Memory is bounded by the currently published generation plus any concurrently leased old generation. The temporary chunk postings used during build are released after merge.

## Automated validation

New backend coverage includes exact, prefix and partial matching; case-insensitive and Unicode names; duplicate names with distinct IDs; category and year filters; ranking and pagination; identifiers and links; event and creature records; empty/malformed input; API validation; cached-world restoration with a throwing parser; world switching; concurrent search during publication; collectible old indexes; and a 5,000-event parallel indexing case.

Final local results:

| Check | Result |
| --- | --- |
| Backend tests | 933/933 passed |
| Frontend Vitest | 53/53 passed |
| Complete solution build | succeeded; three existing benchmark-project CS8632 warnings |
| Standalone frontend production build | succeeded; existing Rollup chunk-size advisory |
| Browser automation | 21 checks passed, 19 screenshots |
| Browser console/page/network/HTTP errors | 0 |

The browser run covered World Library loading, cached region2 and region3 loading, figures, pagination, year filtering, category filters, wars, battles, events, sites, entities, artifacts, detail navigation, browser back, world switching, narrow layout and metadata. Intentional cancellation of one superseded search request was recorded separately from failed requests.

## Measured performance

Measurements use `scripts/benchmark-historical-search.ps1` with a fresh process per region and the existing verified parsed-world caches. The probe restores the cached graph, measures index construction, forces a GC and records search medians. Installed exports were read only. These are local measurements on an AMD Ryzen 5 5500U, .NET 10 Debug build; they are not universal guarantees.

| Measurement | region2 | region3 |
| --- | ---: | ---: |
| Figures | 24,631 | 28,714 |
| Events | 320,829 | 274,689 |
| Indexed records | 375,893 | 342,909 |
| Cache restore (verification + graph), fresh process | 10,080 ms | 8,299 ms |
| Initial index build | 4,760 ms | 4,462 ms |
| Estimated index bytes | 122.8 MiB | 110.1 MiB |
| Managed heap delta after forced GC | 135.6 MiB | 124.7 MiB |
| Working-set delta | 235.6 MiB | 219.1 MiB |
| Peak working set | 494.6 MiB | 496.1 MiB |
| Event preview failures | 0 | 0 |
| XML parser calls | 0 (cache restore) | 0 (cache restore) |

In-process search medians are 25-result pages over the full current-world index:

| Query | region2 total / median | region3 total / median |
| --- | ---: | ---: |
| `Urist` | 616 / 6.3 ms | 720 / 7.2 ms |
| `artifact created` | 1,757 / 12.5 ms | 1,625 / 10.5 ms |
| `war` | 12,536 / 59.5 ms | 14,737 / 48.8 ms |
| `a` (bounded, truncated) | 25,000 / 100.0 ms | 25,000 / 112.0 ms |
| `zzzznotfound` | 0 / 4.9 ms | 0 / 9.5 ms |

Browser/API load requests also confirmed cache hits and index reuse. In the final browser run, the server reported `X-NextPlus-Cache: HIT` for both worlds; its load-phase header (`X-NextPlus-Load-Ms`, before search-index publication) was 8.7 s for region2 and 10.7 s for region3, and the search index itself was reported at 3,118 ms (375,893 records) and 2,783 ms (342,909 records) in those generations. Build times varied with process JIT, concurrent browser work, memory pressure and available CPU, so the fresh-process probe table above is the primary measurement. The browser/API load phase includes source verification, graph restore, map initialization and bookmark/static work, not just search.

## Known limitations

- The index is not persisted separately. A cache hit restores the graph and rebuilds the index; this currently costs roughly 4–5 seconds for the tested large worlds.
- Event preview prose is not stored. Event previews are rendered for the bounded page only, and event searching is based on the event's canonical rendered text at index time.
- There is no dedicated individual-event detail page. The UI opens a parent chronicle when one exists and otherwise marks the result as non-navigable.
- Very broad queries can reach the 25,000-candidate cap and report a truncated result set.
- Query length is capped at 200 characters; page size at 100; page number at 100,000.
- Year filtering excludes records whose year is not reliable in the current export.
- Some exports contain many generic duplicate entity names. Results preserve their distinct IDs and URLs, but the names themselves remain identical.
- The index is not a substitute for an event relation graph or an AI Historian; those remain separate milestones.
- Index construction uses temporary per-chunk postings for large event sets, so build-time peak memory is higher than the retained index size.

## Local artifacts

- `artifacts/search-benchmarks/results.json` — fresh-process probe results.
- `artifacts/browser-4a/` — screenshots and browser report.
- `artifacts/search-test/` — temporary API request/response files.
- `artifacts/browser-4a/user-data-backup/` — backup of user JSON files taken before browser testing.

These directories are ignored and are not part of the pull request. No export, game asset, cache, browser artifact or inherited `tsconfig.app.tsbuildinfo` change is committed.
