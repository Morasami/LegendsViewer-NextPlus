import type { components } from '../generated/api-schema'

export type SearchCategory = components['schemas']['HistoricalSearchCategory']
export type SearchResult = components['schemas']['HistoricalSearchResultDto']
export type SearchResponse = components['schemas']['HistoricalSearchResponse']
export type SearchMetadata = components['schemas']['HistoricalSearchMetadataDto']

export interface SearchCategoryOption {
  value: SearchCategory
  label: string
  shortLabel: string
  icon: string
  color: string
}

export const searchCategoryOptions: SearchCategoryOption[] = [
  { value: 'Figure', label: 'Historical figures', shortLabel: 'Figures', icon: 'mdi-account-star-outline', color: 'deep-orange-lighten-2' },
  { value: 'Entity', label: 'Civilizations & entities', shortLabel: 'Civilizations', icon: 'mdi-account-group-outline', color: 'amber-darken-2' },
  { value: 'Site', label: 'Sites', shortLabel: 'Sites', icon: 'mdi-home-city-outline', color: 'light-blue-lighten-2' },
  { value: 'Place', label: 'Places & geography', shortLabel: 'Places', icon: 'mdi-island-variant', color: 'teal-lighten-2' },
  { value: 'Artifact', label: 'Artifacts', shortLabel: 'Artifacts', icon: 'mdi-diamond-stone', color: 'purple-lighten-2' },
  { value: 'War', label: 'Wars', shortLabel: 'Wars', icon: 'mdi-sword-cross', color: 'red-lighten-1' },
  { value: 'Battle', label: 'Battles', shortLabel: 'Battles', icon: 'mdi-chess-bishop', color: 'red-darken-1' },
  { value: 'EventCollection', label: 'Other chronicles', shortLabel: 'Chronicles', icon: 'mdi-book-open-page-variant-outline', color: 'pink-lighten-2' },
  { value: 'Event', label: 'Individual events', shortLabel: 'Events', icon: 'mdi-calendar-clock', color: 'blue-grey-lighten-2' },
  { value: 'Creature', label: 'Creature definitions', shortLabel: 'Creatures', icon: 'mdi-paw-outline', color: 'green-lighten-2' },
]

export const searchCategoryOption = (category: SearchCategory): SearchCategoryOption =>
  searchCategoryOptions.find(option => option.value === category) ?? {
    value: category,
    label: category,
    shortLabel: category,
    icon: 'mdi-magnify',
    color: 'grey',
  }
