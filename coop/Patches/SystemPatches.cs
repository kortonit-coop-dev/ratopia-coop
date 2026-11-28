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

        #region Game Pause & Speed Synchronization
        private static bool ShouldSuppressGameplayPause()
        {
            if (CoopNetManager.Instance == null || !CoopNetManager.Instance.IsConnected)
                return false;
            if (SaveTransferManager.IsWorldLoading)
                return false;
            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != "GameScene")
                return false;

            var tileMgr = GameMgr.Instance?._TileMgr;
            if (tileMgr == null || tileMgr.m_MapLoading || tileMgr.m_GameLoading)
                return false;

            if (QueenSyncManager.Instance != null && QueenSyncManager.Instance.IsBothDown())
                return false;

            return true;
        }

        [HarmonyPatch(typeof(SystemMgr), "SystemPause")]
        [HarmonyPrefix]
        public static bool SystemPause_Prefix(bool pause)
        {
            // Keep Co-op world simulation unpaused when either player opens Build, MiningBox, Research, or Command menus
            if (pause && !_isApplyingNetworkPause && ShouldSuppressGameplayPause())
            {
                return false;
            }
            return true;
        }

        [HarmonyPatch(typeof(SystemMgr), "SystemPause")]
        [HarmonyPostfix]
        public static void SystemPause_Postfix(bool pause)
        {
            if (_isApplyingNetworkPause) return;
            if (pause && ShouldSuppressGameplayPause()) return;
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

        [HarmonyPatch(typeof(SystemMgr), nameof(SystemMgr.SetTimeScale), new System.Type[] { typeof(float) })]
        [HarmonyPostfix]
        public static void SetTimeScale_Postfix(SystemMgr __instance, float value)
        {
            if (_isApplyingNetworkPause || __instance == null) return;
            if (SaveTransferManager.Instance != null && (SaveTransferManager.Instance.IsSuppressingPauseSync || SaveTransferManager.IsWorldLoading))
                return;

            if (CoopNetManager.Instance != null && CoopNetManager.Instance.IsConnected && CoopNetManager.Instance.IsHost)
            {
                NetDataWriter writer = new NetDataWriter();
                writer.Put(__instance.IsGamePause());
                writer.Put(__instance.IsGamePause() ? 0f : value);
                CoopNetManager.Instance.SendPacket(PacketType.GamePause, writer, DeliveryMethod.ReliableOrdered);
            }
        }

        public static void ApplyNetworkPause(bool pause, float timeScale)
        {
            if (GameMgr.Instance == null || GameMgr.Instance._SysMgr == null) return;
            var sys = GameMgr.Instance._SysMgr;

            if (pause && ShouldSuppressGameplayPause())
            {
                pause = false;
            }

            try
            {
                _isApplyingNetworkPause = true;
                float curScale = Traverse.Create(sys).Field<float>("m_TimeScale").Value;
                if (!pause && timeScale > 0.01f && Mathf.Abs(curScale - timeScale) > 0.01f)
                {
                    sys.SetTimeScale(timeScale);
                }
                if (sys.IsGamePause() != pause)
                {
                    sys.SystemPause(pause);
                }
                if (!pause && timeScale > 0.01f && Mathf.Abs(Time.timeScale - timeScale) > 0.01f)
                {
                    Time.timeScale = timeScale;
                }
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

        #region Immigration, Citizen Caves & Host-Authoritative Spawners
        private static bool IsCitizenEntranceBuilding(Building? building)
        {
            return building != null && building.m_Info != null &&
                   (building.m_Info.Name == BuildingName.CitizenCave ||
                    building.m_Info.Ability == BuildAbility.CitizenCave ||
                    building.m_Info.Name == BuildingName.Almshouse ||
                    building.m_Info.Ability == BuildAbility.Almshouse);
        }

        [HarmonyPatch(typeof(Building_CitizenCave), "MakeCitzenDirect")]
        [HarmonyPrefix]
        public static bool Building_CitizenCave_MakeCitzenDirect_Prefix()
        {
            // Immigration animations/generation run authoritatively on the host
            if (CoopNetManager.Instance == null || !CoopNetManager.Instance.IsConnected) return true;
            if (CoopNetManager.Instance.IsClient) return false;
            return true;
        }

        [HarmonyPatch(typeof(BuildMid_QueenSlot), nameof(BuildMid_QueenSlot.InfoUpdate))]
        [HarmonyPostfix]
        public static void BuildMid_QueenSlot_InfoUpdate_Postfix(BuildMid_QueenSlot __instance)
        {
            if (CoopNetManager.Instance == null || !CoopNetManager.Instance.IsConnected || !CoopNetManager.Instance.IsClient) return;
            if (__instance == null) return;

            try
            {
                var trav = Traverse.Create(__instance);
                var building = trav.Field<Building>("m_Building").Value;
                if (!IsCitizenEntranceBuilding(building)) return;

                var txt = trav.Field<TMPro.TextMeshProUGUI>("Txt_Script").Value;
                if (txt != null && !txt.text.Contains("[Host Only]"))
                {
                    txt.text = $"{txt.text} [Host Only]";
                }
            }
            catch { }
        }

        [HarmonyPatch(typeof(BuildMid_QueenSlot), nameof(BuildMid_QueenSlot.SlotAction), new System.Type[] { typeof(Building), typeof(BuildMidUI) })]
        [HarmonyPrefix]
        public static bool BuildMid_QueenSlot_SlotAction_Prefix(Building _building, ref int __result)
        {
            if (CoopNetManager.Instance == null || !CoopNetManager.Instance.IsConnected || !CoopNetManager.Instance.IsClient) return true;
            if (!IsCitizenEntranceBuilding(_building)) return true;

            try
            {
                CasselGames.Audio.AudioController.PlayUIOneShot("SFX_UI_Cancel");
                GameMgr.Instance?._CenterAlarmUI?.CenterAlarmCustomSet("Only Host can use the City Entrance.", Color.red);
            }
            catch { }

            __result = 2; // Keep BuildMidUI open without disabling it
            return false;
        }

        [HarmonyPatch(typeof(CitizenCaveUI), nameof(CitizenCaveUI.OpenCCUI), new System.Type[] { typeof(bool) })]
        [HarmonyPrefix]
        public static bool CitizenCaveUI_OpenCCUI_Prefix(bool _isquest)
        {
            if (CoopNetManager.Instance == null || !CoopNetManager.Instance.IsConnected || !CoopNetManager.Instance.IsClient) return true;

            // Allow Diplomatic Mail (_isquest) and Dungeon Reward (IsSpaceOut), block City Entrance / Almshouse recruitment
            if (!_isquest && (PlayDataMgr.Instance == null || !PlayDataMgr.Instance.IsSpaceOut))
            {
                try
                {
                    CasselGames.Audio.AudioController.PlayUIOneShot("SFX_UI_Cancel");
                    GameMgr.Instance?._CenterAlarmUI?.CenterAlarmCustomSet("Only Host can use the City Entrance.", Color.red);
                }
                catch { }
                return false;
            }
            return true;
        }

        [HarmonyPatch(typeof(CitizenCaveUI), nameof(CitizenCaveUI.MakeCitizenList))]
        [HarmonyPrefix]
        public static bool CitizenCaveUI_MakeCitizenList_Prefix()
        {
            if (CoopNetManager.Instance == null || !CoopNetManager.Instance.IsConnected) return true;
            if (CoopNetManager.Instance.IsClient) return false;
            return true;
        }

        [HarmonyPatch(typeof(CitizenCaveUI), nameof(CitizenCaveUI.RecruitBtn))]
        [HarmonyPrefix]
        public static bool CitizenCaveUI_RecruitBtn_Prefix(CitizenCaveUI __instance)
        {
            if (CoopNetManager.Instance == null || !CoopNetManager.Instance.IsConnected || !CoopNetManager.Instance.IsClient) return true;
            if (PlayDataMgr.Instance != null && PlayDataMgr.Instance.IsSpaceOut) return true;

            if (__instance != null && __instance.Obj_Main != null && __instance.Obj_Main.activeSelf)
            {
                __instance.Off_CCUI();
            }
            return false;
        }

        [HarmonyPatch(typeof(CitizenCaveUI), "Refuse_Citizen")]
        [HarmonyPrefix]
        public static bool CitizenCaveUI_Refuse_Citizen_Prefix(CitizenCaveUI __instance)
        {
            if (CoopNetManager.Instance == null || !CoopNetManager.Instance.IsConnected || !CoopNetManager.Instance.IsClient) return true;

            if (__instance != null && __instance.Obj_Main != null && __instance.Obj_Main.activeSelf)
            {
                __instance.Off_CCUI();
            }
            return false;
        }

        [HarmonyPatch(typeof(T_UnitMgr), nameof(T_UnitMgr.MakeCtiizen_ByCC), new System.Type[] { typeof(Vector2), typeof(float), typeof(CC_Group) })]
        [HarmonyPrefix]
        public static bool TUnitMgr_MakeCtiizen_ByCC_Prefix()
        {
            if (CoopNetManager.Instance == null || !CoopNetManager.Instance.IsConnected) return true;
            if (CoopNetManager.Instance.IsClient) return CitizenSyncManager.IsApplyingRemoteSpawn || SaveTransferManager.IsWorldLoading;
            return true;
        }

        [HarmonyPatch(typeof(T_Citizen), nameof(T_Citizen.MakeCtizen_ByCC), new System.Type[] { typeof(Vector2), typeof(CCMake_Info) })]
        [HarmonyPrefix]
        public static bool TCitizen_MakeCtizen_ByCC_Prefix()
        {
            if (CoopNetManager.Instance == null || !CoopNetManager.Instance.IsConnected) return true;
            if (CoopNetManager.Instance.IsClient) return CitizenSyncManager.IsApplyingRemoteSpawn || SaveTransferManager.IsWorldLoading;
            return true;
        }

        [HarmonyPatch(typeof(AnimalMgr), nameof(AnimalMgr.MakeAnimal))]
        [HarmonyPrefix]
        public static bool AnimalMgr_MakeAnimal_Prefix()
        {
            if (CoopNetManager.Instance == null || !CoopNetManager.Instance.IsConnected) return true;
            if (CoopNetManager.Instance.IsClient) return CitizenSyncManager.IsApplyingRemoteSpawn || SaveTransferManager.IsWorldLoading;
            return true;
        }

        [HarmonyPatch(typeof(T_UnitMgr), nameof(T_UnitMgr.MakeEnemy))]
        [HarmonyPrefix]
        public static bool TUnitMgr_MakeEnemy_Prefix()
        {
            if (CoopNetManager.Instance == null || !CoopNetManager.Instance.IsConnected) return true;
            if (CoopNetManager.Instance.IsClient) return CitizenSyncManager.IsApplyingRemoteSpawn || SaveTransferManager.IsWorldLoading;
            return true;
        }

        [HarmonyPatch(typeof(T_UnitMgr), nameof(T_UnitMgr.MakeInvasionEnemy))]
        [HarmonyPrefix]
        public static bool TUnitMgr_MakeInvasionEnemy_Prefix()
        {
            if (CoopNetManager.Instance == null || !CoopNetManager.Instance.IsConnected) return true;
            if (CoopNetManager.Instance.IsClient) return CitizenSyncManager.IsApplyingRemoteSpawn || SaveTransferManager.IsWorldLoading;
            return true;
        }

        [HarmonyPatch(typeof(T_UnitMgr), nameof(T_UnitMgr.MakeEliteEnemy))]
        [HarmonyPrefix]
        public static bool TUnitMgr_MakeEliteEnemy_Prefix()
        {
            if (CoopNetManager.Instance == null || !CoopNetManager.Instance.IsConnected) return true;
            if (CoopNetManager.Instance.IsClient) return CitizenSyncManager.IsApplyingRemoteSpawn || SaveTransferManager.IsWorldLoading;
            return true;
        }

        [HarmonyPatch(typeof(T_UnitMgr), nameof(T_UnitMgr.MakeCitizen))]
        [HarmonyPrefix]
        public static bool TUnitMgr_MakeCitizen_Prefix()
        {
            if (CoopNetManager.Instance == null || !CoopNetManager.Instance.IsConnected) return true;
            if (CoopNetManager.Instance.IsClient) return CitizenSyncManager.IsApplyingRemoteSpawn || SaveTransferManager.IsWorldLoading;
            return true;
        }

        [HarmonyPatch(typeof(T_UnitMgr), nameof(T_UnitMgr.MakeSkullRat))]
        [HarmonyPrefix]
        public static bool TUnitMgr_MakeSkullRat_Prefix()
        {
            if (CoopNetManager.Instance == null || !CoopNetManager.Instance.IsConnected) return true;
            if (CoopNetManager.Instance.IsClient) return CitizenSyncManager.IsApplyingRemoteSpawn || SaveTransferManager.IsWorldLoading;
            return true;
        }

        [HarmonyPatch(typeof(EnvironmentMgr), nameof(EnvironmentMgr.MakeEnemyBirthSpot))]
        [HarmonyPrefix]
        public static bool EnvironmentMgr_MakeEnemyBirthSpot_Prefix()
        {
            if (CoopNetManager.Instance == null || !CoopNetManager.Instance.IsConnected) return true;
            if (CoopNetManager.Instance.IsClient) return SaveTransferManager.IsWorldLoading;
            return true;
        }

        [HarmonyPatch(typeof(EnvironmentMgr), nameof(EnvironmentMgr.PurifyingPlantCheck))]
        [HarmonyPrefix]
        public static bool EnvironmentMgr_PurifyingPlantCheck_Prefix()
        {
            if (CoopNetManager.Instance == null || !CoopNetManager.Instance.IsConnected) return true;
            if (CoopNetManager.Instance.IsClient) return SaveTransferManager.IsWorldLoading;
            return true;
        }

        [HarmonyPatch(typeof(WorldObject), nameof(WorldObject.DayUpdate))]
        [HarmonyPrefix]
        public static bool WorldObject_DayUpdate_Prefix()
        {
            if (CoopNetManager.Instance == null || !CoopNetManager.Instance.IsConnected) return true;
            if (CoopNetManager.Instance.IsClient) return false;
            return true;
        }

        [HarmonyPatch(typeof(JewelTreePlant), nameof(JewelTreePlant.DayUpdate))]
        [HarmonyPrefix]
        public static bool JewelTreePlant_DayUpdate_Prefix()
        {
            if (CoopNetManager.Instance == null || !CoopNetManager.Instance.IsConnected) return true;
            if (CoopNetManager.Instance.IsClient) return false;
            return true;
        }
        #endregion
    }
}
