using System;
using HarmonyLib;
using RatopiaUnitBrain.Data;
using RatopiaUnitBrain.Registry;
using UnityEngine;

namespace RatopiaUnitBrain.Sensor.Patches
{
    [HarmonyPatch]
    public static class CombatPatches
    {
        [HarmonyPatch(typeof(GameUnit), nameof(GameUnit.SwdAtk_Call))]
        [HarmonyPostfix]
        public static void GameUnit_SwdAtk_Call_Postfix(GameUnit __instance)
        {
            if (__instance == null) return;
            if (UnitRegistry.Instance.GetControlMode(__instance) != BrainControlMode.Autonomous)
                return;

            BrainSensor.Instance.RaiseAttackAction(__instance, isBow: false);
        }

        [HarmonyPatch(typeof(GameEnemy), nameof(GameEnemy.SwdAtk_Call))]
        [HarmonyPostfix]
        public static void GameEnemy_SwdAtk_Call_Postfix(GameEnemy __instance)
        {
            if (__instance == null) return;
            if (UnitRegistry.Instance.GetControlMode(__instance) != BrainControlMode.Autonomous)
                return;

            BrainSensor.Instance.RaiseAttackAction(__instance, isBow: false);
        }

        [HarmonyPatch(typeof(GameUnit), nameof(GameUnit.BowAtk_Call))]
        [HarmonyPostfix]
        public static void GameUnit_BowAtk_Call_Postfix(GameUnit __instance)
        {
            if (__instance == null) return;
            if (UnitRegistry.Instance.GetControlMode(__instance) != BrainControlMode.Autonomous)
                return;

            BrainSensor.Instance.RaiseAttackAction(__instance, isBow: true);
        }

        [HarmonyPatch(typeof(GameUnit), nameof(GameUnit.BeAttacked), new Type[] { typeof(float), typeof(Unit_Attacekd_Tag), typeof(int) })]
        [HarmonyPostfix]
        public static void GameUnit_BeAttacked_Postfix(GameUnit __instance, float dmg)
        {
            if (__instance == null) return;
            if (UnitRegistry.Instance.GetControlMode(__instance) != BrainControlMode.Autonomous)
                return;

            bool isDead = __instance.m_CharState == CharState.Death || __instance.m_CurHP <= 0;
            BrainSensor.Instance.RaiseUnitDamagedOrDied(__instance, __instance.m_CurHP, __instance.m_MaxHP, isDead, Vector2.zero);
        }

        [HarmonyPatch(typeof(GameEnemy), nameof(GameEnemy.BeAttacked), new Type[] { typeof(float), typeof(Unit_Attacekd_Tag), typeof(int) })]
        [HarmonyPostfix]
        public static void GameEnemy_BeAttacked_Postfix(GameEnemy __instance, float dmg)
        {
            if (__instance == null) return;
            if (UnitRegistry.Instance.GetControlMode(__instance) != BrainControlMode.Autonomous)
                return;

            bool isDead = __instance.m_CharState == CharState.Death || __instance.m_CurHP <= 0;
            BrainSensor.Instance.RaiseUnitDamagedOrDied(__instance, __instance.m_CurHP, __instance.m_MaxHP, isDead, Vector2.zero);
        }

        [HarmonyPatch(typeof(GameUnit), nameof(GameUnit.DeathCheck), new Type[] { typeof(int) })]
        [HarmonyPostfix]
        public static void GameUnit_DeathCheck_Postfix(GameUnit __instance)
        {
            if (__instance == null) return;
            if (UnitRegistry.Instance.GetControlMode(__instance) != BrainControlMode.Autonomous)
                return;

            if (__instance.m_CharState == CharState.Death || __instance.m_CurHP <= 0)
            {
                BrainSensor.Instance.RaiseUnitDamagedOrDied(__instance, 0f, __instance.m_MaxHP, true, Vector2.zero);
            }
        }

        [HarmonyPatch(typeof(GameEnemy), nameof(GameEnemy.DeathCheck), new Type[] { typeof(int) })]
        [HarmonyPostfix]
        public static void GameEnemy_DeathCheck_Postfix(GameEnemy __instance)
        {
            if (__instance == null) return;
            if (UnitRegistry.Instance.GetControlMode(__instance) != BrainControlMode.Autonomous)
                return;

            if (__instance.m_CharState == CharState.Death || __instance.m_CurHP <= 0)
            {
                BrainSensor.Instance.RaiseUnitDamagedOrDied(__instance, 0f, __instance.m_MaxHP, true, Vector2.zero);
            }
        }

        [HarmonyPatch(typeof(AnimalBody), nameof(AnimalBody.BeAttacked), new Type[] { typeof(float), typeof(Unit_Attacekd_Tag) })]
        [HarmonyPostfix]
        public static void AnimalBody_BeAttacked_Postfix(AnimalBody __instance)
        {
            if (__instance == null) return;
            bool facingRight = __instance.m_Spine != null && __instance.m_Spine.skeleton != null && __instance.m_Spine.skeleton.ScaleX > 0;
            string anim = __instance.m_State.ToString();
            BrainSensor.Instance.RaiseAnimalStateChanged(__instance, __instance.Tf != null ? (Vector2)__instance.Tf.position : Vector2.zero, facingRight, anim, __instance.m_CurHP);
        }

        [HarmonyPatch(typeof(AnimalBody), nameof(AnimalBody.SetDeath), new Type[] { typeof(bool), typeof(Unit_Attacekd_Tag) })]
        [HarmonyPostfix]
        public static void AnimalBody_SetDeath_Postfix(AnimalBody __instance)
        {
            if (__instance == null) return;
            bool facingRight = __instance.m_Spine != null && __instance.m_Spine.skeleton != null && __instance.m_Spine.skeleton.ScaleX > 0;
            BrainSensor.Instance.RaiseAnimalStateChanged(__instance, __instance.Tf != null ? (Vector2)__instance.Tf.position : Vector2.zero, facingRight, "Death", 0f);
        }
    }
}
