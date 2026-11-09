using System;
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

        // 4. Ensure QueenCheckBox has valid transform references
        [HarmonyPatch(typeof(QueenCheckBox), "Tf_Update")]
        [HarmonyPrefix]
        public static bool QueenCheckBox_Tf_Update_Prefix(QueenCheckBox __instance)
        {
            if (__instance == null) return false;
            var trav = Traverse.Create(__instance);
            var queen = trav.Field<T_Queen>("m_Queen").Value;
            if (queen != null && queen.Tf == null)
            {
                queen.Tf = queen.transform;
            }
            if (__instance.Tf == null)
            {
                __instance.Tf = __instance.transform;
            }
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
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[QueenPatches] Update_Postfix safe catch: {ex.Message}");
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
        public static void SwdAtk_Call_Prefix(T_Queen __instance)
        {
            if (__instance == null || __instance.m_AtkBox == null) return;
            // Ensure attack box has valid non-zero dimensions
            bool facingRight = __instance.m_Spine != null && __instance.m_Spine.skeleton != null && __instance.m_Spine.skeleton.ScaleX > 0;
            __instance.m_AtkBox.Size = new Vector2(2.6f, 2.4f);
            __instance.m_AtkBox.OffSet = new Vector2(facingRight ? 1.0f : -1.0f, 0.5f);
            __instance.m_AtkBox.BoxPosUpdate();
        }

        [HarmonyPatch(typeof(T_Queen), "SwdAtk_Call")]
        [HarmonyPostfix]
        public static void SwdAtk_Call_Postfix(T_Queen __instance)
        {
            if (QueenSyncManager.Instance != null && __instance == QueenSyncManager.Instance.LocalCharacter)
            {
                QueenSyncManager.Instance.SendPlayerAction(PlayerActionType.SwordAttack);
                ExecuteLocalMeleeStrike(__instance);
            }
        }

        private static void ExecuteLocalMeleeStrike(T_Queen queen)
        {
            if (queen == null || queen.Tf == null) return;

            float dmg = queen.GetDmg();
            Vector2 queenPos = queen.Tf.position;
            bool facingRight = queen.m_Spine != null && queen.m_Spine.skeleton != null && queen.m_Spine.skeleton.ScaleX > 0;

            float hitReachX = 2.6f;
            float hitReachY = 2.0f;

            // 1. Check all enemies in UnitRegistry and List_AllEnemy
            var allUnits = RatopiaUnitBrain.Registry.UnitRegistry.Instance.GetAllUnits();
            for (int i = 0; i < allUnits.Count; i++)
            {
                var unit = allUnits[i];
                if (unit == null || unit == queen || unit.m_CharState == CharState.Death || unit.Tf == null) continue;
                if (!(unit is GameEnemy)) continue;

                Vector2 enemyPos = unit.Tf.position;
                float dx = enemyPos.x - queenPos.x;
                float dy = Mathf.Abs(enemyPos.y - queenPos.y);

                bool inFront = facingRight ? (dx >= -0.4f && dx <= hitReachX) : (dx <= 0.4f && dx >= -hitReachX);
                if (inFront && dy <= hitReachY)
                {
                    unit.BeAttacked(0f - dmg, Unit_Attacekd_Tag.Queen, queen.IsCritical ? 1 : 0);
                    queen.KnockCheck(unit);
                }
            }

            // 2. Check wildlife / animals
            if (GameMgr.Instance != null && GameMgr.Instance._AnimalMgr != null && GameMgr.Instance._AnimalMgr.List_Animal != null)
            {
                var animals = GameMgr.Instance._AnimalMgr.List_Animal;
                for (int i = 0; i < animals.Count; i++)
                {
                    var animal = animals[i];
                    if (animal == null || animal.m_State == AnimalState.Death || animal.Tf == null) continue;

                    Vector2 animalPos = animal.Tf.position;
                    float dx = animalPos.x - queenPos.x;
                    float dy = Mathf.Abs(animalPos.y - queenPos.y);

                    bool inFront = facingRight ? (dx >= -0.4f && dx <= hitReachX) : (dx <= 0.4f && dx >= -hitReachX);
                    if (inFront && dy <= hitReachY)
                    {
                        animal.BeAttacked(queen, 0f - dmg);
                    }
                }
            }
        }

        [HarmonyPatch(typeof(T_Queen), "BowAtk_Call")]
        [HarmonyPostfix]
        public static void BowAtk_Call_Postfix(T_Queen __instance)
        {
            if (QueenSyncManager.Instance != null && __instance == QueenSyncManager.Instance.LocalCharacter)
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
