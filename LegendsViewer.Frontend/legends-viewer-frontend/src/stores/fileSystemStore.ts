import { defineStore } from 'pinia'
import client from '../apiClient'
import type { components } from '../generated/api-schema'
export type FilesAndSubdirectories = components['schemas']['FilesAndSubdirectoriesDto']
export const dfDirectoryStorageKey = 'df-directory'
export const useFileSystemStore = defineStore('fileSystem', {
  state: () => ({ filesAndSubdirectories: {} as FilesAndSubdirectories, loading: false, error: '' }),
  actions: {
    async loadDirectory(path?: string) {
      this.loading = true; this.error = ''
      try {
        const { data, error } = await client.GET('/api/FileSystem/browse', { params: { query: { path } } })
        if (error || !data) throw new Error(error?.detail ?? 'Cannot browse this folder.')
        this.filesAndSubdirectories = data
      } catch (error) { this.error = error instanceof Error ? error.message : 'Cannot browse this folder.' }
      finally { this.loading = false }
    },
    async loadSubDirectory(currentPath = '/', subFolder = '/') {
      const absolute = /^(?:[A-Za-z]:[\\/]|[\\/])/.test(subFolder)
      await this.loadDirectory(absolute ? subFolder : currentPath.replace(/[\\/]$/, '') + '/' + subFolder)
    },
    async initialize() {
      const saved = localStorage.getItem(dfDirectoryStorageKey)
      if (saved) return this.loadDirectory(saved)
      try {
        const { data } = await client.GET('/api/game-settings')
        await this.loadDirectory(data?.settings?.preferredExportDirectory ?? data?.selected?.path ?? undefined)
      } catch { await this.loadDirectory() }
    },
  },
})
