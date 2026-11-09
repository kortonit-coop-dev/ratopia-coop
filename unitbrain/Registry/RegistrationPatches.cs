using System;
using HarmonyLib;
using UnityEngine;

namespace RatopiaUnitBrain.Registry
{
    [HarmonyPatch]
    public static class RegistrationPatches
    {
        [HarmonyPatch(typeof(GameUnit), nameof(GameUnit.Init))]
        [HarmonyPostfix]
        public static void GameUnit_Init_Postfix(GameUnit __instance)
        {
            if (__instance == null) return;
            UnitRegistry.Instance.Register(__instance);
        }

        [HarmonyPatch(typeof(T_Citizen), nameof(T_Citizen.LoadCitizen), new Type[] { typeof(Vector2), typeof(int), typeof(Gender), typeof(bool), typeof(int) })]
        [HarmonyPostfix]
        public static void TCitizen_LoadCitizen_Postfix(T_Citizen __instance)
        {
            if (__instance == null) return;
            // Re-register with the loaded save index m_ID
            UnitRegistry.Instance.Register(__instance);
        }

        [HarmonyPatch(typeof(GameEnemy), nameof(GameEnemy.LoadSetting), new Type[] { typeof(Enemy_Data) })]
        [HarmonyPostfix]
        public static void GameEnemy_LoadSetting_Postfix(GameEnemy __instance, Enemy_Data _data)
        {
            if (__instance == null || _data == null) return;
            // Restore persistent network ID from save data
            __instance.m_ID = _data.m_ID;
            UnitRegistry.Instance.Register(__instance);
        }

        [HarmonyPatch(typeof(AnimalBody), nameof(AnimalBody.MakeAnimal), new Type[] { typeof(AnimalName), typeof(Vector2) })]
        [HarmonyPostfix]
        public static void AnimalBody_MakeAnimal_Postfix(AnimalBody __instance)
        {
            if (__instance == null) return;
            UnitRegistry.Instance.RegisterAnimal(__instance);
        }

        [HarmonyPatch(typeof(AnimalBody), nameof(AnimalBody.LoadSetting), new Type[] { typeof(AnimalData) })]
        [HarmonyPostfix]
        public static void AnimalBody_LoadSetting_Postfix(AnimalBody __instance)
        {
            if (__instance == null) return;
            UnitRegistry.Instance.RegisterAnimal(__instance);
        }

        [HarmonyPatch(typeof(AnimalBody), nameof(AnimalBody.SetDeath), new Type[] { typeof(bool), typeof(Unit_Attacekd_Tag) })]
        [HarmonyPostfix]
        public static void AnimalBody_SetDeath_Postfix(AnimalBody __instance)
        {
            if (__instance == null) return;
            UnitRegistry.Instance.UnregisterAnimal(__instance);
        }

        [HarmonyPatch(typeof(GameUnit), nameof(GameUnit.DeathCheck), new Type[] { typeof(int) })]
        [HarmonyPostfix]
        public static void GameUnit_DeathCheck_Postfix(GameUnit __instance)
        {
            if (__instance == null) return;
            if (__instance.m_CharState == CharState.Death)
            {
                UnitRegistry.Instance.Unregister(__instance);
            }
        }

        [HarmonyPatch(typeof(GameEnemy), nameof(GameEnemy.DeathCheck), new Type[] { typeof(int) })]
        [HarmonyPostfix]
        public static void GameEnemy_DeathCheck_Postfix(GameEnemy __instance)
        {
            if (__instance == null) return;
            if (__instance.m_CharState == CharState.Death)
            {
                UnitRegistry.Instance.Unregister(__instance);
            }
        }
    }
}
