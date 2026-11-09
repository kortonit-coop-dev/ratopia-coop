using System;
using HarmonyLib;
using RatopiaUnitBrain.Data;
using RatopiaUnitBrain.Registry;
using UnityEngine;

namespace RatopiaUnitBrain.Sensor.Patches
{
    [HarmonyPatch]
    public static class InventoryPatches
    {
        [HarmonyPatch(typeof(GameUnit), nameof(GameUnit.StickImgSet), new Type[] { typeof(Sprite), typeof(bool) })]
        [HarmonyPostfix]
        public static void GameUnit_StickImgSet_Postfix(GameUnit __instance)
        {
            if (__instance == null) return;
            if (UnitRegistry.Instance.GetControlMode(__instance) != BrainControlMode.Autonomous)
                return;

            TileType item = TileType.None;
            int count = 0;
            if (__instance.List_Gathering != null && __instance.List_Gathering.Count > 0)
            {
                item = __instance.List_Gathering[0].m_Type;
                count = __instance.List_Gathering.Count;
            }

            BrainSensor.Instance.RaiseBackpackChanged(__instance, item, count);
        }

        [HarmonyPatch(typeof(GameUnit), nameof(GameUnit.Drop_GatheringList))]
        [HarmonyPostfix]
        public static void GameUnit_Drop_GatheringList_Postfix(GameUnit __instance)
        {
            if (__instance == null) return;
            if (UnitRegistry.Instance.GetControlMode(__instance) != BrainControlMode.Autonomous)
                return;

            BrainSensor.Instance.RaiseBackpackChanged(__instance, TileType.None, 0);
        }

        [HarmonyPatch(typeof(GameUnit), nameof(GameUnit.Add_GatheringObj), new Type[] { typeof(TileSt_Info) })]
        [HarmonyPostfix]
        public static void GameUnit_Add_GatheringObj_Postfix(GameUnit __instance, TileSt_Info _info)
        {
            if (__instance == null || _info == null) return;
            if (UnitRegistry.Instance.GetControlMode(__instance) != BrainControlMode.Autonomous)
                return;

            int count = __instance.List_Gathering != null ? __instance.List_Gathering.Count : 1;
            BrainSensor.Instance.RaiseBackpackChanged(__instance, _info.m_Type, count);
        }
    }
}
