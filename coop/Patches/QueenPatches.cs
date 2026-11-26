using System;
using System.Collections.Generic;
using HarmonyLib;
using LiteNetLib;
using LiteNetLib.Utils;
using MEC;
using RatopiaCoop.Network;
using RatopiaCoop.Sync;
using UnityEngine;

namespace RatopiaCoop.Patches
{
    [HarmonyPatch]
    public static class QueenPatches
    {
        // 1. Prevent CocosFunc.ScaleTo null pointer exceptions on inactive or newly cloned GameObjects
        [HarmonyPatch(typeof(CocosFunc), "ScaleTo", new Type[] { typeof(float), typeof(Vector3), typeof(GameObject) }, new ArgumentType[] { ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Ref })]
        [HarmonyPrefix]
        public static bool ScaleTo_Vec3_Prefix(ref GameObject obj, ref CoroutineHandle __result)
        {
            if (obj == null)
            {
                __result = default;
                return false;
            }
            return true;
        }

        [HarmonyPatch(typeof(CocosFunc), "ScaleTo", new Type[] { typeof(float), typeof(Vector2), typeof(GameObject), typeof(Action) }, new ArgumentType[] { ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Ref, ArgumentType.Normal })]
        [HarmonyPrefix]
        public static bool ScaleTo_Vec2_Prefix(ref GameObject obj, ref CoroutineHandle __result)
        {
            if (obj == null)
            {
                __result = default;
                return false;
            }
            return true;
        }

        [HarmonyPatch(typeof(CocosFunc), "UI_ScaleTo")]
        [HarmonyPrefix]
        public static bool UI_ScaleTo_Prefix(ref GameObject obj, ref CoroutineHandle __result)
        {
            if (obj == null)
            {
                __result = default;
                return false;
            }
            return true;
        }

        // 2. Prevent UnitHP null pointer crashes on cloned player units
        [HarmonyPatch(typeof(UnitHP), "UpdateHp")]
        [HarmonyPrefix]
        public static bool UnitHP_UpdateHp_Prefix(UnitHP __instance)
        {
            if (__instance == null || __instance.Obj == null)
            {
                return false;
            }
            return true;
        }

        [HarmonyPatch(typeof(UnitHP), "InitHP")]
        [HarmonyPrefix]
        public static void UnitHP_InitHP_Prefix(UnitHP __instance)
        {
            if (__instance == null) return;
            if (__instance.Tf == null) __instance.Tf = __instance.transform;
            if (__instance.Obj == null) __instance.Obj = __instance.gameObject;
            if (__instance.Tf_HP_Effect != null)
            {
                var field = typeof(UnitHP).GetField("Obj_Effect", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (field != null && field.GetValue(__instance) == null)
                {
                    field.SetValue(__instance, __instance.Tf_HP_Effect.gameObject);
                }
            }
        }

        // BUG FIX #4: Immediate HP synchronization on damage/healing
        [HarmonyPatch(typeof(GameUnit), "HpUpdate")]
        [HarmonyPostfix]
        public static void GameUnit_HpUpdate_Postfix(GameUnit __instance)
        {
            if (CoopNetManager.Instance == null || !CoopNetManager.Instance.IsConnected) return;
            if (QueenSyncManager.Instance == null) return;

            // Only sync local character's HP changes
            if (__instance == QueenSyncManager.Instance.LocalCharacter)
            {
                NetDataWriter writer = new NetDataWriter();
                writer.Put(__instance.m_ID);
                writer.Put(__instance.m_CurHP);
                writer.Put(__instance.GetMaxHP());
                CoopNetManager.Instance.SendPacket(PacketType.PlayerHealthUpdate, writer, DeliveryMethod.ReliableOrdered);
                Plugin.Log.LogInfo($"[QueenPatches] Sent HP update: {__instance.m_CurHP}/{__instance.GetMaxHP()}");
            }
        }
        [HarmonyPatch(typeof(Helpers), "IsActivePosition")]
        [HarmonyPrefix]
        public static bool Helpers_IsActivePosition_Prefix(Vector2 _pos, bool unit_check, bool sp_building_check, float sense_distance, ref bool __result)
        {
            if (unit_check && GameMgr.Instance?._T_UnitMgr != null)
            {
                var team = GameMgr.Instance._T_UnitMgr.List_AllTeam;
                if (team != null)
                {
                    for (int i = 0; i < team.Count; i++)
                    {
                        var unit = team[i];
                        if (unit == null) continue;
                        Transform tf = unit.Tf != null ? unit.Tf : unit.transform;
                        if (tf == null) continue;
                        if (Helpers.IsDistanceSmaller(tf.position, _pos, sense_distance))
                        {
                            __result = true;
                            return false;
                        }
                    }
                }
            }
            return true;
        }

        // 4. Ensure QueenCheckBox has valid transform references and never runs on RemoteCharacter or destroyed objects
        [HarmonyPatch(typeof(QueenCheckBox), "Tf_Update")]
        [HarmonyPrefix]
        public static bool QueenCheckBox_Tf_Update_Prefix(QueenCheckBox __instance)
        {
            if (__instance == null) return false;
            var trav = Traverse.Create(__instance);
            var queen = trav.Field<T_Queen>("m_Queen").Value;
            if (queen == null) return false;

            if (CoopNetManager.Instance != null && CoopNetManager.Instance.IsConnected &&
                QueenSyncManager.Instance != null && queen == QueenSyncManager.Instance.RemoteCharacter)
            {
                return false;
            }

            if (queen.Tf == null)
            {
                queen.Tf = queen.transform;
            }
            if (__instance.Tf == null)
            {
                __instance.Tf = __instance.transform;
            }
            return true;
        }

        [HarmonyPatch(typeof(QueenCheckBox), nameof(QueenCheckBox.HotKeyUpdate))]
        [HarmonyPrefix]
        public static bool QueenCheckBox_HotKeyUpdate_Prefix(QueenCheckBox __instance)
        {
            if (__instance == null || __instance.Spr_Frame == null || __instance.Txt_Hotkey == null)
                return false;

            var queen = Traverse.Create(__instance).Field<T_Queen>("m_Queen").Value;
            if (queen == null || queen.gameObject == null)
                return false;

            if (CoopNetManager.Instance != null && CoopNetManager.Instance.IsConnected &&
                QueenSyncManager.Instance != null && queen == QueenSyncManager.Instance.RemoteCharacter)
                return false;

            return true;
        }

        [HarmonyPatch(typeof(QueenCheckBox), nameof(QueenCheckBox.BoxListUpdate), new Type[] { typeof(int) })]
        [HarmonyPrefix]
        public static bool QueenCheckBox_BoxListUpdate_Prefix(QueenCheckBox __instance)
        {
            if (__instance == null || __instance.Spr_Frame == null)
                return false;

            var queen = Traverse.Create(__instance).Field<T_Queen>("m_Queen").Value;
            if (queen == null || queen.gameObject == null)
                return false;

            return true;
        }

        // 5. Keep Player 2 Worker's ID at 778 even if T_Queen.Init() attempts to force 777
        [HarmonyPatch(typeof(T_Queen), "Init")]
        [HarmonyPostfix]
        public static void T_Queen_Init_Postfix(T_Queen __instance)
        {
            if (QueenSyncManager.Instance != null && QueenSyncManager.Instance.Player2Worker == __instance)
            {
                __instance.m_ID = 778;
                __instance.m_UnitName = "Worker Rat";
            }
        }

        // 6. Input, targeting & action handling
        [HarmonyPatch(typeof(T_Queen), "Update")]
        [HarmonyPrefix]
        public static bool Update_Prefix(T_Queen __instance)
        {
            // If map is currently generating or loading, completely suppress Update()
            // so no physics, ground queries, or falling coroutines can start prematurely!
            if (GameMgr.Instance?._TileMgr != null && (GameMgr.Instance._TileMgr.m_MapLoading || GameMgr.Instance._TileMgr.m_GameLoading || SaveTransferManager.IsWorldLoading))
            {
                return false;
            }

            // In singleplayer, always run full normal vanilla Update
            if (CoopNetManager.Instance == null || !CoopNetManager.Instance.IsConnected)
            {
                return true;
            }

            // In co-op: If this instance is the remote character, skip vanilla Update (input & local camera update)
            if (QueenSyncManager.Instance != null && __instance == QueenSyncManager.Instance.RemoteCharacter)
            {
                return false;
            }
            if (CoopNetManager.Instance.IsClient && __instance.m_ID == 777) return false;
            if (CoopNetManager.Instance.IsHost && __instance.m_ID == 778) return false;

            // In co-op: If local character is downed, prevent normal movement/actions
            if (QueenSyncManager.Instance != null && QueenSyncManager.Instance.IsLocalDowned && __instance == QueenSyncManager.Instance.LocalCharacter)
            {
                __instance.SetCharState(CharState.Injury);
                return false;
            }

            return true;
        }

        [HarmonyPatch(typeof(T_Queen), "Update")]
        [HarmonyPostfix]
        public static void Update_Postfix(T_Queen __instance)
        {
            try
            {
                if (__instance == null || __instance.m_CheckBox == null) return;

                // Failsafe: Emergency void recovery
                // If character has fallen below safe map boundaries (e.g. Y < 5), rescue them back to safe ground
                if (__instance.Tf != null && __instance.Tf.position.y < 5f && GameMgr.Instance?._TileMgr != null && !GameMgr.Instance._TileMgr.m_MapLoading && !GameMgr.Instance._TileMgr.m_GameLoading)
                {
                    Vector2 rescuePos = GameMgr.Instance._TileMgr.Pos_Start;
                    int rx = Mathf.RoundToInt(rescuePos.x);
                    for (int cy = 250; cy >= 10; cy--)
                    {
                        var node = GameMgr.Instance._TileMgr.GetNode(rx, cy);
                        if (node != null && C_Node.CanIOnBox(node))
                        {
                            rescuePos = new Vector2(rx, cy + 1f);
                            break;
                        }
                    }

                    Plugin.Log.LogWarning($"[QueenPatches] Emergency Void Rescue for {__instance.name}! Resetting position to {rescuePos}");
                    __instance.KillMoving(false);
                    if (Timing.IsRunning(__instance._corMoving))
                        Timing.KillCoroutines(__instance._corMoving);
                    if (Timing.IsRunning(__instance._corMoveFunc))
                        Timing.KillCoroutines(__instance._corMoveFunc);

                    __instance.List_State.Remove(CitizenState.FallDown);
                    __instance.m_MoveDir = MoveDir.Null;
                    __instance.Tf.position = new Vector3(rescuePos.x, rescuePos.y, __instance.Tf.position.z);
                    __instance.NodeUpdate();
                    __instance.SetAniState(AniState.Idle, true, false);
                }

                // In co-op: Only run for active local character
                if (CoopNetManager.Instance != null && CoopNetManager.Instance.IsConnected && 
                    QueenSyncManager.Instance != null && __instance == QueenSyncManager.Instance.RemoteCharacter)
                {
                    return;
                }

                if (__instance.IsAirWalk() || __instance.m_CharState == CharState.Mining || __instance.m_CharState == CharState.Gathering || __instance.m_CharState == CharState.Building)
                {
                    return;
                }

                bool isMouseTargetOn = CasselGames.Input.InputMgr.Instance != null && CasselGames.Input.InputMgr.Instance.IsMouseTargetOn();

                // Keyboard targeting: allow aiming directly UP (0, 2), directly DOWN (0, -1), and all surrounding directions
                if (!isMouseTargetOn && GameMgr.Instance?._TileMgr != null)
                {
                    Vector2Int current = __instance.m_CheckBox.Pos_Center;
                    if (CasselGames.Input.InputMgr.GetKeyDown(HotKeyName.UpDir2))
                    {
                        if (current.x == 0) __instance.m_CheckBox.CenterUpdate(new Vector2Int(0, 2));
                        else if (current.y == -1) __instance.m_CheckBox.CenterUpdate(new Vector2Int(current.x, 0));
                        else if (current.y == 0) __instance.m_CheckBox.CenterUpdate(new Vector2Int(current.x, 1));
                        else if (current.y == 1) __instance.m_CheckBox.CenterUpdate(new Vector2Int(current.x, 2));
                        else __instance.m_CheckBox.CenterUpdate(new Vector2Int(0, 2));
                    }
                    else if (CasselGames.Input.InputMgr.GetKeyDown(HotKeyName.BottomDir2))
                    {
                        if (current.x == 0) __instance.m_CheckBox.CenterUpdate(new Vector2Int(0, -1));
                        else if (current.y == 2) __instance.m_CheckBox.CenterUpdate(new Vector2Int(current.x, 1));
                        else if (current.y == 1) __instance.m_CheckBox.CenterUpdate(new Vector2Int(current.x, 0));
                        else if (current.y == 0) __instance.m_CheckBox.CenterUpdate(new Vector2Int(current.x, -1));
                        else __instance.m_CheckBox.CenterUpdate(new Vector2Int(0, -1));
                    }
                    else if (CasselGames.Input.InputMgr.GetKeyDown(HotKeyName.LeftDir2))
                    {
                        if (current.x == 1) __instance.m_CheckBox.CenterUpdate(new Vector2Int(0, current.y >= 1 ? 2 : -1));
                        else __instance.m_CheckBox.CenterUpdate(new Vector2Int(-1, current.y == 2 ? 2 : (current.y == -1 ? -1 : 0)));
                    }
                    else if (CasselGames.Input.InputMgr.GetKeyDown(HotKeyName.RightDir2))
                    {
                        if (current.x == -1) __instance.m_CheckBox.CenterUpdate(new Vector2Int(0, current.y >= 1 ? 2 : -1));
                        else __instance.m_CheckBox.CenterUpdate(new Vector2Int(1, current.y == 2 ? 2 : (current.y == -1 ? -1 : 0)));
                    }
                }

                // Ensure Spine animation events remain bound
                if (__instance.SPE_AtkSwdCheck == null || __instance.m_Spine == null)
                {
                    QueenSyncManager.RebindQueenSpineEvents(__instance);
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[QueenPatches] Update_Postfix safe catch: {ex.Message}");
            }
        }

        // 6.1. Synchronize Queen placement when game batches the Queen to Pos_Start
        [HarmonyPatch(typeof(T_UnitMgr), nameof(T_UnitMgr.BatchQueen), new Type[] { typeof(Vector2) })]
        [HarmonyPostfix]
        public static void BatchQueen_Postfix(T_UnitMgr __instance, Vector2 pos)
        {
            try
            {
                if (__instance == null || __instance.m_Queen == null) return;

                var queen = __instance.m_Queen;
                // Stop any stray moving or falling coroutines on the Host Queen
                queen.KillMoving(false);
                if (Timing.IsRunning(queen._corMoving))
                    Timing.KillCoroutines(queen._corMoving);
                if (Timing.IsRunning(queen._corMoveFunc))
                    Timing.KillCoroutines(queen._corMoveFunc);

                queen.List_State.Remove(CitizenState.FallDown);
                queen.m_MoveDir = MoveDir.Null;
                if (queen.Tf != null)
                {
                    queen.Tf.position = new Vector3(pos.x, pos.y, queen.Tf.position.z);
                }
                queen.NodeUpdate();
                Plugin.Log.LogInfo($"[QueenPatches] BatchQueen executed: Host Queen safely placed at {pos}");

                // If co-op is active and Player 2 Worker exists, batch them right next to Host Queen on solid ground
                if (QueenSyncManager.Instance != null && QueenSyncManager.Instance.Player2Worker != null)
                {
                    var worker = QueenSyncManager.Instance.Player2Worker;
                    worker.KillMoving(false);
                    if (Timing.IsRunning(worker._corMoving))
                        Timing.KillCoroutines(worker._corMoving);
                    if (Timing.IsRunning(worker._corMoveFunc))
                        Timing.KillCoroutines(worker._corMoveFunc);

                    worker.List_State.Remove(CitizenState.FallDown);
                    worker.m_MoveDir = MoveDir.Null;
                    if (worker.Tf != null)
                    {
                        worker.Tf.position = QueenSyncManager.FindSafeStandPosition(new Vector3(pos.x, pos.y, worker.Tf.position.z));
                    }
                    worker.NodeUpdate();
                    Plugin.Log.LogInfo($"[QueenPatches] BatchQueen: Player 2 Worker placed at {worker.Tf?.position}");
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[QueenPatches] BatchQueen_Postfix safe catch: {ex.Message}");
            }
        }

        // 7. Prevent FlipX from cancelling active mining/building actions
        [HarmonyPatch(typeof(T_Queen), "FlipX")]
        [HarmonyPrefix]
        public static bool FlipX_Prefix(T_Queen __instance, bool right)
        {
            if (__instance.m_CharState == CharState.Mining || __instance.m_CharState == CharState.Building)
            {
                return false;
            }
            return true;
        }

        // 8. Prevent NullReferenceException on HotKeySlot.WeaponSet when unarmed on world entry
        [HarmonyPatch(typeof(HotKeySlot), "WeaponSet")]
        [HarmonyPrefix]
        public static bool HotKeySlot_WeaponSet_Prefix(HotKeySlot __instance, bool _on, bool _isfirst)
        {
            var queen = GameMgr.Instance?._T_UnitMgr?.m_Queen;
            if (queen == null || queen.m_WeaponInfo == null)
            {
                if (__instance.Img_IconMain != null)
                {
                    __instance.Img_IconMain.sprite = Func.Instance?.LoadSprite("GameScene/UI/UI_Canvas/Icon/Icon_Hand2");
                }
                return false; // Safely skip vanilla WeaponSet to prevent NullReferenceException
            }
            return true;
        }

        // 8. Fix WorkTimeGauge so mining damage works for both Queen and Player 2 Worker
        // without hijacking the Host Queen's target tile when regular citizens mine!
        [HarmonyPatch(typeof(WorkTimeGauge), "TileUpdate_Call")]
        [HarmonyPrefix]
        public static void WorkTimeGauge_TileUpdate_Call_Prefix(WorkTimeGauge __instance, out C_Tile? __state)
        {
            __state = null;
            TilePatches.IsClientManualMining = false;
            if (__instance == null) return;

            var trav = Traverse.Create(__instance);
            var master = trav.Field<GameUnit>("m_Master").Value;
            if (!(master is T_Queen)) return;

            var tile = trav.Field<C_Tile>("m_Tile").Value;
            if (tile == null) return;

            if (CoopNetManager.Instance != null && CoopNetManager.Instance.IsConnected &&
                CoopNetManager.Instance.IsClient && master == QueenSyncManager.Instance?.LocalCharacter)
            {
                TilePatches.IsClientManualMining = true;
            }

            // Only temporarily align m_Queen.m_CheckBox.m_Tile when Player2Worker is the one mining
            var hostQueen = GameMgr.Instance?._T_UnitMgr?.m_Queen;
            if (hostQueen?.m_CheckBox != null && master != hostQueen)
            {
                __state = hostQueen.m_CheckBox.m_Tile;
                hostQueen.m_CheckBox.m_Tile = tile;
            }
        }

        [HarmonyPatch(typeof(WorkTimeGauge), "TileUpdate_Call")]
        [HarmonyPostfix]
        public static void WorkTimeGauge_TileUpdate_Call_Postfix(C_Tile? __state)
        {
            TilePatches.IsClientManualMining = false;
            var hostQueen = GameMgr.Instance?._T_UnitMgr?.m_Queen;
            if (__state != null && hostQueen?.m_CheckBox != null)
            {
                hostQueen.m_CheckBox.m_Tile = __state;
            }
        }

        [HarmonyPatch(typeof(T_Queen), "DeathCheck")]
        [HarmonyPrefix]
        public static bool DeathCheck_Prefix(T_Queen __instance, int _key)
        {
            if (CoopNetManager.Instance != null && CoopNetManager.Instance.IsConnected)
            {
                var queenMgr = QueenSyncManager.Instance;
                if (queenMgr != null && !queenMgr.IsBothDown())
                {
                    queenMgr.SetDowned(__instance);
                    return false; // Prevent game over trigger
                }
            }

            return true;
        }

        [HarmonyPatch(typeof(T_Queen), "SwdAtk_Call")]
        [HarmonyPrefix]
        public static bool SwdAtk_Call_Prefix(T_Queen __instance, out bool __state)
        {
            __state = false;
            if (__instance == null) return true;

            if (CoopNetManager.Instance != null && CoopNetManager.Instance.IsConnected && QueenSyncManager.Instance != null)
            {
                if (__instance == QueenSyncManager.Instance.RemoteCharacter)
                {
                    return false; // Remote puppet melee attacks must not apply local damage
                }

                if (__instance == QueenSyncManager.Instance.LocalCharacter)
                {
                    CombatPatches.IsLocalPlayerAttacking = true;
                    __state = true;
                }
            }

            if (__instance.m_AtkBox != null)
            {
                // Ensure attack box has valid non-zero dimensions
                bool facingRight = __instance.m_Spine != null && __instance.m_Spine.skeleton != null && __instance.m_Spine.skeleton.ScaleX > 0;
                __instance.m_AtkBox.Size = new Vector2(2.6f, 2.4f);
                __instance.m_AtkBox.OffSet = new Vector2(facingRight ? 1.0f : -1.0f, 0.5f);
                __instance.m_AtkBox.BoxPosUpdate();
            }

            return true;
        }

        [HarmonyPatch(typeof(T_Queen), "SwdAtk_Call")]
        [HarmonyPostfix]
        public static void SwdAtk_Call_Postfix(T_Queen __instance, bool __state)
        {
            if (__state)
            {
                CombatPatches.IsLocalPlayerAttacking = false;
            }

            if (CoopNetManager.Instance != null && CoopNetManager.Instance.IsConnected &&
                QueenSyncManager.Instance != null && __instance == QueenSyncManager.Instance.LocalCharacter)
            {
                QueenSyncManager.Instance.SendPlayerAction(PlayerActionType.SwordAttack);
            }
        }

        [HarmonyPatch(typeof(T_Queen), "BowAtk_Call")]
        [HarmonyPrefix]
        public static bool BowAtk_Call_Prefix(T_Queen __instance)
        {
            if (CoopNetManager.Instance != null && CoopNetManager.Instance.IsConnected &&
                QueenSyncManager.Instance != null && __instance == QueenSyncManager.Instance.RemoteCharacter)
            {
                return false; // Remote puppet bow/staff shots must not apply local damage
            }
            return true;
        }

        [HarmonyPatch(typeof(T_Queen), "BowAtk_Call")]
        [HarmonyPostfix]
        public static void BowAtk_Call_Postfix(T_Queen __instance)
        {
            if (CoopNetManager.Instance != null && CoopNetManager.Instance.IsConnected &&
                QueenSyncManager.Instance != null && __instance == QueenSyncManager.Instance.LocalCharacter)
            {
                QueenSyncManager.Instance.SendPlayerAction(PlayerActionType.BowAttack);
            }
        }

        // 9. Prevent NullReferenceException on cloned Queen / Worker in T_Queen.Start -> PDI_Calculate -> ItemValue
        [HarmonyPatch(typeof(T_Queen), "ItemValue", new Type[] { typeof(ItemInfo), typeof(Res_Ability) })]
        [HarmonyPrefix]
        public static bool ItemValue_Prefix(ItemInfo _info, Res_Ability _res_name, ref float __result)
        {
            if (_info == null || _info.List_Ability == null || _info.List_AbilityValue == null)
            {
                __result = 0f;
                return false;
            }
            return true;
        }

        // 10. Prevent NullReferenceException on cloned Queen / Worker in OnTriggerEnter2D -> CanTouchTaxExcution
        [HarmonyPatch(typeof(T_Queen), "CanTouchTaxExcution")]
        [HarmonyPrefix]
        public static bool CanTouchTaxExcution_Prefix(T_Queen __instance, ref bool __result)
        {
            if (__instance == null)
            {
                __result = false;
                return false;
            }
            if (__instance.IsSkillActivate && __instance.m_PassiveSkill != null && __instance.m_PassiveSkill.Index == 0)
            {
                __result = true;
                return false;
            }
            if (__instance.m_AccessoryInfo != null && __instance.m_AccessoryInfo.List_Ability != null && __instance.m_AccessoryInfo.List_Ability.Contains(Res_Ability.GetTax))
            {
                __result = true;
                return false;
            }
            __result = false;
            return false;
        }

        // 11. Sanitize Unity-deserialized dummy ItemInfo instances before T_Queen.OnTriggerEnter2D runs
        [HarmonyPatch(typeof(T_Queen), "OnTriggerEnter2D")]
        [HarmonyPrefix]
        public static bool OnTriggerEnter2D_Prefix(T_Queen __instance, Collider2D collision)
        {
            if (__instance == null || collision == null) return false;
            if (__instance.m_AccessoryInfo != null && __instance.m_AccessoryInfo.List_Ability == null)
                __instance.m_AccessoryInfo = null;
            if (__instance.m_ClothesInfo != null && __instance.m_ClothesInfo.List_Ability == null)
                __instance.m_ClothesInfo = null;
            if (__instance.m_WeaponInfo != null && __instance.m_WeaponInfo.List_Ability == null)
                __instance.m_WeaponInfo = null;
            if (__instance.m_QueenCharacterInfo != null && __instance.m_QueenCharacterInfo.List_Ability == null)
                __instance.m_QueenCharacterInfo = null;
            return true;
        }
    }
}
