# RecipeGen validation report

Generated at (UTC): 2026-08-12 17:23:20

## Counts by family (target table: docs/plan/04)

| family | recipes |
|---|---|
| A | 21 |
| B | 57 |
| C | 41 |
| D | 57 |
| E | 30 |
| F | 79 |
| G | 20 |
| H | 25 |
| total | 330 |

Items: 344 · Verbs: 14 · Forms: 14 · Tech nodes: 96

## Checks

### 1 reachability (start planet + crash-pod inventory) — PASS
_236/330 recipes reachable_

### 2 no orphan items — PASS

### 3 no dead-end recipes — PASS

### 4 energy monotonic (no free-energy loops) — PASS

### 5 mass conservation warning (>1.3x gain, non-gas) — PASS
_warnings only; whitelist decisions happen in M3 review_
- warning: mass gain form_iron_plate: 3 -> 4
- warning: mass gain form_iron_rod: 3 -> 4
- warning: mass gain form_iron_brick: 3 -> 4
- warning: mass gain form_copper_plate: 3 -> 4
- warning: mass gain form_copper_rod: 3 -> 4
- warning: mass gain form_copper_wire: 3 -> 6
- warning: mass gain form_copper_powder: 3 -> 4
- warning: mass gain form_aluminum_plate: 3 -> 4
- warning: mass gain form_aluminum_rod: 3 -> 4
- warning: mass gain form_aluminum_wire: 3 -> 6
- warning: mass gain form_aluminum_mesh: 3 -> 4
- warning: mass gain form_aluminum_powder: 3 -> 4
- warning: mass gain form_titanium_plate: 3 -> 4
- warning: mass gain form_titanium_rod: 3 -> 4
- warning: mass gain form_titanium_powder: 3 -> 4
- warning: mass gain form_nickel_plate: 3 -> 4
- warning: mass gain form_nickel_powder: 3 -> 4
- warning: mass gain form_rare_earth_wire: 3 -> 6
- warning: mass gain form_rare_earth_powder: 3 -> 4
- warning: mass gain form_platinum_powder: 3 -> 4
- warning: mass gain form_steel_plate: 3 -> 4
- warning: mass gain form_steel_rod: 3 -> 4
- warning: mass gain form_steel_mesh: 3 -> 4
- warning: mass gain form_steel_brick: 3 -> 4
- warning: mass gain form_bronze_rod: 3 -> 4
- warning: mass gain form_duralumin_plate: 3 -> 4
- warning: mass gain form_duralumin_rod: 3 -> 4
- warning: mass gain form_duralumin_wire: 3 -> 6
- warning: mass gain form_duralumin_mesh: 3 -> 4
- warning: mass gain form_titanium_alloy_plate: 3 -> 4
- warning: mass gain form_titanium_alloy_rod: 3 -> 4
- warning: mass gain form_invar_plate: 3 -> 4
- warning: mass gain form_invar_rod: 3 -> 4
- warning: mass gain form_electrical_steel_wire: 3 -> 6
- warning: mass gain form_aurite_steel_plate: 3 -> 4
- warning: mass gain form_aurite_steel_rod: 3 -> 4
- warning: mass gain form_superconductor_alloy_wire: 3 -> 6
- warning: mass gain form_superconductor_alloy_mesh: 3 -> 4
- warning: mass gain form_platinum_mesh_alloy_mesh: 3 -> 4
- warning: mass gain make_fiber: 2 -> 4
- warning: mass gain make_carbon_powder: 2 -> 4
- warning: mass gain make_salt: 2 -> 4
- warning: mass gain make_machined_parts: 2 -> 4
- warning: mass gain make_rivet_set: 2 -> 8
- warning: mass gain make_kinetic_round: 4 -> 8
- warning: mass gain make_hydroponic_greens: 3 -> 4
- warning: mass gain make_stim_shot: 3 -> 4
- warning: mass gain make_seed_kit: 3 -> 4
- warning: mass gain make_firework: 4 -> 6
- warning: mass gain make_water_bottle: 3 -> 4
- warning: mass gain make_nutrient_gel: 3 -> 4
- warning: mass gain make_grow_biomass: 2 -> 17

### 6 unlock coverage (1 tech node per recipe, <=14 per node) — PASS

### 7 icon spec parseable — PASS

### 8 localization strings present (zh/en) — PASS

TOTAL recipes: 330, errors: 0, warnings: 52
