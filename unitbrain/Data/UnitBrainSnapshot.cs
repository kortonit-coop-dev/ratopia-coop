using UnityEngine;

namespace RatopiaUnitBrain.Data
{
    public struct UnitBrainSnapshot
    {
        public NetworkUnitId UnitId;
        public Vector2 Position;
        public float Speed;
        public bool IsMoving;
        public bool FacingRight;
        public float CurHP;
        public float MaxHP;
        public CharState CharState;
        public AniState AniState;
        public string CurrentAnimName;
        public TileType CarriedItem;
        public int CarriedCount;
        public bool IsWorking;
        public float WorkProgress;
        public bool IsInBuilding;

        public static UnitBrainSnapshot FromUnit(GameUnit unit)
        {
            if (unit == null) return default;

            var snapshot = new UnitBrainSnapshot
            {
                UnitId = NetworkUnitId.FromUnit(unit),
                Position = unit.Tf != null ? (Vector2)unit.Tf.position : (Vector2)unit.transform.position,
                Speed = unit.m_Speed > 0.1f ? unit.m_Speed : 3.5f,
                IsMoving = unit.IsMoveState(),
                CurHP = unit.m_CurHP,
                MaxHP = unit.m_MaxHP,
                CharState = unit.m_CharState,
                AniState = unit.m_AniState,
                CurrentAnimName = string.Empty,
                CarriedItem = TileType.None,
                CarriedCount = 0,
                IsWorking = false,
                WorkProgress = 0f,
                IsInBuilding = false
            };

            if (unit.m_Spine != null && unit.m_Spine.skeleton != null)
            {
                snapshot.FacingRight = unit.m_Spine.skeleton.ScaleX > 0;
                if (unit.m_Spine.AnimationState != null)
                {
                    var track = unit.m_Spine.AnimationState.GetCurrent(0);
                    if (track != null && track.Animation != null)
                    {
                        snapshot.CurrentAnimName = track.Animation.Name ?? string.Empty;
                    }
                }
            }

            if (unit.List_Gathering != null && unit.List_Gathering.Count > 0)
            {
                snapshot.CarriedItem = unit.List_Gathering[0].m_Type;
                snapshot.CarriedCount = unit.List_Gathering.Count;
            }

            if (unit.m_WorkTimeGauge != null && unit.m_WorkTimeGauge.IsGaugeAcitve())
            {
                snapshot.IsWorking = true;
                if (unit.m_WorkTimeGauge.m_Gauge != null)
                {
                    snapshot.WorkProgress = unit.m_WorkTimeGauge.m_Gauge.size.x;
                }
            }

            if (unit.Tf != null && unit.Tf.position.z >= 0.35f)
            {
                snapshot.IsInBuilding = true;
            }

            return snapshot;
        }
    }
}
