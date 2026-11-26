using System;
using HarmonyLib;
using RatopiaUnitBrain.Data;
using RatopiaUnitBrain.Registry;
using UnityEngine;

namespace RatopiaUnitBrain.Sensor.Patches
{
    [HarmonyPatch]
    public static class StatePatches
    {
        [HarmonyPatch(typeof(GameUnit), nameof(GameUnit.SetCharState), new Type[] { typeof(CharState) })]
        [HarmonyPrefix]
        public static void GameUnit_SetCharState_Prefix(GameUnit __instance, out CharState __state)
        {
            __state = __instance != null ? __instance.m_CharState : CharState.None;
        }

        [HarmonyPatch(typeof(GameUnit), nameof(GameUnit.SetCharState), new Type[] { typeof(CharState) })]
        [HarmonyPostfix]
        public static void GameUnit_SetCharState_Postfix(GameUnit __instance, CharState _state, CharState __state)
        {
            if (__instance == null) return;
            if (UnitRegistry.Instance.GetControlMode(__instance) != BrainControlMode.Autonomous)
                return;

            if (__state != _state)
            {
                BrainSensor.Instance.RaiseStateChanged(__instance, __state, _state, __instance.m_AniState);
            }
        }

        [HarmonyPatch(typeof(SpeechBubble), nameof(SpeechBubble.SetSpeechBubble), new Type[] { typeof(SpBubbleState) })]
        [HarmonyPostfix]
        public static void SpeechBubble_SetSpeechBubble1_Postfix(SpeechBubble __instance, SpBubbleState _state)
        {
            if (__instance == null || __instance.m_Master == null) return;
            if (UnitRegistry.Instance.GetControlMode(__instance.m_Master) != BrainControlMode.Autonomous)
                return;

            BrainSensor.Instance.RaiseSpeechBubble(__instance.m_Master, _state, 0);
        }

        [HarmonyPatch(typeof(SpeechBubble), nameof(SpeechBubble.SetSpeechBubble), new Type[] { typeof(SpBubbleState), typeof(int) })]
        [HarmonyPostfix]
        public static void SpeechBubble_SetSpeechBubble2_Postfix(SpeechBubble __instance, SpBubbleState _child, int _level)
        {
            if (__instance == null || __instance.m_Master == null) return;
            if (UnitRegistry.Instance.GetControlMode(__instance.m_Master) != BrainControlMode.Autonomous)
                return;

            BrainSensor.Instance.RaiseSpeechBubble(__instance.m_Master, _child, _level);
        }

        [HarmonyPatch(typeof(SpeechBubble), nameof(SpeechBubble.SpeechBubbleOff))]
        [HarmonyPostfix]
        public static void SpeechBubble_SpeechBubbleOff_Postfix(SpeechBubble __instance)
        {
            if (__instance == null || __instance.m_Master == null) return;
            if (UnitRegistry.Instance.GetControlMode(__instance.m_Master) != BrainControlMode.Autonomous)
                return;

            BrainSensor.Instance.RaiseSpeechBubble(__instance.m_Master, SpBubbleState.None, 0);
        }
    }
}
