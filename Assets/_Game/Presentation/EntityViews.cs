using System.Collections.Generic;
using UnityEngine;
using Starsoil.Core;

namespace Starsoil.Presentation
{
    /// <summary>
    /// Grey-box views for colonists, ground piles, resource nodes and blueprints.
    /// Sim entities are polled by id each frame (cheap at M1 scale); buildings stay
    /// event-driven in WorldView. Presentation reads sim state, never mutates it.
    /// </summary>
    public sealed class EntityViews : MonoBehaviour
    {
        private const float ColonistHeight = 1.7f;
        private const float PileSize = 0.45f;
        private const float NodeHeight = 0.5f;
        private const float BlueprintHeight = 1.2f;

        private static readonly Color ColonistColor = new Color(0.95f, 0.82f, 0.35f);
        private static readonly Color ColonistCriticalColor = new Color(0.95f, 0.25f, 0.2f);
        private static readonly Color ColonistDeadColor = new Color(0.45f, 0.45f, 0.45f);
        private static readonly Color NodeColor = new Color(0.45f, 0.55f, 0.75f);
        private static readonly Color NodeDesignatedColor = new Color(0.35f, 0.85f, 0.9f);
        private static readonly Color BlueprintColor = new Color(0.35f, 0.6f, 0.95f, 1f);
        private static readonly Color PileColor = new Color(0.85f, 0.65f, 0.35f);

        private World _world;
        private Material _colonistMat;
        private Material _colonistCriticalMat;
        private Material _colonistDeadMat;
        private Material _nodeMat;
        private Material _nodeDesignatedMat;
        private Material _blueprintMat;
        private Material _pileMat;

        private readonly Dictionary<int, GameObject> _colonists = new Dictionary<int, GameObject>();
        private readonly Dictionary<int, GameObject> _piles = new Dictionary<int, GameObject>();
        private readonly Dictionary<int, GameObject> _nodes = new Dictionary<int, GameObject>();
        private readonly Dictionary<int, GameObject> _blueprints = new Dictionary<int, GameObject>();
        private readonly List<int> _scratch = new List<int>();

        public void Init(World world)
        {
            _world = world;
            _colonistMat = Mats.Solid(ColonistColor);
            _colonistCriticalMat = Mats.Solid(ColonistCriticalColor);
            _colonistDeadMat = Mats.Solid(ColonistDeadColor);
            _nodeMat = Mats.Solid(NodeColor);
            _nodeDesignatedMat = Mats.Solid(NodeDesignatedColor);
            _blueprintMat = Mats.Unlit(BlueprintColor);
            _pileMat = Mats.Solid(PileColor);
        }

        public void SwitchWorld(World world)
        {
            _world = world;
            ClearAll(_colonists);
            ClearAll(_bots);
            ClearAll(_combatUnits);
            ClearAll(_piles);
            ClearAll(_nodes);
            ClearAll(_blueprints);
        }

        private void LateUpdate()
        {
            if (_world == null)
            {
                return;
            }
            SyncColonists();
            SyncBots();
            SyncCombatUnits();
            SyncPiles();
            SyncNodes();
            SyncBlueprints();
        }

        private readonly Dictionary<int, GameObject> _bots = new Dictionary<int, GameObject>();
        private Material _botMat;
        private readonly Dictionary<int, GameObject> _combatUnits = new Dictionary<int, GameObject>();
        private Material _playerUnitMat;
        private Material _hostileUnitMat;

        private void SyncCombatUnits()
        {
            if (_playerUnitMat == null)
            {
                _playerUnitMat = Mats.Solid(new Color(0.35f, 0.65f, 0.95f));
                _hostileUnitMat = Mats.Solid(new Color(0.9f, 0.3f, 0.25f));
            }
            foreach (var pair in _world.Battle.Units)
            {
                var unit = pair.Value;
                if (!_combatUnits.TryGetValue(pair.Key, out var go))
                {
                    go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    go.name = "Unit_" + pair.Key + "_" + unit.Side;
                    go.transform.SetParent(transform, false);
                    go.transform.localScale = new Vector3(0.7f, 0.9f, 0.7f);
                    Destroy(go.GetComponent<Collider>());
                    go.GetComponent<MeshRenderer>().sharedMaterial =
                        unit.Side == UnitSide.Player ? _playerUnitMat : _hostileUnitMat;
                    _combatUnits.Add(pair.Key, go);
                }
                float ground = _world.Terrain.GetHeight(unit.X, unit.Y) * GameConstants.MetersPerTerrainStep;
                go.transform.position = new Vector3(unit.X + 0.5f, ground + 0.45f, unit.Y + 0.5f);
                float hpScale = Mathf.Clamp01(unit.Hp / unit.MaxHp);
                go.transform.localScale = new Vector3(0.7f, 0.4f + 0.5f * hpScale, 0.7f);
            }
            RemoveStale(_combatUnits, id => _world.Battle.Units.ContainsKey(id));
        }

        private void SyncBots()
        {
            if (_botMat == null)
            {
                _botMat = Mats.Solid(new Color(0.3f, 0.8f, 0.85f));
            }
            foreach (var pair in _world.Bots.All)
            {
                var bot = pair.Value;
                if (!_bots.TryGetValue(pair.Key, out var go))
                {
                    go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    go.name = "Bot_" + pair.Key;
                    go.transform.SetParent(transform, false);
                    go.transform.localScale = new Vector3(0.8f, 0.5f, 0.8f);
                    Destroy(go.GetComponent<Collider>());
                    go.GetComponent<MeshRenderer>().sharedMaterial = _botMat;
                    _bots.Add(pair.Key, go);
                }
                float ground = _world.Terrain.GetHeight(bot.X, bot.Y) * GameConstants.MetersPerTerrainStep;
                go.transform.position = new Vector3(bot.X + 0.5f, ground + 0.25f, bot.Y + 0.5f);
            }
            RemoveStale(_bots, id => _world.Bots.All.ContainsKey(id));
        }

        private void SyncColonists()
        {
            foreach (var pair in _world.Colonists.All)
            {
                var colonist = pair.Value;
                if (!_colonists.TryGetValue(pair.Key, out var go))
                {
                    go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                    go.name = "Colonist_" + pair.Key;
                    go.transform.SetParent(transform, false);
                    go.transform.localScale = new Vector3(0.7f, ColonistHeight * 0.5f, 0.7f);
                    Destroy(go.GetComponent<Collider>());
                    _colonists.Add(pair.Key, go);
                }
                float ground = _world.Terrain.GetHeight(colonist.X, colonist.Y) * GameConstants.MetersPerTerrainStep;
                bool dead = !colonist.Alive;
                go.transform.position = new Vector3(colonist.X + 0.5f, ground + (dead ? 0.3f : ColonistHeight * 0.5f), colonist.Y + 0.5f);
                go.transform.rotation = dead ? Quaternion.Euler(90f, 0f, 0f) : Quaternion.identity;
                var renderer = go.GetComponent<MeshRenderer>();
                renderer.sharedMaterial = dead
                    ? _colonistDeadMat
                    : colonist.CriticalCause != DeathCause.None ? _colonistCriticalMat : _colonistMat;
            }
            RemoveStale(_colonists, id => _world.Colonists.All.ContainsKey(id));
        }

        private void SyncPiles()
        {
            foreach (var pair in _world.Piles.All)
            {
                var pile = pair.Value;
                if (!_piles.TryGetValue(pair.Key, out var go))
                {
                    go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    go.name = "Pile_" + pair.Key;
                    go.transform.SetParent(transform, false);
                    Destroy(go.GetComponent<Collider>());
                    go.GetComponent<MeshRenderer>().sharedMaterial = _pileMat;
                    _piles.Add(pair.Key, go);
                }
                float ground = _world.Terrain.GetHeight(pile.X, pile.Y) * GameConstants.MetersPerTerrainStep;
                float size = PileSize + Mathf.Min(0.4f, pile.Count * 0.01f);
                go.transform.position = new Vector3(pile.X + 0.5f, ground + size * 0.5f, pile.Y + 0.5f);
                go.transform.localScale = new Vector3(size, size, size);
            }
            RemoveStale(_piles, id => _world.Piles.All.ContainsKey(id));
        }

        private void SyncNodes()
        {
            foreach (var pair in _world.Nodes.All)
            {
                var node = pair.Value;
                if (!_nodes.TryGetValue(pair.Key, out var go))
                {
                    go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    go.name = "Node_" + pair.Key + "_" + node.ItemId;
                    go.transform.SetParent(transform, false);
                    Destroy(go.GetComponent<Collider>());
                    _nodes.Add(pair.Key, go);
                }
                float ground = _world.Terrain.GetHeight(node.X, node.Y) * GameConstants.MetersPerTerrainStep;
                go.transform.position = new Vector3(node.X + 0.5f, ground + NodeHeight * 0.5f, node.Y + 0.5f);
                go.transform.localScale = new Vector3(0.9f, NodeHeight, 0.9f);
                go.GetComponent<MeshRenderer>().sharedMaterial = node.Designated ? _nodeDesignatedMat : _nodeMat;
            }
            RemoveStale(_nodes, id => _world.Nodes.All.ContainsKey(id));
        }

        private void SyncBlueprints()
        {
            foreach (var pair in _world.Blueprints.All)
            {
                var bp = pair.Value;
                if (!_blueprints.TryGetValue(pair.Key, out var go))
                {
                    go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    go.name = "Blueprint_" + pair.Key + "_" + bp.DefId;
                    go.transform.SetParent(transform, false);
                    Destroy(go.GetComponent<Collider>());
                    go.GetComponent<MeshRenderer>().sharedMaterial = _blueprintMat;
                    _blueprints.Add(pair.Key, go);
                }
                BuildingDefs.TryGet(bp.DefId, out var def);
                BuildingSystem.FootprintSize(def, bp.Rotation, out int w, out int h);
                float ground = _world.Terrain.GetHeight(bp.X, bp.Y) * GameConstants.MetersPerTerrainStep;
                float progress = def.BuildWorkTicks > 0 ? Mathf.Clamp01(bp.BuildProgress / def.BuildWorkTicks) : 0f;
                float height = Mathf.Lerp(0.2f, BlueprintHeight, progress);
                go.transform.position = new Vector3(bp.X + w * 0.5f, ground + height * 0.5f, bp.Y + h * 0.5f);
                go.transform.localScale = new Vector3(w, height, h);
            }
            RemoveStale(_blueprints, id => _world.Blueprints.All.ContainsKey(id));
        }

        private void RemoveStale(Dictionary<int, GameObject> views, System.Func<int, bool> stillExists)
        {
            _scratch.Clear();
            foreach (var pair in views)
            {
                if (!stillExists(pair.Key))
                {
                    _scratch.Add(pair.Key);
                }
            }
            foreach (int id in _scratch)
            {
                Destroy(views[id]);
                views.Remove(id);
            }
        }

        private static void ClearAll(Dictionary<int, GameObject> views)
        {
            foreach (var pair in views)
            {
                Destroy(pair.Value);
            }
            views.Clear();
        }
    }

    /// <summary>Shared grey-box material helpers.</summary>
    public static class Mats
    {
        public static Material Solid(Color color)
        {
            var material = MaterialLib.NewLit();
            material.color = color;
            return material;
        }

        public static Material Unlit(Color color)
        {
            var material = MaterialLib.NewUnlit();
            material.color = color;
            return material;
        }
    }
}
