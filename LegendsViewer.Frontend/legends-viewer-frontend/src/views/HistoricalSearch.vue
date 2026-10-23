<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useSearchStore } from '../stores/searchStore'
import {
  searchCategoryOption,
  searchCategoryOptions,
  type SearchCategory,
  type SearchResult,
} from '../types/search'

const store = useSearchStore()
const route = useRoute()
const router = useRouter()
const yearError = ref('')
const initialized = ref(false)
let debounceTimer: ReturnType<typeof setTimeout> | undefined

const pageSizeOptions = [
  { value: 10, title: '10 per page' },
  { value: 25, title: '25 per page' },
  { value: 50, title: '50 per page' },
  { value: 100, title: '100 per page' },
]

const queryModel = computed({
  get: () => store.query,
  set: (value: string | null) => { store.query = value ?? '' },
})

const fromYear = computed({
  get: () => store.fromYear ?? '',
  set: (value: string | number | null) => {
    store.fromYear = parseYear(value)
    validateYears()
  },
})

const toYear = computed({
  get: () => store.toYear ?? '',
  set: (value: string | number | null) => {
    store.toYear = parseYear(value)
    validateYears()
  },
})

const availableCounts = computed<Record<string, number>>(() => store.availableCounts)
const unavailableLabels = computed(() => {
  const labels = new Set(store.unavailableCategories)
  return searchCategoryOptions.filter(option => labels.has(option.value)).map(option => option.shortLabel)
})

const hasFilters = computed(() =>
  store.categories.length > 0 || store.fromYear != null || store.toYear != null,
)
const hasQuery = computed(() => (store.query ?? '').trim().length > 0)
const formattedIndexedRecords = computed(() =>
  new Intl.NumberFormat().format(store.metadata?.indexedRecordCount ?? store.response?.indexedRecordCount ?? 0),
)
const indexBuildMilliseconds = computed(() =>
  store.metadata?.indexBuildMilliseconds ?? store.response?.indexBuildMilliseconds ?? 0,
)
const resultRange = computed(() => {
  if (store.totalCount === 0) return '0'
  const start = (store.page - 1) * store.pageSize + 1
  const end = Math.min(store.page * store.pageSize, store.totalCount)
  return `${start}–${end} of ${new Intl.NumberFormat().format(store.totalCount)}`
})

const parseYear = (value: string | number | null): number | null => {
  if (value === '' || value == null) return null
  const numeric = Number(value)
  return Number.isInteger(numeric) ? numeric : null
}

const validateYears = () => {
  yearError.value = store.fromYear != null && store.toYear != null && store.fromYear > store.toYear
    ? 'From year cannot be after to year.'
    : ''
}

const categoryCount = (category: SearchCategory) => availableCounts.value[category] ?? 0

const isCategoryAvailable = (category: SearchCategory) => categoryCount(category) > 0

const toggleCategory = (category: SearchCategory) => {
  if (!isCategoryAvailable(category) && !store.categories.includes(category)) {
    return
  }
  store.categories = store.categories.includes(category)
    ? store.categories.filter(value => value !== category)
    : [...store.categories, category]
}

const clearFilters = () => {
  store.clearFilters()
  yearError.value = ''
}

const syncUrl = () => {
  const query: Record<string, string> = {}
  if ((store.query ?? '').trim()) query.q = store.query.trim()
  if (store.categories.length > 0) query.categories = store.categories.join(',')
  if (store.fromYear != null) query.fromYear = String(store.fromYear)
  if (store.toYear != null) query.toYear = String(store.toYear)
  if (store.page > 1) query.page = String(store.page)
  if (store.pageSize !== 25) query.pageSize = String(store.pageSize)
  void router.replace({ query })
}

const executeSearch = async () => {
  validateYears()
  if (yearError.value) {
    store.error = yearError.value
    return
  }
  await store.search()
  syncUrl()
}

const scheduleSearch = () => {
  if (!initialized.value) return
  if (debounceTimer) clearTimeout(debounceTimer)
  if (!hasQuery.value) {
    store.clearResults()
    syncUrl()
    return
  }
  debounceTimer = setTimeout(() => { void executeSearch() }, 300)
}

const changePage = async (value: number) => {
  store.page = value
  await executeSearch()
  window.scrollTo({ top: 0, behavior: 'smooth' })
}

const changePageSize = async (value: number) => {
  store.pageSize = value
  store.page = 1
  await executeSearch()
}

const retry = () => { void executeSearch() }

const clearQuery = () => {
  store.query = ''
  store.page = 1
  store.clearResults()
  syncUrl()
}

const resultLinkLabel = (result: SearchResult) =>
  result.category === 'Event' ? 'Open chronicle' : 'Open record'

const formatCount = (value: number | null | undefined) => new Intl.NumberFormat().format(value ?? 0)

const readInitialState = () => {
  store.query = typeof route.query.q === 'string' ? route.query.q : ''
  const rawCategories = typeof route.query.categories === 'string' ? route.query.categories : ''
  const validValues = new Set<string>(searchCategoryOptions.map(option => option.value))
  store.categories = rawCategories
    .split(',')
    .map(value => value.trim())
    .filter((value): value is SearchCategory => validValues.has(value))
  store.fromYear = parseYear(typeof route.query.fromYear === 'string' ? route.query.fromYear : null)
  store.toYear = parseYear(typeof route.query.toYear === 'string' ? route.query.toYear : null)
  const page = Number(route.query.page ?? 1)
  store.page = Number.isInteger(page) && page > 0 ? page : 1
  const pageSize = Number(route.query.pageSize ?? 25)
  store.pageSize = pageSizeOptions.some(option => option.value === pageSize) ? pageSize : 25
}

onMounted(async () => {
  readInitialState()
  validateYears()
  await store.initialize()
  initialized.value = true
  if (hasQuery.value) {
    await executeSearch()
  }
})

onUnmounted(() => {
  if (debounceTimer) clearTimeout(debounceTimer)
})

watch(() => store.query, () => {
  if (!initialized.value) return
  store.page = 1
  scheduleSearch()
})

watch(
  () => [store.categories.join(','), store.fromYear, store.toYear],
  () => {
    if (!initialized.value) return
    store.page = 1
    scheduleSearch()
  },
)

watch(
  () => route.query.q,
  (value) => {
    if (!initialized.value || typeof value !== 'string' || value === store.query) return
    store.query = value
    store.page = 1
    scheduleSearch()
  },
)
</script>

<template>
  <div class="search-page">
    <header class="search-header">
      <div class="search-heading">
        <v-icon icon="mdi-magnify" size="42" color="deep-orange-lighten-2" class="mr-3" />
        <div>
          <h1 class="text-h4">Historical Search</h1>
          <p class="text-body-1 text-medium-emphasis mb-0">
            <template v-if="store.metadata?.worldLoaded">
              Search every indexed record in <strong>{{ store.metadata?.worldName || 'the loaded world' }}</strong>.
            </template>
            <template v-else>
              Search figures, civilizations, places, artifacts, wars, battles and events in the loaded world.
            </template>
          </p>
        </div>
      </div>
      <v-chip
        v-if="store.metadata?.worldLoaded"
        size="small"
        variant="tonal"
        prepend-icon="mdi-database-search-outline"
        class="ml-auto"
      >
        {{ formatCount(store.metadata?.indexedRecordCount) }} indexed records · generation {{ store.metadata?.generation }}
      </v-chip>
    </header>

    <v-alert
      v-if="store.metadata && !store.metadata.worldLoaded"
      type="info"
      variant="tonal"
      class="mb-4"
      title="No world is loaded"
    >
      Open a world from Explore Worlds to search its history.
      <template #append>
        <v-btn to="/" variant="tonal" color="info">Explore Worlds</v-btn>
      </template>
    </v-alert>

    <v-card class="search-controls" variant="outlined">
      <v-card-text>
        <v-text-field
          v-model="queryModel"
          label="Search historical records"
          placeholder="Try a figure, civilization, site, artifact, war, battle or event phrase"
          prepend-inner-icon="mdi-magnify"
          variant="outlined"
          density="comfortable"
          clearable
          autofocus
          hide-details="auto"
          aria-label="Search historical records"
          @keydown.esc="clearQuery"
        >
          <template #append-inner>
            <v-progress-circular v-if="store.loading" indeterminate size="20" width="2" />
          </template>
        </v-text-field>

        <div class="filter-row" role="group" aria-label="Filter by record category">
          <v-chip
            :variant="store.categories.length === 0 ? 'flat' : 'outlined'"
            :color="store.categories.length === 0 ? 'deep-orange-lighten-2' : undefined"
            size="small"
            @click="store.categories = []; store.page = 1"
          >
            All categories
          </v-chip>
          <v-chip
            v-for="option in searchCategoryOptions"
            :key="option.value"
            :variant="store.categories.includes(option.value) ? 'flat' : 'outlined'"
            :color="store.categories.includes(option.value) ? option.color : undefined"
            :disabled="!isCategoryAvailable(option.value) && !store.categories.includes(option.value)"
            size="small"
            :prepend-icon="option.icon"
            :title="isCategoryAvailable(option.value)
              ? `${formatCount(categoryCount(option.value))} ${option.label.toLowerCase()} in this export`
              : `${option.label} are not available in this export`"
            @click="toggleCategory(option.value)"
          >
            {{ option.shortLabel }}
            <span class="category-count">{{ formatCount(categoryCount(option.value)) }}</span>
          </v-chip>
        </div>

        <div class="year-filter-row">
          <span class="text-caption text-medium-emphasis mr-2">Year range</span>
          <v-text-field
            v-model="fromYear"
            type="number"
            label="From"
            density="compact"
            variant="outlined"
            hide-details
            maxlength="7"
            class="year-field"
            aria-label="From year"
          />
          <span class="text-medium-emphasis px-2">to</span>
          <v-text-field
            v-model="toYear"
            type="number"
            label="To"
            density="compact"
            variant="outlined"
            hide-details
            maxlength="7"
            class="year-field"
            aria-label="To year"
          />
          <v-btn
            v-if="hasFilters"
            variant="text"
            size="small"
            prepend-icon="mdi-filter-remove-outline"
            class="ml-2"
            @click="clearFilters"
          >
            Clear filters
          </v-btn>
        </div>

        <v-alert v-if="yearError" type="warning" density="compact" variant="tonal" class="mt-3">
          {{ yearError }}
        </v-alert>
      </v-card-text>
    </v-card>

    <v-alert
      v-if="store.error"
      type="error"
      variant="tonal"
      class="mt-4"
      title="Search could not be completed"
      closable
      @click:close="store.error = ''"
    >
      <div>{{ store.error }}</div>
      <template #append>
        <v-btn variant="tonal" color="error" prepend-icon="mdi-refresh" @click.stop="retry">Retry</v-btn>
      </template>
    </v-alert>

    <v-alert
      v-if="store.truncated"
      type="warning"
      density="compact"
      variant="tonal"
      class="mt-4"
      title="Large result set"
    >
      {{ store.response?.warning || 'Refine your search to see a complete result set.' }}
    </v-alert>

    <v-progress-linear v-if="store.loading" indeterminate color="deep-orange-lighten-2" class="mt-4" />

    <div v-if="store.loading && store.results.length === 0" class="mt-4">
      <v-skeleton-loader type="list-item-three-line@4" />
    </div>

    <template v-else-if="!hasQuery">
      <v-card class="empty-state mt-6" variant="text">
        <v-icon icon="mdi-history" size="72" color="deep-orange-lighten-2" />
        <h2 class="text-h5 mt-3">Search across the history of this world</h2>
        <p class="text-body-1 text-medium-emphasis">
          Names are matched case-insensitively, including partial and prefix matches. Filter by record type or year
          range, then open a result to its existing detail page.
        </p>
        <div class="example-searches">
          <span class="text-caption text-medium-emphasis">Examples</span>
          <v-chip
            v-for="example in ['Urist', 'the grand hill', 'artifact created', 'war']"
            :key="example"
            size="small"
            variant="outlined"
            @click="store.query = example"
          >
            {{ example }}
          </v-chip>
        </div>
      </v-card>
    </template>

    <v-alert
      v-else-if="!store.loading && !store.error && store.totalCount === 0 && store.metadata?.worldLoaded"
      type="info"
      variant="tonal"
      class="mt-4"
      title="No matching historical records"
    >
      <div>
        Nothing in this world matches <strong>{{ (store.query ?? '').trim() }}</strong> with the current filters.
      </div>
      <div v-if="unavailableLabels.length > 0" class="mt-2 text-body-2">
        Not available in this export: {{ unavailableLabels.join(', ') }}.
      </div>
      <div class="mt-2 text-body-2">Try a shorter name, a different spelling, or clear the category and year filters.</div>
    </v-alert>

    <template v-else-if="store.totalCount > 0">
      <div class="results-heading">
        <div>
          <span class="text-body-2 text-medium-emphasis">{{ resultRange }} matching records</span>
          <span v-if="store.truncated" class="text-caption text-warning ml-2">bounded result set</span>
          <span v-if="store.response?.generation" class="text-caption text-disabled ml-2">
            world generation {{ store.response.generation }}
          </span>
        </div>
        <v-select
          :model-value="store.pageSize"
          :items="pageSizeOptions"
          density="compact"
          variant="outlined"
          hide-details
          aria-label="Results per page"
          class="page-size-select"
          @update:model-value="changePageSize"
        />
      </div>

      <div class="result-grid">
        <v-card
          v-for="result in store.results"
          :key="result.key ?? String(result.id ?? result.name)"
          class="result-card"
          variant="outlined"
          :aria-label="`${result.categoryLabel}: ${result.name}`"
        >
          <v-card-item>
            <template #prepend>
              <v-avatar :color="searchCategoryOption(result.category ?? 'Figure').color" variant="tonal" size="40">
                <v-icon :icon="searchCategoryOption(result.category ?? 'Figure').icon" />
              </v-avatar>
            </template>
            <v-card-title class="result-title">
              <router-link v-if="result.navigable && result.url" :to="result.url!" class="result-link">
                {{ result.name }}
              </router-link>
              <span v-else>{{ result.name }}</span>
            </v-card-title>
            <v-card-subtitle class="result-subtitle">
              <v-chip size="x-small" variant="tonal" :color="searchCategoryOption(result.category ?? 'Figure').color">
                {{ result.categoryLabel }}
              </v-chip>
              <span v-if="result.type" class="ml-2">{{ result.type }}</span>
              <span v-if="result.dateLabel" class="ml-2">· {{ result.dateLabel }}</span>
            </v-card-subtitle>
          </v-card-item>
          <v-card-text class="result-description">
            {{ result.description || 'No additional descriptive text is available for this record.' }}
          </v-card-text>
          <v-card-actions>
            <v-chip size="x-small" variant="text" prepend-icon="mdi-target" class="match-chip">
              {{ result.matchKind }}
            </v-chip>
            <span class="record-key">{{ result.key }}</span>
            <v-spacer />
            <v-btn
              v-if="result.navigable && result.url"
              :to="result.url!"
              variant="tonal"
              size="small"
              color="deep-orange-lighten-2"
              :aria-label="`${resultLinkLabel(result)}: ${result.name}`"
            >
              {{ resultLinkLabel(result) }}
            </v-btn>
            <span v-else class="text-caption text-disabled">No dedicated detail page</span>
          </v-card-actions>
        </v-card>
      </div>

      <div class="pagination-row">
        <v-pagination
          :model-value="store.page"
          :length="store.totalPages"
          :total-visible="5"
          density="comfortable"
          @update:model-value="changePage"
        />
      </div>
    </template>

    <footer v-if="store.metadata?.worldLoaded" class="index-footer">
      <v-icon icon="mdi-database-check-outline" size="14" class="mr-1" />
      Search index built once when this world generation loaded: {{ formattedIndexedRecords }} records in
      {{ indexBuildMilliseconds }} ms. Searches use the in-memory index and do not parse export XML.
    </footer>
  </div>
</template>

<style scoped>
.search-page {
  max-width: 1180px;
  margin: 0 auto;
}
.search-header {
  display: flex;
  align-items: flex-start;
  gap: 12px;
  flex-wrap: wrap;
  margin-bottom: 16px;
}
.search-heading {
  display: flex;
  align-items: flex-start;
  min-width: 280px;
}
.search-controls {
  background: rgba(255, 255, 255, 0.02);
}
.filter-row {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  margin-top: 14px;
}
.category-count {
  margin-left: 6px;
  opacity: 0.68;
  font-variant-numeric: tabular-nums;
}
.year-filter-row {
  display: flex;
  align-items: center;
  gap: 4px;
  flex-wrap: wrap;
  margin-top: 14px;
}
.year-field {
  max-width: 130px;
}
.empty-state {
  text-align: center;
  padding: 42px 20px;
}
.example-searches {
  display: flex;
  align-items: center;
  justify-content: center;
  flex-wrap: wrap;
  gap: 8px;
  margin-top: 18px;
}
.results-heading {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  flex-wrap: wrap;
  margin: 20px 0 10px;
}
.page-size-select {
  max-width: 160px;
}
.result-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(340px, 1fr));
  gap: 14px;
}
.result-card {
  display: flex;
  flex-direction: column;
  min-width: 0;
}
.result-title {
  white-space: normal;
  line-height: 1.25;
  overflow-wrap: anywhere;
}
.result-link {
  color: rgb(var(--v-theme-primary));
  text-decoration: none;
}
.result-link:hover {
  text-decoration: underline;
}
.result-subtitle {
  white-space: normal;
  line-height: 1.5;
  padding-top: 4px;
}
.result-description {
  flex: 1 1 auto;
  overflow-wrap: anywhere;
  white-space: pre-line;
  line-height: 1.55;
  padding-top: 4px;
}
.match-chip {
  text-transform: capitalize;
  opacity: 0.8;
}
.record-key {
  margin-left: 4px;
  color: rgba(255, 255, 255, 0.4);
  font-size: 0.68rem;
  font-family: monospace;
  overflow-wrap: anywhere;
}
.pagination-row {
  display: flex;
  justify-content: center;
  margin-top: 20px;
}
.index-footer {
  margin-top: 28px;
  padding-top: 12px;
  border-top: 1px solid rgba(255, 255, 255, 0.08);
  color: rgba(255, 255, 255, 0.5);
  font-size: 0.75rem;
  text-align: center;
}
@media (max-width: 700px) {
  .result-grid {
    grid-template-columns: 1fr;
  }
  .search-header .v-chip {
    margin-left: 0 !important;
  }
  .year-field {
    max-width: 104px;
  }
  .result-card :deep(.v-card-actions) {
    flex-wrap: wrap;
    gap: 6px;
  }
}
</style>
