using System;
using CasselGames.Input;
using HarmonyLib;
using RatopiaCoop.Sync;
using RatopiaCoop.UI;
using UnityEngine;
using UnityEngine.InputSystem;

namespace RatopiaCoop.Patches
{
    [HarmonyPatch(typeof(InputMgr))]
    public static class InputPatches
    {
        private static bool IsChatOpen() => ChatManager.Instance != null && ChatManager.Instance.IsChatOpen;
        private static bool IsCoopMenuOpen() => CoopGUI.Instance != null && CoopGUI.Instance.IsMenuVisible;
        private static bool ShouldBlockKeyboard() => IsChatOpen() || IsCoopMenuOpen();
        private static bool ShouldBlockPointer() => CoopGUI.Instance != null && CoopGUI.Instance.IsPointerOverGUI();

        [HarmonyPatch("GetKeyAction", new Type[] { typeof(InputResult), typeof(float), typeof(bool) }, new ArgumentType[] { ArgumentType.Out, ArgumentType.Normal, ArgumentType.Normal })]
        [HarmonyPrefix]
        public static bool GetKeyAction_Prefix(out InputResult inputResult, ref bool __result)
        {
            if (ShouldBlockKeyboard())
            {
                inputResult = default;
                __result = false;
                return false;
            }
            inputResult = default;
            return true;
        }

        [HarmonyPatch("GetKeyDown", typeof(HotKeyName), typeof(bool), typeof(bool))]
        [HarmonyPrefix]
        public static bool GetKeyDown_Hotkey_Prefix(ref bool __result)
        {
            if (ShouldBlockKeyboard())
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
            if (ShouldBlockKeyboard())
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
            if (ShouldBlockKeyboard())
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
            if (ShouldBlockKeyboard())
            {
                // Allow enter and escape only for chat handling (never leak into game/menus while CoopGUI is open)
                if (IsChatOpen() && !IsCoopMenuOpen() &&
                    (key == KeyCode.Return || key == KeyCode.KeypadEnter || key == KeyCode.Escape))
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
            if (ShouldBlockKeyboard())
            {
                if (IsChatOpen() && !IsCoopMenuOpen() &&
                    (key == KeyCode.Return || key == KeyCode.KeypadEnter || key == KeyCode.Escape))
                {
                    return true;
                }
                __result = false;
                return false;
            }
            return true;
        }

        [HarmonyPatch("GetKeyDown", typeof(Key))]
        [HarmonyPrefix]
        public static bool GetKeyDown_InputKey_Prefix(ref bool __result)
        {
            if (ShouldBlockKeyboard())
            {
                __result = false;
                return false;
            }
            return true;
        }

        [HarmonyPatch("GetKey", typeof(Key))]
        [HarmonyPrefix]
        public static bool GetKey_InputKey_Prefix(ref bool __result)
        {
            if (ShouldBlockKeyboard())
            {
                __result = false;
                return false;
            }
            return true;
        }

        [HarmonyPatch("GetEscapeButtonDown")]
        [HarmonyPrefix]
        public static bool GetEscapeButtonDown_Prefix(ref bool __result)
        {
            if (ShouldBlockKeyboard())
            {
                __result = false;
                return false;
            }
            return true;
        }

        [HarmonyPatch("GetKeyDownAnyKey")]
        [HarmonyPrefix]
        public static bool GetKeyDownAnyKey_Prefix(ref bool __result)
        {
            if (ShouldBlockKeyboard())
            {
                __result = false;
                return false;
            }
            return true;
        }

        [HarmonyPatch("get_anyKeyDown")]
        [HarmonyPrefix]
        public static bool get_anyKeyDown_Prefix(ref bool __result)
        {
            if (ShouldBlockKeyboard())
            {
                __result = false;
                return false;
            }
            return true;
        }

        [HarmonyPatch("GetMouseButton", typeof(int))]
        [HarmonyPrefix]
        public static bool GetMouseButton_Prefix(ref bool __result)
        {
            if (ShouldBlockPointer())
            {
                __result = false;
                return false;
            }
            return true;
        }

        [HarmonyPatch("GetMouseButtonDown", typeof(int))]
        [HarmonyPrefix]
        public static bool GetMouseButtonDown_Prefix(ref bool __result)
        {
            if (ShouldBlockPointer())
            {
                __result = false;
                return false;
            }
            return true;
        }

        [HarmonyPatch("GetMouseButtonUp", typeof(int))]
        [HarmonyPrefix]
        public static bool GetMouseButtonUp_Prefix(ref bool __result)
        {
            if (ShouldBlockPointer())
            {
                __result = false;
                return false;
            }
            return true;
        }
    }
}
