import { defineStore } from 'pinia'
import client from '../apiClient'
import type { SearchCategory, SearchMetadata, SearchResponse, SearchResult } from '../types/search'

const problemMessage = (error: unknown): string => {
  if (typeof error === 'string') {
    return error
  }
  if (!error || typeof error !== 'object') {
    return 'Search could not be completed.'
  }
  const problem = error as { detail?: string; title?: string; errors?: Record<string, string[]> }
  const fieldMessages = Object.values(problem.errors ?? {}).flat().filter(Boolean)
  if (fieldMessages.length > 0) {
    return fieldMessages.join(' ')
  }
  return problem.detail ?? problem.title ?? 'Search could not be completed.'
}

export const useSearchStore = defineStore('search', {
  state: () => ({
    query: '' as string,
    categories: [] as SearchCategory[],
    fromYear: null as number | null,
    toYear: null as number | null,
    page: 1 as number,
    pageSize: 25 as number,
    metadata: null as SearchMetadata | null,
    response: null as SearchResponse | null,
    results: [] as SearchResult[],
    totalCount: 0 as number,
    totalPages: 0 as number,
    truncated: false as boolean,
    loading: false as boolean,
    error: '' as string,
    requestVersion: 0 as number,
    controller: null as AbortController | null,
  }),
  getters: {
    availableCounts(state): Record<string, number> {
      return state.metadata?.categoryCounts ?? state.response?.availableCategoryCounts ?? {}
    },
    unavailableCategories(state): string[] {
      return state.metadata?.unavailableCategories ?? state.response?.unavailableCategories ?? []
    },
  },
  actions: {
    async initialize() {
      try {
        const { data, error } = await client.GET('/api/Search/metadata')
        if (error) {
          this.error = problemMessage(error)
          return
        }
        if (data) {
          this.metadata = data as SearchMetadata
          if (!this.metadata.worldLoaded) {
            this.results = []
            this.response = null
            this.totalCount = 0
            this.totalPages = 0
          }
        }
      } catch (error) {
        this.error = problemMessage(error)
      }
    },
    async search() {
      const query = (this.query ?? '').trim()
      const version = ++this.requestVersion
      this.controller?.abort()
      this.controller = new AbortController()
      this.error = ''

      if (!query) {
        this.response = null
        this.results = []
        this.totalCount = 0
        this.totalPages = 0
        this.truncated = false
        this.loading = false
        return
      }

      this.loading = true
      try {
        const { data, error } = await client.GET('/api/Search', {
          params: {
            query: {
              q: query,
              categories: this.categories.length > 0 ? this.categories.join(',') : undefined,
              fromYear: this.fromYear ?? undefined,
              toYear: this.toYear ?? undefined,
              page: this.page,
              pageSize: this.pageSize,
            },
          },
          signal: this.controller.signal,
        })

        if (version !== this.requestVersion) {
          return
        }
        if (error) {
          this.error = problemMessage(error)
          this.results = []
          this.totalCount = 0
          this.totalPages = 0
          return
        }
        if (!data) {
          this.error = 'The search service returned no response.'
          return
        }

        this.response = data as SearchResponse
        this.results = (data.items ?? []) as SearchResult[]
        this.totalCount = data.totalCount ?? 0
        this.totalPages = data.totalPages ?? 0
        this.truncated = data.truncated ?? false
        if (!this.metadata && data.worldLoaded) {
          this.metadata = {
            worldLoaded: data.worldLoaded,
            generation: data.generation,
            worldName: data.worldName,
            worldAlternativeName: data.worldAlternativeName,
            currentYear: data.currentYear,
            indexBuildMilliseconds: data.indexBuildMilliseconds,
            indexedRecordCount: data.indexedRecordCount,
            estimatedIndexBytes: 0,
            categoryCounts: data.availableCategoryCounts ?? {},
            unavailableCategories: data.unavailableCategories ?? [],
          }
        }
      } catch (error) {
        if (version !== this.requestVersion || (error instanceof DOMException && error.name === 'AbortError')) {
          return
        }
        this.error = problemMessage(error)
        this.results = []
        this.totalCount = 0
        this.totalPages = 0
      } finally {
        if (version === this.requestVersion) {
          this.loading = false
        }
      }
    },
    clearResults() {
      this.requestVersion++
      this.controller?.abort()
      this.controller = null
      this.response = null
      this.results = []
      this.totalCount = 0
      this.totalPages = 0
      this.truncated = false
      this.loading = false
      this.error = ''
    },
    clearFilters() {
      this.categories = []
      this.fromYear = null
      this.toYear = null
      this.page = 1
    },
  },
})
