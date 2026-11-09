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

        [HarmonyPatch(typeof(GameUnit), nameof(GameUnit.BeAttacked), new Type[] { typeof(float), typeof(Unit_Attacekd_Tag), typeof(int) })]
        [HarmonyPrefix]
        public static void GameUnit_BeAttacked_Prefix(GameUnit __instance, float dmg, Unit_Attacekd_Tag _tag, int _id)
        {
            if (_isProcessingBeAttacked) return;
            if (CoopNetManager.Instance == null || !CoopNetManager.Instance.IsConnected) return;

            try
            {
                _isProcessingBeAttacked = true;
                HandleUnitDamage(__instance, dmg, _tag, _id);
            }
            finally
            {
                _isProcessingBeAttacked = false;
            }
        }

        [HarmonyPatch(typeof(GameEnemy), nameof(GameEnemy.BeAttacked), new Type[] { typeof(float), typeof(Unit_Attacekd_Tag), typeof(int) })]
        [HarmonyPrefix]
        public static void GameEnemy_BeAttacked_Prefix(GameEnemy __instance, float dmg, Unit_Attacekd_Tag _tag, int _id)
        {
            if (_isProcessingBeAttacked) return;
            if (CoopNetManager.Instance == null || !CoopNetManager.Instance.IsConnected) return;

            try
            {
                _isProcessingBeAttacked = true;
                HandleUnitDamage(__instance, dmg, _tag, _id);
            }
            finally
            {
                _isProcessingBeAttacked = false;
            }
        }

        [HarmonyPatch(typeof(T_Citizen), nameof(T_Citizen.BeAttacked), new Type[] { typeof(float), typeof(Unit_Attacekd_Tag), typeof(int) })]
        [HarmonyPrefix]
        public static void TCitizen_BeAttacked_Prefix(T_Citizen __instance, float dmg, Unit_Attacekd_Tag _tag, int _id)
        {
            if (_isProcessingBeAttacked) return;
            if (CoopNetManager.Instance == null || !CoopNetManager.Instance.IsConnected) return;

            try
            {
                _isProcessingBeAttacked = true;
                HandleUnitDamage(__instance, dmg, _tag, _id);
            }
            finally
            {
                _isProcessingBeAttacked = false;
            }
        }

        private static void HandleUnitDamage(GameUnit instance, float dmg, Unit_Attacekd_Tag tag, int id)
        {
            if (instance == null || dmg >= 0f) return;

            // 1. Client: Queen attacks an enemy/citizen -> send damage to Host
            if (CoopNetManager.Instance.IsClient)
            {
                if (QueenSyncManager.Instance != null && instance == QueenSyncManager.Instance.LocalCharacter)
                    return; // Ignore local player taking damage

                Vector2 targetPos = instance.Tf != null ? (Vector2)instance.Tf.position : Vector2.zero;
                CombatSyncManager.Instance?.SendDamageRequest(DamageTargetKind.Unit, instance.m_ID, -dmg, (byte)tag, id, targetPos);
            }
            // 2. Host: Player2Worker (Client's Queen) takes damage on Host -> immediately update Client
            else if (CoopNetManager.Instance.IsHost)
            {
                if (QueenSyncManager.Instance != null && instance == QueenSyncManager.Instance.Player2Worker)
                {
                    float projectedHp = Mathf.Max(0f, instance.m_CurHP + dmg);
                    QueenSyncManager.Instance.SendPlayerHealthUpdate(instance.m_ID, projectedHp, instance.GetMaxHP());
                }
            }
        }

        [HarmonyPatch(typeof(AnimalBody), nameof(AnimalBody.BeAttacked), new Type[] { typeof(GameUnit), typeof(float) })]
        [HarmonyPrefix]
        public static void AnimalBody_BeAttacked_Unit_Prefix(AnimalBody __instance, GameUnit _unit, float _dmg)
        {
            if (_isProcessingBeAttacked) return;
            if (CoopNetManager.Instance == null || !CoopNetManager.Instance.IsConnected || !CoopNetManager.Instance.IsClient) return;
            if (__instance == null || _dmg >= 0f) return;

            try
            {
                _isProcessingBeAttacked = true;
                Vector2 targetPos = __instance.Tf != null ? (Vector2)__instance.Tf.position : Vector2.zero;
                CombatSyncManager.Instance?.SendDamageRequest(DamageTargetKind.Animal, __instance.m_ID, -_dmg, (byte)Unit_Attacekd_Tag.Queen, 0, targetPos);
            }
            finally
            {
                _isProcessingBeAttacked = false;
            }
        }

        [HarmonyPatch(typeof(AnimalBody), nameof(AnimalBody.BeAttacked), new Type[] { typeof(float), typeof(Unit_Attacekd_Tag) })]
        [HarmonyPrefix]
        public static void AnimalBody_BeAttacked_Value_Prefix(AnimalBody __instance, float value, Unit_Attacekd_Tag _tag)
        {
            if (_isProcessingBeAttacked) return;
            if (CoopNetManager.Instance == null || !CoopNetManager.Instance.IsConnected || !CoopNetManager.Instance.IsClient) return;
            if (__instance == null || value >= 0f) return;

            try
            {
                _isProcessingBeAttacked = true;
                CombatSyncManager.Instance?.SendDamageRequest(DamageTargetKind.Animal, __instance.m_ID, -value, (byte)_tag, 0);
            }
            finally
            {
                _isProcessingBeAttacked = false;
            }
        }

        [HarmonyPatch(typeof(Building), nameof(Building.BeAttacked), new Type[] { typeof(float), typeof(Vector2), typeof(Unit_Attacekd_Tag) })]
        [HarmonyPrefix]
        public static void Building_BeAttacked_Prefix(Building __instance, float dmg, Vector2 _hitmanPos, Unit_Attacekd_Tag _tag)
        {
            if (_isProcessingBeAttacked) return;
            if (CoopNetManager.Instance == null || !CoopNetManager.Instance.IsConnected || !CoopNetManager.Instance.IsClient) return;
            if (__instance == null || dmg >= 0f) return;

            try
            {
                _isProcessingBeAttacked = true;
                CombatSyncManager.Instance?.SendDamageRequest(DamageTargetKind.Building, __instance.m_ID, -dmg, (byte)_tag, 0);
            }
            finally
            {
                _isProcessingBeAttacked = false;
            }
        }

        [HarmonyPatch(typeof(MapObj), nameof(MapObj.BeAttacked), new Type[] { typeof(float) })]
        [HarmonyPrefix]
        public static void MapObj_BeAttacked_Prefix(MapObj __instance, float dmg)
        {
            if (_isProcessingBeAttacked) return;
            if (CoopNetManager.Instance == null || !CoopNetManager.Instance.IsConnected || !CoopNetManager.Instance.IsClient) return;
            if (__instance == null || dmg >= 0f) return;

            try
            {
                _isProcessingBeAttacked = true;
                CombatSyncManager.Instance?.SendDamageRequest(DamageTargetKind.MapObj, __instance.m_ID, -dmg, (byte)Unit_Attacekd_Tag.None, 0);
            }
            finally
            {
                _isProcessingBeAttacked = false;
            }
        }
    }
}
