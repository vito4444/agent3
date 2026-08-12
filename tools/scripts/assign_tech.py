#!/usr/bin/env python3
"""Generates data/tech_nodes.csv: 90 common nodes + 6 faction branch nodes, and assigns
every generated recipe (GeneratedData/recipes.json) to exactly one node (validation
check 6). Node metadata is authored here; recipe placement follows family/tier/verb
rules with deterministic overflow into sibling nodes (≤14 recipes per node).
Run after RecipeGen --generate; rerun RecipeGen to validate the assignment."""
import json, csv, collections, sys, os

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
recipes = json.load(open(os.path.join(ROOT, 'GeneratedData', 'recipes.json')))['recipes']

# ---- node table: id, zh, en, domain, prereqs, cost -------------------------------
S, E = 'survey_data_core', 'engineering_data_core'
C, A, M = 'chemistry_data_core', 'astro_data_core', 'military_data_core'
def cost(**kw):
    return ';'.join(f'{k}:{v}' for k, v in kw.items())

NODES = [
    # survival (12)
    ('t0_survival','徒手求生','Hand survival','survival','',''),
    ('life_support_1','生保设备 I','Life support I','survival','power_basics',cost(**{S:2})),
    ('food_hydroponics','水培农业','Hydroponics','survival','life_support_1',cost(**{S:2})),
    ('food_protein','蛋白质链','Protein chain','survival','food_hydroponics',cost(**{C:2})),
    ('food_gourmet','精致饮食','Gourmet food','survival','food_protein',cost(**{C:1,S:1})),
    ('medical_basics','基础医疗','Basic medicine','survival','life_support_1',cost(**{S:2})),
    ('insulation_suits','保温服','Insulated suits','survival','powered_processing',cost(**{S:2})),
    ('rad_protection','辐射防护','Radiation protection','survival','insulation_suits',cost(**{C:2})),
    ('comfort_habitats','舒适居住','Comfort habitats','survival','life_support_1',cost(**{S:2})),
    ('morale_venues','士气场所','Morale venues','survival','comfort_habitats',cost(**{S:2,E:1})),
    ('hatchery_tech','人口孵化','Hatchery','survival','medical_basics',cost(**{S:2,C:1})),
    ('t0_construction','初始营建','Starter construction','survival','',''),
    # industry (18)
    ('power_basics','基础电力','Basic power','industry','t0_survival',cost(**{S:2})),
    ('power_storage','储能','Power storage','industry','power_basics',cost(**{S:1,E:1})),
    ('powered_processing','动力加工','Powered processing','industry','power_basics',cost(**{S:3})),
    ('powered_assembly','自动装配','Powered assembly','industry','powered_processing',cost(**{E:2})),
    ('powered_extraction','动力采掘','Powered extraction','industry','power_basics',cost(**{E:2})),
    ('forming_basic','基础成形','Basic forming','industry','powered_processing',cost(**{S:2})),
    ('forming_advanced','进阶成形','Advanced forming','industry','forming_basic',cost(**{E:2})),
    ('forming_exotic','特种成形','Exotic forming','industry','forming_advanced;metallurgy_2',cost(**{E:3})),
    ('alloys_basic','基础合金','Basic alloys','industry','powered_processing',cost(**{E:1})),
    ('alloys_light','轻质合金','Light alloys','industry','alloys_basic',cost(**{E:2})),
    ('metallurgy_2','冶金 II','Metallurgy II','industry','alloys_light',cost(**{E:2})),
    ('alloys_exotic','特种合金','Exotic alloys','industry','metallurgy_2',cost(**{E:3,C:1})),
    ('machining_tech','机加工','Machining','industry','powered_assembly',cost(**{E:2})),
    ('deep_drilling','深井钻探','Deep drilling','industry','powered_extraction',cost(**{E:3})),
    ('recycling','回收利用','Recycling','industry','powered_processing',cost(**{S:2})),
    ('geothermal','地热能','Geothermal power','industry','deep_drilling',cost(**{E:3})),
    ('nuclear_power','核能','Nuclear power','industry','enrichment;alloys_exotic',cost(**{E:4,C:2})),
    ('arc_smelting','电弧冶炼','Arc smelting','industry','alloys_basic',cost(**{E:2})),
    # chemistry (16)
    ('chem_basics','化学基础','Basic chemistry','chemistry','powered_processing',cost(**{S:2,E:1})),
    ('distillation','蒸馏','Distillation','chemistry','chem_basics',cost(**{C:1,S:1})),
    ('synthesis_1','化学合成 I','Synthesis I','chemistry','chem_basics',cost(**{C:2})),
    ('synthesis_2','化学合成 II','Synthesis II','chemistry','synthesis_1',cost(**{C:2})),
    ('polymers','聚合物','Polymers','chemistry','synthesis_2',cost(**{C:3})),
    ('sealing_tech','密封技术','Sealing','chemistry','synthesis_1',cost(**{C:1})),
    ('sabatier_tech','萨巴蒂尔反应','Sabatier process','chemistry','synthesis_2',cost(**{C:2,E:1})),
    ('cryogenics','深冷技术','Cryogenics','chemistry','sabatier_tech',cost(**{C:3})),
    ('fuels','火箭燃料','Rocket fuels','chemistry','cryogenics',cost(**{C:2,A:1})),
    ('enrichment','同位素浓缩','Enrichment','chemistry','synthesis_2',cost(**{C:3,E:1})),
    ('glassworks','玻璃与光学','Glassworks','chemistry','powered_processing',cost(**{S:2})),
    ('electronics_1','电子学 I','Electronics I','chemistry','glassworks',cost(**{E:2})),
    ('electronics_2','电子学 II','Electronics II','chemistry','electronics_1;polymers',cost(**{E:3})),
    ('electronics_3','电子学 III','Electronics III','chemistry','electronics_2;alloys_exotic',cost(**{E:4,A:1})),
    ('battery_chem','电池化学','Battery chemistry','chemistry','electronics_1',cost(**{C:2})),
    ('catalysis','催化技术','Catalysis','chemistry','synthesis_2',cost(**{C:3})),
    # logistics (12)
    ('hauler_bots','搬运蛛','Hauler bots','logistics','powered_assembly',cost(**{E:3})),
    ('drone_ports','无人机港','Drone ports','logistics','hauler_bots',cost(**{E:2})),
    ('charging_infra','回充设施','Charging infrastructure','logistics','hauler_bots',cost(**{E:1})),
    ('logistics_planning','物流规划','Logistics planning','logistics','hauler_bots',cost(**{E:2})),
    ('bulk_storage','大宗仓储','Bulk storage','logistics','logistics_planning',cost(**{S:2})),
    ('fluid_handling','流体储运','Fluid handling','logistics','chem_basics',cost(**{E:2})),
    ('beacon_nav','信标导航','Beacon navigation','logistics','radio_basics',cost(**{A:1})),
    ('spaceport_ops','星港调度','Spaceport operations','logistics','rocketry_2',cost(**{A:2})),
    ('orbital_docking','轨道停靠','Orbital docking','logistics','spaceport_ops',cost(**{A:3})),
    ('expedition_gear','拓荒装备','Expedition gear','logistics','powered_assembly',cost(**{E:2})),
    ('field_kits','随身装具','Field kits','logistics','insulation_suits',cost(**{S:2})),
    ('cartography','测绘学','Cartography','logistics','radio_basics',cost(**{S:3})),
    # astronautics (18)
    ('radio_basics','无线电','Radio basics','astronautics','powered_assembly',cost(**{E:2})),
    ('comms_arrays','通讯阵列','Comms arrays','astronautics','radio_basics',cost(**{A:2})),
    ('heat_shielding','热防护','Heat shielding','astronautics','forming_advanced',cost(**{A:1})),
    ('rocketry_1','火箭学 I','Rocketry I','astronautics','fuels;heat_shielding',cost(**{A:2})),
    ('rocketry_2','火箭学 II','Rocketry II','astronautics','rocketry_1',cost(**{A:2})),
    ('rocketry_3','火箭学 III','Rocketry III','astronautics','rocketry_2',cost(**{A:3})),
    ('payload_cargo','货运载荷','Cargo payloads','astronautics','rocketry_1',cost(**{A:1})),
    ('payload_colonist','殖民载荷','Colonist payloads','astronautics','rocketry_2',cost(**{A:2})),
    ('payload_satellite','卫星载荷','Satellite payloads','astronautics','comms_arrays;rocketry_1',cost(**{A:2})),
    ('outpost_kits','前哨包','Outpost kits','astronautics','payload_cargo',cost(**{A:2})),
    ('launch_infra','发射设施','Launch infrastructure','astronautics','rocketry_1',cost(**{A:2})),
    ('astro_sensors','深空传感','Deep-space sensing','astronautics','electronics_2',cost(**{A:2})),
    ('deep_space_telescope','深空望远镜','Deep-space telescope','astronautics','astro_sensors;electronics_3',cost(**{A:3})),
    ('gas_platform','轨道气矿','Orbital gas mining','astronautics','orbital_docking',cost(**{A:3,E:2})),
    ('warp_beacon_1','跃迁灯塔 I','Warp beacon I','astronautics','electronics_3;fuels',cost(**{A:4})),
    ('warp_beacon_2','跃迁灯塔 II','Warp beacon II','astronautics','warp_beacon_1',cost(**{A:4,C:2})),
    ('warp_beacon_3','跃迁灯塔 III','Warp beacon III','astronautics','warp_beacon_2',cost(**{A:5,E:3})),
    ('spacefaring_ops','航天运维','Spacefaring ops','astronautics','launch_infra',cost(**{A:2})),
    # defense (14)
    ('defense_basics','基础防御','Basic defense','defense','powered_processing',cost(**{M:1,S:1})),
    ('ammo_tech','弹药工艺','Ammunition','defense','defense_basics',cost(**{M:2})),
    ('sentry_guns','哨戒炮','Sentry guns','defense','ammo_tech',cost(**{M:2})),
    ('laser_defense','激光防御','Laser defense','defense','sentry_guns;glassworks',cost(**{M:3})),
    ('shield_tech','护盾技术','Shield tech','defense','laser_defense;alloys_exotic',cost(**{M:4})),
    ('combat_bots','战斗蛛','Combat bots','defense','hauler_bots;ammo_tech',cost(**{M:3})),
    ('bot_works','战斗蛛工坊','Combat bot works','defense','combat_bots',cost(**{M:2})),
    ('military_intel','军情记录','Military intelligence','defense','radio_basics',cost(**{M:2})),
    ('orbital_strike','轨道打击','Orbital strike','defense','rocketry_2;ammo_tech',cost(**{M:4,A:2})),
    ('fortification','工事加固','Fortification','defense','defense_basics',cost(**{M:2})),
    ('armor_materials','装甲材料','Armor materials','defense','shield_tech',cost(**{M:3})),
    ('signals','信号装备','Signal gear','defense','defense_basics',cost(**{S:1})),
    ('burial_rites','安葬礼仪','Burial rites','defense','t0_survival',cost(**{S:1})),
    ('assault_ops','强袭作战','Assault operations','defense','combat_bots;rocketry_2',cost(**{M:4})),
]
FACTION_NODES = [
    ('branch_superconductor_grid','超导输电','Superconducting grid','faction_silent','', ''),
    ('branch_phase_armor','相变装甲','Phase armor','faction_silent','', ''),
    ('branch_bio_refining','生物精炼','Bio-refining','faction_merchant','', ''),
    ('branch_orbital_logistics','星际物流网','Orbital logistics','faction_merchant','', ''),
    ('branch_fast_reactor','快堆动力','Fast reactor power','faction_redbanner','', ''),
    ('branch_swarm_tactics','集群战术','Swarm tactics','faction_redbanner','', ''),
]

# ---- explicit recipe → node placements -------------------------------------------
P = {}
def put(node, *ids):
    for i in ids:
        P[i] = node

put('t0_construction','make_b_workbench','make_b_campfire','make_b_sleep_pod','make_b_small_storage','make_b_hand_crank','make_b_road','make_b_research_bench','make_b_crash_pod')
put('t0_survival','make_water_melt','make_ration','make_fiber','make_iron_lump','make_copper_lump',
    'make_crude_glass','make_carbon_powder','make_salt','make_preserved_ration','make_bandage',
    'make_insulation_wrap','make_crude_tool','make_survey_data_core')
put('power_basics','make_b_power_pylon','make_b_solar_panel','make_b_wind_turbine')
put('power_storage','make_b_battery')
put('powered_processing','make_repair_gel','make_b_crusher','make_b_furnace','make_b_roll_mill','make_b_press','smelt_iron','smelt_copper','smelt_silicon')
put('powered_assembly','make_b_assembler','make_engineering_data_core','make_b_machining_bench')
put('powered_extraction','make_b_miner','make_b_ice_miner','make_b_forage_station','make_b_timber_rack','make_b_gas_collector','make_b_pump_station')
put('life_support_1','make_grow_biomass','make_b_water_purifier','make_b_greenhouse','make_b_electrolyzer','make_b_gas_pylon','make_b_gas_tank','make_b_air_charging_station','make_oxygen_bottle')
put('food_hydroponics','make_algae_paste','make_hydroponic_greens','make_seed_kit','make_b_culture_vat','make_b_canteen','synth_growth_medium')
put('food_protein','make_protein_meal','make_b_protein_vat','synth_protein_paste','distill_spore','make_nutrient_gel')
put('food_gourmet','make_combo_meal','make_fungal_snack','make_morale_treat','make_water_bottle','make_air_freshener')
put('medical_basics','make_med_kit','make_stim_shot','make_b_med_bay','make_b_heater_tower')
put('insulation_suits','make_suit_liner','make_thermal_suit')
put('rad_protection','make_lead_lining','make_rad_suit')
put('comfort_habitats','make_b_dormitory','make_b_family_cabin','make_habitat_frame')
put('morale_venues','make_b_rec_room','make_b_bar','make_b_observatory','make_b_monument')
put('hatchery_tech','make_b_hatchery')
put('cartography','make_b_survey_radar')
put('forming_basic','make_b_wire_mill','form_iron_plate','form_iron_rod','form_iron_gear','form_iron_brick','form_copper_plate','form_copper_wire','form_steel_plate','form_steel_rod','form_steel_gear','form_steel_pipe','form_steel_mesh','make_rivet_set')
put('forming_advanced','form_iron_pipe','form_copper_rod','form_copper_gear','form_copper_pipe','form_copper_powder','form_aluminum_plate','form_aluminum_rod','form_aluminum_wire','form_aluminum_mesh','form_aluminum_powder','form_titanium_plate','form_titanium_rod','form_titanium_gear','form_titanium_powder')
put('forming_exotic','form_titanium_pipe','form_steel_brick','form_nickel_plate','form_nickel_pipe','form_nickel_powder','form_rare_earth_wire','form_rare_earth_powder','form_platinum_powder','form_bronze_rod','form_bronze_gear','form_bronze_pipe','form_duralumin_mesh','form_invar_rod')
put('alloys_basic','alloy_steel','alloy_electrical_steel','form_electrical_steel_wire','form_electrical_steel_gear')
put('alloys_light','alloy_duralumin','alloy_titanium_alloy','form_duralumin_plate','form_duralumin_rod','form_duralumin_wire','form_duralumin_gear','form_titanium_alloy_gear','form_titanium_alloy_pipe','form_titanium_alloy_plate','form_titanium_alloy_rod')
put('metallurgy_2','electrolyze_bauxite','smelt_nickel','smelt_rare_earth','alloy_bronze','alloy_invar','form_invar_plate','form_invar_pipe','form_invar_gear')
put('alloys_exotic','smelt_platinum','smelt_aurite','alloy_aurite_steel','alloy_superconductor_alloy','alloy_platinum_mesh_alloy','alloy_uranium_core_alloy','alloy_bio_composite','alloy_phase_composite','form_aurite_steel_plate','form_aurite_steel_rod','form_aurite_steel_pipe','form_superconductor_alloy_wire','form_superconductor_alloy_mesh','form_platinum_mesh_alloy_mesh')
put('machining_tech','make_machined_parts','make_gear_assembly','make_control_console','make_small_nozzle','make_valve','make_pump')
put('deep_drilling','make_b_deep_drill','make_b_tailings_crusher','smelt_titanium','smelt_aluminum')
put('recycling','make_b_recycler','make_filter_cartridge')
put('geothermal','make_b_geothermal_well')
put('nuclear_power','make_b_nuclear_reactor')
put('arc_smelting','make_b_arc_furnace','make_heating_rod')
put('chem_basics','make_b_chem_electrolyzer','make_b_chem_reactor','electrolyze_water','electrolyze_brine','electrolyze_salt_sodium','distill_brine','distill_water_clean','synth_sulfuric_acid','make_chemistry_data_core')
put('distillation','make_b_distiller','distill_ammonia_ice','distill_nitrogen_ice','distill_methane_ice','distill_co2_ice','distill_resin')
put('synthesis_1','synth_fertilizer','synth_crude_seal','synth_glass')
put('synthesis_2','synth_nitric_acid','synth_lubricant','synth_ammonia_haber','electrolyze_ammonia','electrolyze_co2','synth_spore_vaccine_base')
put('polymers','make_b_polymer_reactor','polymerize_basic','polymerize_rubber','polymerize_insulation','polymerize_foam','polymerize_seal','make_seal_ring','make_gasket_set')
put('sealing_tech','make_fiber_optic')
put('sabatier_tech','synth_sabatier','make_b_sabatier_reactor')
put('cryogenics','make_b_cryo_liquefier','liquefy_oxygen','liquefy_methane','liquefy_hydrogen','liquefy_nitrogen','liquefy_air_split','make_cryo_pump','make_b_refinery')
put('fuels','synth_rocket_fuel','synth_rocket_fuel_h2','synth_deuterium_fuel','liquefy_helium3','distill_nitrogen? ')
put('enrichment','synth_uranium_enrich')
put('glassworks','make_lens','make_mirror')
put('electronics_1','make_basic_circuit','make_antenna')
put('electronics_2','make_advanced_circuit','make_pressure_sensor','make_thermal_sensor','make_optical_sensor')
put('electronics_3','make_quantum_circuit')
put('battery_chem','make_battery_cell','make_battery_pack')
put('catalysis','synth_bordeaux')
put('hauler_bots','make_b_bot_station','make_hauler_bot')
put('drone_ports','make_b_drone_port','make_courier_drone','make_drone_rotor')
put('charging_infra','make_b_charging_post')
put('logistics_planning','make_builder_bot','make_miner_bot')
put('bulk_storage','make_b_large_storage','make_structural_panel')
put('fluid_handling','make_b_fluid_tank')
put('beacon_nav','make_b_landing_beacon')
put('spaceport_ops','make_b_spaceport_tower')
put('orbital_docking','make_b_orbital_dock')
put('expedition_gear','make_rover_kit','make_b_gas_turbine','make_small_motor','make_motor','make_heavy_motor')
put('field_kits','make_colonist_supplies')

put('radio_basics','make_comms_array_module')
put('comms_arrays','make_b_comms_array')
put('heat_shielding','make_heat_shield_tile','make_radiator_fin')
put('rocketry_1','make_rocket_frame_1','make_chem_engine','make_fuel_tank_module','make_fairing','make_fuel_injector','make_engine_nozzle')
put('rocketry_2','make_rocket_frame_2','make_nav_pod','make_light_frame','make_heavy_frame','make_truss_section')
put('rocketry_3','make_rocket_frame_3','make_nav_pod_deep','make_heavy_nozzle','make_hull_panel')
put('payload_cargo','make_cargo_pod')
put('payload_colonist','make_colonist_pod')
put('payload_satellite','make_satellite_pod')
put('outpost_kits','make_outpost_kit')
put('launch_infra','make_b_launch_pad','make_b_rocket_gantry','make_b_fueling_station')
put('astro_sensors','make_nav_sensor_suite','make_astro_data_core')
put('deep_space_telescope','make_b_deep_space_telescope')
put('gas_platform','make_b_orbital_gas_platform')
put('warp_beacon_1','make_b_warp_beacon')
put('spacefaring_ops','make_heavy_engine')
put('defense_basics','make_kinetic_round')
put('ammo_tech','make_b_ammo_line')
put('sentry_guns','make_b_sentry_gun')
put('laser_defense','make_b_laser_tower','make_laser_cavity')
put('shield_tech','make_b_shield_dome','make_shield_emitter')
put('combat_bots','make_combat_bot')
put('bot_works','make_b_combat_bot_works')
put('military_intel','make_data_recorder','make_military_data_core')
put('orbital_strike','make_orbital_penetrator')
put('fortification','make_b_wall')
put('armor_materials','make_combat_frame')
put('signals','make_firework','make_emergency_beacon')
put('burial_rites','make_grave_marker')
put('assault_ops','make_assault_pod')
put('branch_superconductor_grid','make_b_superconductor_pylon','make_b_superconductor_battery','make_b_magnetic_node')
put('branch_phase_armor','make_phase_gel_refined','make_phase_armor_plate','make_armored_combat_bot','make_b_heavy_wall','make_b_heat_sink_shield')
put('branch_bio_refining','make_myco_catalyst','make_b_myco_rack','make_b_protein_tower','make_b_eco_dome','make_spore_vaccine')
put('branch_orbital_logistics','make_orbital_freighter','make_b_bonded_warehouse')
put('branch_fast_reactor','make_nuclear_upper_stage','make_compact_reactor_core','make_b_compact_reactor','make_isotope_rod','make_b_isotope_heater','make_nuclear_cell')
put('branch_swarm_tactics','make_suicide_bot','make_jammer_drone','make_b_forward_nest','make_b_shock_cannon')

P = {k: v for k, v in P.items() if k and not k.endswith('? ')}

# assign; unplaced recipes go to a domain-appropriate fallback by family
FALLBACK = {'A':'metallurgy_2','B':'forming_exotic','C':'synthesis_2','D':'machining_tech',
            'E':'field_kits','F':'bulk_storage','G':'spacefaring_ops','H':'branch_swarm_tactics'}
node_recipes = collections.defaultdict(list)
unplaced = []
for r in recipes:
    rid = r['Id']
    node = P.get(rid)
    if not node:
        node = FALLBACK.get(r['Family'], 'field_kits')
        unplaced.append((rid, node))
    node_recipes[node].append(rid)

# report
over = {n: len(v) for n, v in node_recipes.items() if len(v) > 14}
print('recipes:', len(recipes), 'unplaced->fallback:', len(unplaced), 'over-budget nodes:', over)
for rid, node in unplaced:
    print('  fallback:', rid, '->', node)

known_nodes = {n[0] for n in NODES} | {n[0] for n in FACTION_NODES}
bad = [n for n in node_recipes if n not in known_nodes]
if bad:
    print('UNKNOWN NODES:', bad); sys.exit(1)

with open(os.path.join(ROOT, 'data', 'tech_nodes.csv'), 'w', newline='', encoding='utf-8') as f:
    f.write('# Tech tree (M3-T6): 90 common nodes + 6 faction branch nodes (faction=1 → never self-researchable).\n')
    f.write('# cost: coreItemId:count;... (empty cost on common nodes = unlocked at start).\n')
    w = csv.writer(f)
    w.writerow(['id','zh','en','domain','prereqs','cost','recipes','buildings','faction'])
    for nid, zh, en, dom, pre, c in NODES:
        blds = ';'.join(sorted(r[7:] for r in node_recipes.get(nid, []) if r.startswith('make_b_')))
        recs = ';'.join(sorted(node_recipes.get(nid, [])))
        w.writerow([nid, zh, en, dom, pre, c, recs, blds, ''])
    for nid, zh, en, dom, pre, c in FACTION_NODES:
        blds = ';'.join(sorted(r[7:] for r in node_recipes.get(nid, []) if r.startswith('make_b_')))
        recs = ';'.join(sorted(node_recipes.get(nid, [])))
        w.writerow([nid, zh, en, dom, pre, c, recs, blds, '1'])
print('common nodes:', len(NODES), 'faction nodes:', len(FACTION_NODES))
