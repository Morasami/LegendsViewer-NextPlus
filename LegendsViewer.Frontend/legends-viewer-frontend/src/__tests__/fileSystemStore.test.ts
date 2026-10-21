import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { dfDirectoryStorageKey, useFileSystemStore } from '../stores/fileSystemStore'
import client from '../apiClient'
vi.mock('../apiClient', () => ({ default: { GET: vi.fn() } }))
beforeEach(() => { setActivePinia(createPinia()); vi.clearAllMocks(); localStorage.clear() })
describe('Manual export browser compatibility', () => {
  it('sends literal Unicode and percent paths through the query API', async () => {
    vi.mocked(client.GET).mockResolvedValue({ data: { currentDirectory: 'D:/é 世界 %20' } } as never)
    const store = useFileSystemStore(); await store.loadDirectory('D:/é 世界 %20')
    expect(client.GET).toHaveBeenCalledWith('/api/FileSystem/browse', { params: { query: { path: 'D:/é 世界 %20' } } })
    expect(store.error).toBe(''); expect(store.loading).toBe(false)
  })
  it('starts in the selected game installation when no prior folder is stored', async () => {
    vi.mocked(client.GET).mockResolvedValueOnce({ data: { selected: { path: 'D:/Game' } } } as never)
      .mockResolvedValueOnce({ data: { currentDirectory: 'D:/Game' } } as never)
    const store = useFileSystemStore(); await store.initialize()
    expect(store.filesAndSubdirectories.currentDirectory).toBe('D:/Game')
  })
  it('respects the previously selected manual location', async () => {
    localStorage.setItem(dfDirectoryStorageKey, 'D:/prior %20')
    vi.mocked(client.GET).mockResolvedValue({ data: { currentDirectory: 'D:/prior %20' } } as never)
    const store = useFileSystemStore(); await store.initialize()
    expect(client.GET).toHaveBeenCalledTimes(1)
    expect(store.filesAndSubdirectories.currentDirectory).toBe('D:/prior %20')
  })
  it('keeps the previous listing and reports inaccessible folders', async () => {
    const store = useFileSystemStore(); store.filesAndSubdirectories = { currentDirectory: 'D:/prior' }
    vi.mocked(client.GET).mockResolvedValue({ error: { detail: 'Access denied. Choose another folder.' } } as never)
    await store.loadDirectory('D:/missing')
    expect(store.error).toContain('Access denied'); expect(store.loading).toBe(false)
    expect(store.filesAndSubdirectories.currentDirectory).toBe('D:/prior')
  })
})
