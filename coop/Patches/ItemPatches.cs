using System;
using System.Collections.Generic;
using HarmonyLib;
using RatopiaCoop.Network;
using RatopiaCoop.Sync;
using UnityEngine;

namespace RatopiaCoop.Patches
{
    [HarmonyPatch]
    public static class ItemPatches
    {
        [ThreadStatic]
        private static bool _handlingDeliveryInteraction;

        [ThreadStatic]
        private static bool _isInStorageWithdrawUi;

        private static Building? _pendingEjectBuilding;
        private static TileType _pendingEjectType;
        private static float _pendingEjectTime;

        // =========================================================================
        // GROUND ITEMS (TileObject)
        // =========================================================================

        private static bool _isInGatherFromBuilding;
        private static bool _isInLocalDrop;
        public static bool IsStorageUiOpen { get; private set; }
        public static Building? OpenStorageBuilding { get; private set; }

        public static bool IsStorageUIOpenFor(Building building)
        {
            if (building == null || !IsStorageUiOpen) return false;
            var storageUi = GameMgr.Instance?._StorageUI;
            if (storageUi == null || !storageUi.enabled) return false;

            var objMain = Traverse.Create(storageUi).Field<GameObject>("Obj_Main").Value;
            if (objMain == null || !objMain.activeInHierarchy) return false;

            if (OpenStorageBuilding == building || storageUi.m_Building_Storage == building) return true;
            var storageInfo = Traverse.Create(storageUi).Field<BuildInfoUI>("m_StorageInfo").Value;
            return storageInfo != null && storageInfo.m_Building == building;
        }

        [HarmonyPatch(typeof(TileObject), nameof(TileObject.ObjectInit), new Type[] { typeof(TileType), typeof(TObjState), typeof(Vector3), typeof(int), typeof(bool) })]
        [HarmonyPrefix]
        public static bool TileObject_ObjectInit_Prefix(TileObject __instance, ref TileObject __result, TileType _type, TObjState _state, int _num)
        {
            if (CoopNetManager.Instance == null || !CoopNetManager.Instance.IsConnected || !CoopNetManager.Instance.IsClient) return true;
            if (ItemSyncManager.IsApplyingRemoteItem || SaveTransferManager.IsWorldLoading) return true;

            // Allow temporary hand-item wrappers (storage withdraw, citizen/bot carry, local drop animation)
            if (_isInGatherFromBuilding || _isInLocalDrop || _type == TileType.A_Citizen || _type == TileType.GBot)
            {
                return true;
            }

            // Suppress all non-authoritative Client ground item spawns (tile breaks, plant drops, enemy drops,
            // WorkTimeGauge bonus drops, StorageUI.DropObj_Function, etc.) and cleanly return pooled object.
            if (__instance != null)
            {
                if (__instance.Tf == null) __instance.Tf = __instance.transform;
                if (__instance.Obj == null) __instance.Obj = __instance.gameObject;
                __instance.m_Info ??= new TileSt_Info(_type, _state, 0);
                __instance.m_Info.m_Type = _type;
                __instance.m_Info.m_State = _state;
                __instance.m_Info.List_Reservation.Clear();
                GameMgr.Instance?._PoolMgr?.List_TileObject?.Remove(__instance);
                GameMgr.Instance?._PoolMgr?.Pool_TileObject?.AddObjTf(__instance.gameObject);
            }
            __result = __instance!;
            return false;
        }

        [HarmonyPatch(typeof(TileObject), nameof(TileObject.LifeTimeCheck), new Type[] { typeof(int) })]
        [HarmonyPrefix]
        public static bool TileObject_LifeTimeCheck_Prefix(ref bool __result)
        {
            if (CoopNetManager.Instance == null || !CoopNetManager.Instance.IsConnected || !CoopNetManager.Instance.IsClient) return true;
            // Host is authoritative for floor item expiration and replicates destruction cleanly
            __result = false;
            return false;
        }

        [HarmonyPatch(typeof(TileObject), nameof(TileObject.DropObject), new Type[] { typeof(TileSt_Info), typeof(int), typeof(GameUnit) })]
        [HarmonyPrefix]
        public static void TileObject_DropObject_Prefix(out bool __state)
        {
            __state = _isInLocalDrop;
            if (CoopNetManager.Instance != null && CoopNetManager.Instance.IsConnected && CoopNetManager.Instance.IsClient &&
                !ItemSyncManager.IsApplyingRemoteItem)
            {
                _isInLocalDrop = true;
            }
        }

        [HarmonyPatch(typeof(TileObject), nameof(TileObject.DropObject), new Type[] { typeof(TileSt_Info), typeof(int), typeof(GameUnit) })]
        [HarmonyPostfix]
        public static void TileObject_DropObject_Postfix(TileObject __instance, bool __state)
        {
            _isInLocalDrop = __state;
            if (CoopNetManager.Instance == null || !CoopNetManager.Instance.IsConnected || !CoopNetManager.Instance.IsClient) return;
            if (ItemSyncManager.IsApplyingRemoteItem || SaveTransferManager.IsWorldLoading) return;

            // Host spawns the authoritative dropped TileObject and replicates it via TileObjectSpawn
            if (__instance != null && __instance.m_Info != null &&
                __instance.m_Info.m_Type != TileType.A_Citizen && __instance.m_Info.m_Type != TileType.GBot &&
                __instance.gameObject != null && __instance.gameObject.activeSelf)
            {
                ItemSyncManager.IsApplyingRemoteItem = true;
                try { __instance.ObjectDelete(); }
                finally { ItemSyncManager.IsApplyingRemoteItem = false; }
            }
        }

        [HarmonyPatch(typeof(TileObject), nameof(TileObject.ObjectInit), new Type[] { typeof(TileType), typeof(TObjState), typeof(Vector3), typeof(int), typeof(bool) })]
        [HarmonyPostfix]
        public static void TileObject_ObjectInit_Postfix(TileObject __result)
        {
            if (CoopNetManager.Instance == null || !CoopNetManager.Instance.IsConnected || !CoopNetManager.Instance.IsHost) return;
            if (ItemSyncManager.IsApplyingRemoteItem || SaveTransferManager.IsWorldLoading) return;

            if (__result != null && __result.m_Info != null)
            {
                ItemSyncManager.Instance?.SendTileObjectSpawn(__result);
            }
        }

        [HarmonyPatch(typeof(TileObject), nameof(TileObject.ObjectDelete))]
        [HarmonyPrefix]
        public static void TileObject_ObjectDelete_Prefix(TileObject __instance)
        {
            if (CoopNetManager.Instance == null || !CoopNetManager.Instance.IsConnected || !CoopNetManager.Instance.IsHost) return;
            if (ItemSyncManager.IsApplyingRemoteItem || SaveTransferManager.IsWorldLoading) return;

            if (__instance != null && __instance.m_Info != null && __instance.Tf != null)
            {
                ItemSyncManager.Instance?.SendTileObjectDestroy(__instance);
            }
        }

        [HarmonyPatch(typeof(TileObject), nameof(TileObject.ObjectGathered), new Type[] { typeof(GameUnit), typeof(bool) })]
        [HarmonyPrefix]
        public static void TileObject_ObjectGathered_Prefix(TileObject __instance, GameUnit _unit)
        {
            if (CoopNetManager.Instance == null || !CoopNetManager.Instance.IsConnected || !CoopNetManager.Instance.IsClient) return;
            if (ItemSyncManager.IsApplyingRemoteItem || SaveTransferManager.IsWorldLoading) return;
            if (_isInGatherFromBuilding || _isInStorageWithdrawUi) return;
            if (__instance == null || __instance.m_Info == null || __instance.gameObject == null || !__instance.gameObject.activeInHierarchy) return;
            if (__instance.m_Info.m_Type == TileType.A_Citizen || __instance.m_Info.m_Type == TileType.GBot) return;

            if (_unit != null && _unit == QueenSyncManager.Instance?.LocalCharacter)
            {
                ItemSyncManager.Instance?.SendRequestPickup(__instance, pickAll: false);
            }
        }

        [HarmonyPatch(typeof(TileObject), nameof(TileObject.ObjectGathered), new Type[] { typeof(GameUnit), typeof(bool) })]
        [HarmonyPostfix]
        public static void TileObject_ObjectGathered_Postfix(TileObject __instance)
        {
            if (CoopNetManager.Instance == null || !CoopNetManager.Instance.IsConnected || !CoopNetManager.Instance.IsHost) return;
            if (ItemSyncManager.IsApplyingRemoteItem || SaveTransferManager.IsWorldLoading) return;

            if (__instance != null && __instance.m_Info != null && __instance.m_Info.List_Reservation != null)
            {
                if (__instance.m_Info.List_Reservation.Count > 0 && __instance.gameObject != null && __instance.gameObject.activeInHierarchy)
                {
                    ItemSyncManager.Instance?.SendTileObjectCountUpdate(__instance);
                }
                else
                {
                    // Notify Client immediately when last unit is picked up instead of waiting 0.25s for MaplePickUpC
                    ItemSyncManager.Instance?.SendTileObjectDestroy(__instance);
                }
            }
        }

        [HarmonyPatch(typeof(TileObject), nameof(TileObject.PickUpDelete), new Type[] { typeof(GameUnit) })]
        [HarmonyPrefix]
        public static void TileObject_PickUpDelete_Prefix(TileObject __instance, GameUnit _unit)
        {
            if (CoopNetManager.Instance == null || !CoopNetManager.Instance.IsConnected || !CoopNetManager.Instance.IsClient) return;
            if (ItemSyncManager.IsApplyingRemoteItem || SaveTransferManager.IsWorldLoading) return;
            if (__instance == null || __instance.gameObject == null || !__instance.gameObject.activeInHierarchy) return;

            if (_unit != null && _unit == QueenSyncManager.Instance?.LocalCharacter)
            {
                ItemSyncManager.Instance?.SendRequestPickup(__instance, pickAll: true);
            }
        }

        // T_Queen.DropAction() calls GameUnit.Drop_GatheringList(), so hooking Drop_GatheringList handles all drops without doubling.
        [HarmonyPatch(typeof(GameUnit), nameof(GameUnit.Drop_GatheringList))]
        [HarmonyPrefix]
        public static void GameUnit_Drop_GatheringList_Prefix(GameUnit __instance)
        {
            if (CoopNetManager.Instance == null || !CoopNetManager.Instance.IsConnected || !CoopNetManager.Instance.IsClient) return;
            if (ItemSyncManager.IsApplyingRemoteItem || SaveTransferManager.IsWorldLoading) return;

            if (__instance is T_Queen queen && queen == QueenSyncManager.Instance?.LocalCharacter && queen.List_Gathering != null && queen.List_Gathering.Count > 0)
            {
                Dictionary<TileType, int> grouped = CountGatheringByType(queen.List_Gathering);
                foreach (var kv in grouped)
                {
                    ItemSyncManager.Instance?.SendRequestDrop(kv.Key, kv.Value, queen.Tf != null ? queen.Tf.position : queen.transform.position);
                }
            }
        }

        private static Dictionary<TileType, int> CountGatheringByType(List<TileSt_Info>? list)
        {
            var counts = new Dictionary<TileType, int>();
            if (list == null) return counts;
            for (int i = 0; i < list.Count; i++)
            {
                var item = list[i];
                if (item == null) continue;
                int qty = (item.List_Reservation != null && item.List_Reservation.Count > 0) ? item.List_Reservation.Count : 1;
                if (counts.TryGetValue(item.m_Type, out int cur))
                    counts[item.m_Type] = cur + qty;
                else
                    counts[item.m_Type] = qty;
            }
            return counts;
        }

        // =========================================================================
        // STORAGE & INVENTORY (Building, Building_Storage, StorageUI & QueenCheckBox)
        // =========================================================================

        [HarmonyPatch(typeof(Building_Storage), nameof(Building_Storage.DeliveryInteraction), new Type[] { typeof(GameUnit), typeof(int) })]
        [HarmonyPrefix]
        public static void Building_Storage_DeliveryInteraction_Prefix(Building_Storage __instance, GameUnit _unit, out Dictionary<TileType, int>? __state)
        {
            __state = null;
            if (_handlingDeliveryInteraction) return;
            if (CoopNetManager.Instance == null || !CoopNetManager.Instance.IsConnected || !CoopNetManager.Instance.IsClient) return;

            if (_unit != null && _unit == QueenSyncManager.Instance?.LocalCharacter && _unit.List_Gathering != null)
            {
                _handlingDeliveryInteraction = true;
                __state = CountGatheringByType(_unit.List_Gathering);
            }
        }

        [HarmonyPatch(typeof(Building_Storage), nameof(Building_Storage.DeliveryInteraction), new Type[] { typeof(GameUnit), typeof(int) })]
        [HarmonyPostfix]
        public static void Building_Storage_DeliveryInteraction_Postfix(Building_Storage __instance, GameUnit _unit, bool __result, Dictionary<TileType, int>? __state)
        {
            if (__state != null)
            {
                _handlingDeliveryInteraction = false;
            }
            if (CoopNetManager.Instance == null || !CoopNetManager.Instance.IsConnected) return;

            if (__result && CoopNetManager.Instance.IsClient && _unit == QueenSyncManager.Instance?.LocalCharacter && __state != null)
            {
                SendDepositedDeltas(__instance, _unit, __state);
            }
            else if (__result && CoopNetManager.Instance.IsHost && __instance != null)
            {
                ItemSyncManager.Instance?.NotifyBuildingInventoryChanged(__instance);
            }
        }

        [HarmonyPatch(typeof(Building), nameof(Building.DeliveryInteraction), new Type[] { typeof(GameUnit), typeof(int) })]
        [HarmonyPrefix]
        public static void Building_DeliveryInteraction_Prefix(Building __instance, GameUnit _unit, out Dictionary<TileType, int>? __state)
        {
            __state = null;
            if (_handlingDeliveryInteraction) return;
            if (CoopNetManager.Instance == null || !CoopNetManager.Instance.IsConnected || !CoopNetManager.Instance.IsClient) return;

            if (_unit != null && _unit == QueenSyncManager.Instance?.LocalCharacter && _unit.List_Gathering != null)
            {
                _handlingDeliveryInteraction = true;
                __state = CountGatheringByType(_unit.List_Gathering);
            }
        }

        [HarmonyPatch(typeof(Building), nameof(Building.DeliveryInteraction), new Type[] { typeof(GameUnit), typeof(int) })]
        [HarmonyPostfix]
        public static void Building_DeliveryInteraction_Postfix(Building __instance, GameUnit _unit, bool __result, Dictionary<TileType, int>? __state)
        {
            if (__state != null)
            {
                _handlingDeliveryInteraction = false;
            }
            if (CoopNetManager.Instance == null || !CoopNetManager.Instance.IsConnected) return;

            if (__result && CoopNetManager.Instance.IsClient && _unit == QueenSyncManager.Instance?.LocalCharacter && __state != null)
            {
                SendDepositedDeltas(__instance, _unit, __state);
            }
            else if (__result && CoopNetManager.Instance.IsHost && __instance != null)
            {
                ItemSyncManager.Instance?.NotifyBuildingInventoryChanged(__instance);
            }
        }

        private static void SendDepositedDeltas(Building building, GameUnit unit, Dictionary<TileType, int> before)
        {
            if (building == null || unit == null) return;
            Dictionary<TileType, int> after = CountGatheringByType(unit.List_Gathering);
            foreach (var kv in before)
            {
                after.TryGetValue(kv.Key, out int rem);
                int diff = kv.Value - rem;
                if (diff > 0)
                {
                    ItemSyncManager.Instance?.SendRequestStorageDeposit(building.m_ID, kv.Key, diff);
                }
            }
        }

        [HarmonyPatch(typeof(StorageUI), "StorageInteraction_Function")]
        [HarmonyPrefix]
        public static void StorageUI_StorageInteraction_Prefix(out bool __state)
        {
            __state = _isInStorageWithdrawUi;
            _isInStorageWithdrawUi = true;
        }

        [HarmonyPatch(typeof(StorageUI), "StorageInteraction_Function")]
        [HarmonyPostfix]
        public static void StorageUI_StorageInteraction_Postfix(bool __state)
        {
            _isInStorageWithdrawUi = __state;
        }

        [HarmonyPatch(typeof(StorageUI), nameof(StorageUI.DropObj_Function))]
        [HarmonyPrefix]
        public static void StorageUI_DropObj_Function_Prefix(StorageUI __instance)
        {
            if (CoopNetManager.Instance == null || !CoopNetManager.Instance.IsConnected || !CoopNetManager.Instance.IsClient) return;
            if (__instance == null) return;

            try
            {
                var trav = Traverse.Create(__instance);
                var storageInfo = trav.Field<BuildInfoUI>("m_StorageInfo").Value;
                object? selectSlotObj = trav.Field("m_SelectSlot").GetValue();
                TileSt_Info? tileInfo = selectSlotObj != null ? Traverse.Create(selectSlotObj).Field<TileSt_Info>("m_TileSt_Info").Value : null;
                if (storageInfo?.m_Building != null && tileInfo != null)
                {
                    _pendingEjectBuilding = storageInfo.m_Building;
                    _pendingEjectType = tileInfo.m_Type;
                    _pendingEjectTime = Time.unscaledTime;
                }
            }
            catch { }
        }

        [HarmonyPatch(typeof(StorageUI), nameof(StorageUI.StorageUI_Open), new Type[] { typeof(Building), typeof(StorageUI.StorageMode), typeof(BuildMidUI) })]
        [HarmonyPostfix]
        public static void StorageUI_Open_Postfix(Building _building, StorageUI.StorageMode _mode)
        {
            IsStorageUiOpen = true;
            OpenStorageBuilding = _building;
        }

        [HarmonyPatch(typeof(StorageUI), nameof(StorageUI.ExitBtn))]
        [HarmonyPrefix]
        public static void StorageUI_ExitBtn_Prefix()
        {
            IsStorageUiOpen = false;
            OpenStorageBuilding = null;
        }

        [HarmonyPatch(typeof(QueenCheckBox), nameof(QueenCheckBox.GatherFromBuilding), new Type[] { typeof(TileType), typeof(int) })]
        [HarmonyPrefix]
        public static void QueenCheckBox_GatherFromBuilding_Prefix(QueenCheckBox __instance, TileType _type, int num, out bool __state)
        {
            __state = _isInGatherFromBuilding;
            if (CoopNetManager.Instance == null || !CoopNetManager.Instance.IsConnected || !CoopNetManager.Instance.IsClient) return;

            _isInGatherFromBuilding = true;

            var localQueen = QueenSyncManager.Instance?.LocalCharacter;
            if (localQueen != null && __instance == localQueen.m_CheckBox)
            {
                var storageUi = GameMgr.Instance?._StorageUI;
                Building? uiBuilding = null;
                if (storageUi != null && (storageUi.enabled || _isStorageUiActiveOrWithdraw(storageUi)))
                {
                    uiBuilding = OpenStorageBuilding
                        ?? Traverse.Create(storageUi).Field<BuildInfoUI>("m_StorageInfo").Value?.m_Building
                        ?? storageUi.m_Building_Storage;
                }

                Building? building = uiBuilding
                    ?? GameMgr.Instance?._BuildMidUI?.m_StorageInfoUI?.m_Building
                    ?? __instance.m_Building;

                if (building != null)
                {
                    ItemSyncManager.Instance?.SendRequestStorageWithdraw(building.m_ID, _type, num);
                }
            }
        }

        private static bool _isStorageUiActiveOrWithdraw(StorageUI storageUi)
        {
            return _isInStorageWithdrawUi || (storageUi != null && IsStorageUiOpen);
        }

        [HarmonyPatch(typeof(QueenCheckBox), nameof(QueenCheckBox.GatherFromBuilding), new Type[] { typeof(TileType), typeof(int) })]
        [HarmonyPostfix]
        public static void QueenCheckBox_GatherFromBuilding_Postfix(bool __state)
        {
            _isInGatherFromBuilding = __state;
        }

        [HarmonyPatch(typeof(Building), nameof(Building.AddValue), new Type[] { typeof(TileType), typeof(TObjState), typeof(int), typeof(bool) })]
        [HarmonyPrefix]
        public static void Building_AddValue_Prefix(Building __instance, TileType _type, TObjState _state, int value)
        {
            if (CoopNetManager.Instance == null || !CoopNetManager.Instance.IsConnected || !CoopNetManager.Instance.IsClient) return;
            if (ItemSyncManager.IsApplyingRemoteStorage || SaveTransferManager.IsWorldLoading || _isInStorageWithdrawUi) return;

            if (value < 0 && _state == TObjState.Purchase && _pendingEjectBuilding != null && __instance == _pendingEjectBuilding && _type == _pendingEjectType)
            {
                _pendingEjectBuilding = null;
                ItemSyncManager.Instance?.SendRequestStorageEject(__instance.m_ID, _type, -value);
            }
        }

        [HarmonyPatch(typeof(Building), nameof(Building.AddValue), new Type[] { typeof(TileType), typeof(TObjState), typeof(int), typeof(bool) })]
        [HarmonyPostfix]
        public static void Building_AddValue_Postfix(Building __instance, bool __result)
        {
            if (CoopNetManager.Instance == null || !CoopNetManager.Instance.IsConnected || !CoopNetManager.Instance.IsHost) return;
            if (ItemSyncManager.IsApplyingRemoteStorage || SaveTransferManager.IsWorldLoading) return;

            if (__result && __instance != null)
            {
                ItemSyncManager.Instance?.NotifyBuildingInventoryChanged(__instance);
            }
        }

        [HarmonyPatch(typeof(Building), nameof(Building.RemoveValue), new Type[] { typeof(TileType), typeof(int) })]
        [HarmonyPostfix]
        public static void Building_RemoveValue_Postfix(Building __instance, bool __result)
        {
            if (CoopNetManager.Instance == null || !CoopNetManager.Instance.IsConnected || !CoopNetManager.Instance.IsHost) return;
            if (ItemSyncManager.IsApplyingRemoteStorage || SaveTransferManager.IsWorldLoading) return;

            if (__result && __instance != null)
            {
                ItemSyncManager.Instance?.NotifyBuildingInventoryChanged(__instance);
            }
        }

        [HarmonyPatch(typeof(QueenCheckBox), nameof(QueenCheckBox.BP_BuildingInteraction))]
        [HarmonyPrefix]
        public static void QueenCheckBox_BP_BuildingInteraction_Prefix(QueenCheckBox __instance, out (BP_Building? bp, Dictionary<TileType, int>? before) __state)
        {
            __state = (null, null);
            if (CoopNetManager.Instance == null || !CoopNetManager.Instance.IsConnected || !CoopNetManager.Instance.IsClient) return;

            var localQueen = QueenSyncManager.Instance?.LocalCharacter;
            if (localQueen != null && __instance == localQueen.m_CheckBox && __instance.m_BP_Building != null)
            {
                __state = (__instance.m_BP_Building, CountGatheringByType(localQueen.List_Gathering));
            }
        }

        [HarmonyPatch(typeof(QueenCheckBox), nameof(QueenCheckBox.BP_BuildingInteraction))]
        [HarmonyPostfix]
        public static void QueenCheckBox_BP_BuildingInteraction_Postfix((BP_Building? bp, Dictionary<TileType, int>? before) __state)
        {
            if (__state.bp == null || __state.before == null) return;
            var localQueen = QueenSyncManager.Instance?.LocalCharacter;
            if (localQueen == null) return;

            Dictionary<TileType, int> after = CountGatheringByType(localQueen.List_Gathering);
            float bx = __state.bp.Pos_Tile.x;
            float by = __state.bp.Pos_Tile.y;
            foreach (var kv in __state.before)
            {
                after.TryGetValue(kv.Key, out int rem);
                int diff = kv.Value - rem;
                if (diff > 0)
                {
                    WorldSyncManager.Instance?.SendRequestBlueprintDeposit(bx, by, kv.Key, diff);
                }
            }
        }

        [HarmonyPatch(typeof(QueenCheckBox), nameof(QueenCheckBox.BP_PlantInteraction))]
        [HarmonyPrefix]
        public static void QueenCheckBox_BP_PlantInteraction_Prefix(QueenCheckBox __instance, out (BP_Plant? bp, Dictionary<TileType, int>? before) __state)
        {
            __state = (null, null);
            if (CoopNetManager.Instance == null || !CoopNetManager.Instance.IsConnected || !CoopNetManager.Instance.IsClient) return;

            var localQueen = QueenSyncManager.Instance?.LocalCharacter;
            if (localQueen != null && __instance == localQueen.m_CheckBox && __instance.m_BP_Plant != null)
            {
                __state = (__instance.m_BP_Plant, CountGatheringByType(localQueen.List_Gathering));
            }
        }

        [HarmonyPatch(typeof(QueenCheckBox), nameof(QueenCheckBox.BP_PlantInteraction))]
        [HarmonyPostfix]
        public static void QueenCheckBox_BP_PlantInteraction_Postfix((BP_Plant? bp, Dictionary<TileType, int>? before) __state)
        {
            if (__state.bp == null || __state.before == null) return;
            var localQueen = QueenSyncManager.Instance?.LocalCharacter;
            if (localQueen == null) return;

            Dictionary<TileType, int> after = CountGatheringByType(localQueen.List_Gathering);
            Vector2 pos = __state.bp.Pos_Tile;
            foreach (var kv in __state.before)
            {
                after.TryGetValue(kv.Key, out int rem);
                int diff = kv.Value - rem;
                if (diff > 0)
                {
                    WorldSyncManager.Instance?.SendPlantBlueprintDeposit(pos, kv.Key, diff);
                }
            }
        }
    }
}
