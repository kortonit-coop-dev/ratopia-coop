using UnityEngine;

namespace RatopiaUnitBrain.Data
{
    public enum UnitTaskType : byte
    {
        None = 0,
        Idle = 1,
        Moving = 2,
        Mining = 3,
        Logging = 4,
        Constructing = 5,
        Repairing = 6,
        Demolition = 7,
        Gathering = 8,
        Crafting = 9,
        Eating = 10,
        Sleeping = 11,
        Attacking = 12,
        EnterBuilding = 13,
        ExitBuilding = 14,
        Downed = 15,
        Death = 16
    }

    public class TaskDescriptor
    {
        public UnitTaskType TaskType { get; set; } = UnitTaskType.None;
        public Vector2 TargetPos { get; set; }
        public int TargetEntityId { get; set; }
        public string ParamString { get; set; } = string.Empty;
        public float Duration { get; set; }
        public TileType ItemType { get; set; } = TileType.None;

        public TaskDescriptor() { }

        public TaskDescriptor(UnitTaskType taskType, Vector2 targetPos, int targetEntityId = 0, string param = "", float duration = 0f, TileType itemType = TileType.None)
        {
            TaskType = taskType;
            TargetPos = targetPos;
            TargetEntityId = targetEntityId;
            ParamString = param;
            Duration = duration;
            ItemType = itemType;
        }

        public override string ToString()
        {
            return $"Task({TaskType}, Pos:{TargetPos}, Param:{ParamString})";
        }
    }
}
