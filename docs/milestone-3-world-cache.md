# Milestone 3: persistent parsed-world cache

The cache restores the backend historical object graph, including private event state and resolved relationships. It does not replay XML properties or restore frontend-only DTOs. No serialization dependency was added.

## Format and identity

`%APPDATA%\LegendsViewer-NextPlus\cache\<snapshot-id>.lvwc` uses the same SHA-256 directory-plus-export-prefix identity as the World Library. Source paths never become cache filenames. Each dated export and each directory has an independent cache.

The version 1 container has a bounded 64 KiB header and a gzip-compressed binary graph. Its JSON manifest records container schema, parser compatibility version, a computed field-contract fingerprint, snapshot ID, UTC creation time, all six source dependencies, compressed/expanded lengths, payload SHA-256, and figure/event counts. Headers are structurally validated; payload integrity is checked before deserialization. Incompatible caches are rebuilt rather than migrated.

The graph uses fixed numeric type codes from a checked-in allowlist of 216 model types. It includes inherited and private instance fields, including fields ignored by the HTTP JSON representation. Every logical reference object is written once; references, lists, arrays, dictionaries and tuples preserve relationships and ordering. Equal strings are deduplicated without adding them to the process-wide intern pool. `CreatureInfo.Unknown` uses a dedicated singleton reference code. Color encodings preserve named, empty and ARGB color semantics.

All allocations use compiled, audited types. Model shells are allocated without invoking parsing constructors; fields are filled before dictionary keys are hashed. No input CLR type names, assembly loading, unrestricted polymorphism, serialization callbacks, delegates or BinaryFormatter are supported. Only closed container types reachable from the approved contracts are accepted. Limits include 1 GiB container size, 2 GiB expanded payload, 20 million nodes, 20 million elements per container, 100 million aggregate elements and 4 MiB per encoded string. These are rejection limits, not promises that every world up to those limits fits available RAM.

Writes use a unique temporary file, flush the completed container to disk, then atomically replace only that snapshot's cache. Failed parsing never publishes a cache. A failure while replacing a cache leaves the previous generation intact. Temporary files are never cache candidates. A cache-save failure can leave a successfully parsed world usable with a warning; a detected source change during import rejects publication of the world as well as the cache.

## Persisted and reconstructed data

Persisted data includes figures, entities, memberships and relationships; sites, structures and ownership; artifacts and written/art forms; concrete events and event collections; wars, battles, squads and participants; creatures and populations; dimensions, dates, regions, rivers, constructions, geography grids; resolved links and lookup data; parsing diagnostics and model back-references.

Presentation memoization, family/war graph DTO caches, formatting/relationship helper instances, cached job projections and cached World Records DTOs are omitted. Formatting helpers are recreated lazily, honor requirement helpers are explicitly reconstructed, and World Records is calculated from the restored graph. Resolved historical structures are retained instead of repeating the XML parser's resolution pipeline. Game graphics and map bitmaps are never in the graph.

The contract fingerprint rejects structural changes automatically. Maintainers must increment `ParserCompatibilityVersion` when parsing or resolution behavior changes without a corresponding model-field change.

## Validity and loading

Main/extended legends XML and history/population text are historical dependencies. Added, removed or changed historical companions invalidate their snapshot. Library startup uses cheap presence/length/UTC timestamp checks and header validation; it does not hash or parse every export. Opening verifies SHA-256 content, including same-length replacements with preserved timestamps, verifies the cache payload, restores/reconstructs the graph, validates canonical tables and references, and initializes the map. Source fingerprints are checked again before accepting restoration or publishing a newly built cache.

CSV/BMP dependencies are recorded separately. Their changes and changes to installed Premium graphics do not invalidate history: a new map generator reads current visual inputs on every load. Missing graphics use the existing BMP/generated-region fallback. If the main export is missing, a matching intact cache can open with an explicit read-only/offline warning. Every remaining available historical source must match; newly added or changed available historical inputs prevent offline reuse. Offline maps can use the generated historical geography when the original main path is unavailable.

Manual import, bookmark loading and library loading share the existing process-wide load gate. Each load prepares a concrete World and map in isolation. Publication swaps one complete generation. Each HTTP request leases a consistent world/map pair; old map resources are released only after the final reader finishes. `WorldView` forwards IWorld without implementing IDisposable, preventing scoped dependency injection from clearing a shared World at request completion. The World Records controller still receives the underlying concrete World. Rejected loads retain the previous world and bookmark.

The library exposes never-imported, unavailable, cached-ready, offline, source-changed and invalid states, plus verification/restoration/parsing/writing/validation/map phases. Ready is a metadata-level indication; cryptographic checks run on opening. Rebuild Cache bypasses restoration for the selected snapshot. Remove cache deletes only its `.lvwc`, preserving active history, bookmarks, favorites, library metadata and thumbnails.

## Correctness evidence

Synthetic warfare and the existing small fixture compare private model fields, a reference bijection, concrete types, all event renderings, canonical lookups, figure/entity/site/artifact views, relationships, warfare, populations and World Records. Serialization precedes behavioral observation because existing rendering methods can fill derived state on first use (for example Abduction.ToLink).

Real installed exports passed the same comparisons and restored map rendering:

| Export | Figures | Events | Wars | Battles | Reference objects compared |
|---|---:|---:|---:|---:|---:|
| region2 | 24,631 | 320,829 | 127 | 415 | 2,154,064 |
| region3 | 28,714 | 274,689 | 297 | 1,827 | 2,762,166 |

Tests cover parser-free restoration after service restart, same-length/same-time changes, companions, corruption/truncation/old versions, independent snapshot identity, offline behavior, failures, concurrent loading, deletion/rebuild and request-generation disposal. Parser streams are now disposed deterministically; the old code left files open until GC. World.Clear also resets the geography grid and dates.

Final automated validation: 919/919 backend tests and 47/47 Vitest tests pass. The complete solution build succeeds with three existing benchmark-project CS8632 nullable warnings. The standalone frontend production build exits 0 with the existing Rollup chunk-size advisory. The separate probe project builds with an NU1900 advisory because NuGet vulnerability data was unavailable; no package was added.

Real Chrome/Playwright at `http://localhost:15422` passed 22 checks and captured 29 screenshots in `artifacts/browser-m3/final/`. This expands the existing Milestone 2B script rather than replacing it. Checks include a first region2 import without cache; complete backend process replacement (PID 1620 to 24828); a confirmed cache HIT after restart; Overview, Premium Map, figures, entities, artifacts, wars, battles and World Records; individual detail pages; region3 switching and return; an intentionally truncated generated cache and its invalid label; explicit rebuild, selected deletion and recreation; existing manual selection; desktop and 390-pixel layouts. All console-error, page-error, failed-request and HTTP-error arrays are empty. Actual screenshots were inspected, including progress, cache-ready status, restored historical pages/maps and narrow controls.

A supplemental Chrome check performed another confirmed cache HIT and enabled Wars & Battles on the restored map: 13 SVG paths rendered, with no console/page/network errors (`warfare-report.json`, `warfare-after-cache.png`). Settings and the inherited tracked tsbuildinfo retain their original SHA-256. Existing user JSON files were backed up before browser work; bookmark/library changes are the normal import metadata updates. No screenshot, cache, export, game asset or existing unrelated audit document is staged.

## Measured performance

Windows/.NET 10 Debug builds, installed exports read only. `scripts/benchmark-world-cache.ps1` starts a fresh process for every measurement, including both warm repetitions. It calls the production loader and graphics resolver. The warm parser seam throws if invoked. Timings include service setup, all source SHA-256 checks, cache checksum verification, deserialization/reconstruction/validation, and Premium map initialization. Complete process wall times additionally include process startup and exit. Cold means an absent parsed-world cache; OS file caches were not purged. Results are local measurements, not a universal speed guarantee.

Benchmark caches were isolated under the normal AppData cache tree. Changed-source cases used disposable copies under ignored artifacts: seed a cache, append XML whitespace, then load normally and verify a parser call. The installed exports were never modified. Proof/equivalence work and browser loads were excluded from the timed runs. Warm and changed-source cases were rechecked on the final metadata-first validation path; the table uses those final rechecks. Both the initial and final JSON results are retained locally.

| Measurement | region2 | region3 |
|---|---:|---:|
| Original parsing only | 25.730 s | 34.766 s |
| Baseline parse plus map/setup | 26.795 s | 41.628 s |
| Initial import including cache creation | 51.222 s | 62.208 s |
| Full warm load, fresh process 1 | 14.440 s | 21.694 s |
| Full warm load, fresh process 2 | 13.897 s | 22.055 s |
| Changed-source rebuild | 42.671 s | 56.667 s |
| Cache size | 28,085,172 bytes (26.78 MiB) | 31,546,161 bytes (30.08 MiB) |
| XML parser calls on each warm load | 0 | 0 |
| Mean warm time reduction vs baseline load | 47.1% | 47.5% |

Process wall times: baseline 27.676/42.027 s; cold 51.572/62.585 s; warm repetitions 15.151 and 14.129 / 22.028 and 22.320 s; changed-source 42.977/56.999 s (region2/region3 respectively).

| Memory observation | region2 | region3 |
|---|---:|---:|
| Baseline peak working set | 398.4 MiB | 564.8 MiB |
| Cold peak working set | 553.3 MiB | 740.4 MiB |
| Warm peak working set, two runs | 265.3–267.1 MiB | 418.0–423.9 MiB |
| Warm managed heap at completion | 208.1–208.3 MiB | 239.5 MiB |
| Changed-source peak working set | 561.1 MiB | 739.8 MiB |

Memory values are observed process working sets and unforced GC heap readings, not retained-heap guarantees. Switching worlds temporarily needs both the old generation and the candidate while requests still hold the old generation. The isolated correctness proof also holds two complete worlds and reference maps, so its higher peak is not a normal warm-load cost. Cold import is deliberately more expensive than parsing alone because it writes and checks the cache.

## Limitations

- No cache migration; incompatible schema/parser/contracts rebuild from sources. An incompatible offline cache cannot be opened.
- No automatic cache eviction or broad pre-import. Deleting the selected cache is explicit; normal rebuilding occurs on load.
- Full content verification reads historical source files even on a cache hit, trading some speed for protection against preserved file metadata.
- Offline mode cannot verify missing sources and warns accordingly. Cache corruption with missing sources is an error, never a partial historical model.
- The format intentionally couples to audited model fields. New model types and private fields require contract review and regression proof.
- A corrupt payload can initially have a metadata-ready library label; integrity is checked on opening, with normal parsing fallback when possible.

Local artifacts: `artifacts/m3-proof/`, `artifacts/cache-benchmarks/`, `artifacts/browser-m3/`. They contain exports, caches, logs or screenshots and are not committed. Existing audit documents and inherited tsbuildinfo changes are preserved.
