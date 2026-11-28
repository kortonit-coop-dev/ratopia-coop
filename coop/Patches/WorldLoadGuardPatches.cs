using System;
using CasselGames.UI;
using HarmonyLib;
using RatopiaCoop.Network;
using RatopiaCoop.Sync;
using RatopiaCoop.UI;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Utility.IO;
using Utility.IO.Data;
using Utility.UI;

namespace RatopiaCoop.Patches
{
    /// <summary>
    /// Prevents IMGUI Co-op menu clicks/hotkeys from passing through into uGUI Title/Load menus
    /// and pins LoadingSceneMgr + TileMgr to the host's world snapshot on the Client.
    /// </summary>
    [HarmonyPatch]
    public static class WorldLoadGuardPatches
    {
        private static bool ShouldBlockPointerClickThrough()
        {
            return CoopGUI.Instance != null && CoopGUI.Instance.IsPointerOverGUI();
        }

        private static bool ShouldBlockLocalMenuLoad()
        {
            if (CoopGUI.Instance != null && (CoopGUI.Instance.IsMenuVisible || CoopGUI.Instance.IsPointerOverGUI()))
                return true;
            if (SaveTransferManager.HasPendingClientLoad)
                return true;
            if (SaveTransferManager.Instance != null && SaveTransferManager.Instance.IsTransferring)
                return true;
            if (CoopNetManager.Instance != null && CoopNetManager.Instance.IsClientActive)
                return true;
            return false;
        }

        #region uGUI Click-Through Prevention
        [HarmonyPatch(typeof(LayoutButtonUI), "OnPointerDown", typeof(PointerEventData))]
        [HarmonyPrefix]
        public static bool LayoutButtonUI_OnPointerDown_Prefix()
        {
            return !ShouldBlockPointerClickThrough();
        }

        [HarmonyPatch(typeof(LayoutButtonUI), "OnPointerUp", typeof(PointerEventData))]
        [HarmonyPrefix]
        public static bool LayoutButtonUI_OnPointerUp_Prefix()
        {
            return !ShouldBlockPointerClickThrough();
        }

        [HarmonyPatch(typeof(Button), "OnPointerClick", typeof(PointerEventData))]
        [HarmonyPrefix]
        public static bool Button_OnPointerClick_Prefix()
        {
            return !ShouldBlockPointerClickThrough();
        }
        #endregion

        #region Title & Save-Load Menu Isolation
        [HarmonyPatch(typeof(TitleUI), "OnResumeEvent")]
        [HarmonyPrefix]
        public static bool TitleUI_OnResumeEvent_Prefix()
        {
            if (ShouldBlockLocalMenuLoad())
            {
                Plugin.Log.LogInfo("[WorldLoadGuardPatches] Blocked TitleUI.OnResumeEvent while Co-op menu or client session is active.");
                return false;
            }
            return true;
        }

        [HarmonyPatch(typeof(TitleUI), "OnStartEvent")]
        [HarmonyPrefix]
        public static bool TitleUI_OnStartEvent_Prefix()
        {
            if (ShouldBlockLocalMenuLoad())
            {
                Plugin.Log.LogInfo("[WorldLoadGuardPatches] Blocked TitleUI.OnStartEvent while Co-op menu or client session is active.");
                return false;
            }
            return true;
        }

        [HarmonyPatch(typeof(TitleUI), "OnLoadEvent")]
        [HarmonyPrefix]
        public static bool TitleUI_OnLoadEvent_Prefix()
        {
            if (ShouldBlockLocalMenuLoad())
            {
                Plugin.Log.LogInfo("[WorldLoadGuardPatches] Blocked TitleUI.OnLoadEvent while Co-op menu or client session is active.");
                return false;
            }
            return true;
        }

        [HarmonyPatch(typeof(LoadMenuUI), "OnSubmitedEvent", typeof(int))]
        [HarmonyPrefix]
        public static bool LoadMenuUI_OnSubmitedEvent_Prefix()
        {
            if (ShouldBlockLocalMenuLoad())
            {
                Plugin.Log.LogInfo("[WorldLoadGuardPatches] Blocked LoadMenuUI.OnSubmitedEvent while Co-op menu or client session is active.");
                return false;
            }
            return true;
        }

        [HarmonyPatch(typeof(LoadMenuUI), "Load", typeof(S_Data))]
        [HarmonyPrefix]
        public static bool LoadMenuUI_Load_Prefix()
        {
            if (ShouldBlockLocalMenuLoad())
            {
                Plugin.Log.LogInfo("[WorldLoadGuardPatches] Blocked LoadMenuUI.Load while Co-op menu or client session is active.");
                return false;
            }
            return true;
        }

        [HarmonyPatch(typeof(LoadMenuUI), "OnLegacyLoadEvent")]
        [HarmonyPrefix]
        public static bool LoadMenuUI_OnLegacyLoadEvent_Prefix()
        {
            if (ShouldBlockLocalMenuLoad())
            {
                Plugin.Log.LogInfo("[WorldLoadGuardPatches] Blocked LoadMenuUI.OnLegacyLoadEvent while Co-op menu or client session is active.");
                return false;
            }
            return true;
        }

        [HarmonyPatch(typeof(StartSettingsUI), "OnContentsSubmitedEvent", typeof(TypeStartSettingsCategory))]
        [HarmonyPrefix]
        public static bool StartSettingsUI_OnContentsSubmitedEvent_Prefix(TypeStartSettingsCategory typeStartSetting)
        {
            if (typeStartSetting == TypeStartSettingsCategory.Start && ShouldBlockLocalMenuLoad())
            {
                Plugin.Log.LogInfo("[WorldLoadGuardPatches] Blocked StartSettingsUI new game start while Co-op menu or client session is active.");
                return false;
            }
            return true;
        }
        #endregion

        #region Host World Pinning During LoadingScene -> GameScene
        [HarmonyPatch(typeof(LoadingSceneMgr), "LoadScene", typeof(string), typeof(int), typeof(bool))]
        [HarmonyPrefix]
        public static bool LoadingSceneMgr_LoadScene_Prefix(string s_name, bool isNewGame)
        {
            if (SaveTransferManager.HasPendingClientLoad && !SaveTransferManager.IsApplyingCoopSceneTransition)
            {
                Plugin.Log.LogWarning($"[WorldLoadGuardPatches] Blocked competing LoadingSceneMgr.LoadScene('{s_name}', isNewGame={isNewGame}) during pending co-op world load.");
                return false;
            }
            return true;
        }

        [HarmonyPatch(typeof(LoadingSceneMgr), "Start")]
        [HarmonyPrefix]
        public static void LoadingSceneMgr_Start_Prefix()
        {
            SaveTransferManager.EnforcePendingClientWorldData("LoadingSceneMgr.Start");
        }

        [HarmonyPatch(typeof(LoadingSceneMgr), "SetConfirmScene")]
        [HarmonyPrefix]
        public static void LoadingSceneMgr_SetConfirmScene_Prefix()
        {
            SaveTransferManager.EnforcePendingClientWorldData("LoadingSceneMgr.SetConfirmScene");
        }

        [HarmonyPatch(typeof(LoadingSceneMgr), "OnStartEvent")]
        [HarmonyPrefix]
        public static void LoadingSceneMgr_OnStartEvent_Prefix()
        {
            SaveTransferManager.EnforcePendingClientWorldData("LoadingSceneMgr.OnStartEvent");
        }

        [HarmonyPatch(typeof(TileMgr), "Awake")]
        [HarmonyPrefix]
        public static void TileMgr_Awake_Prefix()
        {
            SaveTransferManager.EnforcePendingClientWorldData("TileMgr.Awake");
            if (CoopNetManager.Instance != null && (CoopNetManager.Instance.IsConnected || CoopNetManager.Instance.IsClientActive))
            {
                Plugin.Log.LogInfo($"[WorldLoadGuardPatches] TileMgr.Awake: IsClient={CoopNetManager.Instance.IsClient}, IsLoadGame={PlayDataMgr.Instance?.IsLoadGame}, IsSpaceOut={PlayDataMgr.Instance?.IsSpaceOut}, Seed='{PlayDataMgr.Instance?.StartSettingsData.Seed}', Day={PlayDataMgr.Instance?.m_GameData?.SystemData?.m_Day}");
            }
        }

        [HarmonyPatch(typeof(TileMgr), "Awake")]
        [HarmonyPostfix]
        public static void TileMgr_Awake_Postfix()
        {
            if (CoopNetManager.Instance != null && CoopNetManager.Instance.IsClientActive)
            {
                // DB_Mgr.Awake() reloads U_SaveData which resets USE_AUTOSAVE to true; keep Client autosaves off
                SaveLoadConfigure.USE_AUTOSAVE = false;
            }
        }

        [HarmonyPatch(typeof(SaveAlarmUI), "CallAutoSave")]
        [HarmonyPrefix]
        public static bool SaveAlarmUI_CallAutoSave_Prefix()
        {
            if (CoopNetManager.Instance != null && CoopNetManager.Instance.IsConnected && CoopNetManager.Instance.IsClient)
            {
                SaveLoadConfigure.USE_AUTOSAVE = false;
                return false;
            }
            return true;
        }
        #endregion
    }
}
