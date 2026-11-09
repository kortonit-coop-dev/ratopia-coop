using System;
using System.Collections.Generic;
using RatopiaUnitBrain.Data;
using UnityEngine;

namespace RatopiaUnitBrain.Registry
{
    public class UnitRegistry
    {
        private static UnitRegistry? _instance;
        public static UnitRegistry Instance => _instance ??= new UnitRegistry();

        private readonly Dictionary<NetworkUnitId, GameUnit> _units = new Dictionary<NetworkUnitId, GameUnit>(256);
        private readonly Dictionary<GameUnit, NetworkUnitId> _unitToId = new Dictionary<GameUnit, NetworkUnitId>(256);
        private readonly Dictionary<int, AnimalBody> _animals = new Dictionary<int, AnimalBody>(128);
        private readonly Dictionary<GameUnit, BrainControlMode> _controlModes = new Dictionary<GameUnit, BrainControlMode>(256);

        /// <summary>
        /// When true (on Co-op Client), all newly registered units (except LocalExcludeId1/2)
        /// and all animals automatically enter RemotePuppet mode with local AI completely disabled.
        /// </summary>
        public bool DefaultPuppetMode { get; set; } = false;
        public int LocalExcludeId1 { get; set; } = 777;
        public int LocalExcludeId2 { get; set; } = 778;

        public event Action<GameUnit, NetworkUnitId>? OnUnitRegistered;
        public event Action<GameUnit, NetworkUnitId>? OnUnitUnregistered;
        public event Action<AnimalBody, int>? OnAnimalRegistered;
        public event Action<AnimalBody, int>? OnAnimalUnregistered;

        public void Register(GameUnit unit)
        {
            if (unit == null) return;
            NetworkUnitId unitId = NetworkUnitId.FromUnit(unit);
            if (unitId.Kind == NetworkUnitKind.Unknown) return;

            bool shouldPuppet = DefaultPuppetMode && unit.m_ID != LocalExcludeId1 && unit.m_ID != LocalExcludeId2 && !(unit is T_Queen);

            lock (_units)
            {
                if (_unitToId.TryGetValue(unit, out var oldId) && oldId != unitId)
                {
                    _units.Remove(oldId);
                }
                _units[unitId] = unit;
                _unitToId[unit] = unitId;
                if (shouldPuppet)
                {
                    _controlModes[unit] = BrainControlMode.RemotePuppet;
                }
                else if (!_controlModes.ContainsKey(unit))
                {
                    _controlModes[unit] = BrainControlMode.Autonomous;
                }
            }

            if (shouldPuppet)
            {
                Puppeteer.BrainPuppeteer.Instance.ShutdownAutonomousCoroutines(unit);
            }

            OnUnitRegistered?.Invoke(unit, unitId);
        }

        public void MarkAllAsPuppetsExcept(int excludeId1, int excludeId2 = -1)
        {
            DefaultPuppetMode = true;
            LocalExcludeId1 = excludeId1;
            LocalExcludeId2 = excludeId2;

            List<GameUnit> toShutdown = new List<GameUnit>();
            lock (_units)
            {
                foreach (var kvp in _units)
                {
                    var u = kvp.Value;
                    if (u != null && u.m_ID != excludeId1 && u.m_ID != excludeId2 && !(u is T_Queen))
                    {
                        _controlModes[u] = BrainControlMode.RemotePuppet;
                        toShutdown.Add(u);
                    }
                }
            }

            for (int i = 0; i < toShutdown.Count; i++)
            {
                Puppeteer.BrainPuppeteer.Instance.ShutdownAutonomousCoroutines(toShutdown[i]);
            }

            var animals = GetAllAnimals();
            for (int i = 0; i < animals.Count; i++)
            {
                Puppeteer.BrainPuppeteer.Instance.ShutdownAnimalCoroutines(animals[i]);
            }
        }

        public void ResetToAutonomous()
        {
            DefaultPuppetMode = false;
            lock (_units)
            {
                var keys = new List<GameUnit>(_controlModes.Keys);
                for (int i = 0; i < keys.Count; i++)
                {
                    _controlModes[keys[i]] = BrainControlMode.Autonomous;
                }
            }
            Puppeteer.BrainPuppeteer.Instance.ClearAllPuppetStates();
        }

        public void RegisterAnimal(AnimalBody animal)
        {
            if (animal == null || animal.m_ID == 0) return;

            lock (_animals)
            {
                _animals[animal.m_ID] = animal;
            }

            if (DefaultPuppetMode)
            {
                Puppeteer.BrainPuppeteer.Instance.ShutdownAnimalCoroutines(animal);
            }

            OnAnimalRegistered?.Invoke(animal, animal.m_ID);
        }

        public void Unregister(GameUnit unit)
        {
            if (unit == null) return;

            NetworkUnitId unitId;
            lock (_units)
            {
                if (_unitToId.TryGetValue(unit, out unitId))
                {
                    _unitToId.Remove(unit);
                    _units.Remove(unitId);
                }
                _controlModes.Remove(unit);
            }

            if (unitId.Kind != NetworkUnitKind.Unknown)
            {
                OnUnitUnregistered?.Invoke(unit, unitId);
            }
        }

        public void UnregisterAnimal(AnimalBody animal)
        {
            if (animal == null) return;

            lock (_animals)
            {
                _animals.Remove(animal.m_ID);
            }

            OnAnimalUnregistered?.Invoke(animal, animal.m_ID);
        }

        public bool TryGetUnit(NetworkUnitId id, out GameUnit unit)
        {
            lock (_units)
            {
                if (_units.TryGetValue(id, out unit!) && unit != null && unit.gameObject != null)
                {
                    return true;
                }
            }

            // Fallback: search GameMgr._T_UnitMgr
            unit = FindUnitInGameMgr(id);
            if (unit != null)
            {
                Register(unit);
                return true;
            }

            unit = null!;
            return false;
        }

        public bool TryGetUnit(NetworkUnitKind kind, int id, out GameUnit unit)
        {
            return TryGetUnit(new NetworkUnitId(kind, id), out unit);
        }

        public bool TryGetAnimal(int id, out AnimalBody animal)
        {
            lock (_animals)
            {
                if (_animals.TryGetValue(id, out animal!) && animal != null && animal.gameObject != null)
                {
                    return true;
                }
            }

            // Fallback: search GameMgr._AnimalMgr
            animal = FindAnimalInGameMgr(id);
            if (animal != null)
            {
                RegisterAnimal(animal);
                return true;
            }

            animal = null!;
            return false;
        }

        public void SetControlMode(GameUnit unit, BrainControlMode mode)
        {
            if (unit == null) return;
            lock (_units)
            {
                _controlModes[unit] = mode;
            }
            if (mode == BrainControlMode.RemotePuppet)
            {
                Puppeteer.BrainPuppeteer.Instance.ShutdownAutonomousCoroutines(unit);
            }
        }

        public void SetControlMode(NetworkUnitId id, BrainControlMode mode)
        {
            if (TryGetUnit(id, out var unit))
            {
                SetControlMode(unit, mode);
            }
        }

        public BrainControlMode GetControlMode(GameUnit unit)
        {
            if (unit == null) return BrainControlMode.Autonomous;
            lock (_units)
            {
                if (_controlModes.TryGetValue(unit, out var mode))
                {
                    return mode;
                }
            }
            return BrainControlMode.Autonomous;
        }

        public List<GameUnit> GetAllUnits()
        {
            lock (_units)
            {
                var list = new List<GameUnit>(_units.Count);
                foreach (var kvp in _units)
                {
                    if (kvp.Value != null && kvp.Value.gameObject != null && kvp.Value.gameObject.activeInHierarchy)
                    {
                        list.Add(kvp.Value);
                    }
                }
                return list;
            }
        }

        public List<AnimalBody> GetAllAnimals()
        {
            lock (_animals)
            {
                var list = new List<AnimalBody>(_animals.Count);
                foreach (var kvp in _animals)
                {
                    if (kvp.Value != null && kvp.Value.gameObject != null && kvp.Value.gameObject.activeInHierarchy)
                    {
                        list.Add(kvp.Value);
                    }
                }
                return list;
            }
        }

        public void Clear()
        {
            lock (_units)
            {
                _units.Clear();
                _unitToId.Clear();
                _controlModes.Clear();
            }
            lock (_animals)
            {
                _animals.Clear();
            }
        }

        private GameUnit FindUnitInGameMgr(NetworkUnitId id)
        {
            if (GameMgr.Instance == null || GameMgr.Instance._T_UnitMgr == null) return null!;
            var unitMgr = GameMgr.Instance._T_UnitMgr;

            switch (id.Kind)
            {
                case NetworkUnitKind.Queen:
                    if (unitMgr.m_Queen != null && (unitMgr.m_Queen.m_ID == id.Id || id.Id == 777))
                        return unitMgr.m_Queen;
                    break;

                case NetworkUnitKind.Citizen:
                case NetworkUnitKind.Alliance:
                case NetworkUnitKind.SkullRat:
                case NetworkUnitKind.Ant:
                    if (unitMgr.List_Citizen != null)
                    {
                        var c = unitMgr.List_Citizen.Find(u => u != null && u.m_ID == id.Id);
                        if (c != null) return c;
                    }
                    if (unitMgr.List_AllTeam != null)
                    {
                        var t = unitMgr.List_AllTeam.Find(u => u != null && u.m_ID == id.Id);
                        if (t != null) return t;
                    }
                    break;

                case NetworkUnitKind.Enemy:
                    if (unitMgr.List_AllEnemy != null)
                    {
                        var e = unitMgr.List_AllEnemy.Find(u => u != null && u.m_ID == id.Id);
                        if (e != null) return e;
                    }
                    break;

                case NetworkUnitKind.GBot:
                    if (unitMgr.List_GBot != null)
                    {
                        var g = unitMgr.List_GBot.Find(u => u != null && u.m_ID == id.Id);
                        if (g != null) return g;
                    }
                    break;

                case NetworkUnitKind.Drone:
                    if (unitMgr.List_MDrone != null)
                    {
                        var d = unitMgr.List_MDrone.Find(u => u != null && u.m_ID == id.Id);
                        if (d != null) return d;
                    }
                    break;
            }

            return null!;
        }

        private AnimalBody FindAnimalInGameMgr(int id)
        {
            if (GameMgr.Instance == null || GameMgr.Instance._AnimalMgr == null) return null!;
            var animalList = GameMgr.Instance._AnimalMgr.List_Animal;
            if (animalList != null)
            {
                return animalList.Find(a => a != null && a.m_ID == id);
            }
            return null!;
        }
    }
}
