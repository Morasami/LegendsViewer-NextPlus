import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { useGameSettingsStore } from '../stores/gameSettingsStore'
import client from '../apiClient'
vi.mock('../apiClient', () => ({ default: { GET: vi.fn(), POST: vi.fn(), PUT: vi.fn() } }))
const installation = { path: 'D:/Steam/Game', valid: true, premiumGraphicsAvailable: true, status: 'Valid' }
const state = { selected: installation, source: 'Manual', candidates: [installation], settings: { explicitSelection: true }, warning: null }
beforeEach(() => { setActivePinia(createPinia()); vi.clearAllMocks() })
describe('Dwarf Fortress settings', () => {
  it('displays selected installation and manual source', async () => {
    vi.mocked(client.GET).mockResolvedValue({ data: state } as never)
    const store = useGameSettingsStore()
    await store.detect()
    expect(store.path).toBe(installation.path)
    expect(store.configuration?.source).toBe('Manual')
  })
  it('retains the unavailable selected path and warning with alternatives', async () => {
    vi.mocked(client.GET).mockResolvedValue({ data: { ...state, selected: { ...installation, valid: false }, warning: 'Unavailable' } } as never)
    const store = useGameSettingsStore()
    await store.detect()
    expect(store.validation?.valid).toBe(false)
    expect(store.configuration?.warning).toBe('Unavailable')
    expect(store.configuration?.candidates).toHaveLength(1)
  })
  it('validates typed paths and displays invalid-folder status', async () => {
    vi.mocked(client.POST).mockResolvedValue({ data: { ...installation, valid: false, status: 'Folder unavailable' } } as never)
    const store = useGameSettingsStore()
    store.choose('D:/missing')
    await store.validate()
    expect(store.validation?.status).toBe('Folder unavailable')
    expect(store.validation?.valid).toBe(false)
    store.choose('D:/other')
    expect(store.validation).toBeNull()
  })
  it('saves a detected or typed folder with the required header', async () => {
    vi.mocked(client.PUT).mockResolvedValue({ data: state } as never)
    const store = useGameSettingsStore()
    store.choose(installation.path)
    await store.save()
    expect(client.PUT).toHaveBeenCalledWith('/api/game-settings', {
      body: { path: installation.path }, headers: { 'X-NextPlus-Settings': '1' } })
    expect(store.message).toContain('Reload the world')
  })
  it('shows save failures without claiming success', async () => {
    vi.mocked(client.PUT).mockResolvedValue({ error: { detail: 'Invalid folder' } } as never)
    const store = useGameSettingsStore()
    await store.save()
    expect(store.error).toBe('Invalid folder')
    expect(store.message).toBe('')
    expect(store.busy).toBe(false)
  })
  it('browses server folders without changing the selected installation', async () => {
    vi.mocked(client.GET).mockResolvedValue({ data: { currentDirectory: 'D:/Steam', subdirectories: ['D:/Steam/Game'] } } as never)
    const store = useGameSettingsStore()
    store.path = 'D:/saved'
    await store.browse('D:/Steam')
    expect(store.directory?.currentDirectory).toBe('D:/Steam')
    expect(store.path).toBe('D:/saved')
  })
  it('shows directory access errors and handles network failure', async () => {
    vi.mocked(client.GET).mockResolvedValue({ error: { detail: 'Access denied' } } as never)
    const store = useGameSettingsStore()
    await store.browse('D:/private')
    expect(store.error).toBe('Access denied')
    vi.mocked(client.GET).mockRejectedValue(new Error('Offline'))
    await store.detect()
    expect(store.error).toBe('Offline')
    expect(store.busy).toBe(false)
  })
})
