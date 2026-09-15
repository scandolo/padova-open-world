// Central world-space geometry tolerances and vertical relationships.
// Units are metres. Three.js and the gameplay collision system use Y-up.
export const WORLD_UNITS='metres';
export const ROAD_SURFACE_OFFSET=.06;
export const CURB_HEIGHT=.14;
export const TRAM_RAIL_OFFSET=.015;
export const SURFACE_EPSILON=.04;
export const ROAD_ALIGNMENT_TOLERANCE=.05;
export const PHYSICS_ALIGNMENT_TOLERANCE=.06;
export const SEAM_TOLERANCE=.02;
export const GROUND_RENDER_OFFSET=.08;
export const TERRAIN_BASE_TILE=16;
export const TERRAIN_DENSE_TILE=4;
export const ROAD_SKIRT_DEPTH=.45;
export const RIVER_CHANNEL_DEPTH=1.5;
export const RIVER_BANK_BLEND=11;
export const BRIDGE_CLEARANCE=5.4;
export const COLLIDER_AUDIT_RADIUS=500;

export const SURFACE_TYPES=Object.freeze({
  TERRAIN:'terrain',
  ROAD:'road',
  SIDEWALK:'sidewalk',
  TRAM:'tram',
  BRIDGE:'bridge',
  TUNNEL:'tunnel',
  RIVER:'river',
  WATER:'water',
  SPECIAL:'special'
});
