using HarmonyLib;
using LiteNetLib;
using LiteNetLib.Utils;
using RatopiaCoop.Network;
using RatopiaCoop.Patches;
using UnityEngine;

namespace RatopiaCoop.Sync
{
    public class EconomySyncManager : MonoBehaviour
    {
        public static EconomySyncManager Instance { get; private set; } = null!;

        private float _syncInterval = 1.0f; // 1 Hz
        private float _lastSyncTime;

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
            if (CoopNetManager.Instance == null || !CoopNetManager.Instance.IsConnected) return;

            // Host broadcasts kingdom economy to client
            if (CoopNetManager.Instance.IsHost)
            {
                if (Time.time - _lastSyncTime >= _syncInterval)
                {
                    _lastSyncTime = Time.time;
                    SendEconomySync();
                }
            }
        }

        private void SendEconomySync()
        {
            if (GameMgr.Instance == null || GameMgr.Instance._EcoMgr == null) return;

            var eco = GameMgr.Instance._EcoMgr;
            NetDataWriter writer = new NetDataWriter();
            writer.Put(eco.m_Gold);
            writer.Put(eco.m_BoneMedal);

            int prosLevel = 1;
            float prosValue = 0f;
            if (GameMgr.Instance._ProsperityUI != null)
            {
                prosLevel = GameMgr.Instance._ProsperityUI.m_Level;
                prosValue = GameMgr.Instance._ProsperityUI.m_Value;
            }
            writer.Put(prosLevel);
            writer.Put(prosValue);

            int rp = 0;
            if (GameMgr.Instance._ResearchUI != null)
            {
                rp = GameMgr.Instance._ResearchUI.m_Point;
            }
            writer.Put(rp);

            CoopNetManager.Instance.SendPacket(PacketType.EconomySync, writer, DeliveryMethod.ReliableOrdered);
        }

        private void HandlePacket(PacketType type, NetDataReader reader)
        {
            if (type == PacketType.EconomySync && CoopNetManager.Instance.IsClient)
            {
                if (GameMgr.Instance == null || GameMgr.Instance._EcoMgr == null) return;

                try
                {
                    float gold = reader.GetFloat();
                    int boneMedal = reader.GetInt();
                    int prosLevel = reader.GetInt();
                    float prosValue = reader.GetFloat();
                    int rp = reader.GetInt();

                    var eco = GameMgr.Instance._EcoMgr;
                    eco.Country_SetGold(gold);
                    eco.m_BoneMedal = boneMedal;

                    var prosUI = GameMgr.Instance._ProsperityUI;
                    if (prosUI != null && GameMgr.Instance._DB_Mgr != null)
                    {
                        if (prosUI.m_Level != prosLevel)
                        {
                            prosUI.m_Level = prosLevel;
                            prosUI.m_CurPros = GameMgr.Instance._DB_Mgr.List_ProsperityDB?.Find(x => x.Level == prosLevel);
                            Traverse.Create(prosUI).Method("TxtUpdate").GetValue();
                            GameMgr.Instance._EcoMgr?.m_CitizenUI?.CitizenTxtUpdate();
                        }
                        prosUI.m_Value = prosValue;
                        Traverse.Create(prosUI).Method("GaugeUpdate").GetValue();
                        GameMgr.Instance.DiplomaticMgr?.SetHometownProsperityValue((int)prosValue);
                    }

                    if (GameMgr.Instance._ResearchUI != null)
                    {
                        GameMgr.Instance._ResearchUI.m_Point = rp;
                    }
                }
                catch (System.Exception ex)
                {
                    Plugin.Log.LogError($"[EconomySyncManager] Error handling EconomySync: {ex}");
                }
            }
            else if (type == PacketType.ResearchUnlock)
            {
                int techIndex = reader.GetInt();
                ResearchPatches.ApplyRemoteResearch(techIndex);
            }
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
