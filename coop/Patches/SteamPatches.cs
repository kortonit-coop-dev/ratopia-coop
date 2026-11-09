using System;
using HarmonyLib;
using Steamworks;
using UnityEngine;

namespace RatopiaCoop.Patches
{
    [HarmonyPatch(typeof(SteamManager))]
    public static class SteamPatches
    {
        // Prevent SteamManager from shutting down the game if Steam is not running 
        // or when launching a second instance for local co-op testing on the same PC
        [HarmonyPatch("Awake")]
        [HarmonyPrefix]
        public static bool Awake_Prefix(SteamManager __instance)
        {
            try
            {
                // Set singleton instance
                Traverse trav = Traverse.Create(typeof(SteamManager));
                var s_instance = trav.Field("s_instance").GetValue<SteamManager>();
                if (s_instance != null)
                {
                    UnityEngine.Object.Destroy(__instance.gameObject);
                    return false;
                }

                trav.Field("s_instance").SetValue(__instance);
                UnityEngine.Object.DontDestroyOnLoad(__instance.gameObject);

                // Attempt Steam initialization gracefully without quitting on failure
                bool initialized = false;
                try
                {
                    initialized = SteamAPI.Init();
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning($"[SteamPatches] SteamAPI.Init warning (standalone/LAN mode): {ex.Message}");
                }

                Traverse.Create(__instance).Field("m_bInitialized").SetValue(initialized);
                SteamManager.m_SteamEnable = initialized;

                Plugin.Log.LogInfo($"[SteamPatches] SteamManager initialized (Steam active: {initialized}). Multi-instance launch enabled.");
                return false; // Skip original Awake which calls RestartAppIfNecessary & Application.Quit
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[SteamPatches] Error in SteamManager.Awake patch: {ex}");
                return true;
            }
        }

        [HarmonyPatch("get_IsDeviceSteamDeck")]
        [HarmonyPrefix]
        public static bool Get_IsDeviceSteamDeck_Prefix(ref bool __result)
        {
            __result = false;
            return false;
        }

        [HarmonyPatch("get_IsDeviceVR")]
        [HarmonyPrefix]
        public static bool Get_IsDeviceVR_Prefix(ref bool __result)
        {
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(CursorMgr))]
    public static class CursorPatches
    {
        [HarmonyPatch("CursorActivate")]
        [HarmonyPrefix]
        public static bool CursorActivate_Prefix(CursorMgr __instance, bool isActivate)
        {
            // Decouple mouse cursor activation from SteamManager so aiming and dragging work in standalone/LAN
            Cursor.visible = isActivate;
            if (isActivate)
            {
                Cursor.lockState = CursorLockMode.None;
                __instance.SetCursorState(CursorState.None);
            }
            else
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
            }
            return false;
        }
    }
}
