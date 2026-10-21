import { defineStore } from 'pinia'
import client from '../apiClient'
import type { components } from '../generated/api-schema'

type State = components['schemas']['GameInstallationState']
type Installation = components['schemas']['GameInstallation']
type Directory = components['schemas']['FilesAndSubdirectoriesDto']

export const useGameSettingsStore = defineStore('gameSettings', {
  state: () => ({ configuration: null as State | null, path: '', validation: null as Installation | null,
    directory: null as Directory | null, error: '', message: '', busy: false }),
  actions: {
    async run(action: () => Promise<void>) {
      this.busy = true
      this.error = ''
      this.message = ''
      try { await action() }
      catch (error) { this.error = error instanceof Error ? error.message : 'Request failed. Try again.' }
      finally { this.busy = false }
    },
    async detect() {
      await this.run(async () => {
        const { data, error } = await client.GET('/api/game-settings')
        if (error || !data) throw new Error('Could not detect installations. Check that the local service is running.')
        this.configuration = data
        this.path = data.selected?.path ?? ''
        this.validation = data.selected ?? null
      })
    },
    choose(path: string) { this.path = path; this.validation = null; this.message = '' },
    async validate() {
      await this.run(async () => {
        const { data, error } = await client.POST('/api/game-settings/validate', { body: { path: this.path } })
        if (error || !data) throw new Error('Could not validate the folder.')
        this.validation = data
      })
    },
    async save() {
      await this.run(async () => {
        const { data, error } = await client.PUT('/api/game-settings', {
          body: { path: this.path }, headers: { 'X-NextPlus-Settings': '1' } })
        if (error || !data) throw new Error(error?.detail ?? 'Could not save the installation.')
        this.configuration = data
        this.path = data.selected?.path ?? this.path
        this.validation = data.selected ?? null
        this.message = 'Installation saved. Reload the world from Explore Worlds to refresh maps and sprites.'
      })
    },
    async browse(path?: string) {
      await this.run(async () => {
        const { data, error } = await client.GET('/api/game-settings/directories', { params: { query: { path } } })
        if (error || !data) throw new Error(error?.detail ?? 'Could not browse this folder.')
        this.directory = data
      })
    },
  },
})
