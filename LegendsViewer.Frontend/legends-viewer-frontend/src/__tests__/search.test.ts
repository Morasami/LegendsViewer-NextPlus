import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { useSearchStore } from '../stores/searchStore'
import client from '../apiClient'

vi.mock('../apiClient', () => ({ default: { GET: vi.fn(), POST: vi.fn(), PUT: vi.fn(), DELETE: vi.fn() } }))

const response = (query: string, name: string) => ({
  query,
  worldLoaded: true,
  generation: 7,
  worldName: 'Test World',
  worldAlternativeName: '',
  currentYear: 250,
  page: 1,
  pageSize: 25,
  totalCount: 1,
  totalPages: 1,
  truncated: false,
  indexBuildMilliseconds: 42,
  indexedRecordCount: 5000,
  items: [{
    key: 'figure:12',
    category: 'Figure',
    categoryLabel: 'Historical figures',
    id: 12,
    name,
    type: 'Dwarf',
    description: 'Dwarf historical figure',
    year: 120,
    dateLabel: '120–150',
    url: '/hf/12',
    navigable: true,
    matchKind: 'exact name',
    rank: 1000,
  }],
  availableCategoryCounts: { Figure: 97, Site: 20 },
  matchedCategoryCounts: { Figure: 1 },
  unavailableCategories: ['War'],
  message: null,
  warning: null,
})

const deferred = <T>() => {
  let resolve!: (value: T) => void
  const promise = new Promise<T>(done => { resolve = done })
  return { promise, resolve }
}

beforeEach(() => {
  setActivePinia(createPinia())
  vi.clearAllMocks()
})

describe('Historical search store', () => {
  it('loads index metadata and exposes availability counts', async () => {
    vi.mocked(client.GET).mockResolvedValueOnce({
      data: {
        worldLoaded: true,
        generation: 4,
        worldName: 'Test World',
        currentYear: 200,
        indexBuildMilliseconds: 35,
        indexedRecordCount: 1200,
        estimatedIndexBytes: 2000,
        categoryCounts: { Figure: 97, Site: 18 },
        unavailableCategories: ['War'],
      },
    } as never)

    const store = useSearchStore()
    await store.initialize()

    expect(store.metadata?.worldName).toBe('Test World')
    expect(store.availableCounts).toEqual({ Figure: 97, Site: 18 })
    expect(store.unavailableCategories).toEqual(['War'])
    expect(client.GET).toHaveBeenCalledWith('/api/Search/metadata')
  })

  it('sends bounded filters and pagination and applies the response', async () => {
    vi.mocked(client.GET).mockResolvedValueOnce({ data: response('urist', 'Urist McLegend') } as never)
    const store = useSearchStore()
    store.query = '  urist  '
    store.categories = ['Figure', 'Site']
    store.fromYear = 100
    store.toYear = 200
    store.page = 2
    store.pageSize = 50

    await store.search()

    expect(client.GET).toHaveBeenCalledWith('/api/Search', {
      params: {
        query: {
          q: 'urist',
          categories: 'Figure,Site',
          fromYear: 100,
          toYear: 200,
          page: 2,
          pageSize: 50,
        },
      },
      signal: expect.any(AbortSignal),
    })
    expect(store.results[0]?.name).toBe('Urist McLegend')
    expect(store.totalCount).toBe(1)
    expect(store.totalPages).toBe(1)
    expect(store.loading).toBe(false)
  })

  it('does not let an obsolete response replace a newer result', async () => {
    const oldRequest = deferred<unknown>()
    const newRequest = deferred<unknown>()
    vi.mocked(client.GET)
      .mockImplementationOnce(() => oldRequest.promise as never)
      .mockImplementationOnce(() => newRequest.promise as never)

    const store = useSearchStore()
    store.query = 'old'
    const oldSearch = store.search()
    store.query = 'new'
    const newSearch = store.search()

    newRequest.resolve({ data: response('new', 'Newest Match') })
    await newSearch
    expect(store.results[0]?.name).toBe('Newest Match')

    oldRequest.resolve({ data: response('old', 'Obsolete Match') })
    await oldSearch
    expect(store.results[0]?.name).toBe('Newest Match')
    expect(store.loading).toBe(false)
  })

  it('surfaces validation details from the API', async () => {
    vi.mocked(client.GET).mockResolvedValueOnce({
      error: { title: 'One or more validation errors occurred.', detail: 'From year cannot be after to year.' },
    } as never)
    const store = useSearchStore()
    store.query = 'urist'
    await store.search()
    expect(store.error).toBe('From year cannot be after to year.')
    expect(store.results).toEqual([])
    expect(store.loading).toBe(false)
  })

  it('does not call the API for an empty query and clears old results', async () => {
    vi.mocked(client.GET).mockResolvedValueOnce({ data: response('urist', 'Urist McLegend') } as never)
    const store = useSearchStore()
    store.query = 'urist'
    await store.search()
    expect(store.results).toHaveLength(1)

    store.query = '   '
    await store.search()
    expect(store.results).toEqual([])
    expect(store.totalCount).toBe(0)
    expect(client.GET).toHaveBeenCalledTimes(1)
  })

  it('aborts a pending request when results are cleared', async () => {
    let signal: AbortSignal | undefined
    vi.mocked(client.GET).mockImplementationOnce((_path, options) => {
      signal = options?.signal
      return new Promise(() => {}) as never
    })
    const store = useSearchStore()
    store.query = 'pending'
    void store.search()
    expect(signal?.aborted).toBe(false)
    store.clearResults()
    expect(signal?.aborted).toBe(true)
    expect(store.loading).toBe(false)
  })
})
