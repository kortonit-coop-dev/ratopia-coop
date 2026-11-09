using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RatopiaCoop.Network;
using RatopiaCoop.Sync;
using UnityEngine;

namespace RatopiaCoop.Patches
{
    [HarmonyPatch]
    public static class WorldObject_SpawnRefresh_Patch
    {
        [HarmonyTargetMethods]
        public static IEnumerable<MethodBase> TargetMethods()
        {
            var list = new List<MethodBase>();
            try
            {
                var baseMethod = AccessTools.Method(typeof(WorldObject), "SpawnRefresh", new[] { typeof(bool), typeof(int) });
                if (baseMethod != null) list.Add(baseMethod);

                foreach (var type in typeof(WorldObject).Assembly.GetTypes())
                {
                    if (typeof(WorldObject).IsAssignableFrom(type) && type != typeof(WorldObject))
                    {
                        var m = type.GetMethod("SpawnRefresh", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly, null, new[] { typeof(bool), typeof(int) }, null);
                        if (m != null)
                            list.Add(m);
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[WorldObject_SpawnRefresh_Patch] TargetMethods reflection error: {ex}");
            }
            return list;
        }

        [HarmonyPostfix]
        public static void Postfix(WorldObject __instance, bool _first, int _Level)
        {
            if (__instance == null) return;
            if (WorldSyncManager.Instance == null || WorldSyncManager.Instance.IsApplyingRemoteWorldObject) return;
            if (CoopNetManager.Instance == null || !CoopNetManager.Instance.IsConnected) return;
            if (SaveTransferManager.IsWorldLoading) return;
            if (!CoopNetManager.Instance.IsHost) return;

            int x = Mathf.RoundToInt(__instance.m_Pos.x);
            int y = Mathf.RoundToInt(__instance.m_Pos.y);
            WorldSyncManager.Instance.SendWorldObjectUpdate(x, y, (byte)Mathf.Clamp(_Level, 0, 2), isDestroyed: false, (short)__instance.m_TileType);
        }
    }

    [HarmonyPatch]
    public static class WorldObject_DestroyObj_Patch
    {
        [HarmonyTargetMethods]
        public static IEnumerable<MethodBase> TargetMethods()
        {
            var list = new List<MethodBase>();
            try
            {
                var baseMethod = AccessTools.Method(typeof(WorldObject), "DestroyObj");
                if (baseMethod != null) list.Add(baseMethod);

                foreach (var type in typeof(WorldObject).Assembly.GetTypes())
                {
                    if (typeof(WorldObject).IsAssignableFrom(type) && type != typeof(WorldObject))
                    {
                        var m = type.GetMethod("DestroyObj", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                        if (m != null)
                            list.Add(m);
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[WorldObject_DestroyObj_Patch] TargetMethods reflection error: {ex}");
            }
            return list;
        }

        [HarmonyPrefix]
        public static void Prefix(WorldObject __instance)
        {
            if (__instance == null) return;
            if (WorldSyncManager.Instance == null || WorldSyncManager.Instance.IsApplyingRemoteWorldObject) return;
            if (CoopNetManager.Instance == null || !CoopNetManager.Instance.IsConnected) return;
            if (SaveTransferManager.IsWorldLoading) return;
            if (!CoopNetManager.Instance.IsHost) return;

            int x = Mathf.RoundToInt(__instance.m_Pos.x);
            int y = Mathf.RoundToInt(__instance.m_Pos.y);
            WorldSyncManager.Instance.SendWorldObjectUpdate(x, y, 0, isDestroyed: true, (short)__instance.m_TileType);
        }
    }

    [HarmonyPatch]
    public static class WorldObject_Interaction_Patch
    {
        [HarmonyTargetMethods]
        public static IEnumerable<MethodBase> TargetMethods()
        {
            var list = new List<MethodBase>();
            try
            {
                var baseMethod = AccessTools.Method(typeof(WorldObject), "Interaction", new[] { typeof(GameUnit) });
                if (baseMethod != null) list.Add(baseMethod);

                foreach (var type in typeof(WorldObject).Assembly.GetTypes())
                {
                    if (typeof(WorldObject).IsAssignableFrom(type) && type != typeof(WorldObject))
                    {
                        var m = type.GetMethod("Interaction", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly, null, new[] { typeof(GameUnit) }, null);
                        if (m != null)
                            list.Add(m);
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[WorldObject_Interaction_Patch] TargetMethods reflection error: {ex}");
            }
            return list;
        }

        [HarmonyPrefix]
        public static bool Prefix(WorldObject __instance, GameUnit _unit)
        {
            if (__instance == null) return true;
            if (CoopNetManager.Instance == null || !CoopNetManager.Instance.IsConnected) return true;
            if (SaveTransferManager.IsWorldLoading) return true;

            // When Client Queen manually interacts with a plant, forward authoritative harvest request to Host
            if (CoopNetManager.Instance.IsClient)
            {
                if (_unit != null && _unit.m_UnitKind == UnitKind.Queen)
                {
                    int x = Mathf.RoundToInt(__instance.m_Pos.x);
                    int y = Mathf.RoundToInt(__instance.m_Pos.y);
                    WorldSyncManager.Instance?.SendRequestHarvestWorldObj(x, y);
                    return false; // Prevent client side desync; Host will authoritatively harvest and replicate
                }
            }

            return true;
        }
    }
}
