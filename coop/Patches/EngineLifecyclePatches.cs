using System;
using HarmonyLib;
using RatopiaCoop.UI;
using UnityEngine;

namespace RatopiaCoop.Patches
{
    [HarmonyPatch]
    public static class EngineLifecyclePatches
    {
        private static int _cursorUpdateTicks = 0;
        private static bool _subsystemsAttachedToGame = false;

        [HarmonyPatch(typeof(CursorMgr), "Awake")]
        [HarmonyPostfix]
        public static void CursorMgr_Awake_Postfix(CursorMgr __instance)
        {
            Plugin.Print("[Lifecycle] CursorMgr.Awake executed! Game engine is alive.");
            if (__instance != null && __instance.gameObject != null)
            {
                AttachToPersistentGameObject(__instance.gameObject);
            }
        }

        [HarmonyPatch(typeof(CursorMgr), "Update")]
        [HarmonyPostfix]
        public static void CursorMgr_Update_Postfix(CursorMgr __instance)
        {
            _cursorUpdateTicks++;
            if (_cursorUpdateTicks == 60 || _cursorUpdateTicks % 600 == 0)
            {
                Plugin.Print($"[Lifecycle] Engine Heartbeat: CursorMgr.Update ticking! Frame: {_cursorUpdateTicks} | Menu: {CoopGUI.Instance?.IsMenuVisible}");
            }

            // Ensure subsystems are attached to the game's immortal CursorMgr object
            if (!_subsystemsAttachedToGame && __instance != null)
            {
                AttachToPersistentGameObject(__instance.gameObject);
            }

            // Fallback direct polling every frame from the engine's own cursor tick
            CoopGUI.Instance?.PollHardwareHotkeysDirect();
        }

        [HarmonyPatch(typeof(CasselGames.UI.LogoUI), "Show")]
        [HarmonyPostfix]
        public static void LogoUI_Show_Postfix()
        {
            Plugin.Print("[Lifecycle] Logo opening sequence active.");
        }

        [HarmonyPatch(typeof(CasselGames.UI.TitleUI), "Show")]
        [HarmonyPostfix]
        public static void TitleUI_Show_Postfix()
        {
            Plugin.Print("[Lifecycle] >>> TitleUI.Show reached! Main Menu is actively visible on screen! <<<");
        }

        public static void AttachToPersistentGameObject(GameObject targetObj)
        {
            if (targetObj == null) return;

            try
            {
                if (CoopGUI.Instance != null && CoopGUI.Instance.gameObject != null)
                {
                    _subsystemsAttachedToGame = true;
                    return;
                }

                if (targetObj.GetComponent<CoopGUI>() == null)
                {
                    Plugin.Print($"[Lifecycle] Attaching CoopGUI and co-op managers to immortal GameObject '{targetObj.name}'...");

                    // Ensure CoopGUI is present on this permanent, indestructible GameObject
                    targetObj.AddComponent<CoopGUI>();
                    _subsystemsAttachedToGame = true;
                    Plugin.Print("[Lifecycle] CoopGUI attached successfully to immortal GameObject!");
                }
            }
            catch (Exception ex)
            {
                Plugin.Print($"[Lifecycle] Error attaching to GameObject: {ex}");
            }
        }
    }
}
