using UnityEngine;

namespace RatopiaUnitBrain.Data
{
    public struct UnitActionCommand
    {
        public UnitTaskType ActionType;
        public AniState AniState;
        public string AnimationName;
        public bool Loop;
        public Vector2 TargetPos;
        public bool ShowWorkGauge;
        public float WorkGaugeDuration;
        public TileType CarriedItem;
        public int CarriedCount;
        public bool InBuilding;
        public bool FacingRight;

        public static UnitActionCommand Stop(AniState idleState = AniState.Idle)
        {
            return new UnitActionCommand
            {
                ActionType = UnitTaskType.Idle,
                AniState = idleState,
                AnimationName = "Idle",
                Loop = true
            };
        }

        public static UnitActionCommand Work(UnitTaskType type, AniState aniState, string animName, float duration, Vector2 targetPos)
        {
            return new UnitActionCommand
            {
                ActionType = type,
                AniState = aniState,
                AnimationName = animName,
                Loop = true,
                TargetPos = targetPos,
                ShowWorkGauge = true,
                WorkGaugeDuration = duration
            };
        }

        public static UnitActionCommand Attack(bool isBow, Vector2 targetPos, bool facingRight)
        {
            return new UnitActionCommand
            {
                ActionType = UnitTaskType.Attacking,
                AniState = isBow ? AniState.Atk_BowMid : AniState.Attack_Sword_1,
                AnimationName = isBow ? "Attack_Bow" : "AttackCombo_Sword1",
                Loop = false,
                TargetPos = targetPos,
                FacingRight = facingRight
            };
        }
    }
}
