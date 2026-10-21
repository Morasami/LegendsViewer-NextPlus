# Milestone 2A: game discovery and settings

Dwarf Fortress Settings is available on Explore Worlds. Select a detected installation, enter an absolute path, or Browse server-side folders and use the current folder. Validate reports installation and Premium graphics availability. Save persists the choice. Detect Again refreshes discovery without changing the saved manual choice. Use Reload on the loaded world card to refresh maps after changing installations; restarting NextPlus is unnecessary.

## Resolution and storage

`ApplicationSettingsService` stores version 1 JSON at `%APPDATA%\LegendsViewer-NextPlus\settings.json` on Windows, using the platform ApplicationData directory elsewhere. It writes a temporary file in the same directory, flushes it, and replaces the destination. Bookmarks retain their existing format and storage. Settings contain `Version`, `InstallationPath`, `ExplicitSelection`, and an optional `PreferredExportDirectory` reserved for future work. No secrets or game assets are stored.

`GameInstallationService` validates absolute normalized paths, requiring a game executable and data directory. It checks the graphics definition and all four Premium sprite sheets separately. A valid Classic installation can be selected without Premium graphics. Missing or unreadable settings are handled with a recovery message where appropriate.

Resolution order:

1. An explicitly selected installation, including an unavailable selection that must be repaired or changed by the user.
2. A valid `DF_INSTALL_DIR` environment override.
3. A unique valid discovered candidate. Multiple candidates require a user choice.

Discovery reads HKCU/HKLM Valve Steam registry locations (including WOW6432Node), conventional Windows Program Files Steam roots, modern and legacy `libraryfolders.vdf` entries, and secondary libraries. Existing Linux `.local/share/Steam`, `.steam/steam`, `.steam/root`, Flatpak Steam, Bay 12 Games, and macOS `Library/Application Support/Steam` paths are covered. Discovery is bounded; it never scans entire drives. Tests use injectable locations and temporary synthetic games, including spaces and Unicode.

The map generator and site-icon endpoint use the same service. Premium CSV rendering, BMP fallback, generated regions, site markers, and warfare overlays retain their existing rendering logic. Icons disable HTTP caching so reloading a world can refresh graphics. All Steam/game access is read-only.

## API and local browser access

- `GET /api/game-settings`: settings, selected installation, source, warnings, candidates; also used for Detect Again.
- `POST /api/game-settings/validate`: typed path validation.
- `PUT /api/game-settings`: validate and save a selected path; requires `X-NextPlus-Settings: 1`.
- `GET /api/game-settings/directories?path=...`: folder-only browsing, sharing the existing filesystem listing code. Query parameters avoid double-decoding literal percent/Unicode paths.

The backend remains on localhost. The static frontend now also binds localhost. CORS is restricted to the application's local frontend/backend ports and Vite's local development port, with request Host/Origin/Fetch-Site checks to reject unrelated websites and DNS rebinding. There is no arbitrary file-write endpoint. CLI clients without Origin remain supported. Frontend contracts were regenerated using `npm run generate-api-schema`; this also refreshes previously stale upstream declarations.

## Verification (Windows, 2026-10-21)

- Focused discovery/map tests passed; full backend suite: **868 passed, 0 skipped**.
- Frontend Vitest: **31 passed** across three files, including settings selection, validation, saving, missing installation warnings, folder browsing, and request errors.
- Full solution build and frontend production build passed. Three existing nullable-context warnings remain in the benchmark project; npm reports existing dependency advisories and Vite reports bundle-size warnings. No dependency upgrades are included.
- Final backend rebuild and complete backend suite passed after shared folder-browser extraction.
- With `DF_INSTALL_DIR` absent, live API discovery found `D:\Steam\steamapps\common\Dwarf Fortress` automatically via the machine Steam registry entry at `HKLM\SOFTWARE\WOW6432Node\Valve\Steam` (`InstallPath=D:\Steam`). All Premium graphics were recognized.
- Saved the installation through the API to the default settings file. A fresh backend process restored it as Manual despite a conflicting environment override.
- An isolated temporary console harness confirmed the persisted manual choice beats a second valid synthetic environment installation, all five map asset lookups use the selected game, and a synthetic 2x2 CSV renders using real installed Premium sprites (20x20 output, 16 colors).
- The live site-icon endpoint returned the installed PNG byte-for-byte (SHA-256 `FE0AA149C6D7B0C419B2988B828659543C3C4AC7B73660C0DA04F478CDB99817`). Hashes of every file under the game's world-map graphics directory were unchanged before/after smoke checks. No game saves or user exports were written.
- Unrelated-origin requests and writes without the settings header returned 403.
- These are command/API and rendering checks. Browser interaction and visual layout were not manually verified. Settings changes require reloading the world using its Reload button.

Export indexing, a persistent World Library, parsed-world caching, and later milestones are outside this change. Ignored audit documents remain untouched.
