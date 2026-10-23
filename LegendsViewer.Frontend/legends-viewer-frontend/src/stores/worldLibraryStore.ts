import { defineStore } from 'pinia'
import client from '../apiClient'
import type { components } from '../generated/api-schema'
import { useBookmarkStore } from './bookmarkStore'

export type Snapshot = components['schemas']['LibrarySnapshot']
type Library = components['schemas']['LibraryState']
type Directory = components['schemas']['FilesAndSubdirectoriesDto']
const headers = { 'X-NextPlus-Settings': '1' }
function pathKey(path: string) {
  return /^[a-z]:[\\/]|^\\\\/i.test(path) ? path.replace(/\\/g, '/').toLowerCase() : path
}

export const useWorldLibraryStore = defineStore('worldLibrary', {
  state: () => ({ library: null as Library | null, busy: false, error: '', message: '',
    loadingId: '', loadedId: '', directory: null as Directory | null }),
  getters: {
    worlds(state) {
      const groups = new Map<string, Snapshot[]>()
      for (const snapshot of state.library?.snapshots ?? []) {
        const id = snapshot.worldId ?? snapshot.id ?? ''
        if (!groups.has(id)) groups.set(id, [])
        groups.get(id)!.push(snapshot)
      }
      return [...groups.entries()].map(([id, snapshots]) => ({ id, snapshots: snapshots.sort((a, b) =>
        (b.date ?? '').localeCompare(a.date ?? '') || (a.prefix ?? '').localeCompare(b.prefix ?? '')) }))
        .sort((a, b) => (a.snapshots[0]?.name ?? a.snapshots[0]?.region ?? '').localeCompare(b.snapshots[0]?.name ?? b.snapshots[0]?.region ?? ''))
    },
  },
  actions: {
    async run(action: () => Promise<void>) {
      if (this.busy) return
      this.busy = true; this.error = ''
      try { await action() }
      catch (error) { this.error = error instanceof Error ? error.message : 'Library request failed.' }
      finally { this.busy = false }
    },
    async refresh() {
      await this.run(async () => {
        const { data, error } = await client.POST('/api/world-library/refresh', { headers })
        if (error || !data) throw new Error(error?.detail ?? 'Could not rescan exports. Check that the local service is running.')
        this.library = data
      })
    },
    async saveDirectories(paths: string[]) {
      await this.run(async () => {
        const { data, error } = await client.PUT('/api/world-library/directories', { headers, body: { paths } })
        if (error || !data) throw new Error(error?.detail ?? 'Could not save export folders.')
        this.library = data
      })
    },
    async browse(path?: string) {
      await this.run(async () => {
        const { data, error } = await client.GET('/api/game-settings/directories', { params: { query: { path } } })
        if (error || !data) throw new Error(error?.detail ?? 'Cannot browse folder.')
        this.directory = data
      })
    },
    async load(snapshot: Snapshot) {
      const bookmarks = useBookmarkStore()
      if (bookmarks.isLoading || this.loadingId) return
      const main = snapshot.files?.find(file => file.kind === 'main' && file.status === 'Available')
      if (!main?.path) { this.error = 'Main XML unavailable. Restore the file and rescan.'; return }
      this.loadingId = snapshot.id ?? ''; this.message = ''; this.error = ''
      bookmarks.bookmarkError = ''
      try {
        await bookmarks.loadByFullPath(main.path, '')
        if (bookmarks.bookmarkError) throw new Error(bookmarks.bookmarkError)
        if (!this.isLoaded(snapshot)) throw new Error('The backend did not confirm that this snapshot loaded. Try again.')
        this.loadedId = snapshot.id ?? ''
        this.message = `Loaded ${snapshot.name || snapshot.prefix}.`
        await this.refresh()
      } catch (error) { this.loadedId = ''; this.error = error instanceof Error ? error.message : 'Load failed.' }
      finally { this.loadingId = '' }
    },
    isLoaded(snapshot: Snapshot): boolean {
      const current = useBookmarkStore().currentWorld
      const main = snapshot.files?.find(file => file.kind === 'main')?.path
      return !!current?.filePath && !!main && pathKey(current.filePath.replace('{TIMESTAMP}', current.loadedTimestamp ?? '')) === pathKey(main)
    },
  },
})
