<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { useWorldLibraryStore } from '../stores/worldLibraryStore'
import { useBookmarkStore } from '../stores/bookmarkStore'
const library = useWorldLibraryStore()
const bookmarks = useBookmarkStore()
const settingsOpen = ref(false)
const path = ref('')
const cacheLabels: Record<string, string> = { NeverImported: 'Never imported', Unavailable: 'Cache unavailable', Ready: 'Cached and ready', Offline: 'Cached · source unavailable',
  SourceChanged: 'Source changed · update required', Invalid: 'Cache invalid or unavailable', Verifying: 'Verifying cache', Restoring: 'Loading from cache',
  Validating: 'Validating world', Parsing: 'Parsing / rebuilding', Writing: 'Saving cache', Map: 'Initializing map' }
const labels: Record<string, string> = { main: 'Main legends XML', extended: 'Extended legends XML', history: 'History text',
  populations: 'Sites / populations text', csv: 'Premium map CSV', bmp: 'Legacy map BMP' }
const consequences: Record<string, string> = { main: 'Required to load this snapshot.', extended: 'Optional; adds detailed historical information.',
  history: 'Optional; adds world history.', populations: 'Optional; adds population information.',
  csv: 'Optional; Premium rendering also requires valid installed graphics.', bmp: 'Optional; legacy map fallback. Generated region maps remain available.' }
onMounted(() => library.refresh())
async function add() {
  if (!path.value.trim()) return
  await library.saveDirectories([...(library.library?.exportDirectories ?? []), path.value])
  if (!library.error) path.value = ''
}
</script>

<template>
  <section aria-label="World Library" class="mb-6">
    <div class="d-flex flex-wrap align-center ga-2 mb-3">
      <div class="mr-auto"><h1 class="text-h4">World Library</h1><p class="text-medium-emphasis">Discover exports, choose a snapshot, and explore its history.</p></div>
      <v-btn prepend-icon="mdi-folder-cog" variant="tonal" @click="settingsOpen = true">Export directories</v-btn>
      <v-btn prepend-icon="mdi-refresh" :loading="library.busy" :disabled="bookmarks.isLoading" @click="library.refresh">Rescan exports</v-btn>
    </div>
    <v-alert v-if="library.error" type="error" variant="tonal" class="mb-3" role="alert">{{ library.error }}</v-alert>
    <v-alert v-if="library.message" type="success" variant="tonal" class="mb-3">{{ library.message }} <router-link v-if="bookmarks.isLoaded" to="/world">Explore world</router-link></v-alert>
    <v-alert v-if="library.library?.warning" type="warning" variant="tonal" class="mb-3">{{ library.library.warning }}</v-alert>
    <v-alert v-for="root in library.library?.roots?.filter(r => r.warning)" :key="root.path ?? ''" type="warning" variant="tonal" class="mb-3">
      <span class="library-path">{{ root.path }}</span> — {{ root.warning }}
    </v-alert>
    <v-progress-linear v-if="library.busy" indeterminate class="mb-3" aria-label="Scanning exports" />
    <v-card v-if="!library.worlds.length && !library.busy" variant="tonal" class="pa-5 mb-3">
      No exports discovered yet. Check your Dwarf Fortress installation or add an export directory. You can also select a file manually below.
    </v-card>
    <v-expansion-panels multiple variant="accordion">
      <v-expansion-panel v-for="world in library.worlds" :key="world.id">
        <v-expansion-panel-title>
          <div class="library-world">
            <v-img v-if="world.snapshots.find(s => s.thumbnail)?.thumbnail" :src="`data:image/png;base64,${world.snapshots.find(s => s.thumbnail)?.thumbnail}`" width="88" height="88" class="flex-grow-0 pixelated-image" alt="Previously loaded world map" />
            <div v-else class="library-placeholder"><v-icon icon="mdi-earth" size="44" /><span>Not imported</span></div>
            <div class="library-summary">
              <div class="text-h6">{{ world.snapshots[0]?.name || world.snapshots[0]?.region }}</div>
              <div v-if="world.snapshots[0]?.alternativeName" class="text-body-2">{{ world.snapshots[0]?.alternativeName }}</div>
              <div class="text-body-2 text-medium-emphasis">{{ world.snapshots[0]?.region }} · {{ world.snapshots.length }} snapshot{{ world.snapshots.length === 1 ? '' : 's' }} · Latest {{ world.snapshots[0]?.date || 'date unknown' }}
                · {{ world.snapshots.find(s => s.width)?.width ? `${world.snapshots.find(s => s.width)?.width} × ${world.snapshots.find(s => s.height)?.height}` : 'Dimensions after import' }}</div>
              <div class="library-path text-caption text-medium-emphasis">{{ world.snapshots[0]?.directory }}</div>
              <v-chip size="x-small" class="mt-1" :color="world.snapshots.some(s => library.isLoaded(s)) ? 'success' : undefined">{{ world.snapshots.some(s => library.isLoaded(s)) ? 'Loaded' : 'Unloaded' }}</v-chip>
              <v-chip v-if="world.snapshots.some(s => !s.available)" size="x-small" class="ml-2 mt-1">Unavailable exports</v-chip>
            </div>
          </div>
        </v-expansion-panel-title>
        <v-expansion-panel-text>
          <v-card v-for="snapshot in world.snapshots" :key="snapshot.id ?? ''" variant="outlined" class="my-3">
            <v-card-text>
              <div class="d-flex flex-wrap align-center ga-2">
                <div class="mr-auto library-summary"><strong>{{ snapshot.date || 'Undated export' }}</strong><div class="library-path text-caption">{{ snapshot.prefix }}</div></div>
                <v-chip v-if="library.isLoaded(snapshot)" color="success" size="small">Loaded</v-chip>
                <v-chip v-if="snapshot.status === 'Failed'" color="warning" size="small">Last load failed</v-chip>
                <v-chip v-if="snapshot.cache" size="small" :color="snapshot.cache.state === 'Ready' ? 'success' : ['Invalid', 'SourceChanged', 'Offline'].includes(snapshot.cache.state ?? '') ? 'warning' : undefined">{{ cacheLabels[snapshot.cache.state ?? ''] || snapshot.cache.state }}</v-chip>
                <v-btn :disabled="(!snapshot.available && snapshot.cache?.state !== 'Offline') || bookmarks.isLoading || !!library.loadingId" :loading="library.loadingId === snapshot.id" color="primary" variant="tonal" @click="library.load(snapshot)">{{ library.isLoaded(snapshot) ? 'Reload snapshot' : 'Load snapshot' }}</v-btn>
                <v-btn v-if="library.isLoaded(snapshot)" to="/world" variant="tonal" color="success">Explore</v-btn>
                <v-btn size="small" variant="text" :disabled="!snapshot.available || bookmarks.isLoading || !!library.loadingId || library.busy" @click="library.load(snapshot, true)">Rebuild Cache</v-btn>
                <v-btn v-if="snapshot.cache && !['NeverImported', 'Unavailable'].includes(snapshot.cache.state ?? '')" size="small" variant="text" :disabled="bookmarks.isLoading || !!library.loadingId || library.busy" @click="library.removeCache(snapshot)">Remove cache</v-btn>
              </div>
              <div v-if="snapshot.cache" class="text-caption text-medium-emphasis mt-2" role="status">{{ snapshot.cache.message }}<span v-if="snapshot.cache.bytes"> · {{ (snapshot.cache.bytes / 1048576).toFixed(1) }} MiB</span></div>
              <v-progress-linear v-if="library.loadingId === snapshot.id" indeterminate class="mt-2" :aria-label="cacheLabels[snapshot.cache?.state ?? ''] || 'Loading world'" />
              <v-alert v-if="snapshot.warning" type="warning" variant="tonal" class="mt-3">{{ snapshot.warning }}</v-alert>
              <details class="mt-3 companion-details">
                <summary>Companion files · {{ snapshot.files?.filter(f => f.status === 'Available').length ?? 0 }}/6 available</summary>
                <div v-for="file in snapshot.files" :key="file.kind ?? ''" class="companion-row">
                  <v-icon :icon="file.status === 'Available' ? 'mdi-check-circle-outline' : 'mdi-minus-circle-outline'" :color="file.status === 'Available' ? 'success' : (file.kind === 'main' ? 'warning' : undefined)" size="20" />
                  <div><strong>{{ labels[file.kind ?? ''] }}</strong> · {{ file.status }}<div class="text-caption text-medium-emphasis">{{ consequences[file.kind ?? ''] }}</div></div>
                </div>
                <p v-if="!library.library?.premiumGraphicsAvailable" class="text-caption mt-2">Installed Premium graphics are unavailable. Existing BMP and generated map fallbacks remain available.</p>
              </details>
            </v-card-text>
          </v-card>
        </v-expansion-panel-text>
      </v-expansion-panel>
    </v-expansion-panels>
  </section>
  <v-dialog v-model="settingsOpen" max-width="850" scrollable>
    <v-card title="Export directories">
      <v-card-text>
        <p class="mb-3">Scans read files directly in these folders. Saves, mods, and nested folders are not traversed. Your game installation is configured separately.</p>
        <v-alert v-if="library.error" type="error" class="mb-3">{{ library.error }}</v-alert>
        <div v-for="root in library.library?.roots" :key="root.path ?? ''" class="mb-3 library-path"><strong>{{ root.source }}</strong><div>{{ root.path }}</div><span v-if="root.warning" class="text-warning">{{ root.warning }}</span></div>
        <v-list aria-label="Additional export directories">
          <v-list-item v-for="folder in library.library?.exportDirectories" :key="folder" class="library-path" :title="folder">
            <template #append><v-btn icon="mdi-close" :aria-label="`Remove ${folder}`" variant="text" :disabled="library.busy" @click="library.saveDirectories((library.library?.exportDirectories ?? []).filter(p => p !== folder))" /></template>
          </v-list-item>
        </v-list>
        <v-text-field v-model="path" label="Absolute export directory" class="mt-3" @keyup.enter="add" />
        <div class="d-flex flex-wrap ga-2"><v-btn :disabled="library.busy" @click="library.browse(path || undefined)">Browse export folders</v-btn><v-btn :disabled="library.busy || !path.trim()" color="primary" @click="add">Add directory</v-btn></div>
        <v-card v-if="library.directory" variant="outlined" class="mt-4 pa-3">
          <p class="library-path mb-3">{{ library.directory.currentDirectory }}</p>
          <div class="d-flex flex-wrap ga-2"><v-btn @click="library.browse('mounts')">Drives / mounts</v-btn><v-btn v-if="library.directory.parentDirectory" @click="library.browse(library.directory.parentDirectory)">Up</v-btn>
            <v-btn :disabled="library.directory.currentDirectory === 'mounts'" @click="path = library.directory.currentDirectory ?? ''">Use this export folder</v-btn></div>
          <v-list max-height="230" aria-label="Export folders"><v-list-item v-for="folder in library.directory.subdirectories" :key="folder" :title="folder" prepend-icon="mdi-folder" @click="library.browse(folder)" /></v-list>
        </v-card>
      </v-card-text>
      <v-card-actions><v-spacer /><v-btn @click="settingsOpen = false">Close</v-btn></v-card-actions>
    </v-card>
  </v-dialog>
</template>

<style scoped>
.library-world { display: flex; align-items: center; gap: 16px; width: 100%; min-width: 0; }
.library-summary { min-width: 0; overflow-wrap: anywhere; }
.library-placeholder { width: 88px; min-width: 88px; height: 88px; display: flex; flex-direction: column; justify-content: center; align-items: center; background: rgba(128,128,128,.12); border-radius: 8px; font-size: 11px; }
.library-path { overflow-wrap: anywhere; white-space: normal; }
.companion-details summary { cursor: pointer; padding: 6px 0; }
.companion-row { display: flex; gap: 10px; align-items: start; padding: 8px 0; }
@media (max-width: 480px) { .library-world { gap: 10px; } .library-placeholder { width: 52px; min-width: 52px; height: 70px; } }
</style>
