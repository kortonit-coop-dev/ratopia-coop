using System;
using System.Collections.Generic;
using LiteNetLib;
using LiteNetLib.Utils;
using RatopiaCoop.Network;
using RatopiaUnitBrain.Data;
using RatopiaUnitBrain.Puppeteer;
using RatopiaUnitBrain.Registry;
using RatopiaUnitBrain.Sensor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RatopiaCoop.Sync
{
    public class CitizenSyncManager : MonoBehaviour
    {
        public static CitizenSyncManager Instance { get; private set; } = null!;

        private readonly float _syncInterval = 0.08f; // ~12.5 Hz high-frequency snapshot stream
        private float _lastSyncTime;

        private readonly List<GameUnit> _validUnits = new List<GameUnit>(256);
        private readonly List<AnimalBody> _validAnimals = new List<AnimalBody>(64);
        private readonly Dictionary<long, float> _unitLastSeen = new Dictionary<long, float>();
        private readonly HashSet<int> _receivedAnimalIds = new HashSet<int>();

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
                CoopNetManager.Instance.OnConnectedEvent += OnNetworkConnected;
                CoopNetManager.Instance.OnDisconnectedEvent += OnNetworkDisconnected;
            }

            SceneManager.sceneLoaded += OnSceneLoaded;

            // Automatically puppet any non-player unit registered while client is connected
            UnitRegistry.Instance.OnUnitRegistered += OnUnitRegistered;

            // Register Host Sensor event listeners
            BrainSensor.Instance.OnPathCommitted += OnHostPathCommitted;
            BrainSensor.Instance.OnTaskStarted += OnHostTaskStarted;
            BrainSensor.Instance.OnWorkGaugeUpdated += OnHostWorkGaugeUpdated;
            BrainSensor.Instance.OnBackpackChanged += OnHostBackpackChanged;
            BrainSensor.Instance.OnSpeechBubble += OnHostSpeechBubble;
            BrainSensor.Instance.OnAttackAction += OnHostAttackAction;
        }

        private void OnDestroy()
        {
            if (CoopNetManager.Instance != null)
            {
                CoopNetManager.Instance.OnPacketReceivedEvent -= HandlePacket;
                CoopNetManager.Instance.OnConnectedEvent -= OnNetworkConnected;
                CoopNetManager.Instance.OnDisconnectedEvent -= OnNetworkDisconnected;
            }

            SceneManager.sceneLoaded -= OnSceneLoaded;
            UnitRegistry.Instance.OnUnitRegistered -= OnUnitRegistered;

            BrainSensor.Instance.OnPathCommitted -= OnHostPathCommitted;
            BrainSensor.Instance.OnTaskStarted -= OnHostTaskStarted;
            BrainSensor.Instance.OnWorkGaugeUpdated -= OnHostWorkGaugeUpdated;
            BrainSensor.Instance.OnBackpackChanged -= OnHostBackpackChanged;
            BrainSensor.Instance.OnSpeechBubble -= OnHostSpeechBubble;
            BrainSensor.Instance.OnAttackAction -= OnHostAttackAction;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name == SceneConfigure.SCENE_GAME && CoopNetManager.Instance != null && CoopNetManager.Instance.IsConnected && CoopNetManager.Instance.IsClient)
            {
                MarkExistingUnitsAsPuppets();
            }
        }

        private void OnUnitRegistered(GameUnit unit, NetworkUnitId id)
        {
            if (CoopNetManager.Instance != null && CoopNetManager.Instance.IsConnected && CoopNetManager.Instance.IsClient)
            {
                if (id.Id != 777 && id.Id != 778)
                {
                    UnitRegistry.Instance.SetControlMode(unit, BrainControlMode.RemotePuppet);
                }
            }
        }

        private void OnNetworkConnected()
        {
            if (CoopNetManager.Instance.IsClient)
            {
                MarkExistingUnitsAsPuppets();
            }
        }

        private void OnNetworkDisconnected()
        {
            _unitLastSeen.Clear();
            UnitRegistry.Instance.ResetToAutonomous();
        }

        public void MarkExistingUnitsAsPuppets()
        {
            UnitRegistry.Instance.MarkAllAsPuppetsExcept(777, 778);

            if (GameMgr.Instance == null || GameMgr.Instance._T_UnitMgr == null) return;
            var unitMgr = GameMgr.Instance._T_UnitMgr;

            RegisterAndMarkList(unitMgr.List_Citizen);
            RegisterAndMarkList(unitMgr.List_AllEnemy);
            RegisterAndMarkList(unitMgr.List_GBot);
            RegisterAndMarkList(unitMgr.List_Alliance);
            RegisterAndMarkList(unitMgr.List_SkullRat);
            RegisterAndMarkList(unitMgr.List_Ant);
            RegisterAndMarkList(unitMgr.List_MDrone);
        }

        private void RegisterAndMarkList<T>(List<T>? list) where T : GameUnit
        {
            if (list == null) return;
            for (int i = 0; i < list.Count; i++)
            {
                var u = list[i];
                if (u != null && u.m_ID != 777 && u.m_ID != 778)
                {
                    UnitRegistry.Instance.Register(u);
                    UnitRegistry.Instance.SetControlMode(u, BrainControlMode.RemotePuppet);
                }
            }
        }

        private void Update()
        {
            if (CoopNetManager.Instance == null || !CoopNetManager.Instance.IsConnected) return;
            if (SaveTransferManager.IsWorldLoading) return;

            // Host broadcasts periodic delta heartbeat
            if (CoopNetManager.Instance.IsHost)
            {
                if (Time.time - _lastSyncTime >= _syncInterval)
                {
                    _lastSyncTime = Time.time;
                    BroadcastDeltaHeartbeat();
                    BroadcastAnimals();
                }
            }
        }

        // =========================================================================
        // HOST SENSOR EVENT BROADCASTS (Immediate, Reliable)
        // =========================================================================

        private void OnHostPathCommitted(GameUnit unit, List<Vector2> path, float speed)
        {
            if (CoopNetManager.Instance == null || !CoopNetManager.Instance.IsConnected || !CoopNetManager.Instance.IsHost) return;
            if (unit == null || unit is T_Queen || path == null || path.Count == 0) return;

            NetworkUnitId unitId = NetworkUnitId.FromUnit(unit);
            NetDataWriter writer = new NetDataWriter();
            writer.Put((byte)unitId.Kind);
            writer.Put(unitId.Id);
            writer.Put(speed);

            Vector3 pos = unit.Tf != null ? unit.Tf.position : unit.transform.position;
            writer.Put(pos.x);
            writer.Put(pos.y);

            short count = (short)Mathf.Min(path.Count, 256);
            writer.Put(count);
            for (int i = 0; i < count; i++)
            {
                writer.Put(path[i].x);
                writer.Put(path[i].y);
            }

            short enemyType = (short)(unit is GameEnemy e ? e.m_E_Name : 0);
            writer.Put(enemyType);

            CoopNetManager.Instance.SendPacket(PacketType.UnitPathCommit, writer, DeliveryMethod.ReliableOrdered);
        }

        private void OnHostTaskStarted(GameUnit unit, TaskDescriptor task)
        {
            if (CoopNetManager.Instance == null || !CoopNetManager.Instance.IsConnected || !CoopNetManager.Instance.IsHost) return;
            if (unit == null || unit is T_Queen || task == null) return;

            NetworkUnitId unitId = NetworkUnitId.FromUnit(unit);
            NetDataWriter writer = new NetDataWriter();
            writer.Put((byte)unitId.Kind);
            writer.Put(unitId.Id);
            writer.Put((byte)task.TaskType);
            writer.Put((short)unit.m_AniState);

            string animName = string.Empty;
            if (unit.m_Spine != null && unit.m_Spine.AnimationState != null)
            {
                var track = unit.m_Spine.AnimationState.GetCurrent(0);
                if (track != null && track.Animation != null)
                    animName = track.Animation.Name ?? string.Empty;
            }
            writer.Put(animName);

            Vector3 pos = unit.Tf != null ? unit.Tf.position : unit.transform.position;
            writer.Put(pos.x);
            writer.Put(pos.y);
            writer.Put(task.Duration);
            writer.Put((short)task.ItemType);

            bool facingRight = unit.m_Spine != null && unit.m_Spine.skeleton != null && unit.m_Spine.skeleton.ScaleX > 0;
            writer.Put(facingRight);
            bool inBuilding = unit.Tf != null && unit.Tf.position.z >= 0.35f;
            writer.Put(inBuilding);

            short enemyType = (short)(unit is GameEnemy e ? e.m_E_Name : 0);
            writer.Put(enemyType);

            CoopNetManager.Instance.SendPacket(PacketType.UnitAction, writer, DeliveryMethod.ReliableOrdered);
        }

        private void OnHostWorkGaugeUpdated(GameUnit unit, bool active, float progress, float duration)
        {
            if (CoopNetManager.Instance == null || !CoopNetManager.Instance.IsConnected || !CoopNetManager.Instance.IsHost) return;
            if (unit == null || unit is T_Queen) return;

            NetworkUnitId unitId = NetworkUnitId.FromUnit(unit);
            NetDataWriter writer = new NetDataWriter();
            writer.Put((byte)unitId.Kind);
            writer.Put(unitId.Id);
            writer.Put((byte)(active ? UnitTaskType.Crafting : UnitTaskType.Idle));
            writer.Put((short)unit.m_AniState);

            string animName = string.Empty;
            if (unit.m_Spine != null && unit.m_Spine.AnimationState != null)
            {
                var track = unit.m_Spine.AnimationState.GetCurrent(0);
                if (track != null && track.Animation != null)
                    animName = track.Animation.Name ?? string.Empty;
            }
            writer.Put(animName);

            Vector3 pos = unit.Tf != null ? unit.Tf.position : unit.transform.position;
            writer.Put(pos.x);
            writer.Put(pos.y);
            writer.Put(duration);
            writer.Put((short)TileType.None);

            bool facingRight = unit.m_Spine != null && unit.m_Spine.skeleton != null && unit.m_Spine.skeleton.ScaleX > 0;
            writer.Put(facingRight);
            bool inBuilding = unit.Tf != null && unit.Tf.position.z >= 0.35f;
            writer.Put(inBuilding);

            short enemyType = (short)(unit is GameEnemy e ? e.m_E_Name : 0);
            writer.Put(enemyType);

            CoopNetManager.Instance.SendPacket(PacketType.UnitAction, writer, DeliveryMethod.ReliableOrdered);
        }

        private void OnHostBackpackChanged(GameUnit unit, TileType carriedItem, int count)
        {
            if (CoopNetManager.Instance == null || !CoopNetManager.Instance.IsConnected || !CoopNetManager.Instance.IsHost) return;
            if (unit == null || unit is T_Queen) return;

            NetworkUnitId unitId = NetworkUnitId.FromUnit(unit);
            NetDataWriter writer = new NetDataWriter();
            writer.Put((byte)unitId.Kind);
            writer.Put(unitId.Id);
            writer.Put((byte)UnitTaskType.Gathering);
            writer.Put((short)unit.m_AniState);

            string animName = string.Empty;
            if (unit.m_Spine != null && unit.m_Spine.AnimationState != null)
            {
                var track = unit.m_Spine.AnimationState.GetCurrent(0);
                if (track != null && track.Animation != null)
                    animName = track.Animation.Name ?? string.Empty;
            }
            writer.Put(animName);

            Vector3 pos = unit.Tf != null ? unit.Tf.position : unit.transform.position;
            writer.Put(pos.x);
            writer.Put(pos.y);
            writer.Put(0f);
            writer.Put((short)carriedItem);

            bool facingRight = unit.m_Spine != null && unit.m_Spine.skeleton != null && unit.m_Spine.skeleton.ScaleX > 0;
            writer.Put(facingRight);
            bool inBuilding = unit.Tf != null && unit.Tf.position.z >= 0.35f;
            writer.Put(inBuilding);

            short enemyType = (short)(unit is GameEnemy e ? e.m_E_Name : 0);
            writer.Put(enemyType);

            CoopNetManager.Instance.SendPacket(PacketType.UnitAction, writer, DeliveryMethod.ReliableOrdered);
        }

        private void OnHostSpeechBubble(GameUnit unit, SpBubbleState bubble, int level)
        {
            if (CoopNetManager.Instance == null || !CoopNetManager.Instance.IsConnected || !CoopNetManager.Instance.IsHost) return;
            if (unit == null || unit is T_Queen) return;

            NetworkUnitId unitId = NetworkUnitId.FromUnit(unit);
            NetDataWriter writer = new NetDataWriter();
            writer.Put((byte)unitId.Kind);
            writer.Put(unitId.Id);
            writer.Put((short)bubble);
            writer.Put(level);

            CoopNetManager.Instance.SendPacket(PacketType.UnitSpeechBubble, writer, DeliveryMethod.ReliableOrdered);
        }

        private void OnHostAttackAction(GameUnit unit, bool isBow)
        {
            if (CoopNetManager.Instance == null || !CoopNetManager.Instance.IsConnected || !CoopNetManager.Instance.IsHost) return;
            if (unit == null || unit is T_Queen) return;

            NetworkUnitId unitId = NetworkUnitId.FromUnit(unit);
            NetDataWriter writer = new NetDataWriter();
            writer.Put((byte)unitId.Kind);
            writer.Put(unitId.Id);
            writer.Put((byte)UnitTaskType.Attacking);
            writer.Put((short)(isBow ? AniState.Atk_BowMid : AniState.Attack_Sword_1));

            string animName = isBow ? "Attack_Bow" : "AttackCombo_Sword1";
            writer.Put(animName);

            Vector3 pos = unit.Tf != null ? unit.Tf.position : unit.transform.position;
            writer.Put(pos.x);
            writer.Put(pos.y);
            writer.Put(0f);
            writer.Put((short)TileType.None);

            bool facingRight = unit.m_Spine != null && unit.m_Spine.skeleton != null && unit.m_Spine.skeleton.ScaleX > 0;
            writer.Put(facingRight);
            bool inBuilding = false;
            writer.Put(inBuilding);

            short enemyType = (short)(unit is GameEnemy e ? e.m_E_Name : 0);
            writer.Put(enemyType);

            CoopNetManager.Instance.SendPacket(PacketType.UnitAction, writer, DeliveryMethod.ReliableOrdered);
        }

        // =========================================================================
        // PERIODIC DELTA HEARTBEAT & RECONCILIATION (Sequenced, 4 Hz)
        // =========================================================================

        private void BroadcastDeltaHeartbeat()
        {
            _validUnits.Clear();
            var allUnits = UnitRegistry.Instance.GetAllUnits();
            for (int i = 0; i < allUnits.Count && _validUnits.Count < 250; i++)
            {
                var u = allUnits[i];
                if (u != null && u.gameObject != null && u.gameObject.activeInHierarchy && u.m_ID != 777 && u.m_ID != 778)
                {
                    _validUnits.Add(u);
                }
            }

            int total = _validUnits.Count;
            if (total == 0) return;

            // Chunk units into batches of 15 to stay well under LiteNetLib MTU (< 1000 bytes) and avoid TooBigPacketException
            const int batchSize = 15;
            for (int offset = 0; offset < total; offset += batchSize)
            {
                int chunkCount = Mathf.Min(batchSize, total - offset);
                NetDataWriter writer = new NetDataWriter();
                writer.Put((short)chunkCount);

                for (int i = 0; i < chunkCount; i++)
                {
                    var u = _validUnits[offset + i];
                    var id = NetworkUnitId.FromUnit(u);
                    writer.Put((byte)id.Kind);
                    writer.Put(id.Id);

                    Vector3 pos = u.Tf != null ? u.Tf.position : u.transform.position;
                    writer.Put(pos.x);
                    writer.Put(pos.y);
                    writer.Put(u.m_Speed > 0.1f ? u.m_Speed : 3.5f);
                    writer.Put(u.IsMoveState());

                    bool facingRight = u.m_Spine != null && u.m_Spine.skeleton != null && u.m_Spine.skeleton.ScaleX > 0;
                    writer.Put(facingRight);
                    writer.Put(u.m_CurHP);

                    string animName = string.Empty;
                    if (u.m_Spine != null && u.m_Spine.AnimationState != null)
                    {
                        var track = u.m_Spine.AnimationState.GetCurrent(0);
                        if (track != null && track.Animation != null)
                            animName = track.Animation.Name ?? string.Empty;
                    }
                    writer.Put(animName);

                    short carriedItem = (short)((u.List_Gathering != null && u.List_Gathering.Count > 0) ? u.List_Gathering[0].m_Type : TileType.None);
                    writer.Put(carriedItem);

                    bool isWorking = u.m_WorkTimeGauge != null && u.m_WorkTimeGauge.IsGaugeAcitve();
                    writer.Put(isWorking);
                    float workProgress = (isWorking && u.m_WorkTimeGauge != null && u.m_WorkTimeGauge.m_Gauge != null) ? u.m_WorkTimeGauge.m_Gauge.size.x : 0f;
                    writer.Put(workProgress);

                    bool inBuilding = u.Tf != null && u.Tf.position.z >= 0.35f;
                    writer.Put(inBuilding);

                    // For enemies, transmit enemy type so client can instantiate correct prefab if missing
                    if (u is GameEnemy enemy)
                    {
                        writer.Put((short)enemy.m_E_Name);
                    }
                    else
                    {
                        writer.Put((short)0);
                    }
                }

                CoopNetManager.Instance.SendPacket(PacketType.CitizenSync, writer, DeliveryMethod.ReliableOrdered);
            }
        }

        private void BroadcastAnimals()
        {
            if (GameMgr.Instance == null || GameMgr.Instance._AnimalMgr == null) return;
            var animalList = GameMgr.Instance._AnimalMgr.List_Animal;
            if (animalList == null) return;

            _validAnimals.Clear();
            for (int i = 0; i < animalList.Count && _validAnimals.Count < 60; i++)
            {
                var a = animalList[i];
                if (a != null && a.gameObject != null && a.gameObject.activeInHierarchy)
                {
                    _validAnimals.Add(a);
                }
            }

            int total = _validAnimals.Count;
            if (total == 0) return;

            const int batchSize = 15;
            for (int offset = 0; offset < total; offset += batchSize)
            {
                int chunkCount = Mathf.Min(batchSize, total - offset);
                NetDataWriter writer = new NetDataWriter();
                writer.Put((short)chunkCount);

                for (int i = 0; i < chunkCount; i++)
                {
                    var a = _validAnimals[offset + i];
                    writer.Put(a.m_ID);
                    short typeName = (short)(a.m_Info != null ? (short)a.m_Info.m_Name : 0);
                    writer.Put(typeName);

                    Vector3 pos = a.Tf != null ? a.Tf.position : a.transform.position;
                    writer.Put(pos.x);
                    writer.Put(pos.y);

                    bool facingRight = a.m_Spine != null && a.m_Spine.skeleton != null && a.m_Spine.skeleton.ScaleX > 0;
                    writer.Put(facingRight);
                    writer.Put(a.m_CurHP);

                    string animName = string.Empty;
                    if (a.m_Spine != null && a.m_Spine.AnimationState != null)
                    {
                        var track = a.m_Spine.AnimationState.GetCurrent(0);
                        if (track != null && track.Animation != null)
                            animName = track.Animation.Name ?? string.Empty;
                    }
                    writer.Put(animName);
                }

                CoopNetManager.Instance.SendPacket(PacketType.AnimalSync, writer, DeliveryMethod.ReliableOrdered);
            }
        }

        // =========================================================================
        // CLIENT PACKET PROCESSING
        // =========================================================================

        private void HandlePacket(PacketType type, NetDataReader reader)
        {
            if (SaveTransferManager.IsWorldLoading) return;
            if (GameMgr.Instance == null || GameMgr.Instance._T_UnitMgr == null) return;

            switch (type)
            {
                case PacketType.UnitPathCommit:
                    HandleClientUnitPathCommit(reader);
                    break;

                case PacketType.UnitAction:
                    HandleClientUnitAction(reader);
                    break;

                case PacketType.UnitSpeechBubble:
                    HandleClientUnitSpeechBubble(reader);
                    break;

                case PacketType.CitizenSync:
                    HandleClientDeltaHeartbeat(reader);
                    break;

                case PacketType.AnimalSync:
                    HandleClientAnimalSync(reader);
                    break;
            }
        }

        private void HandleClientUnitPathCommit(NetDataReader reader)
        {
            NetworkUnitKind kind = (NetworkUnitKind)reader.GetByte();
            int id = reader.GetInt();
            float speed = reader.GetFloat();
            Vector2 currentPos = new Vector2(reader.GetFloat(), reader.GetFloat());
            short count = reader.GetShort();

            List<Vector2> path = new List<Vector2>(count);
            for (int i = 0; i < count; i++)
            {
                path.Add(new Vector2(reader.GetFloat(), reader.GetFloat()));
            }

            EnemyType enemyType = reader.AvailableBytes >= 2 ? (EnemyType)reader.GetShort() : (EnemyType)0;
            NetworkUnitId unitId = new NetworkUnitId(kind, id);
            GameUnit unit = GetOrSpawnPuppetUnit(unitId, currentPos, enemyType);
            if (unit != null)
            {
                BrainPuppeteer.Instance.ExecuteMovePath(unit, path, currentPos, speed);
            }
        }

        private void HandleClientUnitAction(NetDataReader reader)
        {
            NetworkUnitKind kind = (NetworkUnitKind)reader.GetByte();
            int id = reader.GetInt();
            UnitTaskType taskType = (UnitTaskType)reader.GetByte();
            AniState aniState = (AniState)reader.GetShort();
            string animName = reader.GetString();
            Vector2 targetPos = new Vector2(reader.GetFloat(), reader.GetFloat());
            float duration = reader.GetFloat();
            TileType carriedItem = (TileType)reader.GetShort();
            bool facingRight = reader.GetBool();
            bool inBuilding = reader.GetBool();
            EnemyType enemyType = reader.AvailableBytes >= 2 ? (EnemyType)reader.GetShort() : (EnemyType)0;

            NetworkUnitId unitId = new NetworkUnitId(kind, id);
            GameUnit unit = GetOrSpawnPuppetUnit(unitId, targetPos, enemyType);
            if (unit != null)
            {
                UnitActionCommand cmd = new UnitActionCommand
                {
                    ActionType = taskType,
                    AniState = aniState,
                    AnimationName = animName,
                    Loop = taskType != UnitTaskType.Attacking,
                    TargetPos = targetPos,
                    ShowWorkGauge = taskType == UnitTaskType.Crafting || duration > 0,
                    WorkGaugeDuration = duration,
                    CarriedItem = carriedItem,
                    CarriedCount = carriedItem != TileType.None ? 1 : 0,
                    InBuilding = inBuilding,
                    FacingRight = facingRight
                };
                BrainPuppeteer.Instance.ExecuteAction(unit, cmd);
            }
        }

        private void HandleClientUnitSpeechBubble(NetDataReader reader)
        {
            NetworkUnitKind kind = (NetworkUnitKind)reader.GetByte();
            int id = reader.GetInt();
            SpBubbleState bubble = (SpBubbleState)reader.GetShort();
            int level = reader.GetInt();

            NetworkUnitId unitId = new NetworkUnitId(kind, id);
            if (UnitRegistry.Instance.TryGetUnit(unitId, out var unit) && unit != null)
            {
                if (unit.m_SpeechBubble != null)
                {
                    if (bubble == SpBubbleState.None)
                    {
                        unit.m_SpeechBubble.SpeechBubbleOff();
                    }
                    else if (level == 0)
                    {
                        unit.m_SpeechBubble.SetSpeechBubble(bubble);
                    }
                    else
                    {
                        unit.m_SpeechBubble.SetSpeechBubble(bubble, level);
                    }
                }
            }
        }

        private void HandleClientDeltaHeartbeat(NetDataReader reader)
        {
            short count = reader.GetShort();
            float now = Time.time;

            for (int i = 0; i < count; i++)
            {
                NetworkUnitKind kind = (NetworkUnitKind)reader.GetByte();
                int id = reader.GetInt();
                float x = reader.GetFloat();
                float y = reader.GetFloat();
                float speed = reader.GetFloat();
                bool isMoving = reader.GetBool();
                bool facingRight = reader.GetBool();
                float curHp = reader.GetFloat();
                string animName = reader.GetString();
                TileType carriedItem = (TileType)reader.GetShort();
                bool isWorking = reader.GetBool();
                float workProgress = reader.GetFloat();
                bool inBuilding = reader.GetBool();
                EnemyType enemyType = (EnemyType)reader.GetShort();

                NetworkUnitId unitId = new NetworkUnitId(kind, id);
                _unitLastSeen[unitId.Packed] = now;

                GameUnit unit = GetOrSpawnPuppetUnit(unitId, new Vector2(x, y), enemyType);
                if (unit != null)
                {
                    UnitBrainSnapshot snapshot = new UnitBrainSnapshot
                    {
                        UnitId = unitId,
                        Position = new Vector2(x, y),
                        Speed = speed,
                        IsMoving = isMoving,
                        FacingRight = facingRight,
                        CurHP = curHp,
                        MaxHP = unit.m_MaxHP,
                        CharState = curHp <= 0 ? CharState.Death : unit.m_CharState,
                        AniState = unit.m_AniState,
                        CurrentAnimName = animName,
                        CarriedItem = carriedItem,
                        CarriedCount = carriedItem != TileType.None ? 1 : 0,
                        IsWorking = isWorking,
                        WorkProgress = workProgress,
                        IsInBuilding = inBuilding
                    };
                    BrainPuppeteer.Instance.ApplyStateSnapshot(unit, snapshot);
                }
            }

            // Despawn ghost units on Client that host truly removed or banished (missing for > 6 seconds)
            var allUnits = UnitRegistry.Instance.GetAllUnits();
            for (int j = allUnits.Count - 1; j >= 0; j--)
            {
                var u = allUnits[j];
                if (u != null && u.m_ID != 777 && u.m_ID != 778)
                {
                    var uId = NetworkUnitId.FromUnit(u);
                    if (_unitLastSeen.TryGetValue(uId.Packed, out float lastSeen))
                    {
                        if (now - lastSeen > 6.0f)
                        {
                            _unitLastSeen.Remove(uId.Packed);
                            if (u.m_CharState != CharState.Death)
                            {
                                u.DeathCheck(-999);
                            }
                        }
                    }
                    else
                    {
                        _unitLastSeen[uId.Packed] = now;
                    }
                }
            }
        }

        private void HandleClientAnimalSync(NetDataReader reader)
        {
            if (GameMgr.Instance == null || GameMgr.Instance._AnimalMgr == null) return;
            var animalMgr = GameMgr.Instance._AnimalMgr;

            short count = reader.GetShort();
            _receivedAnimalIds.Clear();

            for (int i = 0; i < count; i++)
            {
                int id = reader.GetInt();
                AnimalName name = (AnimalName)reader.GetShort();
                float x = reader.GetFloat();
                float y = reader.GetFloat();
                bool facingRight = reader.GetBool();
                float curHp = reader.GetFloat();
                string animName = reader.GetString();

                _receivedAnimalIds.Add(id);

                if (!UnitRegistry.Instance.TryGetAnimal(id, out var animal) || animal == null)
                {
                    animal = animalMgr.MakeAnimal(name, new Vector2(x, y));
                    if (animal != null)
                    {
                        animal.m_ID = id;
                        UnitRegistry.Instance.RegisterAnimal(animal);
                    }
                }

                if (animal != null)
                {
                    BrainPuppeteer.Instance.ExecuteAnimalPuppet(animal, new Vector2(x, y), facingRight, animName, curHp);
                }
            }
        }

        private GameUnit GetOrSpawnPuppetUnit(NetworkUnitId id, Vector2 pos, EnemyType enemyType)
        {
            if (UnitRegistry.Instance.TryGetUnit(id, out var unit) && unit != null)
            {
                UnitRegistry.Instance.SetControlMode(unit, BrainControlMode.RemotePuppet);
                return unit;
            }

            var unitMgr = GameMgr.Instance._T_UnitMgr;
            if (unitMgr == null) return null!;

            switch (id.Kind)
            {
                case NetworkUnitKind.Enemy:
                {
                    var enemy = unitMgr.MakeEnemy(enemyType, pos, AI_Mode.Default);
                    if (enemy != null)
                    {
                        enemy.m_ID = id.Id;
                        UnitRegistry.Instance.Register(enemy);
                        UnitRegistry.Instance.SetControlMode(enemy, BrainControlMode.RemotePuppet);
                        return enemy;
                    }
                    break;
                }
                case NetworkUnitKind.Drone:
                {
                    unitMgr.LoadDrone(pos, id.Id, 0);
                    if (UnitRegistry.Instance.TryGetUnit(id, out var drone) && drone != null)
                    {
                        UnitRegistry.Instance.SetControlMode(drone, BrainControlMode.RemotePuppet);
                        return drone;
                    }
                    break;
                }
                case NetworkUnitKind.GBot:
                {
                    unitMgr.MakeGBot(pos, id.Id);
                    if (UnitRegistry.Instance.TryGetUnit(id, out var gbot) && gbot != null)
                    {
                        UnitRegistry.Instance.SetControlMode(gbot, BrainControlMode.RemotePuppet);
                        return gbot;
                    }
                    break;
                }
                case NetworkUnitKind.SkullRat:
                {
                    unitMgr.LoadSkullRat(pos, id.Id, Gender.Male);
                    if (UnitRegistry.Instance.TryGetUnit(id, out var skullRat) && skullRat != null)
                    {
                        UnitRegistry.Instance.SetControlMode(skullRat, BrainControlMode.RemotePuppet);
                        return skullRat;
                    }
                    break;
                }
                case NetworkUnitKind.Alliance:
                {
                    unitMgr.LoadAlliance(pos, id.Id, Gender.Male, 1, "Alliance");
                    if (UnitRegistry.Instance.TryGetUnit(id, out var alliance) && alliance != null)
                    {
                        UnitRegistry.Instance.SetControlMode(alliance, BrainControlMode.RemotePuppet);
                        return alliance;
                    }
                    break;
                }
                case NetworkUnitKind.Ant:
                {
                    unitMgr.LoadAnt(pos, id.Id, Gender.Male, "Ant");
                    if (UnitRegistry.Instance.TryGetUnit(id, out var ant) && ant != null)
                    {
                        UnitRegistry.Instance.SetControlMode(ant, BrainControlMode.RemotePuppet);
                        return ant;
                    }
                    break;
                }
                case NetworkUnitKind.Citizen:
                default:
                {
                    var citizen = unitMgr.MakeCitizen(pos);
                    if (citizen != null)
                    {
                        citizen.m_ID = id.Id;
                        UnitRegistry.Instance.Register(citizen);
                        UnitRegistry.Instance.SetControlMode(citizen, BrainControlMode.RemotePuppet);
                        return citizen;
                    }
                    break;
                }
            }

            return null!;
        }
    }
}
