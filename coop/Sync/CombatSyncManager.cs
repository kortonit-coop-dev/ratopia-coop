using System;
using System.Collections.Generic;
using LiteNetLib;
using LiteNetLib.Utils;
using RatopiaCoop.Network;
using RatopiaUnitBrain.Registry;
using UnityEngine;

namespace RatopiaCoop.Sync
{
    public class CombatSyncManager : MonoBehaviour
    {
        public static CombatSyncManager Instance { get; private set; } = null!;

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

        private void OnDestroy()
        {
            if (CoopNetManager.Instance != null)
            {
                CoopNetManager.Instance.OnPacketReceivedEvent -= HandlePacket;
            }
        }

        public void SendDamageRequest(DamageTargetKind targetKind, int targetId, float damage, byte tag, int crit, Vector2 targetPos = default)
        {
            if (CoopNetManager.Instance == null || !CoopNetManager.Instance.IsConnected || !CoopNetManager.Instance.IsClient)
                return;

            NetDataWriter writer = new NetDataWriter();
            writer.Put((byte)targetKind);
            writer.Put(targetId);
            writer.Put(damage);
            writer.Put(tag);
            writer.Put(crit);
            writer.Put(QueenSyncManager.Instance?.LocalCharacter?.m_ID ?? 778);
            writer.Put(targetPos.x);
            writer.Put(targetPos.y);

            Plugin.Log.LogInfo($"[CombatSyncManager] Client requested {damage} damage to {targetKind} ID {targetId} at {targetPos}");
            CoopNetManager.Instance.SendPacket(PacketType.RequestDamageEntity, writer, DeliveryMethod.ReliableOrdered);
        }

        public void HandlePacket(PacketType type, NetDataReader reader)
        {
            if (type != PacketType.RequestDamageEntity) return;

            // Damage validation and application is strictly authoritative on the Host
            if (CoopNetManager.Instance == null || !CoopNetManager.Instance.IsHost) return;

            try
            {
                DamageTargetKind targetKind = (DamageTargetKind)reader.GetByte();
                int targetId = reader.GetInt();
                float damage = reader.GetFloat();
                byte tag = reader.GetByte();
                int crit = reader.GetInt();
                int attackerId = reader.GetInt();
                Vector2 targetPos = reader.AvailableBytes >= 8 ? new Vector2(reader.GetFloat(), reader.GetFloat()) : Vector2.zero;

                var clientWorker = QueenSyncManager.Instance?.Player2Worker;

                switch (targetKind)
                {
                    case DamageTargetKind.Unit:
                    {
                        GameUnit? target = null;
                        var allUnits = UnitRegistry.Instance.GetAllUnits();
                        for (int i = 0; i < allUnits.Count; i++)
                        {
                            var u = allUnits[i];
                            if (u != null && u.m_ID == targetId)
                            {
                                target = u;
                                break;
                            }
                        }

                        if (target == null && GameMgr.Instance != null && GameMgr.Instance._T_UnitMgr != null)
                        {
                            target = GameMgr.Instance._T_UnitMgr.List_AllEnemy.Find(e => e != null && e.m_ID == targetId)
                                  ?? (GameUnit)GameMgr.Instance._T_UnitMgr.List_Citizen.Find(c => c != null && c.m_ID == targetId);
                        }

                        // Proximity fallback if ID did not match (e.g. un-synchronized enemy IDs)
                        if (target == null && targetPos != Vector2.zero && GameMgr.Instance != null && GameMgr.Instance._T_UnitMgr != null)
                        {
                            float closestDist = 3.5f;
                            var enemies = GameMgr.Instance._T_UnitMgr.List_AllEnemy;
                            for (int i = 0; i < enemies.Count; i++)
                            {
                                var e = enemies[i];
                                if (e != null && e.m_CharState != CharState.Death && e.Tf != null)
                                {
                                    float d = Vector2.Distance(e.Tf.position, targetPos);
                                    if (d < closestDist)
                                    {
                                        closestDist = d;
                                        target = e;
                                    }
                                }
                            }
                        }

                        if (target != null && target.m_CharState != CharState.Death)
                        {
                            target.BeAttacked(0f - damage, (Unit_Attacekd_Tag)tag, crit);
                            Plugin.Log.LogInfo($"[CombatSyncManager] Host applied {damage} damage to enemy {target.name} (ID: {target.m_ID}, HP remaining: {target.m_CurHP})");

                            if (clientWorker != null)
                            {
                                target.m_TargetUnit = clientWorker;
                                clientWorker.KnockCheck(target);

                                // Propagate client Queen weapon buffs on Host
                                if (clientWorker.GetTotalValue(Res_Ability.BloodDrain) != 0f)
                                {
                                    clientWorker.Heal(clientWorker.GetTotalValue(Res_Ability.BloodDrain), true);
                                    clientWorker.HpShowUp();
                                    QueenSyncManager.Instance?.SendPlayerHealthUpdate(clientWorker.m_ID, clientWorker.m_CurHP, clientWorker.GetMaxHP());
                                }
                                if (clientWorker.GetTotalValue(Res_Ability.Poisoned) != 0f)
                                {
                                    target.m_Buff?.BuffRefSet(C_Buff.Poison, "Poisoned", C_Buff_Category.None, clientWorker.GetTotalValue(Res_Ability.Poisoned), 2, false);
                                }
                                if (clientWorker.GetTotalValue(Res_Ability.BurnAtk) != 0f)
                                {
                                    Helpers.MakeAtkByFire(target, clientWorker, clientWorker.GetTotalValue(Res_Ability.BurnAtk));
                                }
                                if (clientWorker.GetTotalValue(Res_Ability.ElecAtk) != 0f && GameMgr.Instance?._PoolMgr?.Pool_ChainLightning != null)
                                {
                                    GameMgr.Instance._PoolMgr.Pool_ChainLightning.GetNextObj()?.GetComponent<ChainLightning>()?.SetChainLight(new List<GameUnit>(), target, target.Tf.position, (int)clientWorker.GetTotalValue(Res_Ability.ElecAtk));
                                }
                            }

                            target.HpShowUp();
                            target.HitBlink(1);
                        }
                        else
                        {
                            Plugin.Log.LogWarning($"[CombatSyncManager] Target enemy not found on Host (ID: {targetId}, Pos: {targetPos})");
                        }
                        break;
                    }

                    case DamageTargetKind.Animal:
                    {
                        AnimalBody? targetAnimal = null;
                        if (UnitRegistry.Instance.TryGetAnimal(targetId, out var regAnimal) && regAnimal != null)
                        {
                            targetAnimal = regAnimal;
                        }
                        else if (GameMgr.Instance != null && GameMgr.Instance._AnimalMgr != null)
                        {
                            targetAnimal = GameMgr.Instance._AnimalMgr.List_Animal.Find(a => a != null && a.m_ID == targetId);
                        }

                        if (targetAnimal != null && targetAnimal.m_State != AnimalState.Death)
                        {
                            targetAnimal.BeAttacked(clientWorker, 0f - damage);
                            targetAnimal.HpShowUp();
                            targetAnimal.HitBlink(1);
                        }
                        break;
                    }

                    case DamageTargetKind.Building:
                    {
                        Building? targetBuilding = null;
                        if (GameMgr.Instance != null && GameMgr.Instance._BuildingMgr != null)
                        {
                            targetBuilding = GameMgr.Instance._BuildingMgr.List_Building.Find(b => b != null && b.m_ID == targetId);
                        }

                        if (targetBuilding != null && targetBuilding.m_BuildState != BuildState.NeedRepair)
                        {
                            Vector2 hitPos = clientWorker != null && clientWorker.Tf != null ? (Vector2)clientWorker.Tf.position : Vector2.zero;
                            targetBuilding.BeAttacked(0f - damage, hitPos, (Unit_Attacekd_Tag)tag);
                        }
                        break;
                    }

                    case DamageTargetKind.MapObj:
                    {
                        MapObj? targetMapObj = null;
                        if (GameMgr.Instance != null && GameMgr.Instance._MapObjMgr != null)
                        {
                            targetMapObj = GameMgr.Instance._MapObjMgr.List_MapObj.Find(m => m != null && m.m_ID == targetId);
                        }

                        if (targetMapObj != null && targetMapObj.m_CurHp > 0f)
                        {
                            targetMapObj.BeAttacked(0f - damage);
                        }
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[CombatSyncManager] HandlePacket error: {ex.Message}");
            }
        }
    }
}
