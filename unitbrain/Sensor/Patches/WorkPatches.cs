using System;
using HarmonyLib;
using RatopiaUnitBrain.Data;
using RatopiaUnitBrain.Registry;
using UnityEngine;

namespace RatopiaUnitBrain.Sensor.Patches
{
    [HarmonyPatch]
    public static class WorkPatches
    {
        private static GameUnit? GetMaster(WorkTimeGauge gauge)
        {
            if (gauge == null) return null;
            return Traverse.Create(gauge).Field("m_Master").GetValue<GameUnit>() ?? gauge.GetComponentInParent<GameUnit>();
        }

        [HarmonyPatch(typeof(WorkTimeGauge), nameof(WorkTimeGauge.GaugeActive), new Type[] { typeof(GameUnit), typeof(EffectActive), typeof(float) })]
        [HarmonyPostfix]
        public static void WorkTimeGauge_GaugeActive_Postfix(WorkTimeGauge __instance, GameUnit _unit, float _time)
        {
            GameUnit? unit = _unit ?? GetMaster(__instance);
            if (unit == null) return;
            if (UnitRegistry.Instance.GetControlMode(unit) != BrainControlMode.Autonomous)
                return;

            BrainSensor.Instance.RaiseWorkGaugeUpdated(unit, true, 0f, _time);
            BrainSensor.Instance.RaiseTaskStarted(unit, new TaskDescriptor(UnitTaskType.Crafting, unit.Tf != null ? (Vector2)unit.Tf.position : Vector2.zero, 0, "GaugeActive", _time));
        }

        [HarmonyPatch(typeof(WorkTimeGauge), nameof(WorkTimeGauge.GaugeActive_RT), new Type[] { typeof(GameUnit), typeof(EffectActive), typeof(float) })]
        [HarmonyPostfix]
        public static void WorkTimeGauge_GaugeActive_RT_Postfix(WorkTimeGauge __instance, GameUnit _unit, float _time)
        {
            GameUnit? unit = _unit ?? GetMaster(__instance);
            if (unit == null) return;
            if (UnitRegistry.Instance.GetControlMode(unit) != BrainControlMode.Autonomous)
                return;

            BrainSensor.Instance.RaiseWorkGaugeUpdated(unit, true, 0f, _time);
            BrainSensor.Instance.RaiseTaskStarted(unit, new TaskDescriptor(UnitTaskType.Crafting, unit.Tf != null ? (Vector2)unit.Tf.position : Vector2.zero, 0, "GaugeActive_RT", _time));
        }

        [HarmonyPatch(typeof(WorkTimeGauge), nameof(WorkTimeGauge.GaugeActive), new Type[] { typeof(C_Tile), typeof(EffectActive) })]
        [HarmonyPostfix]
        public static void WorkTimeGauge_GaugeActive_Tile_Postfix(WorkTimeGauge __instance, C_Tile tile)
        {
            GameUnit? unit = GetMaster(__instance);
            if (unit == null) return;
            if (UnitRegistry.Instance.GetControlMode(unit) != BrainControlMode.Autonomous)
                return;

            float duration = tile != null && tile.m_HP > 0 ? tile.m_HP : 3.5f;
            Vector2 targetPos = tile != null ? tile.GetPos() : (unit.Tf != null ? (Vector2)unit.Tf.position : Vector2.zero);
            BrainSensor.Instance.RaiseWorkGaugeUpdated(unit, true, 0f, duration);
            BrainSensor.Instance.RaiseTaskStarted(unit, new TaskDescriptor(UnitTaskType.Mining, targetPos, 0, "MiningTile", duration));
        }

        [HarmonyPatch(typeof(WorkTimeGauge), nameof(WorkTimeGauge.GaugeActive), new Type[] { typeof(WorldObject), typeof(EffectActive) })]
        [HarmonyPostfix]
        public static void WorkTimeGauge_GaugeActive_WorldObj_Postfix(WorkTimeGauge __instance, WorldObject w_obj)
        {
            GameUnit? unit = GetMaster(__instance);
            if (unit == null) return;
            if (UnitRegistry.Instance.GetControlMode(unit) != BrainControlMode.Autonomous)
                return;

            Vector2 targetPos = w_obj != null ? (Vector2)w_obj.transform.position : (unit.Tf != null ? (Vector2)unit.Tf.position : Vector2.zero);
            BrainSensor.Instance.RaiseWorkGaugeUpdated(unit, true, 0f, 4f);
            BrainSensor.Instance.RaiseTaskStarted(unit, new TaskDescriptor(UnitTaskType.Gathering, targetPos, 0, "GatheringWorldObj", 4f));
        }

        [HarmonyPatch(typeof(WorkTimeGauge), nameof(WorkTimeGauge.GaugeActive_RT), new Type[] { typeof(WorldObject), typeof(EffectActive), typeof(float) })]
        [HarmonyPostfix]
        public static void WorkTimeGauge_GaugeActive_RT_WorldObj_Postfix(WorkTimeGauge __instance, WorldObject _wobj, float _time)
        {
            GameUnit? unit = GetMaster(__instance);
            if (unit == null) return;
            if (UnitRegistry.Instance.GetControlMode(unit) != BrainControlMode.Autonomous)
                return;

            Vector2 targetPos = _wobj != null ? (Vector2)_wobj.transform.position : (unit.Tf != null ? (Vector2)unit.Tf.position : Vector2.zero);
            BrainSensor.Instance.RaiseWorkGaugeUpdated(unit, true, 0f, _time);
            BrainSensor.Instance.RaiseTaskStarted(unit, new TaskDescriptor(UnitTaskType.Gathering, targetPos, 0, "GatheringWorldObj_RT", _time));
        }

        [HarmonyPatch(typeof(WorkTimeGauge), nameof(WorkTimeGauge.GaugeActive), new Type[] { typeof(AnimalBody), typeof(EffectActive) })]
        [HarmonyPostfix]
        public static void WorkTimeGauge_GaugeActive_Animal_Postfix(WorkTimeGauge __instance, AnimalBody _animal)
        {
            GameUnit? unit = GetMaster(__instance);
            if (unit == null) return;
            if (UnitRegistry.Instance.GetControlMode(unit) != BrainControlMode.Autonomous)
                return;

            Vector2 targetPos = _animal != null && _animal.Tf != null ? (Vector2)_animal.Tf.position : (unit.Tf != null ? (Vector2)unit.Tf.position : Vector2.zero);
            BrainSensor.Instance.RaiseWorkGaugeUpdated(unit, true, 0f, 3f);
            BrainSensor.Instance.RaiseTaskStarted(unit, new TaskDescriptor(UnitTaskType.Gathering, targetPos, _animal?.m_ID ?? 0, "GatheringAnimal", 3f));
        }

        [HarmonyPatch(typeof(WorkTimeGauge), nameof(WorkTimeGauge.StopGauge), new Type[] { typeof(TypeOrder) })]
        [HarmonyPostfix]
        public static void WorkTimeGauge_StopGauge_Postfix(WorkTimeGauge __instance)
        {
            if (__instance == null) return;
            GameUnit? unit = GetMaster(__instance);
            if (unit == null) return;
            if (UnitRegistry.Instance.GetControlMode(unit) != BrainControlMode.Autonomous)
                return;

            BrainSensor.Instance.RaiseWorkGaugeUpdated(unit, false, 0f, 0f);
        }

        [HarmonyPatch(typeof(GameUnit), nameof(GameUnit.SetZ_Order), new Type[] { typeof(bool) })]
        [HarmonyPostfix]
        public static void GameUnit_SetZ_Order_Postfix(GameUnit __instance, bool back)
        {
            if (__instance == null) return;
            if (UnitRegistry.Instance.GetControlMode(__instance) != BrainControlMode.Autonomous)
                return;

            var taskType = back ? UnitTaskType.EnterBuilding : UnitTaskType.ExitBuilding;
            BrainSensor.Instance.RaiseTaskStarted(__instance, new TaskDescriptor(taskType, __instance.Tf != null ? (Vector2)__instance.Tf.position : Vector2.zero, 0, back ? "InsideBuilding" : "OutsideBuilding"));
        }
    }
}
