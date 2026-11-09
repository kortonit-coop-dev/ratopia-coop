using System;
using LiteNetLib;
using LiteNetLib.Utils;
using RatopiaCoop.Network;
using RatopiaCoop.Patches;
using UnityEngine;

namespace RatopiaCoop.Sync
{
    public class WorldSyncManager : MonoBehaviour
    {
        public static WorldSyncManager Instance { get; private set; } = null!;

        public bool IsApplyingRemoteTile { get; private set; }
        public bool IsApplyingRemoteWorldObject { get; private set; }

        // BUG FIX #8: Increase time sync frequency for better synchronization
        private float _timeSyncInterval = 0.5f; // Every 0.5 seconds (was 1.5s)
        private float _lastTimeSync;

        private float _worldObjectScanInterval = 1.0f;
        private float _lastWorldObjectScan;
        private float _worldObjectBatchInterval = 5.0f;
        private float _lastWorldObjectBatch;
        private readonly System.Collections.Generic.Dictionary<Vector2Int, byte> _cachedPlantLevels = new System.Collections.Generic.Dictionary<Vector2Int, byte>();

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
                DontDestroyOnLoad(gameObject);
            }
            else if (Instance != this)
            {
                Destroy(this);
            }
        }

        private void Start()
        {
            if (CoopNetManager.Instance != null)
            {
                CoopNetManager.Instance.OnPacketReceivedEvent += HandlePacket;
            }
        }

        private void Update()
        {
            // Host sends periodic time & pause sync to keep both games synchronized
            if (CoopNetManager.Instance != null && CoopNetManager.Instance.IsHost && CoopNetManager.Instance.IsConnected)
            {
                if (Time.time - _lastTimeSync >= _timeSyncInterval)
                {
                    _lastTimeSync = Time.time;
                    SendTimeSync();
                }

                UpdateWorldObjects();
            }
        }

        public void SendTileDestroyed(int x, int y, bool isWater = false)
        {
            if (CoopNetManager.Instance == null || !CoopNetManager.Instance.IsConnected || !CoopNetManager.Instance.IsHost) return;
            if (SaveTransferManager.IsWorldLoading) return;

            NetDataWriter writer = new NetDataWriter();
            writer.Put(x);
            writer.Put(y);
            writer.Put(isWater);
            CoopNetManager.Instance.SendPacket(PacketType.TileDestroyed, writer, DeliveryMethod.ReliableOrdered);
        }

        public void SendRequestMineTile(int x, int y, bool isWater)
        {
            if (CoopNetManager.Instance == null || !CoopNetManager.Instance.IsConnected || !CoopNetManager.Instance.IsClient) return;
            if (SaveTransferManager.IsWorldLoading) return;

            NetDataWriter writer = new NetDataWriter();
            writer.Put(x);
            writer.Put(y);
            writer.Put(isWater);
            CoopNetManager.Instance.SendPacket(PacketType.RequestMineTile, writer, DeliveryMethod.ReliableOrdered);
        }

        public void SendTileCreated(int tileType, int x, int y, int attNum)
        {
            if (CoopNetManager.Instance == null || !CoopNetManager.Instance.IsConnected || !CoopNetManager.Instance.IsHost) return;
            if (SaveTransferManager.IsWorldLoading) return;

            NetDataWriter writer = new NetDataWriter();
            writer.Put(tileType);
            writer.Put(x);
            writer.Put(y);
            writer.Put(attNum);
            CoopNetManager.Instance.SendPacket(PacketType.TileCreated, writer, DeliveryMethod.ReliableOrdered);
        }

        public void SendPlaceBlueprint(string buildInfoName, Vector2 pos, int attNum, int funcNum)
        {
            if (CoopNetManager.Instance == null || !CoopNetManager.Instance.IsConnected) return;
            if (SaveTransferManager.IsWorldLoading) return;

            NetDataWriter writer = new NetDataWriter();
            writer.Put(buildInfoName);
            writer.Put(pos.x);
            writer.Put(pos.y);
            writer.Put(attNum);
            writer.Put(funcNum);
            CoopNetManager.Instance.SendPacket(PacketType.OrderPlaceBlueprint, writer, DeliveryMethod.ReliableOrdered);
        }

        public void SendCancelBlueprint(int x, int y)
        {
            if (CoopNetManager.Instance == null || !CoopNetManager.Instance.IsConnected) return;
            if (SaveTransferManager.IsWorldLoading) return;

            NetDataWriter writer = new NetDataWriter();
            writer.Put(x);
            writer.Put(y);
            CoopNetManager.Instance.SendPacket(PacketType.OrderCancelBlueprint, writer, DeliveryMethod.ReliableOrdered);
        }

        public void SendBuildingCompleted(string buildName, Vector2 pos, int attNum)
        {
            if (CoopNetManager.Instance == null || !CoopNetManager.Instance.IsConnected || !CoopNetManager.Instance.IsHost) return;

            NetDataWriter writer = new NetDataWriter();
            writer.Put(buildName);
            writer.Put(pos.x);
            writer.Put(pos.y);
            writer.Put(attNum);
            CoopNetManager.Instance.SendPacket(PacketType.BuildingCompleted, writer, DeliveryMethod.ReliableOrdered);
        }

        public void SendBuildingDemolished(int x, int y)
        {
            if (CoopNetManager.Instance == null || !CoopNetManager.Instance.IsConnected || !CoopNetManager.Instance.IsHost) return;

            NetDataWriter writer = new NetDataWriter();
            writer.Put(x);
            writer.Put(y);
            CoopNetManager.Instance.SendPacket(PacketType.BuildingDemolished, writer, DeliveryMethod.ReliableOrdered);
        }

        private void SendTimeSync()
        {
            if (GameMgr.Instance == null || GameMgr.Instance._SysMgr == null) return;
            var sys = GameMgr.Instance._SysMgr;
            var weather = GameMgr.Instance._WeatherMgr;

            NetDataWriter writer = new NetDataWriter();
            writer.Put(sys.m_Day);
            writer.Put(sys.m_Hour);
            writer.Put(sys.m_Minute);
            writer.Put(weather != null ? (int)weather.m_SeasonState : 0);
            writer.Put(sys.IsGamePause());
            writer.Put(Time.timeScale);

            CoopNetManager.Instance.SendPacket(PacketType.TimeSync, writer, DeliveryMethod.ReliableOrdered);
        }

        // BUG FIX #8: Send pause acknowledgment from client to host
        private void SendPauseAck(bool isPaused)
        {
            if (CoopNetManager.Instance == null || !CoopNetManager.Instance.IsConnected) return;

            NetDataWriter writer = new NetDataWriter();
            writer.Put(isPaused);
            CoopNetManager.Instance.SendPacket(PacketType.PauseAck, writer, DeliveryMethod.ReliableOrdered);
        }

        private void HandlePacket(PacketType type, NetDataReader reader)
        {
            switch (type)
            {
                case PacketType.TileDestroyed:
                {
                    // Host is the authority - Host must NEVER accept TileDestroyed from a client!
                    if (CoopNetManager.Instance.IsHost)
                    {
                        Plugin.Log.LogWarning("[WorldSyncManager] Ignored TileDestroyed packet received on Host. Only Host can destroy tiles.");
                        break;
                    }

                    int x = reader.GetInt();
                    int y = reader.GetInt();
                    bool isWater = reader.AvailableBytes >= 1 && reader.GetBool();
                    ApplyRemoteTileDestroyed(x, y, isWater);
                    break;
                }
                case PacketType.RequestMineTile:
                {
                    // Only Host processes manual mining requests from Client
                    if (!CoopNetManager.Instance.IsHost || SaveTransferManager.IsWorldLoading)
                        break;

                    int x = reader.GetInt();
                    int y = reader.GetInt();
                    bool isWater = reader.AvailableBytes >= 1 && reader.GetBool();
                    HandleClientMineTileRequest(x, y, isWater);
                    break;
                }
                case PacketType.TileCreated:
                {
                    // Host is the authority - Host must NEVER accept TileCreated from a client!
                    if (CoopNetManager.Instance.IsHost)
                    {
                        Plugin.Log.LogWarning("[WorldSyncManager] Ignored TileCreated packet received on Host. Only Host can create tiles.");
                        break;
                    }

                    int tType = reader.GetInt();
                    int x = reader.GetInt();
                    int y = reader.GetInt();
                    int attNum = reader.GetInt();
                    ApplyRemoteTileCreated(tType, x, y, attNum);
                    break;
                }
                case PacketType.OrderPlaceBlueprint:
                {
                    string buildName = reader.GetString();
                    float posX = reader.GetFloat();
                    float posY = reader.GetFloat();
                    int att = reader.GetInt();
                    int func = reader.GetInt();
                    ApplyRemoteBlueprint(buildName, new Vector2(posX, posY), att, func);
                    break;
                }
                case PacketType.OrderCancelBlueprint:
                {
                    int x = reader.GetInt();
                    int y = reader.GetInt();
                    ApplyRemoteCancelBlueprint(x, y);
                    break;
                }
                case PacketType.BuildingCompleted:
                {
                    // Host is the authority - Host must NEVER accept BuildingCompleted from a client!
                    if (CoopNetManager.Instance.IsHost)
                    {
                        Plugin.Log.LogWarning("[WorldSyncManager] Ignored BuildingCompleted packet received on Host. Only Host completes buildings.");
                        break;
                    }

                    string bName = reader.GetString();
                    float posX = reader.GetFloat();
                    float posY = reader.GetFloat();
                    int att = reader.GetInt();
                    ApplyRemoteBuildingCompleted(bName, new Vector2(posX, posY), att);
                    break;
                }
                case PacketType.BuildingDemolished:
                {
                    // Host is the authority - Host must NEVER accept BuildingDemolished from a client!
                    if (CoopNetManager.Instance.IsHost)
                    {
                        Plugin.Log.LogWarning("[WorldSyncManager] Ignored BuildingDemolished packet received on Host. Only Host demolishes buildings.");
                        break;
                    }

                    int x = reader.GetInt();
                    int y = reader.GetInt();
                    ApplyRemoteBuildingDemolished(x, y);
                    break;
                }
                case PacketType.TimeSync:
                {
                    // BUG FIX #8: More robust time synchronization
                    if (reader.AvailableBytes >= 20)
                    {
                        int day = reader.GetInt();
                        int hour = reader.GetInt();
                        int min = reader.GetInt();
                        int season = reader.GetInt();
                        bool isPaused = reader.GetBool();
                        float timeScale = reader.GetFloat();

                        ApplyRemoteTime(day, hour, min, season);
                        SystemPatches.ApplyNetworkPause(isPaused, timeScale);
                    }
                    break;
                }
                case PacketType.GamePause:
                {
                    // BUG FIX #8: Immediate pause synchronization
                    if (reader.AvailableBytes >= 5)
                    {
                        bool isPaused = reader.GetBool();
                        float timeScale = reader.GetFloat();
                        SystemPatches.ApplyNetworkPause(isPaused, timeScale);

                        Plugin.Log.LogInfo($"[WorldSyncManager] Received GamePause: paused={isPaused}, timeScale={timeScale}");

                        // Send acknowledgment back to host
                        if (!CoopNetManager.Instance.IsHost)
                        {
                            SendPauseAck(isPaused);
                        }
                    }
                    break;
                }
                case PacketType.PauseAck:
                {
                    // BUG FIX #8: Client confirmed pause state
                    if (reader.AvailableBytes >= 1)
                    {
                        bool clientPaused = reader.GetBool();
                        Plugin.Log.LogInfo($"[WorldSyncManager] Client acknowledged pause state: {clientPaused}");
                    }
                    break;
                }
                case PacketType.InvasionAlert:
                {
                    if (reader.AvailableBytes >= 5)
                    {
                        int num = reader.GetInt();
                        bool delay = reader.GetBool();
                        string str = reader.GetString();
                        SystemPatches.ApplyNetworkInvasion(num, delay, str);
                    }
                    break;
                }
                case PacketType.WorldObjectUpdate:
                {
                    if (CoopNetManager.Instance.IsHost) break;
                    if (reader.AvailableBytes >= 10)
                    {
                        int x = reader.GetInt();
                        int y = reader.GetInt();
                        byte level = reader.GetByte();
                        bool isDestroyed = reader.GetBool();
                        short tileType = reader.AvailableBytes >= 2 ? reader.GetShort() : (short)0;
                        ApplyRemoteWorldObject(x, y, level, isDestroyed, (TileType)tileType);
                    }
                    break;
                }
                case PacketType.WorldObjectBatchSync:
                {
                    if (CoopNetManager.Instance.IsHost) break;
                    if (reader.AvailableBytes >= 2)
                    {
                        short count = reader.GetShort();
                        for (int i = 0; i < count; i++)
                        {
                            if (reader.AvailableBytes < 7) break;
                            short x = reader.GetShort();
                            short y = reader.GetShort();
                            byte level = reader.GetByte();
                            short tileType = reader.GetShort();
                            ApplyRemoteWorldObject(x, y, level, false, (TileType)tileType);
                        }
                    }
                    break;
                }
                case PacketType.RequestHarvestWorldObj:
                {
                    if (!CoopNetManager.Instance.IsHost) break;
                    if (reader.AvailableBytes >= 8)
                    {
                        int x = reader.GetInt();
                        int y = reader.GetInt();
                        HandleClientHarvestRequest(x, y);
                    }
                    break;
                }
            }
        }

        private void HandleClientMineTileRequest(int x, int y, bool isWater)
        {
            var tileMgr = GameMgr.Instance?._TileMgr;
            if (tileMgr == null || !tileMgr.IsInsideMap(x, y)) return;

            C_Tile? tile = isWater ? tileMgr.GetWaterTile(x, y) : tileMgr.GetTile(x, y);
            if (tile != null && !tile.IsDestroy)
            {
                // Host destroys the tile normally; TilePatches will broadcast TileDestroyed to Client
                tile.DestroyTile(Dir_check: !isWater);
            }
        }

        private void ApplyRemoteTileDestroyed(int x, int y, bool isWater)
        {
            var tileMgr = GameMgr.Instance?._TileMgr;
            if (tileMgr == null || !tileMgr.IsInsideMap(x, y)) return;

            try
            {
                IsApplyingRemoteTile = true;
                if (isWater)
                {
                    C_Tile? waterTile = tileMgr.GetWaterTile(x, y);
                    if (waterTile != null && !waterTile.IsDestroy)
                    {
                        waterTile.DestroyTile(Dir_check: false);
                    }
                }
                else
                {
                    C_Tile? mainTile = tileMgr.GetTile(x, y);
                    if (mainTile != null && !mainTile.IsDestroy)
                    {
                        mainTile.DestroyTile(Dir_check: true);
                    }
                }
            }
            finally
            {
                IsApplyingRemoteTile = false;
            }
        }

        private void ApplyRemoteTileCreated(int tileType, int x, int y, int attNum)
        {
            if (GameMgr.Instance == null || GameMgr.Instance._TileMgr == null) return;

            try
            {
                IsApplyingRemoteTile = true;
                GameMgr.Instance._TileMgr.MakeTile((TileType)tileType, new Vector2(x, y), attNum);
            }
            finally
            {
                IsApplyingRemoteTile = false;
            }
        }

        private void ApplyRemoteBlueprint(string buildNameStr, Vector2 pos, int att, int func)
        {
            if (SaveTransferManager.IsWorldLoading) return;
            if (GameMgr.Instance == null || GameMgr.Instance._BuildingMgr == null || GameMgr.Instance._DB_Mgr == null) return;

            int rx = Mathf.RoundToInt(pos.x);
            int ry = Mathf.RoundToInt(pos.y);
            var bpList = GameMgr.Instance._BuildingMgr.List_BP_BlueBuilding;
            if (bpList != null && bpList.Exists(b => b != null && Mathf.RoundToInt(b.Pos_Tile.x) == rx && Mathf.RoundToInt(b.Pos_Tile.y) == ry))
            {
                // Prevent duplicate blueprint stacking at the same coordinates
                return;
            }

            if (Enum.TryParse<BuildingName>(buildNameStr, out var bName))
            {
                BuildInfo? info = GameMgr.Instance._DB_Mgr.Dic_BuildDB.ContainsKey(bName) 
                    ? GameMgr.Instance._DB_Mgr.Dic_BuildDB[bName] 
                    : (GameMgr.Instance._DB_Mgr.Dic_BlockBuildDB.ContainsKey(bName) ? GameMgr.Instance._DB_Mgr.Dic_BlockBuildDB[bName] : null);

                if (info != null && GameMgr.Instance._PoolMgr != null && GameMgr.Instance._PoolMgr.Pool_BP_Building != null)
                {
                    try
                    {
                        BuildingPatches.SetSyncingBlueprint(true);
                        var bp = GameMgr.Instance._PoolMgr.Pool_BP_Building.GetNextObj()?.GetComponent<BP_Building>();
                        bp?.BluePrintSet(info, pos, att, func);
                    }
                    finally
                    {
                        BuildingPatches.SetSyncingBlueprint(false);
                    }
                }
            }
        }

        private void ApplyRemoteCancelBlueprint(int x, int y)
        {
            if (GameMgr.Instance == null || GameMgr.Instance._BuildingMgr == null) return;

            var bpList = GameMgr.Instance._BuildingMgr.List_BP_BlueBuilding;
            if (bpList != null)
            {
                var bp = bpList.Find(b => Mathf.RoundToInt(b.Pos_Tile.x) == x && Mathf.RoundToInt(b.Pos_Tile.y) == y);
                if (bp != null)
                {
                    try
                    {
                        BuildingPatches.SetSyncingBlueprint(true);
                        bp.CancelBP();
                    }
                    finally
                    {
                        BuildingPatches.SetSyncingBlueprint(false);
                    }
                }
            }
        }

        private void ApplyRemoteBuildingCompleted(string buildNameStr, Vector2 pos, int attNum)
        {
            if (GameMgr.Instance == null || GameMgr.Instance._BuildingMgr == null) return;

            // Remove any lingering blueprint at this location
            int x = Mathf.RoundToInt(pos.x);
            int y = Mathf.RoundToInt(pos.y);
            ApplyRemoteCancelBlueprint(x, y);

            if (Enum.TryParse<BuildingName>(buildNameStr, out var bName))
            {
                try
                {
                    BuildingPatches.SetSyncingBuildSet(true);
                    GameMgr.Instance._BuildingMgr.BuildSet(bName, pos, attNum);
                }
                finally
                {
                    BuildingPatches.SetSyncingBuildSet(false);
                }
            }
        }

        private void ApplyRemoteBuildingDemolished(int x, int y)
        {
            if (GameMgr.Instance == null || GameMgr.Instance._BuildingMgr == null) return;

            var buildingList = GameMgr.Instance._BuildingMgr.List_Building;
            if (buildingList != null)
            {
                var target = buildingList.Find(b => Mathf.RoundToInt(b.Pos_Tile.x) == x && Mathf.RoundToInt(b.Pos_Tile.y) == y);
                if (target != null)
                {
                    try
                    {
                        BuildingPatches.SetSyncingDemolition(true);
                        target.BuildingDemolition(false);
                    }
                    finally
                    {
                        BuildingPatches.SetSyncingDemolition(false);
                    }
                }
            }
        }

        // BUG FIX #8: Improved time synchronization with drift correction
        private void ApplyRemoteTime(int day, int hour, int min, int season)
        {
            if (GameMgr.Instance == null || GameMgr.Instance._SysMgr == null) return;

            var sys = GameMgr.Instance._SysMgr;

            // Calculate time difference to detect desync
            int currentTotalMinutes = sys.m_Day * 1440 + sys.m_Hour * 60 + sys.m_Minute;
            int remoteTotalMinutes = day * 1440 + hour * 60 + min;
            int diff = Mathf.Abs(currentTotalMinutes - remoteTotalMinutes);

            // Sync if difference is detected (>1 minute)
            if (diff > 1)
            {
                if (diff > 5)
                {
                    Plugin.Log.LogInfo($"[WorldSyncManager] Time drift detected ({diff} minutes), syncing to host time");
                }

                sys.m_Day = day;
                sys.m_Hour = hour;
                sys.m_Minute = min;
                sys.m_Second = (float)hour * 12f + ((float)min / 60f) * 12f;

                if (sys.m_WorldWatch != null)
                {
                    sys.m_WorldWatch.TimeSet(sys.m_Second);
                    sys.m_WorldWatch.HourUpdate();
                }
            }

            // Synchronize season from host
            var weather = GameMgr.Instance._WeatherMgr;
            if (weather != null && (int)weather.m_SeasonState != season)
            {
                weather.m_SeasonState = (SeasonState)season;
                sys.m_WorldWatch?.SeasonUpdate();
            }
        }

        public void SendWorldObjectUpdate(int x, int y, byte level, bool isDestroyed, short tileType)
        {
            if (CoopNetManager.Instance == null || !CoopNetManager.Instance.IsConnected || !CoopNetManager.Instance.IsHost) return;
            if (SaveTransferManager.IsWorldLoading) return;

            NetDataWriter writer = new NetDataWriter();
            writer.Put(x);
            writer.Put(y);
            writer.Put(level);
            writer.Put(isDestroyed);
            writer.Put(tileType);
            CoopNetManager.Instance.SendPacket(PacketType.WorldObjectUpdate, writer, DeliveryMethod.ReliableOrdered);
        }

        public void SendWorldObjectBatch()
        {
            if (CoopNetManager.Instance == null || !CoopNetManager.Instance.IsConnected || !CoopNetManager.Instance.IsHost) return;
            if (SaveTransferManager.IsWorldLoading) return;
            if (GameMgr.Instance?._EnvMgr?.List_WorldObj == null) return;

            var list = GameMgr.Instance._EnvMgr.List_WorldObj;
            NetDataWriter writer = new NetDataWriter();
            writer.Put((short)list.Count);

            for (int i = 0; i < list.Count; i++)
            {
                var obj = list[i];
                if (obj == null)
                {
                    writer.Put((short)0);
                    writer.Put((short)0);
                    writer.Put((byte)0);
                    writer.Put((short)0);
                    continue;
                }

                short x = (short)Mathf.RoundToInt(obj.m_Pos.x);
                short y = (short)Mathf.RoundToInt(obj.m_Pos.y);
                byte level = (byte)Mathf.Clamp(obj.m_Level, 0, 2);
                short tileType = (short)obj.m_TileType;

                writer.Put(x);
                writer.Put(y);
                writer.Put(level);
                writer.Put(tileType);
            }

            CoopNetManager.Instance.SendPacket(PacketType.WorldObjectBatchSync, writer, DeliveryMethod.ReliableOrdered);
        }

        public void SendRequestHarvestWorldObj(int x, int y)
        {
            if (CoopNetManager.Instance == null || !CoopNetManager.Instance.IsConnected || !CoopNetManager.Instance.IsClient) return;
            if (SaveTransferManager.IsWorldLoading) return;

            NetDataWriter writer = new NetDataWriter();
            writer.Put(x);
            writer.Put(y);
            CoopNetManager.Instance.SendPacket(PacketType.RequestHarvestWorldObj, writer, DeliveryMethod.ReliableOrdered);
        }

        private void HandleClientHarvestRequest(int x, int y)
        {
            WorldObject? obj = FindWorldObjectAt(x, y);
            if (obj != null && !obj.m_Gatherd && obj.m_Level > 0)
            {
                GameUnit harvester = QueenSyncManager.Instance?.RemoteQueen ?? (GameUnit)GameMgr.Instance._T_UnitMgr.m_Queen;
                obj.Interaction(harvester);
            }
        }

        private void UpdateWorldObjects()
        {
            if (SaveTransferManager.IsWorldLoading) return;
            if (GameMgr.Instance?._EnvMgr?.List_WorldObj == null) return;

            var list = GameMgr.Instance._EnvMgr.List_WorldObj;

            if (Time.time - _lastWorldObjectBatch >= _worldObjectBatchInterval)
            {
                _lastWorldObjectBatch = Time.time;
                SendWorldObjectBatch();
                return;
            }

            if (Time.time - _lastWorldObjectScan >= _worldObjectScanInterval)
            {
                _lastWorldObjectScan = Time.time;
                for (int i = 0; i < list.Count; i++)
                {
                    var obj = list[i];
                    if (obj == null) continue;

                    Vector2Int posKey = new Vector2Int(Mathf.RoundToInt(obj.m_Pos.x), Mathf.RoundToInt(obj.m_Pos.y));
                    byte curLevel = (byte)Mathf.Clamp(obj.m_Level, 0, 2);

                    if (!_cachedPlantLevels.TryGetValue(posKey, out byte lastLevel) || lastLevel != curLevel)
                    {
                        _cachedPlantLevels[posKey] = curLevel;
                        SendWorldObjectUpdate(posKey.x, posKey.y, curLevel, false, (short)obj.m_TileType);
                    }
                }
            }
        }

        private void ApplyRemoteWorldObject(int x, int y, byte level, bool isDestroyed, TileType tileType)
        {
            if (SaveTransferManager.IsWorldLoading) return;

            try
            {
                IsApplyingRemoteWorldObject = true;
                WorldObject? obj = FindWorldObjectAt(x, y);

                if (isDestroyed)
                {
                    if (obj != null)
                    {
                        obj.DestroyObj(false, false, null);
                    }
                    return;
                }

                if (obj != null)
                {
                    obj.SpawnKill(); // Stop rogue local growth coroutine on client
                    if (obj.m_Level != level)
                    {
                        obj.m_Level = level;
                        if (obj.m_Spine != null && obj.m_Spine.skeleton != null)
                        {
                            string skinName = "Level" + (level + 1);
                            obj.m_Spine.skeleton.SetSkin(skinName);
                            obj.m_Spine.skeleton.SetSlotsToSetupPose();
                            obj.m_Spine.AnimationState.Apply(obj.m_Spine.skeleton);
                        }
                        obj.Weather_Refresh();
                        obj.SpawnRefresh(false, level);

                        if (level == 0)
                        {
                            obj.HitAnimation();
                        }
                    }
                }
                else if (tileType != TileType.None && GameMgr.Instance?._EnvMgr != null)
                {
                    GameMgr.Instance._EnvMgr.SpawnPlant(new Vector2(x, y), tileType, _isload: true, _level: level, _natural: false);
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[WorldSyncManager] Error applying remote WorldObject at ({x}, {y}): {ex}");
            }
            finally
            {
                IsApplyingRemoteWorldObject = false;
            }
        }

        public static WorldObject? FindWorldObjectAt(int x, int y)
        {
            var tileMgr = GameMgr.Instance?._TileMgr;
            if (tileMgr != null)
            {
                if (tileMgr.IsInsideMap(x, y))
                {
                    var node = tileMgr.GetNode(x, y);
                    if (node?.m_WorldObj != null) return node.m_WorldObj;
                }

                if (tileMgr.IsInsideMap(x, y + 1))
                {
                    var nodeUp = tileMgr.GetNode(x, y + 1);
                    if (nodeUp?.m_WorldObj != null) return nodeUp.m_WorldObj;
                }

                if (tileMgr.IsInsideMap(x, y - 1))
                {
                    var nodeDown = tileMgr.GetNode(x, y - 1);
                    if (nodeDown?.m_WorldObj != null) return nodeDown.m_WorldObj;
                }
            }

            if (GameMgr.Instance?._EnvMgr?.List_WorldObj != null)
            {
                var list = GameMgr.Instance._EnvMgr.List_WorldObj;
                for (int i = 0; i < list.Count; i++)
                {
                    var wo = list[i];
                    if (wo != null)
                    {
                        int wx = Mathf.RoundToInt(wo.m_Pos.x);
                        int wy = Mathf.RoundToInt(wo.m_Pos.y);
                        if (wx == x && (wy == y || wy == y + 1 || wy == y - 1))
                        {
                            return wo;
                        }
                    }
                }
            }

            return null;
        }

        private void OnDestroy()
        {
            if (CoopNetManager.Instance != null)
            {
                CoopNetManager.Instance.OnPacketReceivedEvent -= HandlePacket;
            }
        }
    }
}
