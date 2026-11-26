using System;
using System.Collections.Generic;
using LiteNetLib;
using LiteNetLib.Utils;
using RatopiaCoop.Network;
using UnityEngine;

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
        private const float TileObjectBatchInterval = 4.0f;

        private float _lastStorageBatchTime;
        private const float StorageBatchInterval = 2.5f;

        private readonly HashSet<int> _dirtyBuildings = new HashSet<int>();
        private readonly List<TileObject> _cachedFloorItems = new List<TileObject>();
        private ushort _groundBatchSeq;
        private ushort _activeClientBatchSeq;
        private readonly HashSet<int> _clientBatchIds = new HashSet<int>();

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
            BroadcastGroundItemsBatch();
            BroadcastAllStorageBuildings();
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
            }
        }

        // =========================================================================
        // GROUND ITEMS (TileObject) - TRANSMISSION & APPLICATION
        // =========================================================================

        public void SendTileObjectSpawn(TileObject to)
        {
            if (CoopNetManager.Instance == null || !CoopNetManager.Instance.IsConnected || !CoopNetManager.Instance.IsHost) return;
            if (to == null || to.m_Info == null || to.Tf == null) return;

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
                    if (count > 0) SetTileObjectCount(existing, count);
                    return;
                }

                if (GameMgr.Instance?._PoolMgr?.Pool_TileObject != null)
                {
                    TileObject to = GameMgr.Instance._PoolMgr.Pool_TileObject.GetNextObj(_act: false).GetComponent<TileObject>();
                    to.ObjectInit(type, TObjState.Basic, new Vector3(posX, posY, 0f), count, _fadeskip: true);
                    to.m_ID = id;
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
                        if (target.m_Info != null && target.m_Info.List_Reservation.Count > 1)
                        {
                            target.m_Info.RemoveReservationOne();
                        }
                        else
                        {
                            target.ObjectDelete();
                        }
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
                else if (clientWorker != null && clientWorker.List_Gathering.Count < clientWorker.Get_HandCapacity())
                {
                    // Fallback: ground item already despawned or picked, ensure client worker has carried item on Host
                    TileSt_Info item = new TileSt_Info(type, TObjState.Basic, 1);
                    clientWorker.List_Gathering.Add(item);
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
                else if (GameMgr.Instance?._PoolMgr?.Pool_TileObject != null)
                {
                    if (clientWorker != null) clientWorker.List_Gathering?.Clear();
                    TileObject to = GameMgr.Instance._PoolMgr.Pool_TileObject.GetNextObj(_act: false).GetComponent<TileObject>();
                    to.ObjectInit(type, TObjState.Basic, new Vector3(posX, posY, 0f), count, _fadeskip: false);
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
                        if (qty > 0) SetTileObjectCount(local, qty);
                    }
                    else if (GameMgr.Instance?._PoolMgr?.Pool_TileObject != null && qty > 0)
                    {
                        TileObject to = GameMgr.Instance._PoolMgr.Pool_TileObject.GetNextObj(_act: false).GetComponent<TileObject>();
                        to.ObjectInit(type, TObjState.Basic, new Vector3(posX, posY, 0f), qty, _fadeskip: true);
                        to.m_ID = id;
                    }
                }

                if (isLast && GameMgr.Instance?._PoolMgr?.List_TileObject != null)
                {
                    var localList = GameMgr.Instance._PoolMgr.List_TileObject;
                    for (int i = localList.Count - 1; i >= 0; i--)
                    {
                        var to = localList[i];
                        if (to != null && to.gameObject != null && to.gameObject.activeInHierarchy && !_clientBatchIds.Contains(to.m_ID))
                        {
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

        private TileObject? FindGroundItem(int id, float x, float y, TileType type)
        {
            if (GameMgr.Instance?._PoolMgr?.List_TileObject == null) return null;
            var list = GameMgr.Instance._PoolMgr.List_TileObject;

            // 1. Exact ID match
            for (int i = 0; i < list.Count; i++)
            {
                var to = list[i];
                if (to != null && to.m_ID == id) return to;
            }

            // 2. Proximity and TileType fallback match
            for (int i = 0; i < list.Count; i++)
            {
                var to = list[i];
                if (to != null && to.m_Info != null && to.m_Info.m_Type == type && to.Tf != null)
                {
                    if (Mathf.Abs(to.Tf.position.x - x) <= 1.0f && Mathf.Abs(to.Tf.position.y - y) <= 1.0f)
                    {
                        return to;
                    }
                }
            }

            return null;
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
                if (building != null)
                {
                    var clientWorker = QueenSyncManager.Instance?.Player2Worker;
                    building.AddValue(type, TObjState.Purchase, -count);
                    if (clientWorker != null && clientWorker.List_Gathering.Count < clientWorker.Get_HandCapacity())
                    {
                        TileSt_Info item = new TileSt_Info(type, TObjState.Basic, count);
                        clientWorker.List_Gathering.Add(item);
                    }
                    SendBuildingInventory(building);
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[ItemSyncManager] HandleRequestStorageWithdraw error: {ex.Message}");
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
                    var gameMgr = GameMgr.Instance;
                    var storageUi = gameMgr?._StorageUI;
                    if (gameMgr != null && storageUi != null && storageUi.gameObject.activeInHierarchy && storageUi.m_Building_Storage == building)
                    {
                        storageUi.StorageUI_Open(building, storageUi.m_Mode, gameMgr._BuildMidUI);
                    }
                }
                catch { }
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
    }
}
