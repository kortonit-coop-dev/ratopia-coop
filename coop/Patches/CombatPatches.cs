using System;
using HarmonyLib;
using RatopiaCoop.Network;
using RatopiaCoop.Sync;
using UnityEngine;

namespace RatopiaCoop.Patches
{
    [HarmonyPatch]
    public static class CombatPatches
    {
        [ThreadStatic]
        private static bool _isProcessingBeAttacked;

        [ThreadStatic]
        internal static bool IsLocalPlayerAttacking;

        private static bool IsConnectedCoop => CoopNetManager.Instance != null && CoopNetManager.Instance.IsConnected;

        [HarmonyPatch(typeof(Bow_Arrow), "OnTriggerEnter2D", new Type[] { typeof(Collider2D) })]
        [HarmonyPrefix]
        public static bool Bow_Arrow_OnTriggerEnter2D_Prefix(Bow_Arrow __instance, out bool __state)
        {
            __state = IsLocalPlayerAttacking;
            if (!IsConnectedCoop || __instance == null) return true;

            var master = Traverse.Create(__instance).Field<GameUnit>("m_Master").Value;
            if (master != null && master == QueenSyncManager.Instance?.RemoteCharacter)
            {
                return false; // Remote puppet projectiles are visual only
            }

            if (master != null && master == QueenSyncManager.Instance?.LocalCharacter)
            {
                IsLocalPlayerAttacking = true;
            }
            return true;
        }

        [HarmonyPatch(typeof(Bow_Arrow), "OnTriggerEnter2D", new Type[] { typeof(Collider2D) })]
        [HarmonyPostfix]
        public static void Bow_Arrow_OnTriggerEnter2D_Postfix(bool __state)
        {
            IsLocalPlayerAttacking = __state;
        }

        [HarmonyPatch(typeof(EnergyBolt), "OnTriggerEnter2D", new Type[] { typeof(Collider2D) })]
        [HarmonyPrefix]
        public static bool EnergyBolt_OnTriggerEnter2D_Prefix(EnergyBolt __instance, out bool __state)
        {
            __state = IsLocalPlayerAttacking;
            if (!IsConnectedCoop || __instance == null) return true;

            var master = Traverse.Create(__instance).Field<GameUnit>("m_Master").Value;
            if (master != null && master == QueenSyncManager.Instance?.RemoteCharacter)
            {
                return false; // Remote puppet bolts are visual only
            }

            if (master != null && master == QueenSyncManager.Instance?.LocalCharacter)
            {
                IsLocalPlayerAttacking = true;
            }
            return true;
        }

        [HarmonyPatch(typeof(EnergyBolt), "OnTriggerEnter2D", new Type[] { typeof(Collider2D) })]
        [HarmonyPostfix]
        public static void EnergyBolt_OnTriggerEnter2D_Postfix(bool __state)
        {
            IsLocalPlayerAttacking = __state;
        }

        [HarmonyPatch(typeof(T_Queen), nameof(T_Queen.BeAttacked), new Type[] { typeof(float), typeof(Unit_Attacekd_Tag), typeof(int) })]
        [HarmonyPrefix]
        public static bool TQueen_BeAttacked_Prefix(T_Queen __instance)
        {
            if (!IsConnectedCoop || __instance == null) return true;

            var qm = QueenSyncManager.Instance;
            if (qm == null) return true;

            if (CoopNetManager.Instance.IsClient && __instance == qm.RemoteCharacter)
                return false; // Host owns HostQueen HP

            if (__instance == qm.LocalCharacter && qm.IsLocalDowned)
                return false;

            if (__instance == qm.RemoteCharacter && qm.IsRemoteDowned)
                return false;

            return true;
        }

        [HarmonyPatch(typeof(T_Queen), nameof(T_Queen.BeAttacked), new Type[] { typeof(float), typeof(Unit_Attacekd_Tag), typeof(int) })]
        [HarmonyPostfix]
        public static void TQueen_BeAttacked_Postfix(T_Queen __instance)
        {
            if (!IsConnectedCoop || __instance == null) return;

            var qm = QueenSyncManager.Instance;
            if (qm == null) return;

            if (__instance == qm.LocalCharacter || (CoopNetManager.Instance.IsHost && __instance == qm.Player2Worker))
            {
                qm.SendPlayerHealthUpdate(__instance.m_ID, __instance.m_CurHP, __instance.GetMaxHP());
            }
        }

        [HarmonyPatch(typeof(GameUnit), nameof(GameUnit.BeAttacked), new Type[] { typeof(float), typeof(Unit_Attacekd_Tag), typeof(int) })]
        [HarmonyPrefix]
        public static bool GameUnit_BeAttacked_Prefix(GameUnit __instance, float dmg, Unit_Attacekd_Tag _tag, int _id)
        {
            if (_isProcessingBeAttacked) return true;
            if (!IsConnectedCoop) return true;

            try
            {
                _isProcessingBeAttacked = true;
                return HandleUnitDamage(__instance, dmg, _tag, _id);
            }
            finally
            {
                _isProcessingBeAttacked = false;
            }
        }

        [HarmonyPatch(typeof(GameEnemy), nameof(GameEnemy.BeAttacked), new Type[] { typeof(float), typeof(Unit_Attacekd_Tag), typeof(int) })]
        [HarmonyPrefix]
        public static bool GameEnemy_BeAttacked_Prefix(GameEnemy __instance, float dmg, Unit_Attacekd_Tag _tag, int _id)
        {
            if (_isProcessingBeAttacked) return true;
            if (!IsConnectedCoop) return true;

            try
            {
                _isProcessingBeAttacked = true;
                return HandleUnitDamage(__instance, dmg, _tag, _id);
            }
            finally
            {
                _isProcessingBeAttacked = false;
            }
        }

        [HarmonyPatch(typeof(T_Citizen), nameof(T_Citizen.BeAttacked), new Type[] { typeof(float), typeof(Unit_Attacekd_Tag), typeof(int) })]
        [HarmonyPrefix]
        public static bool TCitizen_BeAttacked_Prefix(T_Citizen __instance, float dmg, Unit_Attacekd_Tag _tag, int _id)
        {
            if (_isProcessingBeAttacked) return true;
            if (!IsConnectedCoop) return true;

            try
            {
                _isProcessingBeAttacked = true;
                return HandleUnitDamage(__instance, dmg, _tag, _id);
            }
            finally
            {
                _isProcessingBeAttacked = false;
            }
        }

        private static bool HandleUnitDamage(GameUnit instance, float dmg, Unit_Attacekd_Tag tag, int id)
        {
            if (instance == null || dmg >= 0f) return true;

            // 1. Client: Only forward actual local player attacks (never environmental/AI ticks or remote puppet attacks)
            if (CoopNetManager.Instance.IsClient)
            {
                var qm = QueenSyncManager.Instance;
                if (qm != null && (instance == qm.LocalCharacter || instance == qm.RemoteCharacter))
                    return instance == qm.LocalCharacter; // Host controls RemoteCharacter

                if (!IsLocalPlayerAttacking && tag != Unit_Attacekd_Tag.Queen)
                    return true;

                Vector2 targetPos = instance.Tf != null ? (Vector2)instance.Tf.position : Vector2.zero;
                CombatSyncManager.Instance?.SendDamageRequest(DamageTargetKind.Unit, instance.m_ID, -dmg, (byte)tag, id, targetPos);

                // Show immediate visual hit feedback on Client while Host applies authoritative HP & death
                if (instance.Tf != null && GameMgr.Instance?._PoolMgr?.Pool_DmgEffect != null)
                {
                    GameMgr.Instance._PoolMgr.Pool_DmgEffect.GetNextObj()?.GetComponent<DmgEffect>()?.SetDmgEffect(-(int)dmg, new Vector3(0f, 0.8f, 0f), instance.Tf);
                    instance.HitBlink(1);
                }
                return false;
            }
            // 2. Host: Player2Worker (Client's Queen) takes damage on Host -> immediately update Client
            else if (CoopNetManager.Instance.IsHost)
            {
                if (QueenSyncManager.Instance != null && instance == QueenSyncManager.Instance.Player2Worker)
                {
                    if (QueenSyncManager.Instance.IsRemoteDowned) return false;
                    float projectedHp = Mathf.Max(0f, instance.m_CurHP + dmg);
                    QueenSyncManager.Instance.SendPlayerHealthUpdate(instance.m_ID, projectedHp, instance.GetMaxHP());
                }
            }

            return true;
        }

        [HarmonyPatch(typeof(AnimalBody), nameof(AnimalBody.BeAttacked), new Type[] { typeof(GameUnit), typeof(float) })]
        [HarmonyPrefix]
        public static bool AnimalBody_BeAttacked_Unit_Prefix(AnimalBody __instance, GameUnit _unit, float _dmg)
        {
            if (_isProcessingBeAttacked) return true;
            if (!IsConnectedCoop || !CoopNetManager.Instance.IsClient) return true;
            if (__instance == null || _dmg >= 0f) return true;
            if (_unit == null || _unit != QueenSyncManager.Instance?.LocalCharacter) return true;

            try
            {
                _isProcessingBeAttacked = true;
                Vector2 targetPos = __instance.Tf != null ? (Vector2)__instance.Tf.position : Vector2.zero;
                CombatSyncManager.Instance?.SendDamageRequest(DamageTargetKind.Animal, __instance.m_ID, -_dmg, (byte)Unit_Attacekd_Tag.Queen, 0, targetPos);
                if (__instance.Tf != null && GameMgr.Instance?._PoolMgr?.Pool_DmgEffect != null)
                {
                    GameMgr.Instance._PoolMgr.Pool_DmgEffect.GetNextObj()?.GetComponent<DmgEffect>()?.SetDmgEffect(-(int)_dmg, new Vector3(0f, 0.8f, 0f), __instance.Tf);
                    __instance.HitBlink(1);
                }
                return false;
            }
            finally
            {
                _isProcessingBeAttacked = false;
            }
        }

        [HarmonyPatch(typeof(AnimalBody), nameof(AnimalBody.BeAttacked), new Type[] { typeof(float), typeof(Unit_Attacekd_Tag) })]
        [HarmonyPrefix]
        public static bool AnimalBody_BeAttacked_Value_Prefix(AnimalBody __instance, float value, Unit_Attacekd_Tag _tag)
        {
            if (_isProcessingBeAttacked) return true;
            if (!IsConnectedCoop || !CoopNetManager.Instance.IsClient) return true;
            if (__instance == null || value >= 0f) return true;
            if (!IsLocalPlayerAttacking) return true;

            try
            {
                _isProcessingBeAttacked = true;
                Vector2 targetPos = __instance.Tf != null ? (Vector2)__instance.Tf.position : Vector2.zero;
                CombatSyncManager.Instance?.SendDamageRequest(DamageTargetKind.Animal, __instance.m_ID, -value, (byte)_tag, 0, targetPos);
                if (__instance.Tf != null && GameMgr.Instance?._PoolMgr?.Pool_DmgEffect != null)
                {
                    GameMgr.Instance._PoolMgr.Pool_DmgEffect.GetNextObj()?.GetComponent<DmgEffect>()?.SetDmgEffect(-(int)value, new Vector3(0f, 0.8f, 0f), __instance.Tf);
                    __instance.HitBlink(1);
                }
                return false;
            }
            finally
            {
                _isProcessingBeAttacked = false;
            }
        }

        [HarmonyPatch(typeof(Building), nameof(Building.BeAttacked), new Type[] { typeof(float), typeof(Vector2), typeof(Unit_Attacekd_Tag) })]
        [HarmonyPrefix]
        public static bool Building_BeAttacked_Prefix(Building __instance, float dmg, Vector2 _hitmanPos, Unit_Attacekd_Tag _tag)
        {
            if (_isProcessingBeAttacked) return true;
            if (!IsConnectedCoop || !CoopNetManager.Instance.IsClient) return true;
            if (__instance == null || dmg >= 0f) return true;
            if (!IsLocalPlayerAttacking || _tag == Unit_Attacekd_Tag.Durability) return true;
            if (!__instance.IsEnemyBuilding && __instance.m_Info?.Name != BuildingName.EnemyNexus) return true;

            try
            {
                _isProcessingBeAttacked = true;
                Vector2 targetPos = __instance.Tf != null ? (Vector2)__instance.Tf.position : Vector2.zero;
                CombatSyncManager.Instance?.SendDamageRequest(DamageTargetKind.Building, __instance.m_ID, -dmg, (byte)_tag, 0, targetPos);
                return false;
            }
            finally
            {
                _isProcessingBeAttacked = false;
            }
        }

        [HarmonyPatch(typeof(MapObj), nameof(MapObj.BeAttacked), new Type[] { typeof(float) })]
        [HarmonyPrefix]
        public static bool MapObj_BeAttacked_Prefix(MapObj __instance, float dmg)
        {
            if (_isProcessingBeAttacked) return true;
            if (!IsConnectedCoop || !CoopNetManager.Instance.IsClient) return true;
            if (__instance == null || dmg >= 0f) return true;
            if (!IsLocalPlayerAttacking) return true;

            try
            {
                _isProcessingBeAttacked = true;
                Vector2 targetPos = __instance.Tf != null ? (Vector2)__instance.Tf.position : Vector2.zero;
                CombatSyncManager.Instance?.SendDamageRequest(DamageTargetKind.MapObj, __instance.m_ID, -dmg, (byte)Unit_Attacekd_Tag.Queen, 0, targetPos);
                return false;
            }
            finally
            {
                _isProcessingBeAttacked = false;
            }
        }
    }
}
