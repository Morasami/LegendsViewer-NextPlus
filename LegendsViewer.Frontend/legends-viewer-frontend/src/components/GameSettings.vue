<script setup lang="ts">
import { ref } from 'vue'
import { useGameSettingsStore } from '../stores/gameSettingsStore'
const settings = useGameSettingsStore()
const open = ref(false)
function show() { open.value = true; settings.detect() }
</script>

<template>
  <v-btn prepend-icon="mdi-cog" variant="tonal" class="ma-2" @click="show">Dwarf Fortress Settings</v-btn>
  <v-dialog v-model="open" max-width="800" :persistent="settings.busy">
    <v-card title="Dwarf Fortress Settings">
      <v-card-text>
        <p>Choose the game installation used for Premium world maps and site sprites.</p>
        <p class="my-3">Current: {{ settings.configuration?.selected?.path ?? 'No installation selected' }}
          ({{ settings.configuration?.source ?? 'None' }})</p>
        <v-alert v-if="settings.configuration?.warning" type="warning" class="mb-3">{{ settings.configuration.warning }}</v-alert>
        <v-alert v-if="settings.error" type="error" class="mb-3" role="alert">{{ settings.error }}</v-alert>
        <v-alert v-if="settings.message" type="success" class="mb-3">{{ settings.message }}</v-alert>
        <v-btn :disabled="settings.busy" class="mb-3" @click="settings.detect">Detect Again</v-btn>
        <v-list v-if="settings.configuration?.candidates?.length" aria-label="Detected installations">
          <v-list-item v-for="candidate in settings.configuration.candidates" :key="candidate.path ?? ''"
            :title="candidate.path ?? ''" :subtitle="candidate.status ?? ''" :disabled="settings.busy"
            @click="settings.choose(candidate.path ?? '')" />
        </v-list>
        <v-text-field :model-value="settings.path" label="Absolute Dwarf Fortress game path" :disabled="settings.busy"
          class="mt-3" @update:model-value="settings.choose($event)" />
        <v-alert v-if="settings.validation" :type="settings.validation.valid ? (settings.validation.premiumGraphicsAvailable ? 'success' : 'warning') : 'error'"
          class="mb-3">{{ settings.validation.status }}</v-alert>
        <v-btn :disabled="settings.busy" class="mr-2" @click="settings.browse(settings.path || undefined)">Browse</v-btn>
        <v-btn :disabled="settings.busy || !settings.path" class="mr-2" @click="settings.validate">Validate</v-btn>
        <v-btn :disabled="settings.busy || !settings.path" color="primary" @click="settings.save">Save installation</v-btn>
        <v-card v-if="settings.directory" variant="outlined" class="mt-4">
          <v-card-text>
            <p>{{ settings.directory.currentDirectory }}</p>
            <v-btn :disabled="settings.busy" @click="settings.browse('mounts')">Drives / mounts</v-btn>
            <v-btn v-if="settings.directory.parentDirectory" :disabled="settings.busy"
              @click="settings.browse(settings.directory.parentDirectory)">Up</v-btn>
            <v-btn :disabled="settings.busy || settings.directory.currentDirectory === 'mounts'"
              @click="settings.choose(settings.directory.currentDirectory ?? '')">Use this folder</v-btn>
            <v-list max-height="240" aria-label="Folders">
              <v-list-item v-for="folder in settings.directory.subdirectories" :key="folder" :title="folder"
                prepend-icon="mdi-folder" :disabled="settings.busy" @click="settings.browse(folder)" />
            </v-list>
          </v-card-text>
        </v-card>
        <p class="mt-4">Premium maps require a matching world_map.csv export. Without game graphics, existing map fallbacks remain available.</p>
      </v-card-text>
      <v-card-actions><v-spacer /><v-btn :disabled="settings.busy" @click="open = false">Close</v-btn></v-card-actions>
    </v-card>
  </v-dialog>
</template>
