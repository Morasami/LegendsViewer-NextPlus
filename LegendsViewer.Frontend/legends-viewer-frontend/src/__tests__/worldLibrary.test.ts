import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { useWorldLibraryStore, type Snapshot } from '../stores/worldLibraryStore'
import { useBookmarkStore } from '../stores/bookmarkStore'
import client from '../apiClient'
vi.mock('../apiClient', () => ({ default: { GET: vi.fn(), POST: vi.fn(), PUT: vi.fn() } }))
const snapshot = (id: string, worldId: string, date: string): Snapshot => ({ id, worldId, date, prefix: `region-${date}`,
  available: true, files: [{ kind: 'main', path: `/exports/region-${date}-legends.xml`, status: 'Available' }] })
const first = snapshot('first', 'world', '00184-01-01')
const latest = snapshot('latest', 'world', '00185-01-01')
const other = snapshot('other', 'different-directory', '00184-01-01')
beforeEach(() => { setActivePinia(createPinia()); vi.clearAllMocks() })
describe('World library', () => {
  it('groups by stable world identity and sorts snapshots by reliable export date', async () => {
    vi.mocked(client.POST).mockResolvedValue({ data: { snapshots: [first, other, latest] } } as never)
    const store = useWorldLibraryStore(); await store.refresh()
    expect(store.worlds).toHaveLength(2)
    expect(store.worlds.find(w => w.id === 'world')?.snapshots.map(s => s.id)).toEqual(['latest', 'first'])
    expect(client.POST).toHaveBeenCalledWith('/api/world-library/refresh', { headers: { 'X-NextPlus-Settings': '1' } })
  })
  it('retains prior discovery results when rescan fails', async () => {
    const store = useWorldLibraryStore(); store.library = { snapshots: [first] }
    vi.mocked(client.POST).mockRejectedValue(new Error('Offline'))
    await store.refresh()
    expect(store.error).toBe('Offline'); expect(store.worlds).toHaveLength(1); expect(store.busy).toBe(false)
  })
  it('saves additional folders separately from the installation and displays failures', async () => {
    const store = useWorldLibraryStore()
    vi.mocked(client.PUT).mockResolvedValue({ error: { detail: 'Folder unavailable' } } as never)
    await store.saveDirectories(['/exports é'])
    expect(client.PUT).toHaveBeenCalledWith('/api/world-library/directories', { headers: { 'X-NextPlus-Settings': '1' }, body: { paths: ['/exports é'] } })
    expect(store.error).toBe('Folder unavailable'); expect(store.busy).toBe(false)
  })
  it('browses literal paths without modifying configured folders', async () => {
    vi.mocked(client.GET).mockResolvedValue({ data: { currentDirectory: '/exports %20 世界', subdirectories: [] } } as never)
    const store = useWorldLibraryStore(); store.library = { exportDirectories: ['/saved'] }
    await store.browse('/exports %20 世界')
    expect(store.directory?.currentDirectory).toBe('/exports %20 世界')
    expect(store.library.exportDirectories).toEqual(['/saved'])
  })
  it('does not mark a snapshot loaded before parsing succeeds', async () => {
    const store = useWorldLibraryStore(), bookmarks = useBookmarkStore()
    let finish!: (value: unknown) => void
    vi.mocked(client.POST).mockImplementationOnce(() => new Promise(resolve => { finish = resolve }) as never)
    const pending = store.load(first)
    expect(store.loadingId).toBe('first'); expect(store.isLoaded(first)).toBe(false)
    finish({ data: { filePath: '/exports/region-{TIMESTAMP}-legends.xml', loadedTimestamp: first.date, state: 'Loaded', worldName: 'World' } })
    vi.mocked(client.POST).mockResolvedValue({ data: { snapshots: [first] } } as never)
    await pending
    expect(store.isLoaded(first)).toBe(true); expect(store.message).toContain('Loaded'); expect(bookmarks.isLoading).toBe(false)
    expect(client.POST).toHaveBeenCalledWith('/api/Bookmark/loadByFullPath', { body: first.files![0]!.path })
  })
  it('shows parser errors and never claims a failed load succeeded', async () => {
    vi.mocked(client.POST).mockResolvedValue({ error: 'Bad XML' } as never)
    const store = useWorldLibraryStore(); await store.load(first)
    expect(store.error).toBe('Bad XML'); expect(store.message).toBe(''); expect(store.loadingId).toBe('')
    expect(store.isLoaded(first)).toBe(false)
  })
  it('rejects unavailable main files and overlapping loads', async () => {
    const store = useWorldLibraryStore(); await store.load({ ...first, files: [] })
    expect(store.error).toContain('Main XML unavailable'); expect(client.POST).not.toHaveBeenCalled()
    useBookmarkStore().isLoadingNewWorld = true
    await store.load(first); expect(client.POST).not.toHaveBeenCalled()
  })
  it('resets busy state after a disconnected loading request', async () => {
    vi.mocked(client.POST).mockRejectedValue(new Error('Connection lost'))
    const store = useWorldLibraryStore(); await store.load(first)
    expect(store.error).toBe('Connection lost'); expect(store.loadingId).toBe(''); expect(useBookmarkStore().isLoading).toBe(false)
  })
})
