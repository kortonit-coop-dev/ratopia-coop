using CasselGames.Input;
using HarmonyLib;
using RatopiaCoop.Network;
using RatopiaCoop.Sync;
using UnityEngine;

namespace RatopiaCoop.Patches
{
    [HarmonyPatch(typeof(MiningBox))]
    public static class MiningBoxPatches
    {
        private static Vector2 _dragStart;
        private static Vector2 _dragCur;
        private static MiningBoxMode _dragMode;
        private static bool _dragDeleteMode;
        private static int _dragVariationIndex;

        [HarmonyPatch("Update")]
        [HarmonyPrefix]
        public static void Update_Prefix(MiningBox __instance, out bool __state)
        {
            __state = false;
            if (CoopNetManager.Instance == null || !CoopNetManager.Instance.IsConnected) return;
            if (SaveTransferManager.IsWorldLoading) return;

            Traverse trav = Traverse.Create(__instance);
            bool isMining = trav.Field<bool>("IsMining").Value;

            // Only track when an actual drag selection is currently active inside MiningBox
            if (isMining)
            {
                __state = true;
                _dragStart = trav.Field<Vector2>("Pos_Start").Value;
                _dragCur = trav.Field<Vector2>("Pos_Cur").Value;
                _dragMode = __instance.m_Mode;
                _dragDeleteMode = __instance.m_DeleteMode;
                _dragVariationIndex = GameMgr.Instance?._UnionUI?.m_VariationPannel != null
                    ? GameMgr.Instance._UnionUI.m_VariationPannel.m_Index
                    : 0;
            }
        }

        [HarmonyPatch("Update")]
        [HarmonyPostfix]
        public static void Update_Postfix(MiningBox __instance, bool __state)
        {
            if (!__state) return;
            if (CoopNetManager.Instance == null || !CoopNetManager.Instance.IsConnected) return;
            if (SaveTransferManager.IsWorldLoading) return;

            bool isMiningNow = Traverse.Create(__instance).Field<bool>("IsMining").Value;

            // Commit only when IsMining transitioned from true -> false via normal release (not Escape cancel)
            if (!isMiningNow && __instance.gameObject.activeSelf && !InputMgr.GetKeyDown(HotKeyName.Escape))
            {
                WorkMarkKind? targetKind = null;
                bool isDelete = _dragDeleteMode;

                switch (_dragMode)
                {
                    case MiningBoxMode.Mining:
                        targetKind = isDelete ? WorkMarkKind.Delete_Mining : WorkMarkKind.Mining;
                        break;
                    case MiningBoxMode.Gathering:
                        targetKind = isDelete ? WorkMarkKind.Delete_Gathering : WorkMarkKind.Gathering;
                        break;
                    case MiningBoxMode.Repair:
                        targetKind = isDelete ? WorkMarkKind.Delete_Repair : WorkMarkKind.Repair;
                        break;
                    case MiningBoxMode.Demolition:
                        targetKind = isDelete ? WorkMarkKind.Delete_Demolition : WorkMarkKind.Demolition;
                        break;
                    case MiningBoxMode.Cancel:
                        targetKind = WorkMarkKind.Cancel;
                        isDelete = true;
                        break;
                }

                if (targetKind.HasValue)
                {
                    int minX = Mathf.RoundToInt(Mathf.Min(_dragStart.x, _dragCur.x));
                    int maxX = Mathf.RoundToInt(Mathf.Max(_dragStart.x, _dragCur.x));
                    int minY = Mathf.RoundToInt(Mathf.Min(_dragStart.y, _dragCur.y));
                    int maxY = Mathf.RoundToInt(Mathf.Max(_dragStart.y, _dragCur.y));

                    OrderSyncManager.Instance?.SendOrderArea(minX, minY, maxX, maxY, targetKind.Value, isDelete, _dragVariationIndex);
                }
            }
        }
    }
}
