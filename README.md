# LegendsViewer-NextPlus

**A more persistent, visual, and discoverable way to explore Dwarf Fortress history.**

LegendsViewer-NextPlus is a community fork of [Kromtec's LegendsViewer-Next](https://github.com/Kromtec/LegendsViewer-Next), itself a modern successor to the original LegendsViewer. It turns *Dwarf Fortress* Legends exports into a browsable historical archive: explore people, civilizations, settlements, creatures, artifacts, wars, timelines, populations, and the world map.

NextPlus builds on the original **.NET 10 backend** and **Vue 3 / TypeScript frontend**, adding automatic game and export discovery, Premium-style world maps from your installed game graphics, an organized World Library, and persistent parsed-world caching.

> **Status:** Actively developed. The features under **Available today** are implemented; those under **Planned** are roadmap ideas, not features in the current build. This is an independent community project, not an official Dwarf Fortress or DFHack release.

## Available today

| Feature | What it does |
| --- | --- |
| **World Library** | Finds Legends exports in your configured Dwarf Fortress installation and additional folders; groups known exports and snapshots, with one-click loading. |
| **Game installation settings** | Detects Steam installations and libraries, including custom locations; also supports manual folder selection, validation, and saved settings. |
| **Export diagnostics** | Shows which companion files are available, missing, or inaccessible, without treating optional companions as mandatory. |
| **Premium world maps** | Uses DFHack's `world_map.csv` export with graphics from your locally installed Dwarf Fortress; falls back to legacy BMP or generated maps when needed. |
| **Map exploration** | Interactive Leaflet map with sites, map navigation, civilization layers, and wars/battles overlays. |
| **Historical browsing** | Existing figures and family trees, civilizations, sites, artifacts, events, eras, world records, and other original LegendsViewer-Next views. |
| **Population and warfare data** | Population charts and expanded battle/combatant information drawn from available exports. |
| **Persistent parsed-world cache** | Saves the resolved backend historical graph per snapshot, preserving event types and relationships; checks source fingerprints and rebuilds invalid or outdated entries. |
| **Cache controls** | Inspect cache readiness, rebuild or remove an individual cache, and open an intact cache with an offline warning if its source export is unavailable. |
| **Bookmarks and snapshots** | Remember imported worlds, switch available snapshots, and keep NextPlus configuration separate from upstream LegendsViewer. |

### Loading performance

The first load of a world parses its exports and writes a cache, so **initial imports can take longer**. Subsequent loads restore processed history without re-running the XML parser, while validating source data and initializing the map.

In local **.NET 10 Debug** tests, full warm loads (including verification and map setup) were about **47% faster** than parsing plus map setup for two tested worlds: approximately 14 seconds vs. 27 seconds for region2, and 22 seconds vs. 42 seconds for region3. Results depend on your exports, hardware, and filesystem; loading is not instantaneous.

## Getting started

### Requirements

- Dwarf Fortress Legends export files (see below).
- **.NET 10 SDK** to build and run this repository.
- **Node.js / npm** for frontend development and building.
- An installed copy of *Dwarf Fortress* for Premium graphics. The viewer and its fallback maps can still work without Premium graphics.

### Run from source

```powershell
git clone https://github.com/Morasami/LegendsViewer-NextPlus.git
cd LegendsViewer-NextPlus
dotnet restore
dotnet run --project .\LegendsViewer.Backend\LegendsViewer.Backend.csproj
```

Open **http://localhost:15422** if the frontend does not open automatically. The local backend runs on **http://localhost:15421**.

The normal backend build hosts the built Vue frontend. For separate frontend development:

```powershell
cd LegendsViewer.Frontend\legends-viewer-frontend
npm install
npm run dev
```

Use the URL printed by the frontend development server.

### Import and explore worlds

1. Open **Explore Worlds → Dwarf Fortress Settings**. NextPlus attempts to detect the game installation automatically; you can select or enter its folder manually and save it.
2. Review the **World Library**. The application searches the game folder and configured export directories. You can add another export folder and select **Rescan exports**.
3. Expand a world, inspect its companion-file status, and choose **Load snapshot**. You can still import an individual XML manually.
4. Explore the world, including historical pages, charts, the interactive map, and warfare overlays.
5. On later launches, select the snapshot again to load the verified cached historical model. Use **Rebuild Cache** or **Remove Cache** if needed.

The World Library remembers discovery metadata; the parsed-world cache is a separate, versioned snapshot store.

### Legends export files

A standard main export uses the form:

`<region>-<date>-legends.xml`

NextPlus also recognizes these same-prefix companions in the same directory:

| File suffix | Purpose |
| --- | --- |
| `-legends.xml` | Primary historical XML; required for normal imports. |
| `-legends_plus.xml` | Additional Legends information from DFHack. |
| `-world_history.txt` | World and civilization history details. |
| `-world_sites_and_pops.txt` | Site and population information. |
| `-world_map.csv` | Premium-style world-map terrain metadata. |
| `-world_map.bmp` | Optional older map fallback. |

The main XML is necessary for a first import; other companions improve available information. A matching CSV **and** compatible installed game graphics are needed for Premium map rendering. NextPlus does not copy, bundle, or redistribute the game's proprietary graphics.

For incomplete or older exports, the viewer uses whatever compatible data and fallback map rendering are available. DFHack is useful for exporting extended data, but it does **not** need to remain running to browse an exported world.

### Local data and privacy

NextPlus runs as a **local browser-based application**. World exports and game graphics are read from local files.

Its own data is stored separately under `%APPDATA%\LegendsViewer-NextPlus\` on Windows:

- `settings.json` — selected installation and export directories.
- `bookmarks.json` — saved imported worlds.
- `world-library.json` — discovered worlds and snapshot metadata.
- `cache/` — versioned parsed-world caches.

The application does not modify the original Dwarf Fortress installation, saves, or Legends exports as part of discovery and caching. Cached history can still be opened with an explicit warning in supported offline situations.

## Planned — not yet implemented

Development goals include:

- **Universal historical search** spanning figures, civilizations, sites, artifacts, events, and relationships.
- **Interesting Histories** discovery to surface notable recorded world events and connected stories.
- An **interactive relationship explorer** and richer historical figure, settlement, civilization, artifact, and forgotten-beast profiles.
- **Historical journeys** for following recorded people and artifacts across locations and time.
- Enhanced **favorites, reading progress, and research notes**.
- **Comparison of snapshots** from the same world.
- Research into **authentic individual/creature sprites and additional personality data**, preferably read-only from existing exports, graphics, or save files. Changing DFHack is a last resort.
- An optional, evidence-grounded **AI Historian** for writing chronicles, biographies, and historical studies using user-selected model providers and explicit source references.

Availability of some details will depend on what Dwarf Fortress preserves in exports or save files. Planned features are subject to technical validation.

## Development and contributing

The backend is built with **.NET 10 / ASP.NET Core** and the frontend with **Vue 3, TypeScript, and Vuetify**. Premium maps use **Leaflet** and backend map rendering.

Useful project records:

- [Milestone 2B — World Library](docs/milestone-2b-world-library.md)
- [Milestone 3 — Persistent cache architecture and benchmarks](docs/milestone-3-world-cache.md)

Typical checks:

```powershell
dotnet test
cd LegendsViewer.Frontend\legends-viewer-frontend
npm run test:run
npm run build
```

Please open an issue to discuss a feature or bug, or submit a focused pull request targeting `main`. Run relevant tests, preserve existing export compatibility, and do not commit user game saves, exported world data, proprietary game graphics, local caches, or generated browser-test artifacts.

## Credits and license

NextPlus is based on [LegendsViewer-Next by Kromtec](https://github.com/Kromtec/LegendsViewer-Next) and its upstream contributors, including the original [LegendsViewer](https://www.bay12forums.com/smf/index.php?topic=154617.0) lineage. Thanks also to the DFHack contributors and the Dwarf Fortress community.

This repository is licensed under the [MIT License](LICENSE). Dwarf Fortress and its graphics belong to their respective owners and are not included here.
