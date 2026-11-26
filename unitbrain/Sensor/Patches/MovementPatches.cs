using System;
using System.Collections.Generic;
using HarmonyLib;
using RatopiaUnitBrain.Data;
using RatopiaUnitBrain.Registry;
using UnityEngine;

namespace RatopiaUnitBrain.Sensor.Patches
{
    [HarmonyPatch]
    public static class MovementPatches
    {
        [HarmonyPatch(typeof(GameUnit), nameof(GameUnit.Move), new Type[] { typeof(List<Vector2>) })]
        [HarmonyPostfix]
        public static void GameUnit_Move_Postfix(GameUnit __instance, List<Vector2> _list)
        {
            if (__instance == null || _list == null || _list.Count == 0) return;

            // Only fire sensor if this unit is autonomously controlled (not puppeted from remote)
            if (UnitRegistry.Instance.GetControlMode(__instance) != BrainControlMode.Autonomous)
                return;

            BrainSensor.Instance.RaisePathCommitted(__instance, _list, __instance.m_Speed);
        }

        [HarmonyPatch(typeof(GameEnemy), nameof(GameEnemy.Move), new Type[] { typeof(List<Vector2>) })]
        [HarmonyPostfix]
        public static void GameEnemy_Move_Postfix(GameEnemy __instance, List<Vector2> _list)
        {
            if (__instance == null || _list == null || _list.Count == 0) return;

            if (UnitRegistry.Instance.GetControlMode(__instance) != BrainControlMode.Autonomous)
                return;

            BrainSensor.Instance.RaisePathCommitted(__instance, _list, __instance.m_Speed);
        }

        [HarmonyPatch(typeof(MiningDrone), nameof(MiningDrone.Move), new Type[] { typeof(List<Vector2>) })]
        [HarmonyPostfix]
        public static void MiningDrone_Move_Postfix(MiningDrone __instance, List<Vector2> _list)
        {
            if (__instance == null || _list == null || _list.Count == 0) return;

            if (UnitRegistry.Instance.GetControlMode(__instance) != BrainControlMode.Autonomous)
                return;

            BrainSensor.Instance.RaisePathCommitted(__instance, _list, __instance.m_Speed);
        }

        [HarmonyPatch(typeof(GameUnit), nameof(GameUnit.KillMoving), new Type[] { typeof(bool) })]
        [HarmonyPostfix]
        public static void GameUnit_KillMoving_Postfix(GameUnit __instance)
        {
            if (__instance == null) return;
            if (UnitRegistry.Instance.GetControlMode(__instance) != BrainControlMode.Autonomous)
                return;

            if (!__instance.IsMoveState())
            {
                BrainSensor.Instance.RaiseTaskStarted(__instance, new TaskDescriptor(UnitTaskType.Idle, __instance.Tf != null ? (Vector2)__instance.Tf.position : Vector2.zero));
            }
        }
    }
}
