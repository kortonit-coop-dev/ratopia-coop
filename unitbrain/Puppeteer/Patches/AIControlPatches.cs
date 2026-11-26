using System;
using System.Collections.Generic;
using HarmonyLib;
using RatopiaUnitBrain.Data;
using RatopiaUnitBrain.Registry;
using UnityEngine;

namespace RatopiaUnitBrain.Puppeteer.Patches
{
    [HarmonyPatch]
    public static class AIControlPatches
    {
        private static bool IsPuppet(GameUnit unit)
        {
            if (unit == null) return false;
            if (unit is T_Queen) return false; // Queens are managed by QueenSyncManager
            var mode = UnitRegistry.Instance.GetControlMode(unit);
            if (mode == BrainControlMode.RemotePuppet) return true;
            if (UnitRegistry.Instance.DefaultPuppetMode &&
                unit.m_ID != UnitRegistry.Instance.LocalExcludeId1 &&
                unit.m_ID != UnitRegistry.Instance.LocalExcludeId2)
            {
                UnitRegistry.Instance.SetControlMode(unit, BrainControlMode.RemotePuppet);
                return true;
            }
            return false;
        }

        // =====================================================================
        // 1. Suppress UpdateFunction across all GameUnit subclasses on Puppets
        // =====================================================================

        [HarmonyPatch(typeof(GameUnit), nameof(GameUnit.UpdateFunction))]
        [HarmonyPrefix]
        public static bool GameUnit_UpdateFunction_Prefix(GameUnit __instance)
        {
            return !IsPuppet(__instance);
        }

        [HarmonyPatch(typeof(T_Citizen), nameof(T_Citizen.UpdateFunction))]
        [HarmonyPrefix]
        public static bool TCitizen_UpdateFunction_Prefix(T_Citizen __instance)
        {
            return !IsPuppet(__instance);
        }

        [HarmonyPatch(typeof(GameEnemy), nameof(GameEnemy.UpdateFunction))]
        [HarmonyPrefix]
        public static bool GameEnemy_UpdateFunction_Prefix(GameEnemy __instance)
        {
            return !IsPuppet(__instance);
        }

        [HarmonyPatch(typeof(MiningDrone), nameof(MiningDrone.UpdateFunction))]
        [HarmonyPrefix]
        public static bool MiningDrone_UpdateFunction_Prefix(MiningDrone __instance)
        {
            return !IsPuppet(__instance);
        }

        [HarmonyPatch(typeof(GBot), nameof(GBot.UpdateFunction))]
        [HarmonyPrefix]
        public static bool GBot_UpdateFunction_Prefix(GBot __instance)
        {
            return !IsPuppet(__instance);
        }

        [HarmonyPatch(typeof(LizardEnemy), nameof(LizardEnemy.UpdateFunction))]
        [HarmonyPrefix]
        public static bool LizardEnemy_UpdateFunction_Prefix(LizardEnemy __instance)
        {
            return !IsPuppet(__instance);
        }

        [HarmonyPatch(typeof(WeaselEnemy), nameof(WeaselEnemy.UpdateFunction))]
        [HarmonyPrefix]
        public static bool WeaselEnemy_UpdateFunction_Prefix(WeaselEnemy __instance)
        {
            return !IsPuppet(__instance);
        }

        [HarmonyPatch(typeof(Lizard_Assassin), nameof(Lizard_Assassin.UpdateFunction))]
        [HarmonyPrefix]
        public static bool LizardAssassin_UpdateFunction_Prefix(Lizard_Assassin __instance)
        {
            return !IsPuppet(__instance);
        }

        [HarmonyPatch(typeof(Lizard_SmallCitizen), nameof(Lizard_SmallCitizen.UpdateFunction))]
        [HarmonyPrefix]
        public static bool LizardSmallCitizen_UpdateFunction_Prefix(Lizard_SmallCitizen __instance)
        {
            return !IsPuppet(__instance);
        }

        [HarmonyPatch(typeof(Weasel_AtkMummy), nameof(Weasel_AtkMummy.UpdateFunction))]
        [HarmonyPrefix]
        public static bool WeaselAtkMummy_UpdateFunction_Prefix(Weasel_AtkMummy __instance)
        {
            return !IsPuppet(__instance);
        }

        [HarmonyPatch(typeof(Weasel_AtkMummy2), nameof(Weasel_AtkMummy2.UpdateFunction))]
        [HarmonyPrefix]
        public static bool WeaselAtkMummy2_UpdateFunction_Prefix(Weasel_AtkMummy2 __instance)
        {
            return !IsPuppet(__instance);
        }

        [HarmonyPatch(typeof(Weasel_BasicMummy), nameof(Weasel_BasicMummy.UpdateFunction))]
        [HarmonyPrefix]
        public static bool WeaselBasicMummy_UpdateFunction_Prefix(Weasel_BasicMummy __instance)
        {
            return !IsPuppet(__instance);
        }

        [HarmonyPatch(typeof(Weasel_BeastMummy), nameof(Weasel_BeastMummy.UpdateFunction))]
        [HarmonyPrefix]
        public static bool WeaselBeastMummy_UpdateFunction_Prefix(Weasel_BeastMummy __instance)
        {
            return !IsPuppet(__instance);
        }

        [HarmonyPatch(typeof(Weasel_PriestMummy), nameof(Weasel_PriestMummy.UpdateFunction))]
        [HarmonyPrefix]
        public static bool WeaselPriestMummy_UpdateFunction_Prefix(Weasel_PriestMummy __instance)
        {
            return !IsPuppet(__instance);
        }

        [HarmonyPatch(typeof(Shrew), nameof(Shrew.UpdateFunction))]
        [HarmonyPrefix]
        public static bool Shrew_UpdateFunction_Prefix(Shrew __instance)
        {
            return !IsPuppet(__instance);
        }

        [HarmonyPatch(typeof(HalloweenGhost), nameof(HalloweenGhost.UpdateFunction))]
        [HarmonyPrefix]
        public static bool HalloweenGhost_UpdateFunction_Prefix(HalloweenGhost __instance)
        {
            return !IsPuppet(__instance);
        }

        [HarmonyPatch(typeof(HiredUnit), nameof(HiredUnit.UpdateFunction))]
        [HarmonyPrefix]
        public static bool HiredUnit_UpdateFunction_Prefix(HiredUnit __instance)
        {
            return !IsPuppet(__instance);
        }

        [HarmonyPatch(typeof(BabyRat), nameof(BabyRat.UpdateFunction))]
        [HarmonyPrefix]
        public static bool BabyRat_UpdateFunction_Prefix(BabyRat __instance)
        {
            return !IsPuppet(__instance);
        }

        // =====================================================================
        // 1.1 Suppress Unity Update() on Puppets so vanilla doesn't clear states/coroutine
        // =====================================================================

        [HarmonyPatch(typeof(GameEnemy), "Update")]
        [HarmonyPrefix]
        public static bool GameEnemy_Update_Prefix(GameEnemy __instance)
        {
            return !IsPuppet(__instance);
        }

        [HarmonyPatch(typeof(T_Citizen), "Update")]
        [HarmonyPrefix]
        public static bool TCitizen_Update_Prefix(T_Citizen __instance)
        {
            return !IsPuppet(__instance);
        }

        [HarmonyPatch(typeof(HalloweenGhost), "Update")]
        [HarmonyPrefix]
        public static bool HalloweenGhost_Update_Prefix(HalloweenGhost __instance)
        {
            return !IsPuppet(__instance);
        }

        [HarmonyPatch(typeof(Shrew), "Update")]
        [HarmonyPrefix]
        public static bool Shrew_Update_Prefix(Shrew __instance)
        {
            return !IsPuppet(__instance);
        }

        // =====================================================================
        // 2. Suppress Check_AllDesire & LostWayCheck on Puppets
        // =====================================================================

        [HarmonyPatch(typeof(T_Citizen), "Check_AllDesire")]
        [HarmonyPrefix]
        public static bool TCitizen_Check_AllDesire_Prefix(T_Citizen __instance)
        {
            return !IsPuppet(__instance);
        }

        [HarmonyPatch(typeof(GameUnit), "LostWayCheck", new Type[] { typeof(int) })]
        [HarmonyPrefix]
        public static bool GameUnit_LostWayCheck_Prefix(GameUnit __instance)
        {
            return !IsPuppet(__instance);
        }

        [HarmonyPatch(typeof(T_Citizen), "LostWayCheck", new Type[] { typeof(int) })]
        [HarmonyPrefix]
        public static bool TCitizen_LostWayCheck_Prefix(T_Citizen __instance)
        {
            return !IsPuppet(__instance);
        }

        // =====================================================================
        // 3. Suppress autonomous PathFindCall & PathFindCallBack on Puppets
        // =====================================================================

        [HarmonyPatch(typeof(GameUnit), nameof(GameUnit.PathFindCall), new Type[] { typeof(Vector3), typeof(CitizenState), typeof(C_Key), typeof(bool) })]
        [HarmonyPrefix]
        public static bool GameUnit_PathFindCall_Prefix(GameUnit __instance, ref bool __result)
        {
            if (IsPuppet(__instance))
            {
                __result = false;
                return false;
            }
            return true;
        }

        [HarmonyPatch(typeof(T_Citizen), nameof(T_Citizen.PathFindCall), new Type[] { typeof(Vector3), typeof(CitizenState), typeof(C_Key), typeof(bool) })]
        [HarmonyPrefix]
        public static bool TCitizen_PathFindCall_Prefix(T_Citizen __instance, ref bool __result)
        {
            if (IsPuppet(__instance))
            {
                __result = false;
                return false;
            }
            return true;
        }

        [HarmonyPatch(typeof(GameEnemy), nameof(GameEnemy.PathFindCall), new Type[] { typeof(Vector3), typeof(CitizenState), typeof(C_Key), typeof(bool) })]
        [HarmonyPrefix]
        public static bool GameEnemy_PathFindCall_Prefix(GameEnemy __instance, ref bool __result)
        {
            if (IsPuppet(__instance))
            {
                __result = false;
                return false;
            }
            return true;
        }

        [HarmonyPatch(typeof(GameUnit), nameof(GameUnit.PathFindCallBack), new Type[] { typeof(Vector3), typeof(bool), typeof(CitizenState), typeof(C_Key) })]
        [HarmonyPrefix]
        public static bool GameUnit_PathFindCallBack_Prefix(GameUnit __instance, ref bool __result)
        {
            if (IsPuppet(__instance))
            {
                __result = false;
                return false;
            }
            return true;
        }

        [HarmonyPatch(typeof(T_Citizen), nameof(T_Citizen.PathFindCallBack), new Type[] { typeof(Vector3), typeof(bool), typeof(CitizenState), typeof(C_Key) })]
        [HarmonyPrefix]
        public static bool TCitizen_PathFindCallBack_Prefix(T_Citizen __instance, ref bool __result)
        {
            if (IsPuppet(__instance))
            {
                __result = false;
                return false;
            }
            return true;
        }

        [HarmonyPatch(typeof(GameEnemy), nameof(GameEnemy.PathFindCallBack), new Type[] { typeof(Vector3), typeof(bool), typeof(CitizenState), typeof(C_Key) })]
        [HarmonyPrefix]
        public static bool GameEnemy_PathFindCallBack_Prefix(GameEnemy __instance, ref bool __result)
        {
            if (IsPuppet(__instance))
            {
                __result = false;
                return false;
            }
            return true;
        }

        // =====================================================================
        // 4. Suppress local Move() and FallDownMove() coroutines on Puppets!
        //    Puppet movement is 100% driven by BrainPuppeteer.UpdatePuppets()
        //    every frame from Host calculations.
        // =====================================================================

        [HarmonyPatch(typeof(GameUnit), nameof(GameUnit.Move), new Type[] { typeof(List<Vector2>) })]
        [HarmonyPrefix]
        public static bool GameUnit_Move_Prefix(GameUnit __instance)
        {
            return !IsPuppet(__instance);
        }

        [HarmonyPatch(typeof(GameEnemy), nameof(GameEnemy.Move), new Type[] { typeof(List<Vector2>) })]
        [HarmonyPrefix]
        public static bool GameEnemy_Move_Prefix(GameEnemy __instance)
        {
            return !IsPuppet(__instance);
        }

        [HarmonyPatch(typeof(MiningDrone), nameof(MiningDrone.Move), new Type[] { typeof(List<Vector2>) })]
        [HarmonyPrefix]
        public static bool MiningDrone_Move_Prefix(MiningDrone __instance)
        {
            return !IsPuppet(__instance);
        }

        [HarmonyPatch(typeof(GameUnit), nameof(GameUnit.FallDownMove), new Type[] { typeof(List<Vector2>) })]
        [HarmonyPrefix]
        public static bool GameUnit_FallDownMove_Prefix(GameUnit __instance)
        {
            return !IsPuppet(__instance);
        }

        // =====================================================================
        // 5. Suppress AnimalBody autonomous AI when DefaultPuppetMode is active
        // =====================================================================

        [HarmonyPatch(typeof(AnimalBody), nameof(AnimalBody.UpdateCall))]
        [HarmonyPrefix]
        public static bool AnimalBody_UpdateCall_Prefix(AnimalBody __instance)
        {
            if (UnitRegistry.Instance.DefaultPuppetMode)
            {
                BrainPuppeteer.Instance.ShutdownAnimalCoroutines(__instance);
                return false;
            }
            return true;
        }
    }
}
