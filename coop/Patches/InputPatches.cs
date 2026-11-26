using HarmonyLib;
using RatopiaCoop.Sync;
using UnityEngine;

namespace RatopiaCoop.Patches
{
    [HarmonyPatch(typeof(CasselGames.Input.InputMgr))]
    public static class InputPatches
    {
        [HarmonyPatch("GetKeyDown", typeof(HotKeyName), typeof(bool), typeof(bool))]
        [HarmonyPrefix]
        public static bool GetKeyDown_Hotkey_Prefix(ref bool __result)
        {
            if (ChatManager.Instance != null && ChatManager.Instance.IsChatOpen)
            {
                __result = false;
                return false;
            }
            return true;
        }

        [HarmonyPatch("GetKey", typeof(HotKeyName), typeof(bool))]
        [HarmonyPrefix]
        public static bool GetKey_Hotkey_Prefix(ref bool __result)
        {
            if (ChatManager.Instance != null && ChatManager.Instance.IsChatOpen)
            {
                __result = false;
                return false;
            }
            return true;
        }

        [HarmonyPatch("GetKeyDown_F", typeof(HotKeyName), typeof(bool))]
        [HarmonyPrefix]
        public static bool GetKeyDown_F_Prefix(ref bool __result)
        {
            if (ChatManager.Instance != null && ChatManager.Instance.IsChatOpen)
            {
                __result = false;
                return false;
            }
            return true;
        }

        [HarmonyPatch("GetKeyDown", typeof(KeyCode))]
        [HarmonyPrefix]
        public static bool GetKeyDown_KeyCode_Prefix(KeyCode key, ref bool __result)
        {
            if (ChatManager.Instance != null && ChatManager.Instance.IsChatOpen)
            {
                // Allow enter and escape for chat handling
                if (key == KeyCode.Return || key == KeyCode.KeypadEnter || key == KeyCode.Escape)
                {
                    return true;
                }
                __result = false;
                return false;
            }
            return true;
        }

        [HarmonyPatch("GetKey", typeof(KeyCode))]
        [HarmonyPrefix]
        public static bool GetKey_KeyCode_Prefix(KeyCode key, ref bool __result)
        {
            if (ChatManager.Instance != null && ChatManager.Instance.IsChatOpen)
            {
                if (key == KeyCode.Return || key == KeyCode.KeypadEnter || key == KeyCode.Escape)
                {
                    return true;
                }
                __result = false;
                return false;
            }
            return true;
        }

        [HarmonyPatch("get_anyKeyDown")]
        [HarmonyPrefix]
        public static bool get_anyKeyDown_Prefix(ref bool __result)
        {
            if (ChatManager.Instance != null && ChatManager.Instance.IsChatOpen)
            {
                __result = false;
                return false;
            }
            return true;
        }
    }
}
