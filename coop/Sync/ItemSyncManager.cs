using System;
using System.Collections.Generic;
using Gpm.Ui;
using HarmonyLib;
using LiteNetLib;
using LiteNetLib.Utils;
using RatopiaCoop.Network;
using RatopiaCoop.Patches;
using UnityEngine;
using UnityEngine.UI;

namespace RatopiaCoop.Sync
{
    /// <summary>
    /// Manages real-time synchronization of ground resources (TileObject)
    /// and storage building inventories between Host and Clients.
    /// </summary>
    public class ItemSyncManager : MonoBehaviour
    {
        public static ItemSyncManager Instance { get; private set; } = null!;

        public static bool IsApplyingRemoteItem { get; set; }
        public static bool IsApplyingRemoteStorage { get; set; }

        private float _lastTileObjectBatchTime;
        private const float TileObjectBatchInterval = 2.0f;

        private float _lastStorageBatchTime;
        private const float StorageBatchInterval = 2.5f;

        private readonly HashSet<int> _dirtyBuildings = new HashSet<int>();
        private readonly List<TileObject> _cachedFloorItems = new List<TileObject>();
        private ushort _groundBatchSeq;
        private ushort _activeClientBatchSeq;
        private readonly HashSet<int> _clientBatchIds = new HashSet<int>();
        private readonly HashSet<int> _sentDestroyIds = new HashSet<int>();
        private readonly Queue<int> _sentDestroyQueue = new Queue<int>();

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
            }
            else if (Instance != this)
            {
                Destroy(this);
                return;
            }
        }

        private void Start()
        {
            if (CoopNetManager.Instance != null)
            {
                CoopNetManager.Instance.OnPacketReceivedEvent += HandlePacket;
            }
        }

        private void OnDestroy()
        {
            if (CoopNetManager.Instance != null)
            {
                CoopNetManager.Instance.OnPacketReceivedEvent -= HandlePacket;
            }
        }

        private void Update()
        {
            if (CoopNetManager.Instance == null || !CoopNetManager.Instance.IsConnected) return;
            if (SaveTransferManager.IsWorldLoading) return;

            if (CoopNetManager.Instance.IsHost)
            {
                // 1. Flush modified storage buildings immediately
                if (_dirtyBuildings.Count > 0)
                {
                    FlushDirtyBuildings();
                }

                // 2. Periodic reconciliation of ground items (every 4 seconds)
                if (Time.time - _lastTileObjectBatchTime >= TileObjectBatchInterval)
                {
                    _lastTileObjectBatchTime = Time.time;
                    BroadcastGroundItemsBatch();
                }

                // 3. Periodic reconciliation of storage buildings (every 2.5 seconds)
                if (Time.time - _lastStorageBatchTime >= StorageBatchInterval)
                {
                    _lastStorageBatchTime = Time.time;
                    BroadcastAllStorageBuildings();
                }
            }
        }

        public void SendFullReconciliation()
        {
            if (CoopNetManager.Instance == null || !CoopNetManager.Instance.IsConnected || !CoopNetManager.Instance.IsHost) return;
            _sentDestroyIds.Clear();
            _sentDestroyQueue.Clear();
            BroadcastGroundItemsBatch();
            BroadcastAllStorageBuildings();
            if (QueenSyncManager.Instance?.Player2Worker != null)
            {
                SendClientHandSync(QueenSyncManager.Instance.Player2Worker);
            }
        }

        // =========================================================================
        // PACKET ROUTING
        // =========================================================================

        private void HandlePacket(PacketType type, NetDataReader reader)
        {
            switch (type)
            {
                case PacketType.TileObjectSpawn:
                    HandleTileObjectSpawn(reader);
                    break;
                case PacketType.TileObjectDestroy:
                    HandleTileObjectDestroy(reader);
                    break;
                case PacketType.TileObjectBatchSync:
                    HandleTileObjectBatchSync(reader);
                    break;
                case PacketType.TileObjectCountUpdate:
                    HandleTileObjectCountUpdate(reader);
                    break;
                case PacketType.RequestPickupTileObject:
                    HandleRequestPickupTileObject(reader);
                    break;
                case PacketType.RequestDropTileObject:
                    HandleRequestDropTileObject(reader);
                    break;
                case PacketType.RequestStorageDeposit:
                    HandleRequestStorageDeposit(reader);
                    break;
                case PacketType.RequestStorageWithdraw:
                    HandleRequestStorageWithdraw(reader);
                    break;
                case PacketType.RequestStorageEject:
                    HandleRequestStorageEject(reader);
                    break;
                case PacketType.BuildingInventorySync:
                    HandleBuildingInventorySync(reader);
                    break;
                case PacketType.ClientHandSync:
                    HandleClientHandSync(reader);
                    break;
            }
        }

        // =========================================================================
        // GROUND ITEMS (TileObject) - TRANSMISSION & APPLICATION
        // =========================================================================

        public void SendTileObjectSpawn(TileObject to)
        {
            if (CoopNetManager.Instance == null || !CoopNetManager.Instance.IsConnected || !CoopNetManager.Instance.IsHost) return;
            if (to == null || to.m_Info == null || to.Tf == null) return;
            if (to.m_Info.m_Type == TileType.A_Citizen || to.m_Info.m_Type == TileType.GBot) return;

            _sentDestroyIds.Remove(to.m_ID);

            NetDataWriter writer = new NetDataWriter();
            writer.Put(to.m_ID);
            writer.Put((short)to.m_Info.m_Type);
            writer.Put(to.Tf.position.x);
            writer.Put(to.Tf.position.y);
            writer.Put((short)to.m_Info.List_Reservation.Count);

            CoopNetManager.Instance.SendPacket(PacketType.TileObjectSpawn, writer, DeliveryMethod.ReliableOrdered);
        }

        public void SendTileObjectDestroy(TileObject to)
        {
            if (CoopNetManager.Instance == null || !CoopNetManager.Instance.IsConnected || !CoopNetManager.Instance.IsHost) return;
            if (to == null || to.m_Info == null || to.Tf == null) return;
            if (to.m_Info.m_Type == TileType.A_Citizen || to.m_Info.m_Type == TileType.GBot) return;

            if (!_sentDestroyIds.Add(to.m_ID)) return;
            _sentDestroyQueue.Enqueue(to.m_ID);
            if (_sentDestroyQueue.Count > 1024)
            {
                int old = _sentDestroyQueue.Dequeue();
                _sentDestroyIds.Remove(old);
            }

            NetDataWriter writer = new NetDataWriter();
            writer.Put(to.m_ID);
            writer.Put(to.Tf.position.x);
            writer.Put(to.Tf.position.y);
            writer.Put((short)to.m_Info.m_Type);

            CoopNetManager.Instance.SendPacket(PacketType.TileObjectDestroy, writer, DeliveryMethod.ReliableOrdered);
        }

        public void SendTileObjectCountUpdate(TileObject to)
        {
            if (CoopNetManager.Instance == null || !CoopNetManager.Instance.IsConnected || !CoopNetManager.Instance.IsHost) return;
            if (to == null || to.m_Info == null || to.Tf == null) return;

            NetDataWriter writer = new NetDataWriter();
            writer.Put(to.m_ID);
            writer.Put((short)to.m_Info.m_Type);
            writer.Put(to.Tf.position.x);
            writer.Put(to.Tf.position.y);
            writer.Put((short)to.m_Info.List_Reservation.Count);

            CoopNetManager.Instance.SendPacket(PacketType.TileObjectCountUpdate, writer, DeliveryMethod.ReliableOrdered);
        }

        private void HandleTileObjectCountUpdate(NetDataReader reader)
        {
            if (CoopNetManager.Instance.IsHost) return;
            if (SaveTransferManager.IsWorldLoading) return;
            if (reader.AvailableBytes < 16) return;

            int id = reader.GetInt();
            TileType type = (TileType)reader.GetShort();
            float posX = reader.GetFloat();
            float posY = reader.GetFloat();
            short count = reader.GetShort();

            try
            {
                IsApplyingRemoteItem = true;
                TileObject? target = FindGroundItem(id, posX, posY, type);
                if (target != null)
                {
                    target.m_ID = id;
                    if (count <= 0)
                    {
                        target.ObjectDelete();
                    }
                    else
                    {
                        SetTileObjectCount(target, count);
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[ItemSyncManager] HandleTileObjectCountUpdate error: {ex.Message}");
            }
            finally
            {
                IsApplyingRemoteItem = false;
            }
        }

        private static void SetTileObjectCount(TileObject to, int count)
        {
            if (to?.m_Info?.List_Reservation == null) return;
            int current = to.m_Info.List_Reservation.Count;
            if (current == count) return;

            while (to.m_Info.List_Reservation.Count > count && to.m_Info.List_Reservation.Count > 0)
            {
                to.m_Info.RemoveReservationOne();
            }
            while (to.m_Info.List_Reservation.Count < count)
            {
                to.m_Info.List_Reservation.Add(0);
            }
        }

        private void HandleTileObjectSpawn(NetDataReader reader)
        {
            if (CoopNetManager.Instance.IsHost) return;
            if (SaveTransferManager.IsWorldLoading) return;
            if (reader.AvailableBytes < 16) return;

            int id = reader.GetInt();
            TileType type = (TileType)reader.GetShort();
            float posX = reader.GetFloat();
            float posY = reader.GetFloat();
            short count = reader.GetShort();

            try
            {
                IsApplyingRemoteItem = true;
                TileObject? existing = FindGroundItem(id, posX, posY, type);
                if (existing != null)
                {
                    existing.m_ID = id;
                    EnsureActiveGroundItemVisuals(existing, posX, posY);
                    if (count > 0) SetTileObjectCount(existing, count);
                    return;
                }

                if (GameMgr.Instance?._PoolMgr?.Pool_TileObject != null && count > 0)
                {
                    TileObject to = GameMgr.Instance._PoolMgr.Pool_TileObject.GetNextObj().GetComponent<TileObject>();
                    to.ObjectInit(type, TObjState.Basic, new Vector3(posX, posY, 0f), count, _fadeskip: true);
                    to.m_ID = id;
                    ApplyExactSyncedTransform(to, posX, posY);
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[ItemSyncManager] HandleTileObjectSpawn error: {ex.Message}");
            }
            finally
            {
                IsApplyingRemoteItem = false;
            }
        }

        private void HandleTileObjectDestroy(NetDataReader reader)
        {
            if (CoopNetManager.Instance.IsHost) return;
            if (SaveTransferManager.IsWorldLoading) return;
            if (reader.AvailableBytes < 14) return;

            int id = reader.GetInt();
            float posX = reader.GetFloat();
            float posY = reader.GetFloat();
            TileType type = (TileType)reader.GetShort();

            try
            {
                IsApplyingRemoteItem = true;
                TileObject? target = FindGroundItem(id, posX, posY, type);
                if (target != null)
                {
                    target.ObjectDelete();
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[ItemSyncManager] HandleTileObjectDestroy error: {ex.Message}");
            }
            finally
            {
                IsApplyingRemoteItem = false;
            }
        }

        public void SendRequestPickup(TileObject to, bool pickAll = false)
        {
            if (CoopNetManager.Instance == null || !CoopNetManager.Instance.IsConnected || !CoopNetManager.Instance.IsClient) return;
            if (to == null || to.m_Info == null || to.Tf == null) return;

            NetDataWriter writer = new NetDataWriter();
            writer.Put(to.m_ID);
            writer.Put(to.Tf.position.x);
            writer.Put(to.Tf.position.y);
            writer.Put((short)to.m_Info.m_Type);
            writer.Put((short)to.m_Info.List_Reservation.Count);
            writer.Put(pickAll);

            CoopNetManager.Instance.SendPacket(PacketType.RequestPickupTileObject, writer, DeliveryMethod.ReliableOrdered);
        }

        private void HandleRequestPickupTileObject(NetDataReader reader)
        {
            if (!CoopNetManager.Instance.IsHost) return;
            if (reader.AvailableBytes < 16) return;

            int id = reader.GetInt();
            float posX = reader.GetFloat();
            float posY = reader.GetFloat();
            TileType type = (TileType)reader.GetShort();
            short count = reader.GetShort();
            bool pickAll = reader.AvailableBytes >= 1 && reader.GetBool();

            try
            {
                TileObject? target = FindGroundItem(id, posX, posY, type);
                var clientWorker = QueenSyncManager.Instance?.Player2Worker;

                if (target != null)
                {
                    if (pickAll && target.m_Info != null && GameMgr.Instance?._SysMgr?.m_PileMatNumUI != null &&
                        target.m_Info.m_Type == GameMgr.Instance._SysMgr.m_PileMatNumUI.m_Type)
                    {
                        GameMgr.Instance._SysMgr.m_PileMatNumUI.UseNum(target.m_Info.List_Reservation.Count);
                        if (clientWorker != null)
                            target.PickUpDelete(clientWorker);
                        else
                            target.ObjectDelete();
                        SendTileObjectDestroy(target);
                        return;
                    }

                    if (clientWorker != null && clientWorker.List_Gathering.Count < clientWorker.Get_HandCapacity())
                    {
                        target.ObjectGathered(clientWorker, pick_ani: true);
                    }
                    else
                    {
                        // Hands are full (or worker missing): keep floor item intact and resync client hands + floor item
                        SendTileObjectSpawn(target);
                        if (clientWorker != null)
                        {
                            SendClientHandSync(clientWorker);
                        }
                        return;
                    }

                    if (target.gameObject != null && target.gameObject.activeInHierarchy &&
                        target.m_Info != null && target.m_Info.List_Reservation.Count > 0)
                    {
                        SendTileObjectCountUpdate(target);
                    }
                    else
                    {
                        SendTileObjectDestroy(target);
                    }
                }
                else
                {
                    // Floor item no longer exists on Host: reject phantom pickup and reconcile client hands
                    if (clientWorker != null)
                    {
                        SendClientHandSync(clientWorker);
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[ItemSyncManager] HandleRequestPickupTileObject error: {ex.Message}");
            }
        }

        public void SendRequestDrop(TileType type, int count, Vector2 pos)
        {
            if (CoopNetManager.Instance == null || !CoopNetManager.Instance.IsConnected || !CoopNetManager.Instance.IsClient) return;

            NetDataWriter writer = new NetDataWriter();
            writer.Put((short)type);
            writer.Put((short)count);
            writer.Put(pos.x);
            writer.Put(pos.y);

            CoopNetManager.Instance.SendPacket(PacketType.RequestDropTileObject, writer, DeliveryMethod.ReliableOrdered);
        }

        private void HandleRequestDropTileObject(NetDataReader reader)
        {
            if (!CoopNetManager.Instance.IsHost) return;
            if (reader.AvailableBytes < 12) return;

            TileType type = (TileType)reader.GetShort();
            short count = reader.GetShort();
            float posX = reader.GetFloat();
            float posY = reader.GetFloat();

            try
            {
                var clientWorker = QueenSyncManager.Instance?.Player2Worker;
                if (clientWorker != null && clientWorker.List_Gathering.Count > 0 && clientWorker.List_Gathering.Exists(x => x != null && x.m_Type == type))
                {
                    clientWorker.Drop_GatheringList(); // ObjectInit postfix broadcasts TileObjectSpawn
                }
                else if (clientWorker != null)
                {
                    SendClientHandSync(clientWorker);
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[ItemSyncManager] HandleRequestDropTileObject error: {ex.Message}");
            }
        }

        private void BroadcastGroundItemsBatch()
        {
            if (GameMgr.Instance?._PoolMgr?.List_TileObject == null) return;
            var list = GameMgr.Instance._PoolMgr.List_TileObject;

            _cachedFloorItems.Clear();
            for (int i = 0; i < list.Count; i++)
            {
                var item = list[i];
                if (item != null && item.gameObject != null && item.gameObject.activeInHierarchy && item.m_Info != null && item.Tf != null)
                {
                    _cachedFloorItems.Add(item);
                }
            }

            _groundBatchSeq++;
            int total = _cachedFloorItems.Count;
            if (total == 0)
            {
                NetDataWriter emptyWriter = new NetDataWriter();
                emptyWriter.Put(_groundBatchSeq);
                emptyWriter.Put(true);
                emptyWriter.Put(true);
                emptyWriter.Put((short)0);
                CoopNetManager.Instance.SendPacket(PacketType.TileObjectBatchSync, emptyWriter, DeliveryMethod.ReliableOrdered);
                return;
            }

            const int batchSize = 25;
            for (int offset = 0; offset < total; offset += batchSize)
            {
                int chunkCount = Mathf.Min(batchSize, total - offset);
                bool isFirst = offset == 0;
                bool isLast = offset + chunkCount >= total;

                NetDataWriter writer = new NetDataWriter();
                writer.Put(_groundBatchSeq);
                writer.Put(isFirst);
                writer.Put(isLast);
                writer.Put((short)chunkCount);

                for (int i = 0; i < chunkCount; i++)
                {
                    var to = _cachedFloorItems[offset + i];
                    writer.Put(to.m_ID);
                    writer.Put((short)to.m_Info.m_Type);
                    writer.Put(to.Tf.position.x);
                    writer.Put(to.Tf.position.y);
                    writer.Put((short)to.m_Info.List_Reservation.Count);
                }

                CoopNetManager.Instance.SendPacket(PacketType.TileObjectBatchSync, writer, DeliveryMethod.ReliableOrdered);
            }
        }

        private void HandleTileObjectBatchSync(NetDataReader reader)
        {
            if (CoopNetManager.Instance.IsHost) return;
            if (SaveTransferManager.IsWorldLoading) return;
            if (reader.AvailableBytes < 6) return;

            ushort batchSeq = reader.GetUShort();
            bool isFirst = reader.GetBool();
            bool isLast = reader.GetBool();
            short count = reader.GetShort();

            if (isFirst || batchSeq != _activeClientBatchSeq)
            {
                _activeClientBatchSeq = batchSeq;
                _clientBatchIds.Clear();
            }

            try
            {
                IsApplyingRemoteItem = true;
                for (int i = 0; i < count; i++)
                {
                    if (reader.AvailableBytes < 16) break;
                    int id = reader.GetInt();
                    TileType type = (TileType)reader.GetShort();
                    float posX = reader.GetFloat();
                    float posY = reader.GetFloat();
                    short qty = reader.GetShort();

                    _clientBatchIds.Add(id);

                    TileObject? local = FindGroundItem(id, posX, posY, type);
                    if (local != null)
                    {
                        local.m_ID = id;
                        EnsureActiveGroundItemVisuals(local, posX, posY);
                        if (qty > 0) SetTileObjectCount(local, qty);
                    }
                    else if (GameMgr.Instance?._PoolMgr?.Pool_TileObject != null && qty > 0)
                    {
                        TileObject to = GameMgr.Instance._PoolMgr.Pool_TileObject.GetNextObj().GetComponent<TileObject>();
                        to.ObjectInit(type, TObjState.Basic, new Vector3(posX, posY, 0f), qty, _fadeskip: true);
                        to.m_ID = id;
                        ApplyExactSyncedTransform(to, posX, posY);
                    }
                }

                if (isLast && GameMgr.Instance?._PoolMgr?.List_TileObject != null)
                {
                    var localList = GameMgr.Instance._PoolMgr.List_TileObject;
                    for (int i = localList.Count - 1; i >= 0; i--)
                    {
                        var to = localList[i];
                        if (to != null && to.gameObject != null && !_clientBatchIds.Contains(to.m_ID))
                        {
                            if (to.m_Info != null && (to.m_Info.m_Type == TileType.A_Citizen || to.m_Info.m_Type == TileType.GBot))
                                continue;
                            to.ObjectDelete();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[ItemSyncManager] HandleTileObjectBatchSync error: {ex.Message}");
            }
            finally
            {
                IsApplyingRemoteItem = false;
            }
        }

        private static void ApplyExactSyncedTransform(TileObject to, float posX, float posY)
        {
            if (to == null) return;
            to.SetPosition(new Vector2(posX, posY));
            if (to.Tf != null) to.Tf.localScale = Vector3.one;
            if (to.m_Spr != null) to.m_Spr.color = Color.white;
            if (to.m_Cir2d != null) to.m_Cir2d.enabled = true;
            to.MakeEnableList();
            if (to.gameObject != null && !to.gameObject.activeSelf) to.gameObject.SetActive(true);
        }

        private static void EnsureActiveGroundItemVisuals(TileObject to, float posX, float posY)
        {
            if (to == null) return;
            if (to.gameObject != null && !to.gameObject.activeSelf)
            {
                to.gameObject.SetActive(true);
            }
            if (to.Tf != null)
            {
                to.Tf.localScale = Vector3.one;
                if ((to.Tf.position - new Vector3(posX, posY, to.Tf.position.z)).sqrMagnitude > 0.25f)
                {
                    to.SetPosition(new Vector2(posX, posY));
                    to.MakeEnableList();
                }
            }
            if (to.m_Spr != null) to.m_Spr.color = Color.white;
            if (to.m_Cir2d != null && !to.m_Cir2d.enabled) to.m_Cir2d.enabled = true;
        }

        private TileObject? FindGroundItem(int id, float x, float y, TileType type)
        {
            if (GameMgr.Instance?._PoolMgr?.List_TileObject == null) return null;
            var list = GameMgr.Instance._PoolMgr.List_TileObject;

            // 1. Exact ID + TileType match (prevents cross-type matches if local m_IndexMaker collided)
            for (int i = 0; i < list.Count; i++)
            {
                var to = list[i];
                if (to != null && to.m_ID == id && to.m_Info != null && to.m_Info.m_Type == type)
                {
                    return to;
                }
            }

            // 2. Closest Proximity and TileType fallback match
            TileObject? best = null;
            float bestDistSq = float.MaxValue;
            for (int i = 0; i < list.Count; i++)
            {
                var to = list[i];
                if (to != null && to.m_Info != null && to.m_Info.m_Type == type && to.Tf != null)
                {
                    float dx = to.Tf.position.x - x;
                    float dy = to.Tf.position.y - y;
                    if (Mathf.Abs(dx) <= 1.25f && Mathf.Abs(dy) <= 1.25f)
                    {
                        float dSq = dx * dx + dy * dy;
                        if (dSq < bestDistSq)
                        {
                            bestDistSq = dSq;
                            best = to;
                        }
                    }
                }
            }

            return best;
        }

        // =========================================================================
        // STORAGE BUILDINGS - TRANSMISSION & APPLICATION
        // =========================================================================

        public void NotifyBuildingInventoryChanged(Building building)
        {
            if (building == null) return;
            if (CoopNetManager.Instance == null || !CoopNetManager.Instance.IsConnected || !CoopNetManager.Instance.IsHost) return;

            _dirtyBuildings.Add(building.m_ID);
        }

        private void FlushDirtyBuildings()
        {
            if (GameMgr.Instance?._BuildingMgr?.List_Building == null) return;
            var allBuildings = GameMgr.Instance._BuildingMgr.List_Building;

            foreach (int bId in _dirtyBuildings)
            {
                for (int i = 0; i < allBuildings.Count; i++)
                {
                    var b = allBuildings[i];
                    if (b != null && b.m_ID == bId)
                    {
                        SendBuildingInventory(b);
                        break;
                    }
                }
            }
            _dirtyBuildings.Clear();
        }

        private void BroadcastAllStorageBuildings()
        {
            if (GameMgr.Instance?._BuildingMgr?.List_Storage == null) return;
            var storages = GameMgr.Instance._BuildingMgr.List_Storage;

            for (int i = 0; i < storages.Count; i++)
            {
                var s = storages[i];
                if (s != null && s.m_BuildState == BuildState.Basic)
                {
                    SendBuildingInventory(s);
                }
            }
        }

        public void SendBuildingInventory(Building building)
        {
            if (CoopNetManager.Instance == null || !CoopNetManager.Instance.IsConnected || !CoopNetManager.Instance.IsHost) return;
            if (building == null || building.List_TileObj == null) return;

            NetDataWriter writer = new NetDataWriter();
            writer.Put(building.m_ID);
            writer.Put((short)building.List_TileObj.Count);

            for (int i = 0; i < building.List_TileObj.Count; i++)
            {
                var item = building.List_TileObj[i];
                if (item != null)
                {
                    writer.Put((short)item.m_Type);
                    writer.Put((short)item.List_Reservation.Count);
                    writer.Put((byte)item.m_State);
                }
                else
                {
                    writer.Put((short)0);
                    writer.Put((short)0);
                    writer.Put((byte)0);
                }
            }

            CoopNetManager.Instance.SendPacket(PacketType.BuildingInventorySync, writer, DeliveryMethod.ReliableOrdered);
        }

        public void SendRequestStorageDeposit(int buildingId, TileType type, int count)
        {
            if (CoopNetManager.Instance == null || !CoopNetManager.Instance.IsConnected || !CoopNetManager.Instance.IsClient) return;

            NetDataWriter writer = new NetDataWriter();
            writer.Put(buildingId);
            writer.Put((short)type);
            writer.Put(count);

            CoopNetManager.Instance.SendPacket(PacketType.RequestStorageDeposit, writer, DeliveryMethod.ReliableOrdered);
        }

        private void HandleRequestStorageDeposit(NetDataReader reader)
        {
            if (!CoopNetManager.Instance.IsHost) return;
            if (reader.AvailableBytes < 10) return;

            int buildingId = reader.GetInt();
            TileType type = (TileType)reader.GetShort();
            int count = reader.GetInt();

            try
            {
                Building? building = GameMgr.Instance?._BuildingMgr?.List_Building?.Find(b => b != null && b.m_ID == buildingId);
                if (building != null)
                {
                    var clientWorker = QueenSyncManager.Instance?.Player2Worker;
                    building.AddValue(type, TObjState.Basic, count);
                    if (clientWorker != null)
                    {
                        for (int i = 0; i < count; i++)
                        {
                            clientWorker.Remove_GatheringObj(type);
                        }
                    }
                    SendBuildingInventory(building);
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[ItemSyncManager] HandleRequestStorageDeposit error: {ex.Message}");
            }
        }

        public void SendRequestStorageEject(int buildingId, TileType type, int count)
        {
            if (CoopNetManager.Instance == null || !CoopNetManager.Instance.IsConnected || !CoopNetManager.Instance.IsClient) return;

            NetDataWriter writer = new NetDataWriter();
            writer.Put(buildingId);
            writer.Put((short)type);
            writer.Put(count);

            CoopNetManager.Instance.SendPacket(PacketType.RequestStorageEject, writer, DeliveryMethod.ReliableOrdered);
        }

        private void HandleRequestStorageEject(NetDataReader reader)
        {
            if (!CoopNetManager.Instance.IsHost) return;
            if (reader.AvailableBytes < 10) return;

            int buildingId = reader.GetInt();
            TileType type = (TileType)reader.GetShort();
            int count = reader.GetInt();

            try
            {
                Building? building = GameMgr.Instance?._BuildingMgr?.List_Building?.Find(b => b != null && b.m_ID == buildingId);
                if (building != null && count > 0)
                {
                    int actual = Mathf.Min(count, building.GetNum(type));
                    if (actual > 0)
                    {
                        if (GameMgr.Instance?._PoolMgr?.Pool_TileObject != null && building.Tf != null)
                        {
                            TileObject to = GameMgr.Instance._PoolMgr.Pool_TileObject.GetNextObj().GetComponent<TileObject>();
                            to.ObjectInit(type, TObjState.NeedSave, new Vector2(UnityEngine.Random.Range(-0.5f, 0.5f) + building.Tf.position.x, building.Pos_Tile.y), actual);
                        }
                        building.AddValue(type, TObjState.Purchase, -actual);
                    }
                    SendBuildingInventory(building);
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[ItemSyncManager] HandleRequestStorageEject error: {ex.Message}");
            }
        }

        public void SendRequestStorageWithdraw(int buildingId, TileType type, int count)
        {
            if (CoopNetManager.Instance == null || !CoopNetManager.Instance.IsConnected || !CoopNetManager.Instance.IsClient) return;

            NetDataWriter writer = new NetDataWriter();
            writer.Put(buildingId);
            writer.Put((short)type);
            writer.Put(count);

            CoopNetManager.Instance.SendPacket(PacketType.RequestStorageWithdraw, writer, DeliveryMethod.ReliableOrdered);
        }

        private void HandleRequestStorageWithdraw(NetDataReader reader)
        {
            if (!CoopNetManager.Instance.IsHost) return;
            if (reader.AvailableBytes < 10) return;

            int buildingId = reader.GetInt();
            TileType type = (TileType)reader.GetShort();
            int count = reader.GetInt();

            try
            {
                Building? building = GameMgr.Instance?._BuildingMgr?.List_Building?.Find(b => b != null && b.m_ID == buildingId);
                if (building != null && count > 0)
                {
                    var clientWorker = QueenSyncManager.Instance?.Player2Worker;
                    int available = building.GetNum(type);
                    int freeSlots = clientWorker != null ? Mathf.Max(0, clientWorker.Get_HandCapacity() - clientWorker.List_Gathering.Count) : 0;
                    int actual = Mathf.Min(count, Mathf.Min(available, freeSlots));

                    if (actual > 0 && clientWorker != null)
                    {
                        bool wasEmpty = clientWorker.List_Gathering.Count == 0;
                        building.AddValue(type, TObjState.Purchase, -actual);
                        for (int i = 0; i < actual; i++)
                        {
                            clientWorker.List_Gathering.Add(new TileSt_Info(type, TObjState.Basic, 1));
                        }
                        if (wasEmpty)
                        {
                            var spr = Func.Instance?.LoadSprite($"{type}Object");
                            if (spr != null) clientWorker.StickImgSet(spr, true);
                            clientWorker.SetCharState(CharState.Carrying);
                        }
                    }

                    SendBuildingInventory(building);
                    if (actual < count && clientWorker != null)
                    {
                        SendClientHandSync(clientWorker);
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[ItemSyncManager] HandleRequestStorageWithdraw error: {ex.Message}");
            }
        }

        public void SendClientHandSync(T_Queen clientWorker)
        {
            if (CoopNetManager.Instance == null || !CoopNetManager.Instance.IsConnected || !CoopNetManager.Instance.IsHost) return;
            if (clientWorker == null || clientWorker.List_Gathering == null) return;

            NetDataWriter writer = new NetDataWriter();
            writer.Put(clientWorker.m_GatherUnitIndex);
            byte count = (byte)Mathf.Min(clientWorker.List_Gathering.Count, 255);
            writer.Put(count);
            for (int i = 0; i < count; i++)
            {
                var entry = clientWorker.List_Gathering[i];
                if (entry != null)
                {
                    writer.Put((short)entry.m_Type);
                    writer.Put((byte)entry.m_State);
                    writer.Put((short)(entry.List_Reservation != null ? entry.List_Reservation.Count : 1));
                }
                else
                {
                    writer.Put((short)0);
                    writer.Put((byte)0);
                    writer.Put((short)0);
                }
            }

            CoopNetManager.Instance.SendPacket(PacketType.ClientHandSync, writer, DeliveryMethod.ReliableOrdered);
        }

        private void HandleClientHandSync(NetDataReader reader)
        {
            if (CoopNetManager.Instance.IsHost) return;
            if (SaveTransferManager.IsWorldLoading) return;
            if (reader.AvailableBytes < 5) return;

            int gatherUnitIndex = reader.GetInt();
            byte count = reader.GetByte();

            try
            {
                var localChar = QueenSyncManager.Instance?.LocalCharacter;
                if (localChar == null || localChar.List_Gathering == null) return;

                // Preserve active Citizen/GBot rescue carry if locally mid-carry
                if (localChar.m_GatherUnitIndex != 0 && localChar.List_Gathering.Count > 0 &&
                    (localChar.List_Gathering[0].m_Type == TileType.A_Citizen || localChar.List_Gathering[0].m_Type == TileType.GBot))
                {
                    return;
                }

                localChar.m_GatherUnitIndex = gatherUnitIndex;
                localChar.List_Gathering.Clear();

                for (int i = 0; i < count; i++)
                {
                    if (reader.AvailableBytes < 5) break;
                    TileType type = (TileType)reader.GetShort();
                    TObjState state = (TObjState)reader.GetByte();
                    short resCount = reader.GetShort();
                    if (type != TileType.None && type != TileType.Default)
                    {
                        localChar.List_Gathering.Add(new TileSt_Info(type, state, resCount > 0 ? resCount : 1));
                    }
                }

                if (localChar.List_Gathering.Count > 0)
                {
                    var first = localChar.List_Gathering[0];
                    var spr = Func.Instance?.LoadSprite($"{first.m_Type}Object");
                    if (spr != null) localChar.StickImgSet(spr, true);
                    localChar.SetCharState(CharState.Carrying);
                }
                else
                {
                    localChar.StickImgSet(null, false);
                    if (localChar.m_CharState == CharState.Carrying)
                    {
                        localChar.SetCharState(CharState.None);
                        localChar.SetAniState(AniState.Idle, _loop: true, _now: false);
                    }
                }

                try { localChar.GatherNumEffect(); } catch { }
                try { localChar.m_CheckBox?.HotKeyUpdate(); } catch { }

                try
                {
                    var storageUi = GameMgr.Instance?._StorageUI;
                    if (storageUi != null && ItemPatches.IsStorageUiOpen && storageUi.enabled)
                    {
                        Traverse.Create(storageUi).Method("MineUpdate").GetValue();
                    }
                }
                catch { }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[ItemSyncManager] HandleClientHandSync error: {ex.Message}");
            }
        }

        private void HandleBuildingInventorySync(NetDataReader reader)
        {
            if (CoopNetManager.Instance.IsHost) return;
            if (SaveTransferManager.IsWorldLoading) return;
            if (reader.AvailableBytes < 6) return;

            int buildingId = reader.GetInt();
            short itemsCount = reader.GetShort();

            try
            {
                IsApplyingRemoteStorage = true;
                Building? building = GameMgr.Instance?._BuildingMgr?.List_Building?.Find(b => b != null && b.m_ID == buildingId);
                if (building == null) return;

                if (building.List_TileObj == null)
                {
                    building.List_TileObj = new List<TileSt_Info>();
                }
                else
                {
                    building.List_TileObj.Clear();
                }

                for (int i = 0; i < itemsCount; i++)
                {
                    if (reader.AvailableBytes < 5) break;
                    TileType type = (TileType)reader.GetShort();
                    short count = reader.GetShort();
                    TObjState state = (TObjState)reader.GetByte();

                    if (count > 0)
                    {
                        TileSt_Info item = new TileSt_Info(type, state, count);
                        building.List_TileObj.Add(item);
                    }
                }

                // Refresh UI and visuals safely (isolated guards avoid partial UI NRE aborts)
                try
                {
                    if (building.m_BuildInfoUI != null && building.List_TileObj != null)
                    {
                        building.m_BuildInfoUI.InfoUpdate();
                    }
                }
                catch { }

                try
                {
                    if (GameMgr.Instance?._BuildMidUI != null && GameMgr.Instance._BuildMidUI.m_StorageInfoUI != null && GameMgr.Instance._BuildMidUI.gameObject.activeInHierarchy)
                    {
                        GameMgr.Instance._BuildMidUI.m_StorageInfoUI.InfoUpdate();
                    }
                }
                catch { }

                try
                {
                    var storageUi = GameMgr.Instance?._StorageUI;
                    if (storageUi != null && ItemPatches.IsStorageUIOpenFor(building) && storageUi.m_Mode == StorageUI.StorageMode.Interaction)
                    {
                        RefreshOpenStorageUiInPlace(storageUi, building);
                    }
                }
                catch (Exception exUi)
                {
                    Plugin.Log.LogWarning($"[ItemSyncManager] RefreshOpenStorageUiInPlace error: {exUi.Message}");
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[ItemSyncManager] HandleBuildingInventorySync error: {ex.Message}");
            }
            finally
            {
                IsApplyingRemoteStorage = false;
            }
        }

        private static bool InventoryMatchesUiList(List<TileSt_Info> bldItems, List<StorageUI_MatSlot_Data> uiItems)
        {
            if (bldItems.Count != uiItems.Count) return false;
            for (int i = 0; i < uiItems.Count; i++)
            {
                var uiInfo = uiItems[i]?.m_TileSt_Info;
                if (uiInfo == null) return false;
                bool matched = false;
                for (int j = 0; j < bldItems.Count; j++)
                {
                    var b = bldItems[j];
                    if (b != null && b.m_Type == uiInfo.m_Type &&
                        b.List_Reservation.Count == uiInfo.List_Reservation.Count &&
                        b.m_State == uiInfo.m_State)
                    {
                        matched = true;
                        break;
                    }
                }
                if (!matched) return false;
            }
            return true;
        }

        private static void RefreshOpenStorageUiInPlace(StorageUI storageUi, Building building)
        {
            var trav = Traverse.Create(storageUi);
            var listMatSlotData = trav.Field<List<StorageUI_MatSlot_Data>>("List_MatSlotData").Value;
            var gpmScroll = trav.Field<InfiniteScroll>("m_Gpm_Scorll").Value;
            if (listMatSlotData == null || gpmScroll == null || building.List_TileObj == null) return;

            // Fast path: if inventory counts/types already match (periodic 2.5s sync or Client's optimistic update),
            // just rebind TileSt_Info references without rebuilding the scroll list, jumping scroll, or switching tabs!
            if (InventoryMatchesUiList(building.List_TileObj, listMatSlotData))
            {
                for (int i = 0; i < listMatSlotData.Count; i++)
                {
                    var t = listMatSlotData[i].m_TileSt_Info.m_Type;
                    var replacement = building.List_TileObj.Find(x => x != null && x.m_Type == t);
                    if (replacement != null)
                    {
                        listMatSlotData[i].m_TileSt_Info = replacement;
                    }
                }
                if (gpmScroll.items != null)
                {
                    for (int i = 0; i < gpmScroll.items.Count; i++)
                    {
                        if (gpmScroll.items[i] is StorageUI_MatSlot matSlot && matSlot.m_TileSt_Info != null)
                        {
                            var replacement = building.List_TileObj.Find(x => x != null && x.m_Type == matSlot.m_TileSt_Info.m_Type);
                            if (replacement != null) matSlot.m_TileSt_Info = replacement;
                        }
                    }
                }
                trav.Method("MineUpdate").GetValue();
                return;
            }

            // Contents actually changed (e.g. Host or citizen withdrew/deposited while StorageUI is open):
            // rebuild list cleanly in-place while preserving active tab (Mine vs Storage), scroll, and selection!
            var savedState = storageUi.m_State;
            var scrollBar = trav.Field<Scrollbar>("m_ScrollBar").Value;
            float savedScroll = scrollBar != null ? scrollBar.value : 1f;
            var selectSlot = trav.Field<StorageUI_Slot?>("m_SelectSlot").Value as StorageUI_MatSlot;
            TileType? savedSelectedType = selectSlot != null && selectSlot.m_TileSt_Info != null
                ? selectSlot.m_TileSt_Info.m_Type
                : (TileType?)null;
            int savedSelectIndex = trav.Field<int>("m_SelectIndex").Value;
            int sortIndex = trav.Field<int>("m_SortIndex").Value;

            var rebuilt = new List<StorageUI_MatSlot_Data>(building.List_TileObj.Count);
            for (int i = 0; i < building.List_TileObj.Count; i++)
            {
                var info = building.List_TileObj[i];
                if (info == null) continue;
                var data = new StorageUI_MatSlot_Data
                {
                    m_Index = i,
                    m_Mode = storageUi.m_Mode,
                    m_TileSt_Info = info
                };
                rebuilt.Add(data);
            }

            var dbMgr = GameMgr.Instance?._DB_Mgr;
            if (sortIndex == 0 && dbMgr != null)
            {
                rebuilt.Sort((a, b) => string.Compare(
                    dbMgr.GetTileInfo(a.m_TileSt_Info.m_Type)?.T_Name ?? string.Empty,
                    dbMgr.GetTileInfo(b.m_TileSt_Info.m_Type)?.T_Name ?? string.Empty,
                    StringComparison.Ordinal));
            }
            else if (sortIndex == 1)
            {
                rebuilt.Sort((a, b) => b.m_TileSt_Info.List_Reservation.Count.CompareTo(a.m_TileSt_Info.List_Reservation.Count));
            }
            else if (sortIndex == 2 && dbMgr != null)
            {
                rebuilt.Sort((a, b) => (dbMgr.GetTileInfo(b.m_TileSt_Info.m_Type)?.Price ?? 0).CompareTo(
                    dbMgr.GetTileInfo(a.m_TileSt_Info.m_Type)?.Price ?? 0));
            }
            else if (sortIndex == 3 && dbMgr != null)
            {
                rebuilt.Sort((a, b) => (dbMgr.GetTileInfo(b.m_TileSt_Info.m_Type)?.Category ?? 0).CompareTo(
                    dbMgr.GetTileInfo(a.m_TileSt_Info.m_Type)?.Category ?? 0));
            }

            listMatSlotData.Clear();
            gpmScroll.Clear();

            for (int i = 0; i < rebuilt.Count; i++)
            {
                rebuilt[i].m_Index = i;
                listMatSlotData.Add(rebuilt[i]);
                gpmScroll.InsertData(rebuilt[i]);
            }

            if (listMatSlotData.Count == 0)
            {
                trav.Field<int>("m_SelectIndex").Value = 0;
                trav.Field<StorageUI_Slot?>("m_SelectSlot").Value = null;
                gpmScroll.UpdateAllData();
                if (savedState == StorageUI.StorageUI_State.Storage && GameMgr.Instance?._BatchUI?.m_ResToolTip != null)
                {
                    GameMgr.Instance._BatchUI.m_ResToolTip.gameObject.SetActive(false);
                }
            }
            else if (savedState == StorageUI.StorageUI_State.Storage)
            {
                int newIndex = -1;
                if (savedSelectedType.HasValue)
                {
                    newIndex = listMatSlotData.FindIndex(x => x.m_TileSt_Info.m_Type == savedSelectedType.Value);
                }
                if (newIndex < 0)
                {
                    newIndex = Mathf.Clamp(savedSelectIndex, 0, listMatSlotData.Count - 1);
                }
                trav.Field<bool>("m_OpenSfxCheck").Value = true;
                storageUi.Selected(newIndex, _move: false);
            }
            else
            {
                trav.Field<int>("m_SelectIndex").Value = Mathf.Clamp(savedSelectIndex, 0, listMatSlotData.Count - 1);
                trav.Field<StorageUI_Slot?>("m_SelectSlot").Value = null;
                gpmScroll.UpdateAllData();
                if (gpmScroll.items != null)
                {
                    for (int i = 0; i < gpmScroll.items.Count; i++)
                    {
                        if (gpmScroll.items[i] is StorageUI_Slot slotItem)
                        {
                            slotItem.Selected(_act: false);
                        }
                    }
                }
            }

            if (scrollBar != null)
            {
                scrollBar.value = savedScroll;
            }
            trav.Method("MineUpdate").GetValue();
        }
    }
}
