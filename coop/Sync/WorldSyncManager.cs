using System;
using LiteNetLib;
using LiteNetLib.Utils;
using RatopiaCoop.Network;
using RatopiaCoop.Patches;
using RatopiaUnitBrain.Puppeteer;
using RatopiaUnitBrain.Registry;
using UnityEngine;

namespace RatopiaCoop.Sync
{
    public class WorldSyncManager : MonoBehaviour
    {
        public static WorldSyncManager Instance { get; private set; } = null!;

        public bool IsApplyingRemoteTile { get; private set; }
        public bool IsApplyingRemoteWorldObject { get; private set; }
        public bool IsApplyingRemoteBuildingWork { get; private set; }
        public bool IsApplyingRemotePlantBP { get; private set; }
        public GameUnit? OverrideConstructingWorkUnit { get; private set; }
        public float OverrideConstructingWorkValue { get; private set; }

        // BUG FIX #8: Increase time sync frequency for better synchronization
        private float _timeSyncInterval = 0.5f; // Every 0.5 seconds (was 1.5s)
        private float _lastTimeSync;

        private float _worldObjectScanInterval = 1.0f;
        private float _lastWorldObjectScan;
        private float _worldObjectBatchInterval = 5.0f;
        private float _lastWorldObjectBatch;
        private readonly System.Collections.Generic.Dictionary<Vector2Int, byte> _cachedPlantLevels = new System.Collections.Generic.Dictionary<Vector2Int, byte>();
        private ushort _worldObjBatchSeq;
        private ushort _activeClientWorldObjBatchSeq;
        private readonly System.Collections.Generic.HashSet<Vector2Int> _pendingWorldObjPositions = new System.Collections.Generic.HashSet<Vector2Int>();

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

        public void SendTileHpUpdate(int x, int y, bool isWater, float hp, float maxHp)
        {
            if (CoopNetManager.Instance == null || !CoopNetManager.Instance.IsConnected || !CoopNetManager.Instance.IsHost) return;
            if (SaveTransferManager.IsWorldLoading) return;

            NetDataWriter writer = new NetDataWriter();
            writer.Put(x);
            writer.Put(y);
            writer.Put(isWater);
            writer.Put(hp);
            writer.Put(maxHp);
            CoopNetManager.Instance.SendPacket(PacketType.TileHpUpdate, writer, DeliveryMethod.ReliableOrdered);
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

        public void SendCancelBlueprint(float x, float y)
        {
            if (CoopNetManager.Instance == null || !CoopNetManager.Instance.IsConnected) return;
            if (SaveTransferManager.IsWorldLoading) return;

            NetDataWriter writer = new NetDataWriter();
            writer.Put(x);
            writer.Put(y);
            CoopNetManager.Instance.SendPacket(PacketType.OrderCancelBlueprint, writer, DeliveryMethod.ReliableOrdered);
        }

        public void SendBuildingCompleted(string buildName, Vector2 pos, int attNum, int funcNum = 0)
        {
            if (CoopNetManager.Instance == null || !CoopNetManager.Instance.IsConnected || !CoopNetManager.Instance.IsHost) return;

            NetDataWriter writer = new NetDataWriter();
            writer.Put(buildName);
            writer.Put(pos.x);
            writer.Put(pos.y);
            writer.Put(attNum);
            writer.Put(funcNum);
            CoopNetManager.Instance.SendPacket(PacketType.BuildingCompleted, writer, DeliveryMethod.ReliableOrdered);
        }

        public void SendBuildingDemolished(float x, float y)
        {
            if (CoopNetManager.Instance == null || !CoopNetManager.Instance.IsConnected || !CoopNetManager.Instance.IsHost) return;

            NetDataWriter writer = new NetDataWriter();
            writer.Put(x);
            writer.Put(y);
            CoopNetManager.Instance.SendPacket(PacketType.BuildingDemolished, writer, DeliveryMethod.ReliableOrdered);
        }

        public void SendRequestBlueprintWork(float posX, float posY, float workAmount, bool requestComplete)
        {
            if (CoopNetManager.Instance == null || !CoopNetManager.Instance.IsConnected || !CoopNetManager.Instance.IsClient) return;
            if (SaveTransferManager.IsWorldLoading) return;

            NetDataWriter writer = new NetDataWriter();
            writer.Put(posX);
            writer.Put(posY);
            writer.Put(workAmount);
            writer.Put(requestComplete);
            CoopNetManager.Instance.SendPacket(PacketType.RequestBlueprintWork, writer, DeliveryMethod.ReliableOrdered);
        }

        public void SendBlueprintProgress(BP_Building bp)
        {
            if (bp == null || bp.m_MatBox == null || bp.m_MatBox.m_Slot == null) return;
            if (CoopNetManager.Instance == null || !CoopNetManager.Instance.IsConnected || !CoopNetManager.Instance.IsHost) return;
            if (SaveTransferManager.IsWorldLoading) return;

            NetDataWriter writer = new NetDataWriter();
            writer.Put(bp.Pos_Tile.x);
            writer.Put(bp.Pos_Tile.y);
            writer.Put((byte)bp.m_BuildState);
            writer.Put(bp.m_CurTime);
            byte slotCount = (byte)Mathf.Min(bp.m_MatBox.m_Slot.Length, 16);
            writer.Put(slotCount);
            for (int i = 0; i < slotCount; i++)
            {
                writer.Put(bp.m_MatBox.m_Slot[i] != null ? bp.m_MatBox.m_Slot[i].m_CurNum : 0);
            }
            CoopNetManager.Instance.SendPacket(PacketType.BlueprintProgressSync, writer, DeliveryMethod.ReliableOrdered);
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
                case PacketType.TileHpUpdate:
                {
                    if (CoopNetManager.Instance.IsHost) break;
                    if (reader.AvailableBytes < 17) break;

                    int x = reader.GetInt();
                    int y = reader.GetInt();
                    bool isWater = reader.GetBool();
                    float hp = reader.GetFloat();
                    float maxHp = reader.GetFloat();
                    ApplyRemoteTileHpUpdate(x, y, isWater, hp, maxHp);
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
                    float x = reader.GetFloat();
                    float y = reader.GetFloat();
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
                    int funcNum = reader.AvailableBytes >= 4 ? reader.GetInt() : 0;
                    ApplyRemoteBuildingCompleted(bName, new Vector2(posX, posY), att, funcNum);
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

                    float x = reader.GetFloat();
                    float y = reader.GetFloat();
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
                    if (reader.AvailableBytes >= 6)
                    {
                        ushort batchSeq = reader.GetUShort();
                        bool isFirst = reader.GetBool();
                        bool isLast = reader.GetBool();
                        short count = reader.GetShort();

                        if (isFirst || batchSeq != _activeClientWorldObjBatchSeq)
                        {
                            _activeClientWorldObjBatchSeq = batchSeq;
                            _pendingWorldObjPositions.Clear();
                        }

                        for (int i = 0; i < count; i++)
                        {
                            if (reader.AvailableBytes < 7) break;
                            short x = reader.GetShort();
                            short y = reader.GetShort();
                            byte level = reader.GetByte();
                            short tileType = reader.GetShort();
                            _pendingWorldObjPositions.Add(new Vector2Int(x, y));
                            ApplyRemoteWorldObject(x, y, level, false, (TileType)tileType);
                        }

                        if (isLast && !SaveTransferManager.IsWorldLoading && GameMgr.Instance?._EnvMgr?.List_WorldObj != null)
                        {
                            try
                            {
                                IsApplyingRemoteWorldObject = true;
                                var localList = GameMgr.Instance._EnvMgr.List_WorldObj;
                                for (int i = localList.Count - 1; i >= 0; i--)
                                {
                                    var wo = localList[i];
                                    if (wo == null) continue;
                                    var posKey = new Vector2Int(Mathf.RoundToInt(wo.m_Pos.x), Mathf.RoundToInt(wo.m_Pos.y));
                                    if (!_pendingWorldObjPositions.Contains(posKey))
                                    {
                                        wo.DestroyObj(false, false, null);
                                    }
                                }
                            }
                            finally
                            {
                                IsApplyingRemoteWorldObject = false;
                            }
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
                case PacketType.RequestBlueprintDeposit:
                {
                    if (!CoopNetManager.Instance.IsHost || SaveTransferManager.IsWorldLoading) break;
                    if (reader.AvailableBytes >= 14)
                    {
                        float x = reader.GetFloat();
                        float y = reader.GetFloat();
                        TileType tileType = (TileType)reader.GetShort();
                        int count = reader.GetInt();
                        HandleClientBlueprintDeposit(x, y, tileType, count);
                    }
                    break;
                }
                case PacketType.RequestBlueprintWork:
                {
                    if (!CoopNetManager.Instance.IsHost || SaveTransferManager.IsWorldLoading) break;
                    if (reader.AvailableBytes >= 13)
                    {
                        float x = reader.GetFloat();
                        float y = reader.GetFloat();
                        float workAmount = reader.GetFloat();
                        bool requestComplete = reader.GetBool();
                        HandleClientBlueprintWork(x, y, workAmount, requestComplete);
                    }
                    break;
                }
                case PacketType.BlueprintProgressSync:
                {
                    if (!CoopNetManager.Instance.IsClient || SaveTransferManager.IsWorldLoading) break;
                    if (reader.AvailableBytes >= 14)
                    {
                        float x = reader.GetFloat();
                        float y = reader.GetFloat();
                        BP_State state = (BP_State)reader.GetByte();
                        float curTime = reader.GetFloat();
                        byte slotCount = reader.GetByte();
                        int[] slotCurNums = new int[slotCount];
                        for (int i = 0; i < slotCount && reader.AvailableBytes >= 4; i++)
                        {
                            slotCurNums[i] = reader.GetInt();
                        }
                        ApplyRemoteBlueprintProgress(x, y, state, curTime, slotCurNums);
                    }
                    break;
                }
                case PacketType.PlantBlueprintSync:
                {
                    if (SaveTransferManager.IsWorldLoading || reader.AvailableBytes < 9) break;
                    byte op = reader.GetByte();
                    HandlePlantBlueprintPacket(op, reader);
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
                        DetachPuppetWorkGauges(waterTile);
                        waterTile.DestroyTile(Dir_check: false);
                    }
                }
                else
                {
                    C_Tile? mainTile = tileMgr.GetTile(x, y);
                    if (mainTile != null && !mainTile.IsDestroy)
                    {
                        DetachPuppetWorkGauges(mainTile);
                        mainTile.DestroyTile(Dir_check: true);
                    }
                }
            }
            finally
            {
                IsApplyingRemoteTile = false;
            }
        }

        private void ApplyRemoteTileHpUpdate(int x, int y, bool isWater, float hp, float maxHp)
        {
            var tileMgr = GameMgr.Instance?._TileMgr;
            if (tileMgr == null || !tileMgr.IsInsideMap(x, y)) return;

            C_Tile? tile = isWater ? tileMgr.GetWaterTile(x, y) : tileMgr.GetTile(x, y);
            if (tile == null || tile.IsDestroy) return;

            try
            {
                IsApplyingRemoteTile = true;
                if (maxHp > 0f) tile.m_MaxHP = maxHp;
                float clampMax = tile.m_MaxHP > 0f ? tile.m_MaxHP : hp;
                tile.m_HP = Mathf.Clamp(hp, 0f, clampMax);

                if (!tile.IsWater)
                {
                    if (tile.m_HP < tile.m_MaxHP)
                    {
                        tile.TC_Effect_Check();
                    }
                    else
                    {
                        tile.Remove_TC_Effect();
                    }
                }

                if (GameMgr.Instance?._OverView != null && GameMgr.Instance._OverView.m_Tile == tile)
                {
                    GameMgr.Instance._OverView.TileHpTxt_Update();
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

        private static void DetachPuppetWorkGauges(C_Tile tile)
        {
            if (tile == null || tile.Tf == null) return;
            var gauges = tile.GetComponentsInChildren<WorkTimeGauge>(true);
            if (gauges == null || gauges.Length == 0) return;

            var allUnits = UnitRegistry.Instance.GetAllUnits();
            for (int i = 0; i < gauges.Length; i++)
            {
                var g = gauges[i];
                if (g == null) continue;
                GameUnit? owner = null;
                for (int j = 0; j < allUnits.Count; j++)
                {
                    if (allUnits[j] != null && allUnits[j].m_WorkTimeGauge == g)
                    {
                        owner = allUnits[j];
                        break;
                    }
                }
                if (owner != null)
                {
                    BrainPuppeteer.Instance.StopPuppetWorkGauge(owner);
                }
                else
                {
                    g.transform.SetParent(null);
                    g.gameObject.SetActive(false);
                }
            }
        }

        private static BP_Building? FindBlueprintAt(float posX, float posY, BuildingName? expectedName = null)
        {
            var bpList = GameMgr.Instance?._BuildingMgr?.List_BP_BlueBuilding;
            if (bpList == null) return null;

            Vector2 target = new Vector2(posX, posY);
            BP_Building? best = null;
            float bestDistSq = 0.09f; // < 0.3 tiles prevents adjacent 0.5f / 1.0f collisions

            for (int i = 0; i < bpList.Count; i++)
            {
                var bp = bpList[i];
                if (bp == null || bp.m_BuildState == BP_State.Finish) continue;
                if (expectedName.HasValue && bp.m_Info != null && bp.m_Info.Name != expectedName.Value) continue;

                float distSq = (bp.Pos_Tile - target).sqrMagnitude;
                if (distSq < bestDistSq)
                {
                    bestDistSq = distSq;
                    best = bp;
                }
            }
            return best;
        }

        private static BP_Plant? FindPlantBlueprintAt(float posX, float posY)
        {
            var list = GameMgr.Instance?._PoolMgr?.List_BP_Plant;
            if (list == null) return null;

            Vector2 target = new Vector2(posX, posY);
            BP_Plant? best = null;
            float bestDistSq = 0.09f;

            for (int i = 0; i < list.Count; i++)
            {
                var bp = list[i];
                if (bp == null || bp.m_BuildState == BP_State.Finish) continue;

                float distSq = (bp.Pos_Tile - target).sqrMagnitude;
                if (distSq < bestDistSq)
                {
                    bestDistSq = distSq;
                    best = bp;
                }
            }
            return best;
        }

        private static Building? FindBuildingAt(float posX, float posY)
        {
            var bMgr = GameMgr.Instance?._BuildingMgr;
            if (bMgr == null) return null;

            Vector2 target = new Vector2(posX, posY);
            Building? best = null;
            float bestDistSq = 0.09f;

            void Scan(System.Collections.IList? list)
            {
                if (list == null) return;
                for (int i = 0; i < list.Count; i++)
                {
                    if (list[i] is Building b && b != null)
                    {
                        float d = (b.Pos_Tile - target).sqrMagnitude;
                        if (d < bestDistSq)
                        {
                            bestDistSq = d;
                            best = b;
                        }
                    }
                }
            }

            Scan(bMgr.List_Building);
            if (best == null) Scan(bMgr.List_HeavyWire);
            if (best == null) Scan(bMgr.List_Wallpaper);
            return best;
        }

        private void ApplyRemoteBlueprint(string buildNameStr, Vector2 pos, int att, int func)
        {
            if (SaveTransferManager.IsWorldLoading) return;
            if (GameMgr.Instance == null || GameMgr.Instance._BuildingMgr == null || GameMgr.Instance._DB_Mgr == null) return;

            if (!Enum.TryParse<BuildingName>(buildNameStr, out var bName))
            {
                Plugin.Log.LogWarning($"[WorldSyncManager] ApplyRemoteBlueprint: Unknown BuildingName '{buildNameStr}'");
                return;
            }

            if (FindBlueprintAt(pos.x, pos.y) != null)
            {
                // Prevent duplicate blueprint stacking at the exact coordinates
                return;
            }

            BuildInfo? info = GameMgr.Instance._DB_Mgr.Dic_BuildDB.ContainsKey(bName)
                ? GameMgr.Instance._DB_Mgr.Dic_BuildDB[bName]
                : (GameMgr.Instance._DB_Mgr.Dic_BlockBuildDB.ContainsKey(bName) ? GameMgr.Instance._DB_Mgr.Dic_BlockBuildDB[bName] : null);

            if (info == null)
            {
                Plugin.Log.LogWarning($"[WorldSyncManager] ApplyRemoteBlueprint: No BuildInfo for '{bName}'");
                return;
            }

            if (GameMgr.Instance._PoolMgr != null && GameMgr.Instance._PoolMgr.Pool_BP_Building != null)
            {
                var sys = GameMgr.Instance._SysMgr;
                int savedMoveId = sys != null ? sys.m_TargetMovementID : 99999;
                int savedCopyId = sys != null ? sys.m_TaregCopyID : 99999;
                try
                {
                    BuildingPatches.SetSyncingBlueprint(true);
                    if (sys != null)
                    {
                        sys.m_TargetMovementID = 99999;
                        sys.m_TaregCopyID = 99999;
                    }
                    var bp = GameMgr.Instance._PoolMgr.Pool_BP_Building.GetNextObj()?.GetComponent<BP_Building>();
                    bp?.BluePrintSet(info, pos, att, func);
                }
                finally
                {
                    if (sys != null)
                    {
                        sys.m_TargetMovementID = savedMoveId;
                        sys.m_TaregCopyID = savedCopyId;
                    }
                    BuildingPatches.SetSyncingBlueprint(false);
                }
            }
        }

        private void ApplyRemoteCancelBlueprint(float x, float y)
        {
            if (GameMgr.Instance == null || GameMgr.Instance._BuildingMgr == null) return;

            var bp = FindBlueprintAt(x, y);
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

        private void ApplyRemoteBuildingCompleted(string buildNameStr, Vector2 pos, int attNum, int funcNum)
        {
            if (GameMgr.Instance == null || GameMgr.Instance._BuildingMgr == null) return;

            bool parsed = Enum.TryParse<BuildingName>(buildNameStr, out var bName);
            BP_Building? matchingBp = parsed
                ? (FindBlueprintAt(pos.x, pos.y, bName) ?? FindBlueprintAt(pos.x, pos.y))
                : FindBlueprintAt(pos.x, pos.y);
            int effectiveFuncNum = funcNum != 0 ? funcNum : (matchingBp != null ? matchingBp.m_FuncNum : 0);

            // Silently clear matching blueprint WITHOUT calling CancelBP(), which would dump duplicate mats onto the floor
            SilentlyFinishBlueprint(pos.x, pos.y, matchingBp);

            if (parsed)
            {
                try
                {
                    BuildingPatches.SetSyncingBuildSet(true);
                    Building? building = GameMgr.Instance._BuildingMgr.BuildSet(bName, pos, attNum);
                    if (building != null)
                    {
                        if (effectiveFuncNum != 0)
                        {
                            if (building.m_Info.Ability == BuildAbility.Gravegarden || building.m_Info.Ability == BuildAbility.Minigarden)
                            {
                                building.m_ProductIndex = effectiveFuncNum - 1;
                                building.m_BuildInfoUI?.InfoUpdate();
                            }
                            if (!building.m_Info.List_Condition.Contains(BuildCondition.FixRange) && building.m_Info.Range > 0)
                            {
                                building.m_Range = effectiveFuncNum;
                            }
                            building.ApplyFuncNum(effectiveFuncNum);
                        }

                        try
                        {
                            if (GameMgr.Instance._PoolMgr?.Pool_BuildSmokeEffect != null && building.m_Info != null)
                            {
                                GameMgr.Instance._PoolMgr.Pool_BuildSmokeEffect.GetNextObj()?.GetComponent<BuildSmokeEffect>()?.BSE_EffectSet(pos, building.m_Info.Width);
                            }
                            if (GameMgr.Instance._CamMgr?.m_MainCam != null)
                            {
                                CasselGames.Audio.AudioController.PlaySFXOneShot("SFX_Build_Wood_Complete", GameMgr.Instance._CamMgr.m_MainCam.transform.position, building.Tf.position);
                            }
                        }
                        catch { }
                    }
                }
                finally
                {
                    BuildingPatches.SetSyncingBuildSet(false);
                }
            }
        }

        private void SilentlyFinishBlueprint(float x, float y, BP_Building? knownBp = null)
        {
            if (GameMgr.Instance?._BuildingMgr?.List_BP_BlueBuilding == null) return;
            var bpList = GameMgr.Instance._BuildingMgr.List_BP_BlueBuilding;
            var bp = knownBp ?? FindBlueprintAt(x, y);
            if (bp == null) return;

            try
            {
                bp.IsAct = false;
                bp.m_BuildState = BP_State.Finish;
                if (bp.m_Unit != null)
                {
                    if (bp.m_Unit.m_WorkTimeGauge != null && bp.m_Unit.m_WorkTimeGauge.IsGaugeAcitve() && bp.m_Unit.m_BP_Builidng == bp)
                    {
                        bp.m_Unit.m_WorkTimeGauge.StopGauge(TypeOrder.Constructing);
                        bp.m_Unit.BehaviorStop();
                    }
                    else if (bp.m_Unit.m_UnitKind == UnitKind.Queen && bp.m_Unit.m_CharState == CharState.Building)
                    {
                        bp.m_Unit.BehaviorStop();
                    }
                    bp.m_Unit = null;
                }

                var localQueen = QueenSyncManager.Instance?.LocalCharacter;
                if (localQueen != null && localQueen.m_CheckBox != null && localQueen.m_CheckBox.m_BP_Building == bp)
                {
                    if (localQueen.m_CharState == CharState.Building)
                    {
                        localQueen.BehaviorStop();
                    }
                    localQueen.m_CheckBox.m_BP_Building = null;
                }

                if (bp.m_Scaffolding != null && bp.m_Scaffolding.gameObject != null)
                {
                    bp.m_Scaffolding.gameObject.SetActive(false);
                }
                bpList.Remove(bp);
                if (GameMgr.Instance._PoolMgr?.Pool_BP_Building != null && bp.gameObject != null)
                {
                    GameMgr.Instance._PoolMgr.Pool_BP_Building.AddObj(bp.gameObject);
                }
                GameMgr.Instance._UnionUI?.m_BluePrintNumUI?.NumUpdate();
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[WorldSyncManager] SilentlyFinishBlueprint warning: {ex.Message}");
            }
        }

        private void ApplyRemoteBuildingDemolished(float x, float y)
        {
            if (GameMgr.Instance == null || GameMgr.Instance._BuildingMgr == null) return;

            var target = FindBuildingAt(x, y);
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
            _worldObjBatchSeq++;
            int total = list.Count;
            if (total == 0)
            {
                NetDataWriter emptyWriter = new NetDataWriter();
                emptyWriter.Put(_worldObjBatchSeq);
                emptyWriter.Put(true);
                emptyWriter.Put(true);
                emptyWriter.Put((short)0);
                CoopNetManager.Instance.SendPacket(PacketType.WorldObjectBatchSync, emptyWriter, DeliveryMethod.ReliableOrdered);
                return;
            }

            const int chunkLimit = 50;
            for (int offset = 0; offset < total; offset += chunkLimit)
            {
                int chunkCount = Mathf.Min(chunkLimit, total - offset);
                bool isFirst = offset == 0;
                bool isLast = offset + chunkCount >= total;

                NetDataWriter writer = new NetDataWriter();
                writer.Put(_worldObjBatchSeq);
                writer.Put(isFirst);
                writer.Put(isLast);
                writer.Put((short)chunkCount);

                for (int i = 0; i < chunkCount; i++)
                {
                    var obj = list[offset + i];
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
        }

        public void SendRequestBlueprintDeposit(float x, float y, TileType tileType, int count)
        {
            if (CoopNetManager.Instance == null || !CoopNetManager.Instance.IsConnected || !CoopNetManager.Instance.IsClient) return;
            if (SaveTransferManager.IsWorldLoading || count <= 0) return;

            NetDataWriter writer = new NetDataWriter();
            writer.Put(x);
            writer.Put(y);
            writer.Put((short)tileType);
            writer.Put(count);
            CoopNetManager.Instance.SendPacket(PacketType.RequestBlueprintDeposit, writer, DeliveryMethod.ReliableOrdered);
        }

        private void HandleClientBlueprintDeposit(float x, float y, TileType tileType, int count)
        {
            var bp = FindBlueprintAt(x, y);
            if (bp == null) return;

            var clientWorker = QueenSyncManager.Instance?.Player2Worker;
            bool added = bp.AddMatValue(tileType, count);
            if (added && clientWorker != null)
            {
                for (int i = 0; i < count; i++)
                {
                    clientWorker.Remove_GatheringObj(tileType);
                }
            }
            else if (!added)
            {
                SendBlueprintProgress(bp);
            }
        }

        private void HandleClientBlueprintWork(float posX, float posY, float workAmount, bool requestComplete)
        {
            var bp = FindBlueprintAt(posX, posY);
            if (bp == null || bp.m_BuildState == BP_State.Finish) return;

            if (bp.m_BuildState != BP_State.Construct)
            {
                if (bp.IsReadyToBuild())
                {
                    bp.SetReadyToBuild();
                }
                else
                {
                    SendBlueprintProgress(bp);
                    return;
                }
            }

            GameUnit? worker = (GameUnit?)QueenSyncManager.Instance?.Player2Worker ?? QueenSyncManager.Instance?.LocalCharacter;
            if (worker == null) return;

            float effectiveWork = Mathf.Clamp(workAmount, 0.001f, Mathf.Max(5f, bp.m_MaxTime + 1f));
            if (requestComplete && (bp.m_CurTime + effectiveWork) >= bp.m_MaxTime - 0.05f)
            {
                effectiveWork = Mathf.Max(effectiveWork, bp.m_MaxTime - bp.m_CurTime + 0.01f);
            }

            try
            {
                IsApplyingRemoteBuildingWork = true;
                OverrideConstructingWorkUnit = worker;
                OverrideConstructingWorkValue = effectiveWork;
                bp.BuildingUpdate_Call(worker);
            }
            finally
            {
                OverrideConstructingWorkUnit = null;
                OverrideConstructingWorkValue = 0f;
                IsApplyingRemoteBuildingWork = false;
            }
        }

        private void ApplyRemoteBlueprintProgress(float posX, float posY, BP_State state, float curTime, int[] slotCurNums)
        {
            var bp = FindBlueprintAt(posX, posY);
            if (bp == null || bp.m_BuildState == BP_State.Finish) return;

            try
            {
                IsApplyingRemoteBuildingWork = true;
                ApplyRemoteMatBoxSlots(bp.m_MatBox, slotCurNums);

                if (state == BP_State.Construct && bp.m_BuildState != BP_State.Construct)
                {
                    bp.SetReadyToBuild();
                }

                if (bp.m_BuildState == BP_State.Construct)
                {
                    bp.m_CurTime = Mathf.Clamp(curTime, 0f, Mathf.Max(0f, bp.m_MaxTime - 0.001f));
                    float ratio = bp.m_MaxTime > 0f ? Mathf.Clamp01(bp.m_CurTime / bp.m_MaxTime) : 0f;
                    bp.m_Scaffolding?.RemoteUpdate(ratio);
                    UpdateBlueprintGaugeVisual(bp.m_Spr_Gauge, bp.m_Info != null ? bp.m_Info.Width : 1f, ratio);
                }

                if (GameMgr.Instance?._OverView != null && GameMgr.Instance._OverView.m_BP_Building == bp)
                {
                    GameMgr.Instance._OverView.Set_Update();
                    GameMgr.Instance._OverView.BP_GaugeUpdate();
                }
            }
            finally
            {
                IsApplyingRemoteBuildingWork = false;
            }
        }

        private static void ApplyRemoteMatBoxSlots(BP_MatBox? matBox, int[] slotCurNums)
        {
            if (matBox?.m_Slot == null || slotCurNums == null) return;
            int len = Mathf.Min(matBox.m_Slot.Length, slotCurNums.Length);
            for (int i = 0; i < len; i++)
            {
                var slot = matBox.m_Slot[i];
                if (slot == null) continue;
                int delta = slotCurNums[i] - slot.m_CurNum;
                if (delta != 0)
                {
                    slot.AddValue(delta);
                }
            }
        }

        private static float? _cachedMiningFixValue;

        public static float GetPlantWorkStep(GameUnit unit)
        {
            if (!_cachedMiningFixValue.HasValue)
            {
                float val = 10f;
                try
                {
                    var definesType = typeof(BP_Plant).Assembly.GetType("Defines");
                    var field = definesType?.GetField("Mining_FixValue", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                    if (field?.GetValue(null) is float f && f > 0f)
                    {
                        val = f;
                    }
                }
                catch { }
                _cachedMiningFixValue = val;
            }
            return _cachedMiningFixValue.Value * (1f + unit.GetPDI(PDI.Dex) * 0.01f);
        }

        private static void UpdateBlueprintGaugeVisual(SpriteRenderer? gauge, float width, float ratio)
        {
            if (gauge == null) return;
            gauge.size = new Vector2(ratio * width, gauge.size.y);
        }

        private const byte PlantOpPlace = 0;
        private const byte PlantOpCancel = 1;
        private const byte PlantOpDeposit = 2;
        private const byte PlantOpWork = 3;
        private const byte PlantOpProgress = 4;
        private const byte PlantOpComplete = 5;

        public void SendPlantBlueprintPlace(short tileType, Vector2 pos, int masterId)
        {
            if (CoopNetManager.Instance == null || !CoopNetManager.Instance.IsConnected || SaveTransferManager.IsWorldLoading) return;

            Building? master = masterId > 0 ? GameMgr.Instance?._BuildingMgr?.List_Building?.Find(b => b != null && b.m_ID == masterId) : null;
            NetDataWriter writer = new NetDataWriter();
            writer.Put(PlantOpPlace);
            writer.Put(tileType);
            writer.Put(pos.x);
            writer.Put(pos.y);
            writer.Put(master != null);
            if (master != null)
            {
                writer.Put(master.Pos_Tile.x);
                writer.Put(master.Pos_Tile.y);
            }
            CoopNetManager.Instance.SendPacket(PacketType.PlantBlueprintSync, writer, DeliveryMethod.ReliableOrdered);
        }

        public void SendPlantBlueprintCancel(Vector2 pos)
        {
            if (CoopNetManager.Instance == null || !CoopNetManager.Instance.IsConnected || SaveTransferManager.IsWorldLoading) return;

            NetDataWriter writer = new NetDataWriter();
            writer.Put(PlantOpCancel);
            writer.Put(pos.x);
            writer.Put(pos.y);
            CoopNetManager.Instance.SendPacket(PacketType.PlantBlueprintSync, writer, DeliveryMethod.ReliableOrdered);
        }

        public void SendPlantBlueprintDeposit(Vector2 pos, TileType tileType, int count)
        {
            if (CoopNetManager.Instance == null || !CoopNetManager.Instance.IsConnected || !CoopNetManager.Instance.IsClient) return;
            if (SaveTransferManager.IsWorldLoading || count <= 0) return;

            NetDataWriter writer = new NetDataWriter();
            writer.Put(PlantOpDeposit);
            writer.Put(pos.x);
            writer.Put(pos.y);
            writer.Put((short)tileType);
            writer.Put(count);
            CoopNetManager.Instance.SendPacket(PacketType.PlantBlueprintSync, writer, DeliveryMethod.ReliableOrdered);
        }

        public void SendPlantBlueprintWork(Vector2 pos, float workAmount, bool requestComplete)
        {
            if (CoopNetManager.Instance == null || !CoopNetManager.Instance.IsConnected || !CoopNetManager.Instance.IsClient) return;
            if (SaveTransferManager.IsWorldLoading) return;

            NetDataWriter writer = new NetDataWriter();
            writer.Put(PlantOpWork);
            writer.Put(pos.x);
            writer.Put(pos.y);
            writer.Put(workAmount);
            writer.Put(requestComplete);
            CoopNetManager.Instance.SendPacket(PacketType.PlantBlueprintSync, writer, DeliveryMethod.ReliableOrdered);
        }

        public void SendPlantBlueprintProgress(BP_Plant bp)
        {
            if (bp == null || bp.m_MatBox == null || bp.m_MatBox.m_Slot == null) return;
            if (CoopNetManager.Instance == null || !CoopNetManager.Instance.IsConnected || !CoopNetManager.Instance.IsHost) return;
            if (SaveTransferManager.IsWorldLoading) return;

            NetDataWriter writer = new NetDataWriter();
            writer.Put(PlantOpProgress);
            writer.Put(bp.Pos_Tile.x);
            writer.Put(bp.Pos_Tile.y);
            writer.Put((byte)bp.m_BuildState);
            writer.Put(bp.m_CurTime);
            byte slotCount = (byte)Mathf.Min(bp.m_MatBox.m_Slot.Length, 16);
            writer.Put(slotCount);
            for (int i = 0; i < slotCount; i++)
            {
                writer.Put(bp.m_MatBox.m_Slot[i] != null ? bp.m_MatBox.m_Slot[i].m_CurNum : 0);
            }
            CoopNetManager.Instance.SendPacket(PacketType.PlantBlueprintSync, writer, DeliveryMethod.ReliableOrdered);
        }

        public void SendPlantBlueprintCompleted(Vector2 pos)
        {
            if (CoopNetManager.Instance == null || !CoopNetManager.Instance.IsConnected || !CoopNetManager.Instance.IsHost) return;
            if (SaveTransferManager.IsWorldLoading) return;

            NetDataWriter writer = new NetDataWriter();
            writer.Put(PlantOpComplete);
            writer.Put(pos.x);
            writer.Put(pos.y);
            CoopNetManager.Instance.SendPacket(PacketType.PlantBlueprintSync, writer, DeliveryMethod.ReliableOrdered);
        }

        private void HandlePlantBlueprintPacket(byte op, NetDataReader reader)
        {
            switch (op)
            {
                case PlantOpPlace:
                {
                    if (reader.AvailableBytes < 11) break;
                    TileType tType = (TileType)reader.GetShort();
                    Vector2 pos = new Vector2(reader.GetFloat(), reader.GetFloat());
                    bool hasMaster = reader.GetBool();
                    Building? master = hasMaster && reader.AvailableBytes >= 8
                        ? FindBuildingAt(reader.GetFloat(), reader.GetFloat())
                        : null;

                    if (FindPlantBlueprintAt(pos.x, pos.y) != null || GameMgr.Instance?._PoolMgr?.Pool_BP_Plant == null) break;
                    try
                    {
                        IsApplyingRemotePlantBP = true;
                        BuildingPatches.SetSyncingPlantBlueprint(true);
                        var bp = GameMgr.Instance._PoolMgr.Pool_BP_Plant.GetNextObj()?.GetComponent<BP_Plant>();
                        if (bp != null)
                        {
                            bp.PlantPrintSet(tType, pos, master != null ? master.m_ID : -1);
                            LinkPlantBlueprintToMaster(master, bp, tType);
                        }
                    }
                    finally
                    {
                        BuildingPatches.SetSyncingPlantBlueprint(false);
                        IsApplyingRemotePlantBP = false;
                    }
                    break;
                }
                case PlantOpCancel:
                {
                    if (reader.AvailableBytes < 8) break;
                    var bp = FindPlantBlueprintAt(reader.GetFloat(), reader.GetFloat());
                    if (bp == null) break;
                    try
                    {
                        IsApplyingRemotePlantBP = true;
                        BuildingPatches.SetSyncingPlantBlueprint(true);
                        ClearMasterPlantBlueprintRef(bp);
                        bp.CancelBP();
                    }
                    finally
                    {
                        BuildingPatches.SetSyncingPlantBlueprint(false);
                        IsApplyingRemotePlantBP = false;
                    }
                    break;
                }
                case PlantOpDeposit:
                {
                    if (!CoopNetManager.Instance.IsHost || reader.AvailableBytes < 14) break;
                    var bp = FindPlantBlueprintAt(reader.GetFloat(), reader.GetFloat());
                    if (bp == null) break;
                    TileType tileType = (TileType)reader.GetShort();
                    int count = reader.GetInt();
                    bool added = bp.AddMatValue(tileType, count);
                    var clientWorker = QueenSyncManager.Instance?.Player2Worker;
                    if (added && clientWorker != null)
                    {
                        for (int i = 0; i < count; i++)
                        {
                            clientWorker.Remove_GatheringObj(tileType);
                        }
                    }
                    else if (!added)
                    {
                        SendPlantBlueprintProgress(bp);
                    }
                    break;
                }
                case PlantOpWork:
                {
                    if (!CoopNetManager.Instance.IsHost || reader.AvailableBytes < 13) break;
                    var bp = FindPlantBlueprintAt(reader.GetFloat(), reader.GetFloat());
                    if (bp == null || bp.m_BuildState == BP_State.Finish) break;
                    float workAmount = reader.GetFloat();
                    bool requestComplete = reader.GetBool();

                    if (bp.m_BuildState != BP_State.Construct)
                    {
                        if (bp.IsReadyToBuild()) bp.SetReadyToBuild();
                        else { SendPlantBlueprintProgress(bp); break; }
                    }

                    GameUnit? worker = (GameUnit?)QueenSyncManager.Instance?.Player2Worker ?? QueenSyncManager.Instance?.LocalCharacter;
                    if (worker == null) break;

                    float vanillaStep = GetPlantWorkStep(worker);
                    float targetWork = Mathf.Clamp(workAmount, 0.001f, Mathf.Max(5f, bp.m_MaxTime + 1f));
                    if (requestComplete && bp.m_CurTime + targetWork >= bp.m_MaxTime - 0.05f)
                    {
                        targetWork = Mathf.Max(targetWork, bp.m_MaxTime - bp.m_CurTime + 0.01f);
                    }

                    bp.m_CurTime = Mathf.Max(0f, bp.m_CurTime + (targetWork - vanillaStep));
                    try
                    {
                        IsApplyingRemotePlantBP = true;
                        bp.PlantUpdate_Call(worker);
                    }
                    finally
                    {
                        IsApplyingRemotePlantBP = false;
                    }
                    break;
                }
                case PlantOpProgress:
                {
                    if (!CoopNetManager.Instance.IsClient || reader.AvailableBytes < 14) break;
                    var bp = FindPlantBlueprintAt(reader.GetFloat(), reader.GetFloat());
                    if (bp == null || bp.m_BuildState == BP_State.Finish) break;
                    BP_State state = (BP_State)reader.GetByte();
                    float curTime = reader.GetFloat();
                    byte slotCount = reader.GetByte();
                    int[] slotCurNums = new int[slotCount];
                    for (int i = 0; i < slotCount && reader.AvailableBytes >= 4; i++)
                    {
                        slotCurNums[i] = reader.GetInt();
                    }
                    try
                    {
                        IsApplyingRemotePlantBP = true;
                        ApplyRemoteMatBoxSlots(bp.m_MatBox, slotCurNums);
                        if (state == BP_State.Construct && bp.m_BuildState != BP_State.Construct)
                        {
                            bp.SetReadyToBuild();
                        }
                        if (bp.m_BuildState == BP_State.Construct)
                        {
                            bp.m_CurTime = Mathf.Clamp(curTime, 0f, Mathf.Max(0f, bp.m_MaxTime - 0.001f));
                            float ratio = bp.m_MaxTime > 0f ? Mathf.Clamp01(bp.m_CurTime / bp.m_MaxTime) : 0f;
                            UpdateBlueprintGaugeVisual(bp.m_Spr_Gauge, bp.m_Info != null ? bp.m_Info.Width : 1f, ratio);
                        }
                        if (GameMgr.Instance?._OverView != null && GameMgr.Instance._OverView.m_BP_Plant == bp)
                        {
                            GameMgr.Instance._OverView.BP_Plant_GaugeUpdate();
                        }
                    }
                    finally
                    {
                        IsApplyingRemotePlantBP = false;
                    }
                    break;
                }
                case PlantOpComplete:
                {
                    if (!CoopNetManager.Instance.IsClient || reader.AvailableBytes < 8) break;
                    var bp = FindPlantBlueprintAt(reader.GetFloat(), reader.GetFloat());
                    if (bp == null) break;
                    try
                    {
                        IsApplyingRemotePlantBP = true;
                        bp.m_BuildState = BP_State.Finish;
                        if (bp.m_Unit != null)
                        {
                            if (bp.m_Unit.m_UnitKind == UnitKind.Queen && bp.m_Unit.m_CharState == CharState.Planting)
                            {
                                bp.m_Unit.BehaviorStop();
                            }
                            bp.m_Unit = null;
                        }
                        var localQueen = QueenSyncManager.Instance?.LocalCharacter;
                        if (localQueen != null && localQueen.m_CheckBox != null && localQueen.m_CheckBox.m_BP_Plant == bp)
                        {
                            if (localQueen.m_CharState == CharState.Planting)
                            {
                                localQueen.BehaviorStop();
                            }
                            localQueen.m_CheckBox.m_BP_Plant = null;
                        }
                        ClearMasterPlantBlueprintRef(bp);
                        GameMgr.Instance?._PoolMgr?.List_BP_Plant?.Remove(bp);
                        if (GameMgr.Instance?._PoolMgr?.Pool_BP_Plant != null && bp.gameObject != null)
                        {
                            GameMgr.Instance._PoolMgr.Pool_BP_Plant.AddObj(bp.gameObject);
                        }
                    }
                    finally
                    {
                        IsApplyingRemotePlantBP = false;
                    }
                    break;
                }
            }
        }

        private static void LinkPlantBlueprintToMaster(Building? master, BP_Plant bp, TileType tileType)
        {
            if (master == null) return;
            if (master.m_Info?.List_Effect3 != null)
            {
                int idx = master.m_Info.List_Effect3.IndexOf(tileType);
                if (idx >= 0) master.m_ProductIndex = idx;
            }
            if (master.m_BuildInfoUI is MinigardenInfo mg) mg.m_BP_Plant = bp;
            else if (master.m_BuildInfoUI is GraveInfo gr) gr.m_BP_Plant = bp;
        }

        private static void ClearMasterPlantBlueprintRef(BP_Plant bp)
        {
            if (bp == null || GameMgr.Instance?._BuildingMgr?.List_Building == null) return;
            var master = GameMgr.Instance._BuildingMgr.List_Building.Find(b => b != null && b.m_ID == bp.m_MasterID);
            if (master == null) return;
            if (master.m_BuildInfoUI is MinigardenInfo mg && mg.m_BP_Plant == bp) mg.m_BP_Plant = null;
            else if (master.m_BuildInfoUI is GraveInfo gr && gr.m_BP_Plant == bp) gr.m_BP_Plant = null;
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
                WorldObject? obj = FindWorldObjectAt(x, y, tileType);

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

                        // 1. Refresh game logic, colliders, nodes, and buffs
                        try
                        {
                            obj.SpawnRefresh(false, level);
                        }
                        catch (Exception refreshEx)
                        {
                            Plugin.Log.LogWarning($"[WorldSyncManager] SpawnRefresh catch at ({x}, {y}): {refreshEx.Message}");
                        }

                        // 2. Safely apply Spine skins if a matching skin exists
                        try
                        {
                            if (obj.m_Spine != null && obj.m_Spine.skeleton != null && obj.m_Spine.skeleton.Data != null)
                            {
                                string skinName = "Level" + (level + 1);
                                if (obj.m_Spine.skeleton.Data.FindSkin(skinName) != null)
                                {
                                    obj.m_Spine.skeleton.SetSkin(skinName);
                                    obj.m_Spine.skeleton.SetSlotsToSetupPose();
                                }
                                else
                                {
                                    // For plants without "LevelX" skin (like Tree, BerryBush, etc.), use SpineLevelSet
                                    obj.SpineLevelSet(true);
                                }
                            }
                        }
                        catch (Exception spineEx)
                        {
                            Plugin.Log.LogWarning($"[WorldSyncManager] Spine skin update catch at ({x}, {y}): {spineEx.Message}");
                        }

                        // 3. Refresh weather/idle animation and apply to skeleton so visual mesh updates immediately
                        try
                        {
                            obj.Weather_Refresh();
                            if (obj.m_Spine?.AnimationState != null && obj.m_Spine.skeleton != null)
                            {
                                obj.m_Spine.AnimationState.Apply(obj.m_Spine.skeleton);
                                obj.m_Spine.Update(0f);
                            }
                        }
                        catch { }

                        if (level == 0)
                        {
                            try
                            {
                                obj.HitAnimation();
                            }
                            catch { }
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

        public static WorldObject? FindWorldObjectAt(int x, int y, TileType expectedType = TileType.None)
        {
            bool MatchesType(WorldObject wo) => expectedType == TileType.None || wo.m_TileType == expectedType;

            var tileMgr = GameMgr.Instance?._TileMgr;
            if (tileMgr != null)
            {
                if (tileMgr.IsInsideMap(x, y))
                {
                    var node = tileMgr.GetNode(x, y);
                    if (node?.m_WorldObj != null && MatchesType(node.m_WorldObj)) return node.m_WorldObj;
                }

                if (tileMgr.IsInsideMap(x, y + 1))
                {
                    var nodeUp = tileMgr.GetNode(x, y + 1);
                    if (nodeUp?.m_WorldObj != null && MatchesType(nodeUp.m_WorldObj)) return nodeUp.m_WorldObj;
                }

                if (tileMgr.IsInsideMap(x, y - 1))
                {
                    var nodeDown = tileMgr.GetNode(x, y - 1);
                    if (nodeDown?.m_WorldObj != null && MatchesType(nodeDown.m_WorldObj)) return nodeDown.m_WorldObj;
                }
            }

            if (GameMgr.Instance?._EnvMgr?.List_WorldObj != null)
            {
                var list = GameMgr.Instance._EnvMgr.List_WorldObj;
                WorldObject? closest = null;
                float closestDistSq = float.MaxValue;

                for (int i = 0; i < list.Count; i++)
                {
                    var wo = list[i];
                    if (wo == null || !MatchesType(wo)) continue;

                    float dx = wo.m_Pos.x - x;
                    float dy = wo.m_Pos.y - y;
                    float distSq = dx * dx + dy * dy;

                    if (distSq < 0.25f)
                    {
                        return wo;
                    }

                    if (expectedType != TileType.None && distSq <= 4.0f && distSq < closestDistSq)
                    {
                        closestDistSq = distSq;
                        closest = wo;
                    }
                }

                if (closest != null)
                {
                    return closest;
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
