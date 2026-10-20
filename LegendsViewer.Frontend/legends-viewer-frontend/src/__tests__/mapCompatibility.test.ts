// @vitest-environment jsdom
import { describe, it, expect } from 'vitest';
import { createMarker } from '../views/Map.vue';
import L from 'leaflet';

describe('site marker compatibility', () => {
  it('keeps a visible interactive marker when game graphics are unavailable', () => {
    const marker = createMarker('Unknown', [0, 0], false) as L.Marker;
    const html = String((marker.options.icon as L.DivIcon).options.html);
    expect(html).toContain('background:#ddd');
    expect(html).not.toContain('game-site-icons');
    expect(marker.options.interactive).toBe(true);
  });
  it('uses the local game sprite endpoint when graphics are available', () => {
    const marker = createMarker('Unknown', [0, 0], true) as L.Marker;
    expect((marker.options.icon as L.DivIcon).options.html).toContain('game-site-icons');
  });
});
