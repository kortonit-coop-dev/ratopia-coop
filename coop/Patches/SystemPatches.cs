using HarmonyLib;
using LiteNetLib;
using LiteNetLib.Utils;
using RatopiaCoop.Network;
using RatopiaCoop.Sync;
using UnityEngine;

namespace RatopiaCoop.Patches
{
    [HarmonyPatch]
    public static class SystemPatches
    {
        private static bool _isApplyingNetworkPause;
        private static bool _isApplyingNetworkInvasion;

        #region Game Pause Synchronization
        [HarmonyPatch(typeof(SystemMgr), "SystemPause")]
        [HarmonyPostfix]
        public static void SystemPause_Postfix(bool pause)
        {
            if (_isApplyingNetworkPause) return;
            if (SaveTransferManager.Instance != null && (SaveTransferManager.Instance.IsSuppressingPauseSync || SaveTransferManager.IsWorldLoading))
                return;

            if (CoopNetManager.Instance != null && CoopNetManager.Instance.IsConnected && CoopNetManager.Instance.IsHost)
            {
                NetDataWriter writer = new NetDataWriter();
                writer.Put(pause);
                writer.Put(Time.timeScale);
                CoopNetManager.Instance.SendPacket(PacketType.GamePause, writer, DeliveryMethod.ReliableOrdered);
            }
        }

        public static void ApplyNetworkPause(bool pause, float timeScale)
        {
            if (GameMgr.Instance == null || GameMgr.Instance._SysMgr == null) return;

            try
            {
                _isApplyingNetworkPause = true;
                GameMgr.Instance._SysMgr.SystemPause(pause);
                Time.timeScale = timeScale;
            }
            finally
            {
                _isApplyingNetworkPause = false;
            }
        }
        #endregion

        #region Save Protection
        // BUG FIX #2: Block both manual and automatic saves on client
        [HarmonyPatch(typeof(PlayDataMgr), "Save")]
        [HarmonyPrefix]
        public static bool PlayDataMgr_Save_Prefix()
        {
            // Protect client player from overwriting their local campaign saves with host world data
            if (CoopNetManager.Instance != null && CoopNetManager.Instance.IsConnected && CoopNetManager.Instance.IsClient)
            {
                Plugin.Log.LogWarning("[SystemPatches] Client save attempt blocked. Only Host can save the world.");
                ChatManager.Instance?.AddMessage("SYSTEM", "Saving is disabled while connected. The Host manages world saves.");
                return false; // Block execution
            }
            return true; // Allow host and singleplayer to save
        }
        #endregion

        #region Colony Random Events (Host-Authoritative)
        [HarmonyPatch(typeof(RandomEventMgr), "RandomEventChecker")]
        [HarmonyPrefix]
        public static bool RandomEventChecker_Prefix()
        {
            if (CoopNetManager.Instance == null || !CoopNetManager.Instance.IsConnected) return true;
            if (CoopNetManager.Instance.IsClient) return false;
            return true;
        }

        [HarmonyPatch(typeof(RandomEventMgr), "TwentyMinuteCheck")]
        [HarmonyPrefix]
        public static bool TwentyMinuteCheck_Prefix()
        {
            if (CoopNetManager.Instance == null || !CoopNetManager.Instance.IsConnected) return true;
            if (CoopNetManager.Instance.IsClient) return false;
            return true;
        }

        [HarmonyPatch(typeof(RandomEventMgr), "CountryRandomEventChecker")]
        [HarmonyPrefix]
        public static bool CountryRandomEventChecker_Prefix()
        {
            if (CoopNetManager.Instance == null || !CoopNetManager.Instance.IsConnected) return true;
            if (CoopNetManager.Instance.IsClient) return false;
            return true;
        }
        #endregion

        #region Invasions (Host-Authoritative & Synchronized Alert)
        [HarmonyPatch(typeof(SystemMgr), "InvasionDayReset")]
        [HarmonyPrefix]
        public static bool InvasionDayReset_Prefix()
        {
            // Only host determines invasion days
            if (CoopNetManager.Instance == null || !CoopNetManager.Instance.IsConnected) return true;
            if (CoopNetManager.Instance.IsClient) return false;
            return true;
        }

        [HarmonyPatch(typeof(UI_Invasion), "InvasionActionSet")]
        [HarmonyPrefix]
        public static bool InvasionActionSet_Prefix(int _num, bool _delay, string _str)
        {
            if (CoopNetManager.Instance == null || !CoopNetManager.Instance.IsConnected) return true;

            // Client: block locally-initiated invasion animations if not triggered from network
            if (CoopNetManager.Instance.IsClient && !_isApplyingNetworkInvasion)
                return false;

            // Host: broadcast invasion alert banner to client
            if (CoopNetManager.Instance.IsHost)
            {
                NetDataWriter writer = new NetDataWriter();
                writer.Put(_num);
                writer.Put(_delay);
                writer.Put(_str ?? string.Empty);
                CoopNetManager.Instance.SendPacket(PacketType.InvasionAlert, writer, DeliveryMethod.ReliableOrdered);
            }

            return true;
        }

        public static void ApplyNetworkInvasion(int num, bool delay, string str)
        {
            if (GameMgr.Instance == null || GameMgr.Instance._InvasionUI == null) return;
            try
            {
                _isApplyingNetworkInvasion = true;
                GameMgr.Instance._InvasionUI.InvasionActionSet(num, delay, str);
            }
            finally
            {
                _isApplyingNetworkInvasion = false;
            }
        }
        #endregion

        #region Prosperity Simulation (Host-Authoritative)
        [HarmonyPatch(typeof(ProsperityUI), "ProsperityUpdate")]
        [HarmonyPrefix]
        public static bool ProsperityUI_ProsperityUpdate_Prefix()
        {
            if (CoopNetManager.Instance == null || !CoopNetManager.Instance.IsConnected) return true;
            if (CoopNetManager.Instance.IsClient) return false;
            return true;
        }

        [HarmonyPatch(typeof(ProsperityUI), "ValueUpdate")]
        [HarmonyPrefix]
        public static bool ProsperityUI_ValueUpdate_Prefix()
        {
            if (CoopNetManager.Instance == null || !CoopNetManager.Instance.IsConnected) return true;
            if (CoopNetManager.Instance.IsClient) return false;
            return true;
        }

        [HarmonyPatch(typeof(ProsperityUI), "RecordedLedgerSheetData")]
        [HarmonyPrefix]
        public static bool ProsperityUI_RecordedLedgerSheetData_Prefix()
        {
            if (CoopNetManager.Instance == null || !CoopNetManager.Instance.IsConnected) return true;
            if (CoopNetManager.Instance.IsClient) return false;
            return true;
        }
        #endregion

        #region Weather Simulation (Host-Authoritative)
        [HarmonyPatch(typeof(WeatherMgr), "SeasonState_Update")]
        [HarmonyPrefix]
        public static bool WeatherMgr_SeasonState_Update_Prefix()
        {
            if (CoopNetManager.Instance == null || !CoopNetManager.Instance.IsConnected) return true;
            if (CoopNetManager.Instance.IsClient) return false;
            return true;
        }

        [HarmonyPatch(typeof(WeatherMgr), "ChangeWeatherState")]
        [HarmonyPrefix]
        public static bool WeatherMgr_ChangeWeatherState_Prefix()
        {
            if (CoopNetManager.Instance == null || !CoopNetManager.Instance.IsConnected) return true;
            if (CoopNetManager.Instance.IsClient) return false;
            return true;
        }
        #endregion

        #region Citizen & Enemy AI Simulation (Puppet Mode on Client)
        [HarmonyPatch(typeof(T_Citizen), "UpdateFunction")]
        [HarmonyPrefix]
        public static bool T_Citizen_UpdateFunction_Prefix()
        {
            // On client, suppress autonomous citizen AI. Citizens are interpolated via CitizenSyncManager.
            if (CoopNetManager.Instance == null || !CoopNetManager.Instance.IsConnected) return true;
            if (CoopNetManager.Instance.IsClient) return false;
            return true;
        }

        [HarmonyPatch(typeof(T_Citizen), "Check_AllDesire")]
        [HarmonyPrefix]
        public static bool T_Citizen_Check_AllDesire_Prefix()
        {
            // On client, suppress autonomous citizen desires (eating, fun, sleep, prayer).
            if (CoopNetManager.Instance == null || !CoopNetManager.Instance.IsConnected) return true;
            if (CoopNetManager.Instance.IsClient) return false;
            return true;
        }

        [HarmonyPatch(typeof(GameEnemy), "UpdateFunction")]
        [HarmonyPrefix]
        public static bool GameEnemy_UpdateFunction_Prefix()
        {
            // On client, suppress autonomous enemy AI. Enemies are replicated via CitizenSyncManager.
            if (CoopNetManager.Instance == null || !CoopNetManager.Instance.IsConnected) return true;
            if (CoopNetManager.Instance.IsClient) return false;
            return true;
        }
        #endregion

        #region Citizen Needs Decay (Host-Authoritative)
        [HarmonyPatch(typeof(T_Citizen), "HungerUpdate")]
        [HarmonyPrefix]
        public static bool T_Citizen_HungerUpdate_Prefix(float value)
        {
            // On client, prevent local hunger starvation decay; host dictates health and statuses.
            if (CoopNetManager.Instance == null || !CoopNetManager.Instance.IsConnected) return true;
            if (CoopNetManager.Instance.IsClient && value < 0f) return false;
            return true;
        }

        [HarmonyPatch(typeof(GameUnit), "FatigueUpate")]
        [HarmonyPrefix]
        public static bool GameUnit_FatigueUpate_Prefix(GameUnit __instance, float value)
        {
            // On client, prevent local fatigue exhaustion decay on citizens.
            if (CoopNetManager.Instance == null || !CoopNetManager.Instance.IsConnected) return true;
            if (CoopNetManager.Instance.IsClient && __instance is T_Citizen && value < 0f) return false;
            return true;
        }
        #endregion

        #region Immigration & Citizen Caves
        [HarmonyPatch(typeof(Building_CitizenCave), "MakeCitzenDirect")]
        [HarmonyPrefix]
        public static bool Building_CitizenCave_MakeCitzenDirect_Prefix()
        {
            // Immigration animations/generation run authoritatively on the host
            if (CoopNetManager.Instance == null || !CoopNetManager.Instance.IsConnected) return true;
            if (CoopNetManager.Instance.IsClient) return false;
            return true;
        }
        #endregion
    }
}
