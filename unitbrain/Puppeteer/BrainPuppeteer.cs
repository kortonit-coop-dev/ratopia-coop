using System;
using System.Collections.Generic;
using HarmonyLib;
using MEC;
using RatopiaUnitBrain.Data;
using RatopiaUnitBrain.Registry;
using UnityEngine;

namespace RatopiaUnitBrain.Puppeteer
{
    public class BrainPuppeteer
    {
        private static BrainPuppeteer? _instance;
        public static BrainPuppeteer Instance => _instance ??= new BrainPuppeteer();

        private class PuppetMotionState
        {
            public readonly List<Vector2> Waypoints = new List<Vector2>(16);
            public Vector2 AuthoritativePos;
            public Vector2 Velocity;
            public float MoveSpeed = 3.5f;
            public float LastSnapshotTime;
            public bool IsFollowingPath;
            public bool IsHostMoving;
            public bool InBuilding;
            public bool Initialized;
            public int LastNodeX = int.MinValue;
            public int LastNodeY = int.MinValue;
        }

        private class AnimalMotionState
        {
            public Vector2 TargetPos;
            public Vector2 Velocity;
            public float LastPacketTime;
            public bool Initialized;
        }

        private readonly Dictionary<GameUnit, PuppetMotionState> _puppetStates = new Dictionary<GameUnit, PuppetMotionState>(256);
        private readonly Dictionary<AnimalBody, AnimalMotionState> _animalStates = new Dictionary<AnimalBody, AnimalMotionState>(128);
        private readonly Dictionary<GameUnit, CoroutineHandle> _gaugeCoroutines = new Dictionary<GameUnit, CoroutineHandle>();
        private readonly List<GameUnit> _deadUnitKeys = new List<GameUnit>(32);
        private readonly List<AnimalBody> _deadAnimalKeys = new List<AnimalBody>(32);

        public void ClearAllPuppetStates()
        {
            _puppetStates.Clear();
            _animalStates.Clear();
        }

        /// <summary>
        /// Immediately terminates all local autonomous movement, falling, work, and building coroutines on a unit.
        /// </summary>
        public void ShutdownAutonomousCoroutines(GameUnit unit)
        {
            if (unit == null) return;
            try
            {
                if (Timing.IsRunning(unit._corMoving))
                    Timing.KillCoroutines(unit._corMoving);
                if (Timing.IsRunning(unit._corFallDown))
                    Timing.KillCoroutines(unit._corFallDown);
                if (Timing.IsRunning(unit._corMoveFunc))
                    Timing.KillCoroutines(unit._corMoveFunc);
                if (Timing.IsRunning(unit._corWorking))
                    Timing.KillCoroutines(unit._corWorking);
                if (Timing.IsRunning(unit._corUseBuilding))
                    Timing.KillCoroutines(unit._corUseBuilding);
                if (Timing.IsRunning(unit._corUseBuilding_Deco))
                    Timing.KillCoroutines(unit._corUseBuilding_Deco);

                if (unit._cormoveInfo != null)
                {
                    unit._cormoveInfo.end = true;
                }

                if (unit.List_State != null)
                {
                    unit.List_State.Remove(CitizenState.Move);
                    unit.List_State.Remove(CitizenState.FallDown);
                }
            }
            catch { }
        }

        /// <summary>
        /// Immediately terminates all local autonomous AI and movement coroutines on an AnimalBody.
        /// </summary>
        public void ShutdownAnimalCoroutines(AnimalBody animal)
        {
            if (animal == null) return;
            try
            {
                if (Timing.IsRunning(animal._corhandle))
                    Timing.KillCoroutines(animal._corhandle);
                if (Timing.IsRunning(animal._cormoveinfo))
                    Timing.KillCoroutines(animal._cormoveinfo);
                if (Timing.IsRunning(animal._corFallDown))
                    Timing.KillCoroutines(animal._corFallDown);
                if (Timing.IsRunning(animal._corMoveFunc))
                    Timing.KillCoroutines(animal._corMoveFunc);

                if (animal._corFall != null)
                {
                    animal._corFall.end = true;
                }

                if (animal.m_Box != null && !animal.m_Box.enabled && animal.m_State != AnimalState.Death)
                {
                    animal.m_Box.enabled = true;
                }
            }
            catch { }
        }

        private PuppetMotionState GetOrCreateState(GameUnit unit, Vector2 initialPos)
        {
            if (!_puppetStates.TryGetValue(unit, out var state))
            {
                state = new PuppetMotionState
                {
                    AuthoritativePos = initialPos,
                    LastSnapshotTime = Time.unscaledTime,
                    Initialized = true
                };
                _puppetStates[unit] = state;
            }
            return state;
        }

        /// <summary>
        /// Records a Host-calculated waypoint path for smooth per-frame execution on the Client without starting conflicting vanilla coroutines.
        /// </summary>
        public void ExecuteMovePath(GameUnit unit, List<Vector2> pathNodes, Vector2 hostPos, float speed)
        {
            if (unit == null || pathNodes == null || pathNodes.Count == 0) return;

            try
            {
                UnitRegistry.Instance.SetControlMode(unit, BrainControlMode.RemotePuppet);
                ShutdownAutonomousCoroutines(unit);

                if (speed > 0.1f)
                {
                    unit.m_Speed = speed;
                }

                var state = GetOrCreateState(unit, hostPos);
                state.AuthoritativePos = hostPos;
                state.MoveSpeed = speed > 0.1f ? speed : (unit.m_Speed > 0.1f ? unit.m_Speed : 3.5f);
                state.LastSnapshotTime = Time.unscaledTime;
                state.IsHostMoving = true;

                if (unit.Tf != null)
                {
                    Vector2 currentPos = unit.Tf.position;
                    float distToHost = Vector2.Distance(currentPos, hostPos);
                    if (distToHost > 15f)
                    {
                        // Only snap on initial spawn or extreme teleport (> 15 tiles)
                        unit.Tf.position = new Vector3(hostPos.x, hostPos.y, unit.Tf.position.z);
                        currentPos = hostPos;
                    }

                    // Populate waypoints starting from the node ahead of current position
                    state.Waypoints.Clear();
                    int startIdx = 0;
                    float bestDist = float.MaxValue;
                    for (int i = 0; i < pathNodes.Count; i++)
                    {
                        float d = Vector2.Distance(currentPos, pathNodes[i]);
                        if (d < bestDist)
                        {
                            bestDist = d;
                            startIdx = i;
                        }
                    }

                    // If we are already very close to startIdx and there are more nodes, advance to next node
                    if (startIdx < pathNodes.Count - 1 && bestDist < 0.2f)
                    {
                        startIdx++;
                    }

                    for (int i = startIdx; i < pathNodes.Count; i++)
                    {
                        state.Waypoints.Add(pathNodes[i]);
                    }

                    state.IsFollowingPath = state.Waypoints.Count > 0;

                    if (state.Waypoints.Count > 0)
                    {
                        float dx = state.Waypoints[0].x - currentPos.x;
                        if (Mathf.Abs(dx) > 0.05f)
                        {
                            unit.FlipX(dx > 0f);
                        }
                    }
                }

                PlayMoveAnimation(unit);
            }
            catch (Exception ex)
            {
                UnitBrainPlugin.Log?.LogWarning($"[BrainPuppeteer] ExecuteMovePath error: {ex.Message}");
            }
        }

        private void PlayMoveAnimation(GameUnit unit)
        {
            if (unit == null || unit.m_Spine == null || unit.m_Spine.AnimationState == null) return;
            var track = unit.m_Spine.AnimationState.GetCurrent(0);
            string currentAnim = track?.Animation?.Name ?? string.Empty;
            if (currentAnim.IndexOf("Move", StringComparison.OrdinalIgnoreCase) >= 0 ||
                currentAnim.IndexOf("Run", StringComparison.OrdinalIgnoreCase) >= 0 ||
                currentAnim.IndexOf("Walk", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return;
            }

            if (!PlaySpineAnim(unit, "Move", true))
            {
                if (!PlaySpineAnim(unit, "Run", true))
                {
                    unit.SetAniState(AniState.Walking, true, false);
                }
            }
        }

        public void ExecuteAction(GameUnit unit, UnitActionCommand cmd)
        {
            if (unit == null) return;

            try
            {
                UnitRegistry.Instance.SetControlMode(unit, BrainControlMode.RemotePuppet);
                ShutdownAutonomousCoroutines(unit);

                var state = GetOrCreateState(unit, cmd.TargetPos != Vector2.zero ? cmd.TargetPos : (unit.Tf != null ? (Vector2)unit.Tf.position : Vector2.zero));

                if (cmd.ActionType != UnitTaskType.Moving)
                {
                    state.Waypoints.Clear();
                    state.IsFollowingPath = false;
                    state.IsHostMoving = false;
                    state.Velocity = Vector2.zero;
                }

                if (cmd.TargetPos != Vector2.zero)
                {
                    state.AuthoritativePos = cmd.TargetPos;
                    state.LastSnapshotTime = Time.unscaledTime;

                    if (unit.Tf != null && Vector2.Distance(unit.Tf.position, cmd.TargetPos) > 15f)
                    {
                        unit.Tf.position = new Vector3(cmd.TargetPos.x, cmd.TargetPos.y, unit.Tf.position.z);
                    }
                }

                // Building Z-Order (enter/exit building)
                state.InBuilding = cmd.InBuilding;
                if (cmd.ActionType == UnitTaskType.EnterBuilding)
                {
                    state.InBuilding = true;
                    unit.SetZ_Order(true);
                }
                else if (cmd.ActionType == UnitTaskType.ExitBuilding)
                {
                    state.InBuilding = false;
                    unit.SetZ_Order(false);
                }
                else
                {
                    unit.SetZ_Order(cmd.InBuilding);
                }

                // Facing direction (true = right, false = left)
                unit.FlipX(cmd.FacingRight);

                // Animation
                if (!string.IsNullOrEmpty(cmd.AnimationName))
                {
                    if (!PlaySpineAnim(unit, cmd.AnimationName, cmd.Loop))
                    {
                        unit.SetAniState(cmd.AniState, cmd.Loop, false);
                    }
                }
                else if (cmd.AniState != AniState.End)
                {
                    unit.SetAniState(cmd.AniState, cmd.Loop, false);
                }

                // Work gauge
                if (cmd.ShowWorkGauge && cmd.WorkGaugeDuration > 0f)
                {
                    StartPuppetWorkGauge(unit, cmd.WorkGaugeDuration);
                }
                else if (!cmd.ShowWorkGauge && unit.m_WorkTimeGauge != null && unit.m_WorkTimeGauge.IsGaugeAcitve())
                {
                    unit.m_WorkTimeGauge.StopGauge(TypeOrder.Other);
                }

                // Carried item visual
                ApplyCarriedItemVisual(unit, cmd.CarriedItem, cmd.CarriedCount);
            }
            catch (Exception ex)
            {
                UnitBrainPlugin.Log?.LogWarning($"[BrainPuppeteer] ExecuteAction error: {ex.Message}");
            }
        }

        public void ApplyStateSnapshot(GameUnit unit, UnitBrainSnapshot snapshot)
        {
            if (unit == null) return;

            try
            {
                UnitRegistry.Instance.SetControlMode(unit, BrainControlMode.RemotePuppet);
                ShutdownAutonomousCoroutines(unit);

                var state = GetOrCreateState(unit, snapshot.Position);
                float now = Time.unscaledTime;
                float dtPacket = now - state.LastSnapshotTime;

                if (snapshot.IsMoving && dtPacket > 0.01f && dtPacket < 1.0f)
                {
                    Vector2 rawVel = (snapshot.Position - state.AuthoritativePos) / dtPacket;
                    float maxSpd = Mathf.Max(snapshot.Speed, 3.5f) * 1.5f;
                    if (rawVel.sqrMagnitude > maxSpd * maxSpd)
                    {
                        rawVel = rawVel.normalized * maxSpd;
                    }
                    state.Velocity = Vector2.Lerp(state.Velocity, rawVel, 0.6f);
                }
                else if (!snapshot.IsMoving)
                {
                    state.Velocity = Vector2.zero;
                    state.Waypoints.Clear();
                    state.IsFollowingPath = false;
                }

                state.AuthoritativePos = snapshot.Position;
                state.MoveSpeed = snapshot.Speed > 0.1f ? snapshot.Speed : (unit.m_Speed > 0.1f ? unit.m_Speed : 3.5f);
                state.IsHostMoving = snapshot.IsMoving;
                state.InBuilding = snapshot.IsInBuilding;
                state.LastSnapshotTime = now;

                // Only snap on initial spawn or extreme teleport (> 15 tiles)
                if (unit.Tf != null && Vector2.Distance(unit.Tf.position, snapshot.Position) > 15f)
                {
                    float z = snapshot.IsInBuilding ? 0.4f : 0f;
                    unit.Tf.position = new Vector3(snapshot.Position.x, snapshot.Position.y, z);
                }

                // HP & Death
                float previousHp = unit.m_CurHP;
                float newHp = snapshot.CurHP;
                if (snapshot.MaxHP > 0) unit.m_MaxHP = snapshot.MaxHP;
                unit.m_CurHP = newHp;

                if (unit.m_UnitHP != null && unit.m_MaxHP > 0)
                {
                    unit.m_UnitHP.UpdateHp(unit.m_CurHP / unit.m_MaxHP);
                }

                // If unit took damage on Host (e.g. from Host attacks, traps, soldiers), show hit reactions on Client!
                if (newHp < previousHp && previousHp > 0f)
                {
                    float damageTaken = previousHp - newHp;
                    try
                    {
                        if (unit.m_UnitHP != null)
                        {
                            if (unit.m_UnitHP.Obj == null) unit.m_UnitHP.Obj = unit.m_UnitHP.gameObject;
                            if (unit.m_UnitHP.Tf == null) unit.m_UnitHP.Tf = unit.m_UnitHP.transform;
                        }
                        unit.HpShowUp();
                        unit.HitBlink(1);

                        if (GameMgr.Instance != null && GameMgr.Instance._PoolMgr != null && GameMgr.Instance._PoolMgr.Pool_DmgEffect != null && unit.Tf != null)
                        {
                            var dmgObj = GameMgr.Instance._PoolMgr.Pool_DmgEffect.GetNextObj();
                            if (dmgObj != null)
                            {
                                var dmgComp = dmgObj.GetComponent<DmgEffect>();
                                if (dmgComp != null)
                                {
                                    dmgComp.SetDmgEffect(-(int)damageTaken, new Vector3(0f, 0.8f, 0f), unit.Tf);
                                }
                            }
                        }
                    }
                    catch { }
                }
                else if (unit.m_CurHP < unit.m_MaxHP && unit.m_CurHP > 0f)
                {
                    try
                    {
                        if (unit.m_UnitHP != null && unit.m_UnitHP.Obj != null && !unit.m_UnitHP.Obj.activeSelf)
                        {
                            unit.HpShowUp();
                        }
                    }
                    catch { }
                }

                if (snapshot.CharState == CharState.Death && unit.m_CharState != CharState.Death)
                {
                    unit.DeathCheck(0);
                    return;
                }

                if (unit.m_CharState != snapshot.CharState && snapshot.CharState != CharState.None)
                {
                    unit.SetCharState(snapshot.CharState);
                }

                // Building Z-Order
                bool currentlyInBuilding = unit.Tf != null && unit.Tf.position.z >= 0.35f;
                if (currentlyInBuilding != snapshot.IsInBuilding)
                {
                    unit.SetZ_Order(snapshot.IsInBuilding);
                }

                // Facing direction (if not actively following waypoints that dictate facing)
                if (!state.IsFollowingPath && unit.m_Spine != null && unit.m_Spine.skeleton != null)
                {
                    bool curFacingRight = unit.m_Spine.skeleton.ScaleX > 0;
                    if (curFacingRight != snapshot.FacingRight)
                    {
                        unit.FlipX(snapshot.FacingRight);
                    }
                }

                // Animation sync
                if (!string.IsNullOrEmpty(snapshot.CurrentAnimName) && unit.m_Spine != null && unit.m_Spine.AnimationState != null)
                {
                    var track = unit.m_Spine.AnimationState.GetCurrent(0);
                    string curAnim = track?.Animation?.Name ?? string.Empty;
                    if (curAnim != snapshot.CurrentAnimName)
                    {
                        bool loop = !snapshot.CurrentAnimName.Contains("Attack") && !snapshot.CurrentAnimName.Contains("Atk") && !snapshot.CurrentAnimName.Contains("Death");
                        if (!PlaySpineAnim(unit, snapshot.CurrentAnimName, loop))
                        {
                            unit.SetAniState(snapshot.AniState, loop, false);
                        }
                    }
                }

                // Carried resource icon
                ApplyCarriedItemVisual(unit, snapshot.CarriedItem, snapshot.CarriedCount);

                // Work gauge progress
                if (snapshot.IsWorking)
                {
                    SetPuppetWorkGaugeProgress(unit, snapshot.WorkProgress);
                }
                else if (unit.m_WorkTimeGauge != null && unit.m_WorkTimeGauge.IsGaugeAcitve() && !_gaugeCoroutines.ContainsKey(unit))
                {
                    unit.m_WorkTimeGauge.StopGauge(TypeOrder.Other);
                }
            }
            catch (Exception ex)
            {
                UnitBrainPlugin.Log?.LogWarning($"[BrainPuppeteer] ApplyStateSnapshot error: {ex.Message}");
            }
        }

        /// <summary>
        /// Executed every render frame (60-144 FPS) by UnitBrainPlugin.Update() to smoothly advance all puppet units and animals without any teleporting or 4 Hz stepping.
        /// </summary>
        public void UpdatePuppets(float deltaTime)
        {
            if (deltaTime <= 0f || deltaTime > 0.5f) return;

            // 1. Update GameUnit puppets
            if (_puppetStates.Count > 0)
            {
                _deadUnitKeys.Clear();
                float now = Time.unscaledTime;

                foreach (var kvp in _puppetStates)
                {
                    GameUnit unit = kvp.Key;
                    PuppetMotionState state = kvp.Value;

                    if (unit == null || unit.gameObject == null || !unit.gameObject.activeInHierarchy || unit.Tf == null)
                    {
                        _deadUnitKeys.Add(unit!);
                        continue;
                    }

                    if (UnitRegistry.Instance.GetControlMode(unit) != BrainControlMode.RemotePuppet)
                    {
                        _deadUnitKeys.Add(unit);
                        continue;
                    }

                    // Keep autonomous coroutines suppressed at all times
                    if (Timing.IsRunning(unit._corMoving) || Timing.IsRunning(unit._corFallDown) || Timing.IsRunning(unit._corMoveFunc))
                    {
                        ShutdownAutonomousCoroutines(unit);
                    }

                    Vector2 currentPos = unit.Tf.position;
                    Vector2 nextPos = currentPos;

                    if (state.IsFollowingPath && state.Waypoints.Count > 0)
                    {
                        Vector2 targetNode = state.Waypoints[0];
                        float distToAuth = Vector2.Distance(currentPos, state.AuthoritativePos);

                        // Adaptive speed scaling so puppet naturally catches up or slows down to match Host
                        float speedScale = 1.0f;
                        if (distToAuth > 0.6f && now - state.LastSnapshotTime < 0.4f)
                        {
                            speedScale = Mathf.Clamp(1.0f + (distToAuth - 0.5f) * 0.35f, 0.85f, 1.45f);
                        }

                        float step = state.MoveSpeed * speedScale * deltaTime;
                        nextPos = Vector2.MoveTowards(currentPos, targetNode, step);

                        // Gentle attraction toward authoritative Host position to prevent perpendicular drift
                        if (now - state.LastSnapshotTime < 0.3f && distToAuth > 0.15f && distToAuth < 4.0f)
                        {
                            nextPos = Vector2.Lerp(nextPos, state.AuthoritativePos, 1f - Mathf.Exp(-6f * deltaTime));
                        }

                        float dx = nextPos.x - currentPos.x;
                        if (Mathf.Abs(dx) > 0.002f)
                        {
                            unit.FlipX(dx > 0f);
                        }

                        if (Vector2.Distance(nextPos, targetNode) <= 0.06f)
                        {
                            state.Waypoints.RemoveAt(0);
                            if (state.Waypoints.Count == 0)
                            {
                                state.IsFollowingPath = false;
                            }
                        }
                    }
                    else
                    {
                        // Dead-reckoning + exponential frame-rate independent smoothing toward AuthoritativePos
                        float age = Mathf.Clamp(now - state.LastSnapshotTime, 0f, 0.15f);
                        Vector2 target = state.IsHostMoving
                            ? state.AuthoritativePos + state.Velocity * age
                            : state.AuthoritativePos;

                        float dist = Vector2.Distance(currentPos, target);
                        if (dist > 8.0f)
                        {
                            nextPos = target;
                        }
                        else if (dist > 0.005f)
                        {
                            float speedMult = dist > 1.5f ? Mathf.Clamp(dist * 1.5f, 1.35f, 4.0f) : 1.35f;
                            Vector2 moved = Vector2.MoveTowards(currentPos, target, state.MoveSpeed * speedMult * deltaTime);
                            float lambda = dist > 1.5f ? 24f : 16f;
                            float smoothFactor = 1f - Mathf.Exp(-lambda * deltaTime);
                            nextPos = Vector2.Lerp(moved, target, smoothFactor);

                            if (state.IsHostMoving)
                            {
                                float dx = nextPos.x - currentPos.x;
                                if (Mathf.Abs(dx) > 0.003f)
                                {
                                    unit.FlipX(dx > 0f);
                                }
                            }
                        }
                    }

                    float z = state.InBuilding ? 0.4f : 0f;
                    unit.Tf.position = new Vector3(nextPos.x, nextPos.y, z);

                    int nodeX = Mathf.RoundToInt(nextPos.x);
                    int nodeY = Mathf.RoundToInt(nextPos.y);
                    if (nodeX != state.LastNodeX || nodeY != state.LastNodeY)
                    {
                        state.LastNodeX = nodeX;
                        state.LastNodeY = nodeY;
                        try
                        {
                            unit.NodeUpdate();
                        }
                        catch { }
                    }
                }

                for (int i = 0; i < _deadUnitKeys.Count; i++)
                {
                    if (_deadUnitKeys[i] != null)
                        _puppetStates.Remove(_deadUnitKeys[i]);
                }
            }

            // 2. Update AnimalBody puppets
            if (_animalStates.Count > 0)
            {
                _deadAnimalKeys.Clear();
                float now = Time.unscaledTime;

                foreach (var kvp in _animalStates)
                {
                    AnimalBody animal = kvp.Key;
                    AnimalMotionState state = kvp.Value;

                    if (animal == null || animal.gameObject == null || !animal.gameObject.activeInHierarchy || animal.Tf == null)
                    {
                        _deadAnimalKeys.Add(animal!);
                        continue;
                    }

                    ShutdownAnimalCoroutines(animal);

                    Vector2 currentPos = animal.Tf.position;
                    float age = Mathf.Clamp(now - state.LastPacketTime, 0f, 0.15f);
                    Vector2 predicted = state.TargetPos + state.Velocity * age;
                    float dist = Vector2.Distance(currentPos, predicted);

                    if (dist > 0.005f)
                    {
                        float smoothFactor = 1f - Mathf.Exp(-14f * deltaTime);
                        Vector2 nextPos = Vector2.Lerp(currentPos, predicted, smoothFactor);
                        animal.Tf.position = new Vector3(nextPos.x, nextPos.y, animal.Tf.position.z);
                        float dx = nextPos.x - currentPos.x;
                        if (Mathf.Abs(dx) > 0.003f)
                        {
                            animal.FlipX(dx > 0f);
                        }
                        try
                        {
                            animal.NodeUpdate();
                        }
                        catch { }
                    }
                }

                for (int i = 0; i < _deadAnimalKeys.Count; i++)
                {
                    if (_deadAnimalKeys[i] != null)
                        _animalStates.Remove(_deadAnimalKeys[i]);
                }
            }
        }

        public void ExecuteSpeechBubble(GameUnit unit, SpBubbleState bubbleState, int level)
        {
            if (unit == null || unit.m_SpeechBubble == null) return;
            try
            {
                if (bubbleState == SpBubbleState.None)
                {
                    unit.m_SpeechBubble.SpeechBubbleOff();
                }
                else if (level > 0)
                {
                    unit.m_SpeechBubble.SetSpeechBubble(bubbleState, level);
                }
                else
                {
                    unit.m_SpeechBubble.SetSpeechBubble(bubbleState);
                }
            }
            catch { }
        }

        public void ExecuteAnimalPuppet(AnimalBody animal, Vector2 pos, bool facingRight, string animName, float hp)
        {
            if (animal == null) return;
            try
            {
                ShutdownAnimalCoroutines(animal);

                float now = Time.unscaledTime;
                if (!_animalStates.TryGetValue(animal, out var state))
                {
                    state = new AnimalMotionState
                    {
                        TargetPos = pos,
                        LastPacketTime = now,
                        Initialized = true
                    };
                    _animalStates[animal] = state;
                }
                else
                {
                    float dt = now - state.LastPacketTime;
                    if (dt > 0.01f && dt < 1.0f)
                    {
                        Vector2 rawVel = (pos - state.TargetPos) / dt;
                        if (rawVel.sqrMagnitude > 64f) rawVel = rawVel.normalized * 8f;
                        state.Velocity = Vector2.Lerp(state.Velocity, rawVel, 0.5f);
                    }
                    state.TargetPos = pos;
                    state.LastPacketTime = now;
                }

                if (animal.Tf != null && Vector2.Distance(animal.Tf.position, pos) > 15f)
                {
                    animal.Tf.position = new Vector3(pos.x, pos.y, animal.Tf.position.z);
                }

                float prevAnimalHp = animal.m_CurHP;
                animal.m_CurHP = hp;
                if (animal.m_UnitHP != null && animal.m_MaxHP > 0)
                {
                    animal.m_UnitHP.UpdateHp(animal.m_CurHP / animal.m_MaxHP);
                }

                if (hp < prevAnimalHp && prevAnimalHp > 0f)
                {
                    float damageTaken = prevAnimalHp - hp;
                    try
                    {
                        if (animal.m_UnitHP != null)
                        {
                            if (animal.m_UnitHP.Obj == null) animal.m_UnitHP.Obj = animal.m_UnitHP.gameObject;
                            if (animal.m_UnitHP.Tf == null) animal.m_UnitHP.Tf = animal.m_UnitHP.transform;
                        }
                        animal.HpShowUp();
                        animal.HitBlink(1);

                        if (GameMgr.Instance != null && GameMgr.Instance._PoolMgr != null && GameMgr.Instance._PoolMgr.Pool_DmgEffect != null && animal.Tf != null)
                        {
                            var dmgObj = GameMgr.Instance._PoolMgr.Pool_DmgEffect.GetNextObj();
                            if (dmgObj != null)
                            {
                                var dmgComp = dmgObj.GetComponent<DmgEffect>();
                                if (dmgComp != null)
                                {
                                    dmgComp.SetDmgEffect(-(int)damageTaken, new Vector3(0f, 0.5f, 0f), animal.Tf);
                                }
                            }
                        }
                    }
                    catch { }
                }
                else if (animal.m_CurHP < animal.m_MaxHP && animal.m_CurHP > 0f)
                {
                    try
                    {
                        if (animal.m_UnitHP != null && animal.m_UnitHP.Obj != null && !animal.m_UnitHP.Obj.activeSelf)
                        {
                            animal.HpShowUp();
                        }
                    }
                    catch { }
                }

                animal.FlipX(facingRight);

                if (!string.IsNullOrEmpty(animName) && Enum.TryParse<AnimalState>(animName, out var aState))
                {
                    if (animal.m_State != aState)
                    {
                        bool loop = aState != AnimalState.Death && aState != AnimalState.Attack && aState != AnimalState.Hit;
                        animal.SetAniState(aState, loop);
                    }
                }
            }
            catch { }
        }

        public void StartPuppetWorkGauge(GameUnit unit, float duration)
        {
            if (unit == null || unit.m_WorkTimeGauge == null) return;
            try
            {
                if (_gaugeCoroutines.TryGetValue(unit, out var existing) && Timing.IsRunning(existing))
                {
                    Timing.KillCoroutines(existing);
                }

                var handle = Timing.RunCoroutine(PuppetGaugeCoroutine(unit, duration));
                _gaugeCoroutines[unit] = handle;
            }
            catch { }
        }

        public void SetPuppetWorkGaugeProgress(GameUnit unit, float normalizedWidth)
        {
            if (unit == null || unit.m_WorkTimeGauge == null) return;
            try
            {
                var gauge = unit.m_WorkTimeGauge;
                if (gauge.gameObject != null && !gauge.gameObject.activeSelf)
                {
                    gauge.gameObject.SetActive(true);
                }
                if (gauge.m_Gauge != null)
                {
                    gauge.m_Gauge.size = new Vector2(Mathf.Clamp(normalizedWidth, 0f, 0.88f), 0.16f);
                }
            }
            catch { }
        }

        private IEnumerator<float> PuppetGaugeCoroutine(GameUnit unit, float duration)
        {
            var gauge = unit.m_WorkTimeGauge;
            if (gauge == null || gauge.gameObject == null || gauge.m_Gauge == null) yield break;

            gauge.gameObject.SetActive(true);
            float elapsed = 0f;
            float maxDur = Mathf.Max(0.2f, duration);

            while (elapsed < maxDur && unit != null && gauge.gameObject != null && gauge.gameObject.activeSelf)
            {
                elapsed += Time.deltaTime;
                float ratio = Mathf.Clamp01(elapsed / maxDur);
                gauge.m_Gauge.size = new Vector2(0.88f * ratio, 0.16f);
                yield return 0f;
            }

            if (gauge != null && gauge.gameObject != null)
            {
                gauge.gameObject.SetActive(false);
            }
        }

        public void ApplyCarriedItemVisual(GameUnit unit, TileType itemType, int count)
        {
            if (unit == null) return;
            try
            {
                if (itemType == TileType.None || count <= 0)
                {
                    unit.StickImgSet(null, false);
                }
                else if (Func.Instance != null)
                {
                    var sprite = Func.Instance.LoadSprite($"{itemType}Object");
                    if (sprite != null)
                    {
                        unit.StickImgSet(sprite, true);
                    }
                }
            }
            catch { }
        }

        private bool PlaySpineAnim(GameUnit unit, string animName, bool loop)
        {
            if (unit == null || unit.m_Spine == null || unit.m_Spine.AnimationState == null || unit.m_Spine.Skeleton == null)
                return false;

            var anim = unit.m_Spine.Skeleton.Data.FindAnimation(animName);
            if (anim != null)
            {
                unit.m_Spine.AnimationState.SetAnimation(0, anim, loop);
                return true;
            }
            return false;
        }
    }
}
