using System;
using LiteNetLib;
using LiteNetLib.Utils;
using RatopiaCoop.Network;
using Spine.Unity;
using UnityEngine;
using Utility.IO;

namespace RatopiaCoop.Sync
{
    public class QueenSyncManager : MonoBehaviour
    {
        public static QueenSyncManager Instance { get; private set; } = null!;

        // Dedicated references for both characters
        public T_Queen? HostQueen { get; private set; }
        public T_Queen? Player2Worker { get; private set; }
        public T_Queen? Player2Knight => Player2Worker;

        public T_Queen? LocalCharacter { get; private set; }
        public T_Queen? RemoteCharacter { get; private set; }

        // Backwards compatibility properties
        public T_Queen? LocalQueen => LocalCharacter;
        public T_Queen? RemoteQueen => RemoteCharacter;

        public RemoteQueenController? RemoteController { get; private set; }

        public bool IsLocalDowned { get; private set; }
        public bool IsRemoteDowned { get; private set; }
        public bool IsLocalQueenDowned => IsLocalDowned;
        public bool IsRemoteQueenDowned => IsRemoteDowned;

        private T_Queen? _originalHostQueen;
        private float _syncInterval = 1f / 30f; // 30 Hz
        private float _lastSyncTime;
        private float _reviveProgress;
        private const float REVIVE_DURATION = 2.5f;

        // BUG FIX #3: Position preservation during scene changes
        private Vector3 _lastLocalPosition = Vector3.zero;
        private Vector3 _lastRemotePosition = Vector3.zero;
        private bool _pendingSceneLoad = false;

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
                CoopNetManager.Instance.OnConnectedEvent += HandleConnected;
                CoopNetManager.Instance.OnDisconnectedEvent += HandleDisconnected;
            }

            // BUG FIX #3: Subscribe to scene changes to preserve positions
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
            UnityEngine.SceneManagement.SceneManager.sceneUnloaded += OnSceneUnloaded;
        }

        private void Update()
        {
            // Auto-setup characters when game world is ready
            if (LocalCharacter == null && GameMgr.Instance != null && GameMgr.Instance._T_UnitMgr != null && GameMgr.Instance._T_UnitMgr.m_Queen != null)
            {
                SetupQueens();
            }

            // Sync local character state across network
            if (LocalCharacter != null && CoopNetManager.Instance != null && CoopNetManager.Instance.IsConnected)
            {
                if (Time.time - _lastSyncTime >= _syncInterval)
                {
                    _lastSyncTime = Time.time;
                    SendLocalCharacterState();
                }

                // Fog of War update for local character
                LocalCharacter.m_FogCircle?.UpdateCall();

                // Revive proximity logic
                UpdateReviveLogic();
            }
        }

        public void SetupQueens()
        {
            if (GameMgr.Instance == null || GameMgr.Instance._T_UnitMgr == null) return;
            var unitMgr = GameMgr.Instance._T_UnitMgr;
            if (unitMgr.m_Queen == null && HostQueen == null) return;

            // 1. Reliably locate the real Host Queen (ID 777), preventing identity confusion with Player2Worker (ID 778)
            T_Queen? trueHostQueen = null;
            if (HostQueen != null && HostQueen.gameObject != null && HostQueen.m_ID == 777)
            {
                trueHostQueen = HostQueen;
            }
            else
            {
                var allQueens = GameObject.FindObjectsOfType<T_Queen>();
                foreach (var q in allQueens)
                {
                    if (q != null && q.gameObject != null && (q.m_ID == 777 || (q.name == "Queen" && q != Player2Worker)))
                    {
                        trueHostQueen = q;
                        break;
                    }
                }
                if (trueHostQueen == null && unitMgr.m_Queen != null && unitMgr.m_Queen != Player2Worker)
                {
                    trueHostQueen = unitMgr.m_Queen;
                }
            }

            if (trueHostQueen == null) return;

            HostQueen = trueHostQueen;
            _originalHostQueen = HostQueen;
            HostQueen.m_ID = 777;
            HostQueen.name = "Queen";
            HostQueen.Tf = HostQueen.transform;
            HostQueen.Obj = HostQueen.gameObject;
            HostQueen.gameObject.SetActive(true);

            var hostMr = HostQueen.GetComponentInChildren<MeshRenderer>();
            if (hostMr != null) hostMr.enabled = true;

            if (CoopNetManager.Instance != null && CoopNetManager.Instance.IsConnected)
            {
                // Ensure Player 2 Worker unit exists
                Player2Worker = SpawnOrGetPlayer2Worker(unitMgr, HostQueen);

                if (CoopNetManager.Instance.IsHost)
                {
                    LocalCharacter = HostQueen;
                    RemoteCharacter = Player2Worker;
                    unitMgr.m_Queen = HostQueen;
                }
                else if (CoopNetManager.Instance.IsClient)
                {
                    LocalCharacter = Player2Worker;
                    RemoteCharacter = HostQueen;

                    if (Player2Worker != null)
                    {
                        unitMgr.m_Queen = Player2Worker;

                        // Center camera on Client's character
                        if (GameMgr.Instance._CamMgr != null)
                        {
                            GameMgr.Instance._CamMgr.Tf_Update(Player2Worker.transform.position);
                        }
                    }
                }

                // Attach RemoteController ONLY to the remote unit
                if (RemoteCharacter != null)
                {
                    RemoteController = RemoteCharacter.gameObject.GetComponent<RemoteQueenController>();
                    if (RemoteController == null)
                    {
                        RemoteController = RemoteCharacter.gameObject.AddComponent<RemoteQueenController>();
                    }
                    RemoteController.Initialize(RemoteCharacter);
                }

                // Clean up any stray RemoteController on the local unit
                if (LocalCharacter != null)
                {
                    var stray = LocalCharacter.gameObject.GetComponent<RemoteQueenController>();
                    if (stray != null)
                    {
                        Destroy(stray);
                    }
                }

                // Sanitize all team units to prevent MapObj or MonkfishHole null transform errors
                SanitizeTeamUnits(unitMgr);

                Plugin.Log.LogInfo($"[QueenSyncManager] Characters linked. Local: {LocalCharacter?.name} (ID: {LocalCharacter?.m_ID}), Remote: {RemoteCharacter?.name} (ID: {RemoteCharacter?.m_ID})");
            }
            else
            {
                // Single-player fallback
                unitMgr.m_Queen = HostQueen;
                LocalCharacter = HostQueen;
                RemoteCharacter = null;
                RemoteController = null;
            }
        }

        private static void SanitizeQueenSerializableFields(T_Queen worker, T_Queen? hostQueen)
        {
            if (worker == null) return;

            worker.m_AccessoryInfo = null; // Worker rat does not wear royal crown

            if (worker.m_WeaponInfo != null && worker.m_WeaponInfo.List_Ability == null)
            {
                worker.m_WeaponInfo = (hostQueen != null && hostQueen.m_WeaponInfo != null && hostQueen.m_WeaponInfo.List_Ability != null)
                    ? hostQueen.m_WeaponInfo
                    : null;
            }

            if (worker.m_ClothesInfo != null && worker.m_ClothesInfo.List_Ability == null)
            {
                worker.m_ClothesInfo = (hostQueen != null && hostQueen.m_ClothesInfo != null && hostQueen.m_ClothesInfo.List_Ability != null)
                    ? hostQueen.m_ClothesInfo
                    : null;
            }

            if (worker.m_QueenCharacterInfo != null && worker.m_QueenCharacterInfo.List_Ability == null)
            {
                worker.m_QueenCharacterInfo = (hostQueen != null && hostQueen.m_QueenCharacterInfo != null && hostQueen.m_QueenCharacterInfo.List_Ability != null)
                    ? hostQueen.m_QueenCharacterInfo
                    : null;
            }

            if (hostQueen != null && hostQueen.m_PassiveSkill != null)
            {
                worker.m_PassiveSkill = hostQueen.m_PassiveSkill;
            }
        }

        public static void EnsureQueenAtkBox(T_Queen? queen)
        {
            if (queen == null) return;
            try
            {
                if (queen.m_AtkBox != null)
                {
                    queen.m_AtkBox.Tf = queen.m_AtkBox.transform;
                    if (queen.m_AtkBox.Rect == null || queen.m_AtkBox.Rect.Length < 4)
                        queen.m_AtkBox.Rect = new Vector2[4];
                    queen.m_AtkBox.BoxSet(1);
                }
            }
            catch { }
        }

        private T_Queen SpawnOrGetPlayer2Worker(T_UnitMgr unitMgr, T_Queen hostQueen)
        {
            // BUG FIX #1: Check if Player2Worker already exists and reuse it
            if (Player2Worker != null && Player2Worker.gameObject != null)
            {
                Plugin.Log.LogInfo("[QueenSyncManager] Reusing existing Player2Worker instance.");
                Player2Worker.m_ID = 778;
                Player2Worker.m_UnitName = "Worker Rat";
                Player2Worker.Tf = Player2Worker.transform;
                Player2Worker.Obj = Player2Worker.gameObject;
                SanitizeQueenSerializableFields(Player2Worker, hostQueen);
                EnsureQueenAtkBox(Player2Worker);
                Player2Worker.gameObject.SetActive(true);
                return Player2Worker;
            }

            // Search for existing Player2_Worker in the scene
            var existing = GameObject.Find("Player2_Worker");
            if (existing != null)
            {
                var existingWorker = existing.GetComponent<T_Queen>();
                if (existingWorker != null)
                {
                    Plugin.Log.LogInfo("[QueenSyncManager] Found existing Player2_Worker GameObject in scene.");
                    existingWorker.m_ID = 778;
                    existingWorker.m_UnitName = "Worker Rat";
                    existingWorker.Tf = existingWorker.transform;
                    existingWorker.Obj = existingWorker.gameObject;
                    SanitizeQueenSerializableFields(existingWorker, hostQueen);
                    EnsureQueenAtkBox(existingWorker);
                    existingWorker.gameObject.SetActive(true);
                    return existingWorker;
                }
            }

            // Clean up any stray duplicates before creating new one
            var allWorkers = GameObject.FindObjectsOfType<T_Queen>();
            foreach (var existingW in allWorkers)
            {
                if (existingW.m_ID == 778 && existingW != Player2Worker)
                {
                    Plugin.Log.LogWarning($"[QueenSyncManager] Found stray Player2Worker duplicate. Cleaning up: {existingW.name}");
                    Destroy(existingW.gameObject);
                }
            }

            Plugin.Log.LogInfo("[QueenSyncManager] Creating new Player2_Worker unit.");
            // Clone host Queen safely to create Player 2 worker unit
            var hostObj = hostQueen.gameObject;
            GameObject workerObj = Instantiate(hostObj, hostObj.transform.parent);

            // Re-ensure host object remains active
            hostObj.SetActive(true);

            workerObj.name = "Player2_Worker";
            var worker = workerObj.GetComponent<T_Queen>();
            worker.m_ID = 778;
            worker.m_UnitName = "Worker Rat";
            SanitizeQueenSerializableFields(worker, hostQueen);
            worker.transform.position = hostQueen.transform.position + new Vector3(1.5f, 0f, 0f);
            worker.Tf = worker.transform;
            worker.Obj = worker.gameObject;

            // Pre-initialize UnitHP before activating to avoid null reference in CocosFunc
            if (worker.m_UnitHP != null)
            {
                worker.m_UnitHP.Tf = worker.m_UnitHP.transform;
                worker.m_UnitHP.Obj = worker.m_UnitHP.gameObject;
                if (worker.m_UnitHP.Tf_HP_Effect != null)
                {
                    var objEffectField = typeof(UnitHP).GetField("Obj_Effect", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    objEffectField?.SetValue(worker.m_UnitHP, worker.m_UnitHP.Tf_HP_Effect.gameObject);
                }
            }

            // Activate Worker instance
            workerObj.SetActive(true);

            // Enforce ID 778 and Worker attributes after Awake
            worker.m_ID = 778;
            worker.m_UnitName = "Worker Rat";
            worker.Tf = worker.transform;
            worker.Obj = worker.gameObject;
            SanitizeQueenSerializableFields(worker, hostQueen);

            // Properly initialize subsystems safely so mining, building, and interaction work
            try
            {
                worker.m_UnitHP?.InitHP(true);
                if (worker.m_CheckBox != null)
                {
                    worker.m_CheckBox.Tf = worker.m_CheckBox.transform;
                    if (worker.m_CheckBox.Tf_InfoBoxParent != null)
                    {
                        worker.m_CheckBox.Tf_InfoBoxParent.gameObject.SetActive(true);
                    }
                    worker.m_CheckBox.Init(worker);
                }
                if (worker.m_WorkTimeGauge != null)
                {
                    worker.m_WorkTimeGauge.Init(worker);
                }
                worker.m_SkillBox?.BoxSet(4);
                EnsureQueenAtkBox(worker);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[QueenSyncManager] Non-critical warning in worker subsystem init: {ex.Message}");
            }

            // Set initial Spine Idle animation and worker green tint
            if (worker.m_Spine != null)
            {
                try
                {
                    worker.m_Spine.Initialize(true);
                    worker.m_Spine.Update(0f);
                    if (worker.m_Spine.skeleton != null)
                    {
                        worker.m_Spine.skeleton.SetColor(new Color(0.75f, 1.0f, 0.75f, 1.0f));
                    }
                    worker.m_Spine.AnimationState?.SetAnimation(0, "Idle", true);
                }
                catch { }
            }

            var mr = worker.GetComponentInChildren<MeshRenderer>();
            if (mr != null) mr.enabled = true;

            // Register in the colony team list
            if (!unitMgr.List_AllTeam.Contains(worker))
            {
                unitMgr.List_AllTeam.Add(worker);
            }

            try
            {
                worker.m_FogCircle?.UpdateCall();
            }
            catch { }

            Plugin.Log.LogInfo("[QueenSyncManager] Player 2 Worker Rat spawned and initialized (ID: 778).");
            return worker;
        }

        private void SanitizeTeamUnits(T_UnitMgr unitMgr)
        {
            if (unitMgr == null || unitMgr.List_AllTeam == null) return;

            for (int i = 0; i < unitMgr.List_AllTeam.Count; i++)
            {
                var unit = unitMgr.List_AllTeam[i];
                if (unit != null)
                {
                    if (unit.Tf == null) unit.Tf = unit.transform;
                    if (unit.Obj == null) unit.Obj = unit.gameObject;
                }
            }
        }

        private void SendLocalCharacterState()
        {
            if (LocalCharacter == null || CoopNetManager.Instance == null) return;

            NetDataWriter writer = new NetDataWriter();
            Vector3 pos = LocalCharacter.transform.position;
            writer.Put(pos.x);
            writer.Put(pos.y);
            writer.Put(pos.z);

            // Direction & Weapon state
            bool facingRight = LocalCharacter.m_Spine != null && LocalCharacter.m_Spine.skeleton.ScaleX > 0;
            writer.Put(facingRight);
            writer.Put(LocalCharacter.m_WeaponOn);
            writer.Put(IsLocalDowned);
            writer.Put(LocalCharacter.m_CurHP);

            // Spine animation name
            string animName = "Idle";
            if (LocalCharacter.m_Spine != null && LocalCharacter.m_Spine.AnimationState != null)
            {
                var track = LocalCharacter.m_Spine.AnimationState.GetCurrent(0);
                if (track != null && track.Animation != null && !string.IsNullOrEmpty(track.Animation.Name))
                {
                    animName = track.Animation.Name;
                }
            }
            writer.Put(animName);

            // Equipped weapon name
            writer.Put(LocalCharacter.m_WeaponName.ToString());

            CoopNetManager.Instance.SendPacket(PacketType.PlayerPosition, writer, DeliveryMethod.ReliableOrdered);
        }

        public void SendPlayerHealthUpdate(int characterId, float curHp, float maxHp)
        {
            if (CoopNetManager.Instance == null || !CoopNetManager.Instance.IsConnected) return;
            NetDataWriter writer = new NetDataWriter();
            writer.Put(characterId);
            writer.Put(curHp);
            writer.Put(maxHp);
            CoopNetManager.Instance.SendPacket(PacketType.PlayerHealthUpdate, writer, DeliveryMethod.ReliableOrdered);
        }

        public void SendPlayerAction(PlayerActionType action)
        {
            if (CoopNetManager.Instance == null || !CoopNetManager.Instance.IsConnected) return;

            NetDataWriter writer = new NetDataWriter();
            writer.Put((byte)action);
            CoopNetManager.Instance.SendPacket(PacketType.PlayerAction, writer, DeliveryMethod.ReliableOrdered);
        }

        public void SetDowned(T_Queen character)
        {
            if (character == LocalCharacter)
            {
                IsLocalDowned = true;
                LocalCharacter.m_CurHP = 1f; // Keep alive in downed state
                LocalCharacter.SetCharState(CharState.Injury);
                LocalCharacter.m_Spine?.AnimationState?.SetAnimation(0, "Down", true);
                ChatManager.Instance?.AddMessage("SYSTEM", $"{character.name} is incapacitated! Hold [E] near them to revive!");
            }
            else if (character == RemoteCharacter)
            {
                IsRemoteDowned = true;
            }
        }

        public bool IsBothDown()
        {
            return IsLocalDowned && IsRemoteDowned;
        }

        public void ReviveCharacter(T_Queen character)
        {
            if (character == LocalCharacter)
            {
                IsLocalDowned = false;
                LocalCharacter.m_CurHP = LocalCharacter.GetMaxHP() * 0.45f;
                LocalCharacter.SetCharState(CharState.None);
                LocalCharacter.m_UnitHP?.UpdateHp(LocalCharacter.m_CurHP / LocalCharacter.GetMaxHP());
                ChatManager.Instance?.AddMessage("SYSTEM", $"{character.name} was revived!");
            }
            else if (character == RemoteCharacter)
            {
                IsRemoteDowned = false;
                RemoteCharacter.m_CurHP = RemoteCharacter.GetMaxHP() * 0.45f;
                RemoteCharacter.SetCharState(CharState.None);
            }
        }

        private void UpdateReviveLogic()
        {
            if (LocalCharacter == null || RemoteCharacter == null) return;

            // If remote player is downed and local player is near, allow revive channel
            if (IsRemoteDowned && !IsLocalDowned)
            {
                float dist = Vector3.Distance(LocalCharacter.transform.position, RemoteCharacter.transform.position);
                if (dist <= 2.5f)
                {
                    if (UnityEngine.Input.GetKey(KeyCode.E) || UnityEngine.Input.GetKey(KeyCode.Mouse1))
                    {
                        _reviveProgress += Time.deltaTime;
                        if (_reviveProgress >= REVIVE_DURATION)
                        {
                            _reviveProgress = 0f;
                            ReviveCharacter(RemoteCharacter);

                            // Send revive packet
                            NetDataWriter writer = new NetDataWriter();
                            writer.Put(RemoteCharacter.m_ID);
                            CoopNetManager.Instance.SendPacket(PacketType.PlayerRevived, writer, DeliveryMethod.ReliableOrdered);
                        }
                    }
                    else
                    {
                        _reviveProgress = 0f;
                    }
                }
                else
                {
                    _reviveProgress = 0f;
                }
            }
        }

        private void HandlePacket(PacketType type, NetDataReader reader)
        {
            if (type == PacketType.PlayerPosition)
            {
                float x = reader.GetFloat();
                float y = reader.GetFloat();
                float z = reader.GetFloat();
                bool facingRight = reader.GetBool();
                bool weaponOn = reader.GetBool();
                bool isDowned = reader.GetBool();
                float curHp = reader.GetFloat();
                string animName = reader.GetString();
                string weaponNameStr = reader.GetString();

                IsRemoteDowned = isDowned;
                if (RemoteCharacter != null)
                {
                    RemoteCharacter.m_CurHP = curHp;

                    // Sync equipped weapon visually
                    if (Enum.TryParse<WeaponName>(weaponNameStr, out var wName) && RemoteCharacter.m_WeaponName != wName)
                    {
                        RemoteCharacter.WeaponUpdate(wName, RemoteCharacter.m_SheidName);
                    }
                }

                RemoteController?.ApplyNetworkState(new Vector3(x, y, z), Vector2.zero, animName, facingRight, weaponOn);

                // Reveal fog of war for remote character position
                RemoteCharacter?.m_FogCircle?.UpdateCall();
            }
            else if (type == PacketType.PlayerAction)
            {
                PlayerActionType action = (PlayerActionType)reader.GetByte();
                RemoteController?.PlayAction(action);
            }
            else if (type == PacketType.PlayerRevived)
            {
                int targetId = reader.GetInt();
                if (LocalCharacter != null && LocalCharacter.m_ID == targetId)
                {
                    ReviveCharacter(LocalCharacter);
                }
                else if (RemoteCharacter != null && RemoteCharacter.m_ID == targetId)
                {
                    ReviveCharacter(RemoteCharacter);
                }
            }
            else if (type == PacketType.SceneTransition)
            {
                // BUG FIX #3: Receive remote character position after scene transition
                float x = reader.GetFloat();
                float y = reader.GetFloat();
                float z = reader.GetFloat();
                _lastRemotePosition = new Vector3(x, y, z);

                if (RemoteCharacter != null)
                {
                    RemoteCharacter.transform.position = _lastRemotePosition;
                    Plugin.Log.LogInfo($"[QueenSyncManager] Restored remote character position after scene transition: {_lastRemotePosition}");
                }
            }
            else if (type == PacketType.PlayerHealthUpdate)
            {
                // Immediate HP synchronization
                int characterId = reader.GetInt();
                float newHp = reader.GetFloat();
                float maxHp = reader.GetFloat();

                if (LocalCharacter != null && LocalCharacter.m_ID == characterId)
                {
                    float prevHp = LocalCharacter.m_CurHP;
                    LocalCharacter.m_CurHP = newHp;
                    LocalCharacter.m_UnitHP?.UpdateHp(maxHp > 0 ? newHp / maxHp : 1f);
                    if (newHp < prevHp)
                    {
                        LocalCharacter.HpShowUp();
                        LocalCharacter.HitBlink(1);
                    }
                    if (newHp <= 0)
                    {
                        SetDowned(LocalCharacter);
                    }
                    Plugin.Log.LogInfo($"[QueenSyncManager] Local character HP updated from network: {newHp}/{maxHp}");
                }
                else if (RemoteCharacter != null && RemoteCharacter.m_ID == characterId)
                {
                    float prevHp = RemoteCharacter.m_CurHP;
                    RemoteCharacter.m_CurHP = newHp;
                    RemoteCharacter.m_UnitHP?.UpdateHp(maxHp > 0 ? newHp / maxHp : 1f);
                    if (newHp < prevHp)
                    {
                        RemoteCharacter.HpShowUp();
                        RemoteCharacter.HitBlink(1);
                    }
                    if (newHp <= 0)
                    {
                        SetDowned(RemoteCharacter);
                    }
                    Plugin.Log.LogInfo($"[QueenSyncManager] Remote character HP updated: {newHp}/{maxHp}");
                }
            }
        }

        private void HandleConnected()
        {
            SetupQueens();
            if (CoopNetManager.Instance != null && CoopNetManager.Instance.IsClient)
            {
                SaveLoadConfigure.USE_AUTOSAVE = false;
                Plugin.Log.LogInfo("[QueenSyncManager] Client connected: Auto-save disabled to protect local saves.");
            }
        }

        private void HandleDisconnected()
        {
            Plugin.Log.LogInfo("[QueenSyncManager] Disconnected. Cleaning up characters to prevent duplication on reconnect.");

            // BUG FIX #1: Destroy Player2Worker GameObject to prevent duplication on reconnect
            if (Player2Worker != null && Player2Worker.gameObject != null)
            {
                Plugin.Log.LogInfo("[QueenSyncManager] Destroying Player2Worker GameObject.");
                if (GameMgr.Instance?._T_UnitMgr != null)
                {
                    if (GameMgr.Instance._T_UnitMgr.m_Queen == Player2Worker && HostQueen != null)
                    {
                        GameMgr.Instance._T_UnitMgr.m_Queen = HostQueen;
                    }
                    GameMgr.Instance._T_UnitMgr.List_AllTeam?.Remove(Player2Worker);
                }
                Destroy(Player2Worker.gameObject);
            }

            // Clean up RemoteController component
            if (RemoteController != null)
            {
                Destroy(RemoteController);
            }

            // Clear all references
            LocalCharacter = null;
            RemoteCharacter = null;
            RemoteController = null;
            Player2Worker = null;
            _originalHostQueen = null;
            IsLocalDowned = false;
            IsRemoteDowned = false;
            SaveLoadConfigure.USE_AUTOSAVE = true;
        }

        private void OnGUI()
        {
            if (CoopNetManager.Instance == null || !CoopNetManager.Instance.IsConnected) return;

            Camera cam = Camera.main;
            if (cam == null) return;

            GUIStyle labelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };

            // Draw Host Queen Tag
            if (HostQueen != null && HostQueen.gameObject.activeSelf)
            {
                string text = CoopNetManager.Instance.IsHost ? "[Host] Queen (You)" : "[Host] Queen";
                Color tagColor = (RemoteCharacter == HostQueen && IsRemoteDowned) ? Color.red : new Color(1.0f, 0.85f, 0.2f);
                DrawCharacterWorldTag(cam, HostQueen, (RemoteCharacter == HostQueen && IsRemoteDowned) ? $"{text} [DOWNED - HOLD E]" : text, tagColor, labelStyle);
            }

            // Draw Player 2 Worker Rat Tag
            if (Player2Worker != null && Player2Worker.gameObject.activeSelf)
            {
                string text = CoopNetManager.Instance.IsClient ? "[Player 2] Worker Rat (You)" : "[Player 2] Worker Rat";
                Color tagColor = (RemoteCharacter == Player2Worker && IsRemoteDowned) ? Color.red : new Color(0.2f, 0.95f, 0.5f);
                DrawCharacterWorldTag(cam, Player2Worker, (RemoteCharacter == Player2Worker && IsRemoteDowned) ? $"{text} [DOWNED - HOLD E]" : text, tagColor, labelStyle);

                // Draw Revive Bar if Remote Character is downed and local is channeling
                if (IsRemoteDowned && _reviveProgress > 0f && RemoteCharacter != null)
                {
                    Vector3 screenPos = cam.WorldToScreenPoint(RemoteCharacter.transform.position + new Vector3(0, 1.6f, 0));
                    if (screenPos.z > 0)
                    {
                        float barWidth = 140f;
                        float barHeight = 12f;
                        float x = screenPos.x - (barWidth / 2);
                        float y = Screen.height - screenPos.y;

                        GUI.Box(new Rect(x, y, barWidth, barHeight), GUIContent.none);
                        float progressWidth = (barWidth - 4) * (_reviveProgress / REVIVE_DURATION);
                        Color oldColor = GUI.color;
                        GUI.color = Color.green;
                        GUI.DrawTexture(new Rect(x + 2, y + 2, progressWidth, barHeight - 4), Texture2D.whiteTexture);
                        GUI.color = oldColor;
                    }
                }
            }
        }

        private void DrawCharacterWorldTag(Camera cam, T_Queen character, string text, Color color, GUIStyle style)
        {
            Vector3 screenPos = cam.WorldToScreenPoint(character.transform.position + new Vector3(0, 2.0f, 0));
            if (screenPos.z > 0)
            {
                float x = screenPos.x - 100;
                float y = Screen.height - screenPos.y;
                Color oldColor = GUI.contentColor;
                GUI.contentColor = color;
                GUI.Label(new Rect(x, y, 200, 25), text, style);
                GUI.contentColor = oldColor;
            }
        }

        private void OnDestroy()
        {
            if (CoopNetManager.Instance != null)
            {
                CoopNetManager.Instance.OnPacketReceivedEvent -= HandlePacket;
                CoopNetManager.Instance.OnConnectedEvent -= HandleConnected;
                CoopNetManager.Instance.OnDisconnectedEvent -= HandleDisconnected;
            }

            // BUG FIX #3: Unsubscribe from scene events
            UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
            UnityEngine.SceneManagement.SceneManager.sceneUnloaded -= OnSceneUnloaded;
        }

        // BUG FIX #3: Save positions before scene unload
        private void OnSceneUnloaded(UnityEngine.SceneManagement.Scene scene)
        {
            if (CoopNetManager.Instance == null || !CoopNetManager.Instance.IsConnected) return;

            // Save current positions before scene unload
            if (LocalCharacter != null)
            {
                _lastLocalPosition = LocalCharacter.transform.position;
                Plugin.Log.LogInfo($"[QueenSyncManager] Saved local position before scene unload: {_lastLocalPosition}");
            }
            if (RemoteCharacter != null)
            {
                _lastRemotePosition = RemoteCharacter.transform.position;
            }
            _pendingSceneLoad = true;
        }

        // BUG FIX #3: Restore positions after scene load
        private void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
        {
            if (!_pendingSceneLoad || CoopNetManager.Instance == null || !CoopNetManager.Instance.IsConnected) return;

            // Ignore intermediate LoadingScene or TitleScene; only restore in GameScene
            if (scene.name != SceneConfigure.SCENE_GAME) return;

            Plugin.Log.LogInfo($"[QueenSyncManager] Scene loaded: {scene.name}. Restoring character positions...");

            // Give the game a frame to initialize before restoring positions
            StartCoroutine(RestorePositionsAfterSceneLoad());
        }

        private System.Collections.IEnumerator RestorePositionsAfterSceneLoad()
        {
            // Wait using unscaled realtime (since Time.timeScale may be 0f during pause)
            yield return new WaitForSecondsRealtime(0.5f);

            while (SaveTransferManager.IsWorldLoading)
            {
                yield return new WaitForSecondsRealtime(0.2f);
            }

            // Re-setup queens after scene load
            SetupQueens();

            // Restore saved positions
            if (LocalCharacter != null && _lastLocalPosition != Vector3.zero)
            {
                LocalCharacter.transform.position = _lastLocalPosition;
                Plugin.Log.LogInfo($"[QueenSyncManager] Restored local character position: {_lastLocalPosition}");

                // Notify camera
                if (GameMgr.Instance?._CamMgr != null)
                {
                    GameMgr.Instance._CamMgr.Tf_Update(_lastLocalPosition);
                }

                // Send position to remote player
                if (CoopNetManager.Instance != null && CoopNetManager.Instance.IsConnected)
                {
                    NetDataWriter writer = new NetDataWriter();
                    writer.Put(_lastLocalPosition.x);
                    writer.Put(_lastLocalPosition.y);
                    writer.Put(_lastLocalPosition.z);
                    CoopNetManager.Instance.SendPacket(PacketType.SceneTransition, writer, DeliveryMethod.ReliableOrdered);
                }
            }

            _pendingSceneLoad = false;
        }
    }
}
