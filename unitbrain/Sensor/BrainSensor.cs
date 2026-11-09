using System;
using System.Collections.Generic;
using RatopiaUnitBrain.Data;
using UnityEngine;

namespace RatopiaUnitBrain.Sensor
{
    public class BrainSensor
    {
        private static BrainSensor? _instance;
        public static BrainSensor Instance => _instance ??= new BrainSensor();

        /// <summary>
        /// Fired when any unit begins moving along an A* path (List_MoveNode).
        /// </summary>
        public event Action<GameUnit, List<Vector2>, float>? OnPathCommitted;

        /// <summary>
        /// Fired when unit CharState or AniState changes.
        /// </summary>
        public event Action<GameUnit, CharState, CharState, AniState>? OnStateChanged;

        /// <summary>
        /// Fired when unit starts a high-level task (mining, gathering, building, eating, sleeping).
        /// </summary>
        public event Action<GameUnit, TaskDescriptor>? OnTaskStarted;

        /// <summary>
        /// Fired when WorkTimeGauge starts, updates, or stops (active, progress, duration).
        /// </summary>
        public event Action<GameUnit, bool, float, float>? OnWorkGaugeUpdated;

        /// <summary>
        /// Fired when resources carried in hands/backpack change (TileType, count).
        /// </summary>
        public event Action<GameUnit, TileType, int>? OnBackpackChanged;

        /// <summary>
        /// Fired when thought / emotion bubble appears or changes.
        /// </summary>
        public event Action<GameUnit, SpBubbleState, int>? OnSpeechBubble;

        /// <summary>
        /// Fired when unit receives damage or dies.
        /// </summary>
        public event Action<GameUnit, float, float, bool, Vector2>? OnUnitDamagedOrDied;

        /// <summary>
        /// Fired when unit performs attack (isBow: true for bow, false for sword/melee).
        /// </summary>
        public event Action<GameUnit, bool>? OnAttackAction;

        /// <summary>
        /// Fired for animal state synchronization.
        /// </summary>
        public event Action<AnimalBody, Vector2, bool, string, float>? OnAnimalStateChanged;

        public void RaisePathCommitted(GameUnit unit, List<Vector2> path, float speed)
        {
            if (unit == null || path == null) return;
            OnPathCommitted?.Invoke(unit, path, speed);
        }

        public void RaiseStateChanged(GameUnit unit, CharState oldState, CharState newState, AniState aniState)
        {
            if (unit == null) return;
            OnStateChanged?.Invoke(unit, oldState, newState, aniState);
        }

        public void RaiseTaskStarted(GameUnit unit, TaskDescriptor task)
        {
            if (unit == null || task == null) return;
            OnTaskStarted?.Invoke(unit, task);
        }

        public void RaiseWorkGaugeUpdated(GameUnit unit, bool active, float progress, float duration)
        {
            if (unit == null) return;
            OnWorkGaugeUpdated?.Invoke(unit, active, progress, duration);
        }

        public void RaiseBackpackChanged(GameUnit unit, TileType carriedItem, int count)
        {
            if (unit == null) return;
            OnBackpackChanged?.Invoke(unit, carriedItem, count);
        }

        public void RaiseSpeechBubble(GameUnit unit, SpBubbleState bubble, int level)
        {
            if (unit == null) return;
            OnSpeechBubble?.Invoke(unit, bubble, level);
        }

        public void RaiseUnitDamagedOrDied(GameUnit unit, float curHp, float maxHp, bool isDead, Vector2 knockback)
        {
            if (unit == null) return;
            OnUnitDamagedOrDied?.Invoke(unit, curHp, maxHp, isDead, knockback);
        }

        public void RaiseAttackAction(GameUnit unit, bool isBow)
        {
            if (unit == null) return;
            OnAttackAction?.Invoke(unit, isBow);
        }

        public void RaiseAnimalStateChanged(AnimalBody animal, Vector2 pos, bool facingRight, string animName, float curHp)
        {
            if (animal == null) return;
            OnAnimalStateChanged?.Invoke(animal, pos, facingRight, animName, curHp);
        }
    }
}
