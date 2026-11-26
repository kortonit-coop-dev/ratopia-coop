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

        // Both Host and Client can place blueprints (blueprint placement is a player order)
        [HarmonyPatch(typeof(BP_Building), "BluePrintSet")]
        [HarmonyPostfix]
        public static void BluePrintSet_Postfix(BuildInfo info, Vector2 pos, int _att_num, int _func_num)
        {
            if (_isSyncingBlueprint || SaveTransferManager.IsWorldLoading) return;

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

        // Both Host and Client can cancel blueprints
        [HarmonyPatch(typeof(BP_Building), "CancelBP")]
        [HarmonyPostfix]
        public static void CancelBP_Postfix(BP_Building __instance)
        {
            if (_isSyncingBlueprint || SaveTransferManager.IsWorldLoading) return;

            if (CoopNetManager.Instance != null && CoopNetManager.Instance.IsConnected)
            {
                try
                {
                    _isSyncingBlueprint = true;
                    int x = Mathf.RoundToInt(__instance.Pos_Tile.x);
                    int y = Mathf.RoundToInt(__instance.Pos_Tile.y);
                    WorldSyncManager.Instance?.SendCancelBlueprint(x, y);
                }
                finally
                {
                    _isSyncingBlueprint = false;
                }
            }
        }

        // Demolitions are executed by citizens, so ONLY HOST broadcasts finished demolition!
        [HarmonyPatch(typeof(Building), "BuildingDemolition")]
        [HarmonyPostfix]
        public static void BuildingDemolition_Postfix(Building __instance, bool _return_mat)
        {
            if (_isSyncingDemolition || SaveTransferManager.IsWorldLoading) return;

            if (CoopNetManager.Instance != null && CoopNetManager.Instance.IsConnected && CoopNetManager.Instance.IsHost)
            {
                try
                {
                    _isSyncingDemolition = true;
                    int x = Mathf.RoundToInt(__instance.Pos_Tile.x);
                    int y = Mathf.RoundToInt(__instance.Pos_Tile.y);
                    WorldSyncManager.Instance?.SendBuildingDemolished(x, y);
                }
                finally
                {
                    _isSyncingDemolition = false;
                }
            }
        }

        // Construction is completed by citizens, so ONLY HOST broadcasts finished building!
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
                    WorldSyncManager.Instance?.SendBuildingCompleted(name.ToString(), pos, _att_num);
                }
                finally
                {
                    _isSyncingBuildSet = false;
                }
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
    }
}
