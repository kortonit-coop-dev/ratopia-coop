using System;
using CasselGames.Audio;
using CasselGames.Input;
using HarmonyLib;
using RatopiaCoop.Network;
using RatopiaCoop.Sync;
using UnityEngine;

namespace RatopiaCoop.Patches
{
    [HarmonyPatch]
    public static class BuildingPatches
    {
        private static bool _isSyncingBlueprint;
        private static bool _isSyncingDemolition;
        private static bool _isSyncingBuildSet;
        private static bool _isSyncingPlantBlueprint;

        [ThreadStatic]
        private static int _activeBpFuncNum;

        [ThreadStatic]
        private static bool _hasActiveBpFuncNum;

        // Send blueprint placement before BluePrintSet's internal skip-cheat instant build runs
        [HarmonyPatch(typeof(BP_Building), "BluePrintSet")]
        [HarmonyPrefix]
        public static void BluePrintSet_Prefix(BuildInfo info, Vector2 pos, int _att_num, int _func_num)
        {
            if (_isSyncingBlueprint || SaveTransferManager.IsWorldLoading || info == null) return;

            if (CoopNetManager.Instance != null && CoopNetManager.Instance.IsConnected)
            {
                try
                {
                    _isSyncingBlueprint = true;
                    WorldSyncManager.Instance?.SendPlaceBlueprint(info.Name.ToString(), pos, _att_num, _func_num);
                }
                finally
                {
                    _isSyncingBlueprint = false;
                }
            }
        }

        // Both Host and Client can cancel blueprints (match exact Pos_Tile floats to avoid half-tile collisions)
        [HarmonyPatch(typeof(BP_Building), "CancelBP")]
        [HarmonyPrefix]
        public static void CancelBP_Prefix(BP_Building __instance)
        {
            if (__instance == null || _isSyncingBlueprint || SaveTransferManager.IsWorldLoading) return;

            if (CoopNetManager.Instance != null && CoopNetManager.Instance.IsConnected)
            {
                try
                {
                    _isSyncingBlueprint = true;
                    WorldSyncManager.Instance?.SendCancelBlueprint(__instance.Pos_Tile.x, __instance.Pos_Tile.y);
                }
                finally
                {
                    _isSyncingBlueprint = false;
                }
            }
        }

        // Demolitions are executed by Host, so ONLY HOST broadcasts finished demolition!
        [HarmonyPatch(typeof(Building), "BuildingDemolition")]
        [HarmonyPostfix]
        public static void BuildingDemolition_Postfix(Building __instance, bool _return_mat)
        {
            if (__instance == null || _isSyncingDemolition || SaveTransferManager.IsWorldLoading) return;

            if (CoopNetManager.Instance != null && CoopNetManager.Instance.IsConnected && CoopNetManager.Instance.IsHost)
            {
                try
                {
                    _isSyncingDemolition = true;
                    WorldSyncManager.Instance?.SendBuildingDemolished(__instance.Pos_Tile.x, __instance.Pos_Tile.y);
                }
                finally
                {
                    _isSyncingDemolition = false;
                }
            }
        }

        // Host broadcasts material / Construct readiness updates to keep Client blueprints synced
        [HarmonyPatch(typeof(BP_Building), nameof(BP_Building.AddMatValue), typeof(TileType), typeof(int))]
        [HarmonyPostfix]
        public static void BP_Building_AddMatValue_Postfix(BP_Building __instance, bool __result)
        {
            if (__instance == null || !__result || SaveTransferManager.IsWorldLoading) return;
            if (WorldSyncManager.Instance != null && WorldSyncManager.Instance.IsApplyingRemoteBuildingWork) return;
            if (CoopNetManager.Instance == null || !CoopNetManager.Instance.IsConnected || !CoopNetManager.Instance.IsHost) return;

            WorldSyncManager.Instance?.SendBlueprintProgress(__instance);
        }

        // Override ConstructingValue temporarily while Host replays Client's exact construction work amount
        [HarmonyPatch(typeof(GameUnit), nameof(GameUnit.GetConstructingValue))]
        [HarmonyPrefix]
        public static bool GameUnit_GetConstructingValue_Prefix(GameUnit __instance, ref float __result)
        {
            var worldSync = WorldSyncManager.Instance;
            if (worldSync != null &&
                worldSync.OverrideConstructingWorkValue > 0f &&
                __instance != null &&
                __instance == worldSync.OverrideConstructingWorkUnit)
            {
                __result = worldSync.OverrideConstructingWorkValue;
                return false;
            }
            return true;
        }

        // Host-authoritative manual construction work: forward Client Queen hammer strikes to Host
        [HarmonyPatch(typeof(BP_Building), nameof(BP_Building.BuildingUpdate_Call), typeof(GameUnit))]
        [HarmonyPrefix]
        public static bool BP_Building_BuildingUpdate_Call_Prefix(BP_Building __instance, GameUnit _unit, out bool __state)
        {
            __state = false;
            if (__instance == null || SaveTransferManager.IsWorldLoading) return true;

            _activeBpFuncNum = __instance.m_FuncNum;
            _hasActiveBpFuncNum = true;
            __state = true;

            if (CoopNetManager.Instance == null || !CoopNetManager.Instance.IsConnected) return true;
            if (WorldSyncManager.Instance != null && WorldSyncManager.Instance.IsApplyingRemoteBuildingWork) return true;

            if (CoopNetManager.Instance.IsClient)
            {
                var localQueen = QueenSyncManager.Instance?.LocalCharacter;
                if (localQueen == null || _unit != localQueen)
                {
                    // Puppet citizens/drones on Client never advance or complete blueprints locally
                    return false;
                }

                float workAmount = Mathf.Max(0.001f, _unit.GetConstructingValue());
                bool wouldFinish = __instance.m_CurTime + workAmount >= __instance.m_MaxTime;
                WorldSyncManager.Instance?.SendRequestBlueprintWork(__instance.Pos_Tile.x, __instance.Pos_Tile.y, workAmount, wouldFinish);

                if (wouldFinish)
                {
                    // Hold Client blueprint just short of completion so Host authoritatively executes BuildSet + overlap drops
                    try
                    {
                        AudioController.PlaySFXOneShot("SFX_QueenWork_Hammer_Build", GameMgr.Instance._CamMgr.m_MainCam.transform.position, __instance.Tf.position);
                        InputMgr.SetRumble(TypeSimpleRumble.Strong);
                        _unit.UpdatePDI(PDI.Dex, 1f);
                        __instance.m_CurTime = Mathf.Max(__instance.m_CurTime, Mathf.Max(0f, __instance.m_MaxTime - 0.001f));
                        __instance.m_Scaffolding?.RemoteUpdate(__instance.m_MaxTime > 0f ? __instance.m_CurTime / __instance.m_MaxTime : 0.999f);
                        if (__instance.m_Spr_Gauge != null && __instance.m_Info != null)
                        {
                            float ratio = __instance.m_MaxTime > 0f ? Mathf.Clamp01(__instance.m_CurTime / __instance.m_MaxTime) : 0.999f;
                            __instance.m_Spr_Gauge.size = new Vector2(ratio * __instance.m_Info.Width, __instance.m_Spr_Gauge.size.y);
                        }
                        if (GameMgr.Instance?._OverView != null && GameMgr.Instance._OverView.m_BP_Building == __instance)
                        {
                            GameMgr.Instance._OverView.BP_GaugeUpdate();
                        }
                    }
                    catch { }
                    return false;
                }
            }

            return true;
        }

        [HarmonyPatch(typeof(BP_Building), nameof(BP_Building.BuildingUpdate_Call), typeof(GameUnit))]
        [HarmonyPostfix]
        public static void BP_Building_BuildingUpdate_Call_Postfix(BP_Building __instance, bool __state)
        {
            if (__state)
            {
                _hasActiveBpFuncNum = false;
                _activeBpFuncNum = 0;
            }

            if (__instance == null || SaveTransferManager.IsWorldLoading) return;
            if (CoopNetManager.Instance == null || !CoopNetManager.Instance.IsConnected || !CoopNetManager.Instance.IsHost) return;

            if (__instance.m_BuildState == BP_State.Construct && __instance.m_CurTime < __instance.m_MaxTime)
            {
                WorldSyncManager.Instance?.SendBlueprintProgress(__instance);
            }
        }

        // Construction completion is Host-authoritative and replicates BuildSet + funcNum to Client
        [HarmonyPatch(typeof(BuildingMgr), "BuildSet", typeof(BuildingName), typeof(Vector2), typeof(int))]
        [HarmonyPostfix]
        public static void BuildSet_Postfix(BuildingName name, Vector2 pos, int _att_num)
        {
            if (_isSyncingBuildSet || SaveTransferManager.IsWorldLoading) return;

            if (CoopNetManager.Instance != null && CoopNetManager.Instance.IsConnected && CoopNetManager.Instance.IsHost)
            {
                try
                {
                    _isSyncingBuildSet = true;
                    int funcNum = _hasActiveBpFuncNum ? _activeBpFuncNum : 0;
                    WorldSyncManager.Instance?.SendBuildingCompleted(name.ToString(), pos, _att_num, funcNum);
                }
                finally
                {
                    _isSyncingBuildSet = false;
                }
            }
        }

        // Plant blueprints (BP_Plant - Minigarden, Gravegarden, etc.)
        [HarmonyPatch(typeof(BP_Plant), nameof(BP_Plant.PlantPrintSet), typeof(TileType), typeof(Vector2), typeof(int))]
        [HarmonyPostfix]
        public static void BP_Plant_PlantPrintSet_Postfix(BP_Plant __instance, TileType _type, Vector2 pos, int _MasterID)
        {
            if (__instance == null || _isSyncingPlantBlueprint || SaveTransferManager.IsWorldLoading) return;
            if (WorldSyncManager.Instance != null && WorldSyncManager.Instance.IsApplyingRemotePlantBP) return;
            if (CoopNetManager.Instance == null || !CoopNetManager.Instance.IsConnected) return;

            try
            {
                _isSyncingPlantBlueprint = true;
                WorldSyncManager.Instance?.SendPlantBlueprintPlace((short)_type, pos, _MasterID);
            }
            finally
            {
                _isSyncingPlantBlueprint = false;
            }
        }

        [HarmonyPatch(typeof(BP_Plant), nameof(BP_Plant.CancelBP))]
        [HarmonyPrefix]
        public static void BP_Plant_CancelBP_Prefix(BP_Plant __instance)
        {
            if (__instance == null || _isSyncingPlantBlueprint || SaveTransferManager.IsWorldLoading) return;
            if (WorldSyncManager.Instance != null && WorldSyncManager.Instance.IsApplyingRemotePlantBP) return;
            if (CoopNetManager.Instance == null || !CoopNetManager.Instance.IsConnected) return;

            try
            {
                _isSyncingPlantBlueprint = true;
                WorldSyncManager.Instance?.SendPlantBlueprintCancel(__instance.Pos_Tile);
            }
            finally
            {
                _isSyncingPlantBlueprint = false;
            }
        }

        [HarmonyPatch(typeof(BP_Plant), nameof(BP_Plant.AddMatValue), typeof(TileType), typeof(int))]
        [HarmonyPostfix]
        public static void BP_Plant_AddMatValue_Postfix(BP_Plant __instance, bool __result)
        {
            if (__instance == null || !__result || SaveTransferManager.IsWorldLoading) return;
            if (WorldSyncManager.Instance != null && WorldSyncManager.Instance.IsApplyingRemotePlantBP) return;
            if (CoopNetManager.Instance == null || !CoopNetManager.Instance.IsConnected || !CoopNetManager.Instance.IsHost) return;

            WorldSyncManager.Instance?.SendPlantBlueprintProgress(__instance);
        }

        [HarmonyPatch(typeof(BP_Plant), nameof(BP_Plant.PlantUpdate_Call), typeof(GameUnit))]
        [HarmonyPrefix]
        public static bool BP_Plant_PlantUpdate_Call_Prefix(BP_Plant __instance, GameUnit _unit)
        {
            if (__instance == null || SaveTransferManager.IsWorldLoading) return true;
            if (CoopNetManager.Instance == null || !CoopNetManager.Instance.IsConnected) return true;
            if (WorldSyncManager.Instance != null && WorldSyncManager.Instance.IsApplyingRemotePlantBP) return true;

            if (CoopNetManager.Instance.IsClient)
            {
                var localQueen = QueenSyncManager.Instance?.LocalCharacter;
                if (localQueen == null || _unit != localQueen)
                {
                    return false;
                }

                float workAmount = WorldSyncManager.GetPlantWorkStep(_unit);
                bool wouldFinish = __instance.m_CurTime + workAmount >= __instance.m_MaxTime;
                WorldSyncManager.Instance?.SendPlantBlueprintWork(__instance.Pos_Tile, workAmount, wouldFinish);

                if (wouldFinish)
                {
                    try
                    {
                        AudioController.PlaySFXOneShot("SFX_QueenWork_Sickle", GameMgr.Instance._CamMgr.m_MainCam.transform.position, __instance.Tf.position);
                        InputMgr.SetRumble(TypeSimpleRumble.Strong);
                        _unit.UpdatePDI(PDI.Dex, 1f);
                        __instance.m_CurTime = Mathf.Max(__instance.m_CurTime, Mathf.Max(0f, __instance.m_MaxTime - 0.001f));
                        if (__instance.m_Spr_Gauge != null && __instance.m_Info != null)
                        {
                            float ratio = __instance.m_MaxTime > 0f ? Mathf.Clamp01(__instance.m_CurTime / __instance.m_MaxTime) : 0.999f;
                            __instance.m_Spr_Gauge.size = new Vector2(ratio * __instance.m_Info.Width, __instance.m_Spr_Gauge.size.y);
                        }
                        if (GameMgr.Instance?._OverView != null && GameMgr.Instance._OverView.m_BP_Plant == __instance)
                        {
                            GameMgr.Instance._OverView.BP_Plant_GaugeUpdate();
                        }
                    }
                    catch { }
                    return false;
                }
            }

            return true;
        }

        [HarmonyPatch(typeof(BP_Plant), nameof(BP_Plant.PlantUpdate_Call), typeof(GameUnit))]
        [HarmonyPostfix]
        public static void BP_Plant_PlantUpdate_Call_Postfix(BP_Plant __instance)
        {
            if (__instance == null || SaveTransferManager.IsWorldLoading) return;
            if (CoopNetManager.Instance == null || !CoopNetManager.Instance.IsConnected || !CoopNetManager.Instance.IsHost) return;

            if (__instance.m_BuildState == BP_State.Finish)
            {
                WorldSyncManager.Instance?.SendPlantBlueprintCompleted(__instance.Pos_Tile);
            }
            else if (__instance.m_BuildState == BP_State.Construct)
            {
                WorldSyncManager.Instance?.SendPlantBlueprintProgress(__instance);
            }
        }

        public static void SetSyncingBuildSet(bool value)
        {
            _isSyncingBuildSet = value;
        }

        public static void SetSyncingDemolition(bool value)
        {
            _isSyncingDemolition = value;
        }

        public static void SetSyncingBlueprint(bool value)
        {
            _isSyncingBlueprint = value;
        }

        public static void SetSyncingPlantBlueprint(bool value)
        {
            _isSyncingPlantBlueprint = value;
        }
    }
}
