# LegendsViewer-NextPlus

**A persistent, visual history browser for [Dwarf Fortress](https://www.bay12games.com/dwarves/).**

NextPlus is a community fork of [LegendsViewer-Next](https://github.com/Kromtec/LegendsViewer-Next). It makes Dwarf Fortress Legends exports easier to discover and explore, combining historical records, interactive Premium-style maps, and a library of cached worlds.

> **Status:** Actively developed. Features listed below are implemented; [planned features](#roadmap) are not yet available. NextPlus is an independent community project, not an official Dwarf Fortress or DFHack application.

## Features

- **World Library:** Automatically finds Legends exports, organizes known snapshots, and lets you load or switch worlds without browsing for XML each time.
- **Installation detection:** Finds Steam installations (including custom libraries), with a manual folder picker and saved export directories.
- **Premium maps:** Interactive Leaflet maps with terrain and site sprites sourced from your locally installed Dwarf Fortress, plus civilization and warfare overlays. BMP and generated-map fallbacks remain available.
- **Historical exploration:** Browse figures, family trees, civilizations, sites, artifacts, wars, battles, eras, world records, and populations.
- **Persistent caching:** Reopens verified historical data without reparsing the XML. Supports cache rebuild/removal and warned offline opening when the source export is unavailable.
- **Export diagnostics and bookmarks:** Shows missing companion files and remembers previously opened worlds.

### Loading performance

The first import parses history and creates a cache, so it can take longer. In local .NET 10 Debug benchmarks with two worlds, subsequent full loads were approximately **47% faster** than parsing and map setup (about 14 vs. 27 seconds for region2; 22 vs. 42 seconds for region3). Performance depends on world size and hardware.

## Getting started

**For developers:** Install the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) and [Node.js](https://nodejs.org/), then run:

```powershell
git clone https://github.com/Morasami/LegendsViewer-NextPlus.git
cd LegendsViewer-NextPlus
dotnet restore
dotnet run --project .\LegendsViewer.Backend\LegendsViewer.Backend.csproj
```

Open **http://localhost:15422** if the local frontend does not open automatically.

**For players:** A downloadable Windows release is planned. There is not yet a verified NextPlus release package; until then, run from source. See [Releases](https://github.com/Morasami/LegendsViewer-NextPlus/releases) for future builds.

### Import a world

1. Export Legends data from Dwarf Fortress (DFHack can provide extended companion files).
2. Open **Explore Worlds → Dwarf Fortress Settings** to confirm or choose the game folder.
3. Let the World Library discover exports, or add an export directory and rescan.
4. Select a snapshot to load; later openings can use its verified cache.

The main `<region>-<date>-legends.xml` is required for a first import. Matching optional companion files extend available information:

| Suffix | Additional information |
| --- | --- |
| `-legends_plus.xml` | Extended historical data |
| `-world_history.txt` | World/civilization history |
| `-world_sites_and_pops.txt` | Sites and populations |
| `-world_map.csv` | Premium world-map data |
| `-world_map.bmp` | Legacy map fallback |

Premium rendering also requires compatible **locally installed game graphics**. NextPlus does not bundle or redistribute those graphics. DFHack is not required to remain running while browsing an exported world.

### Local data

On Windows, NextPlus keeps its settings, bookmarks, World Library index, and parsed-world cache under:

`%APPDATA%\LegendsViewer-NextPlus\`

Discovery and loading do not modify original game files, saves, or exports.

## Roadmap

Planned, **not implemented yet**:

- Universal historical search and automatic discovery of interesting stories.
- Relationship graphs, deeper biographies and bestiary pages, artifact journeys, and research notes.
- Snapshot comparisons and exploration of additional save-derived data and authentic individual sprites.
- Optional evidence-grounded **AI Historian** for chronicles and historical studies.
- Versioned, tested Windows releases for players who do not build from source.

## Development

Built with **.NET 10 / ASP.NET Core**, **Vue 3 / TypeScript / Vuetify**, and **Leaflet**. The Windows self-contained publish workflow exists, but end-user release packaging and verification are still planned.

```powershell
dotnet test
cd LegendsViewer.Frontend\legends-viewer-frontend
npm run test:run
npm run build
```

See the [World Library](docs/milestone-2b-world-library.md) and [cache architecture](docs/milestone-3-world-cache.md) reports. Contributions are welcome: open an issue or submit a focused PR to `main`. Please do not commit game assets, world exports, saves, or local caches.

## Credits and license

Based on [Kromtec's LegendsViewer-Next](https://github.com/Kromtec/LegendsViewer-Next), the original [LegendsViewer](https://www.bay12forums.com/smf/index.php?topic=154617.0), and contributions from the Dwarf Fortress/DFHack communities.

Licensed under [MIT](LICENSE). Dwarf Fortress and its graphics belong to their respective owners.
