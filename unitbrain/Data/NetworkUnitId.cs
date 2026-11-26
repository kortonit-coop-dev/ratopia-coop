using System;

namespace RatopiaUnitBrain.Data
{
    public enum NetworkUnitKind : byte
    {
        Queen = 0,
        Citizen = 1,
        Enemy = 2,
        Animal = 3,
        Drone = 4,
        GBot = 5,
        Alliance = 6,
        SkullRat = 7,
        Ant = 8,
        Unknown = 255
    }

    public readonly struct NetworkUnitId : IEquatable<NetworkUnitId>
    {
        public readonly NetworkUnitKind Kind;
        public readonly int Id;

        public NetworkUnitId(NetworkUnitKind kind, int id)
        {
            Kind = kind;
            Id = id;
        }

        public long Packed => ((long)(byte)Kind << 32) | (uint)Id;

        public static NetworkUnitId FromPacked(long packed)
        {
            NetworkUnitKind kind = (NetworkUnitKind)(byte)(packed >> 32);
            int id = (int)(packed & 0xFFFFFFFF);
            return new NetworkUnitId(kind, id);
        }

        public static NetworkUnitId FromUnit(GameUnit unit)
        {
            if (unit == null) return new NetworkUnitId(NetworkUnitKind.Unknown, 0);

            if (unit is T_Queen)
            {
                return new NetworkUnitId(NetworkUnitKind.Queen, unit.m_ID != 0 ? unit.m_ID : 777);
            }
            if (unit is GBot)
            {
                return new NetworkUnitId(NetworkUnitKind.GBot, unit.m_ID);
            }
            if (unit is Alliance_Tier)
            {
                return new NetworkUnitId(NetworkUnitKind.Alliance, unit.m_ID);
            }
            if (unit is SkullRat)
            {
                return new NetworkUnitId(NetworkUnitKind.SkullRat, unit.m_ID);
            }
            if (unit is Ant)
            {
                return new NetworkUnitId(NetworkUnitKind.Ant, unit.m_ID);
            }
            if (unit is MiningDrone)
            {
                return new NetworkUnitId(NetworkUnitKind.Drone, unit.m_ID);
            }
            if (unit is GameEnemy)
            {
                return new NetworkUnitId(NetworkUnitKind.Enemy, unit.m_ID);
            }
            if (unit is T_Citizen)
            {
                return new NetworkUnitId(NetworkUnitKind.Citizen, unit.m_ID);
            }

            // Fallback based on UnitKind enum
            switch (unit.m_UnitKind)
            {
                case UnitKind.Queen:
                    return new NetworkUnitId(NetworkUnitKind.Queen, unit.m_ID);
                case UnitKind.Enemy:
                    return new NetworkUnitId(NetworkUnitKind.Enemy, unit.m_ID);
                case UnitKind.Drone:
                    return new NetworkUnitId(NetworkUnitKind.Drone, unit.m_ID);
                case UnitKind.GBot:
                    return new NetworkUnitId(NetworkUnitKind.GBot, unit.m_ID);
                case UnitKind.Citizen:
                default:
                    return new NetworkUnitId(NetworkUnitKind.Citizen, unit.m_ID);
            }
        }

        public static NetworkUnitId FromAnimal(AnimalBody animal)
        {
            if (animal == null) return new NetworkUnitId(NetworkUnitKind.Unknown, 0);
            return new NetworkUnitId(NetworkUnitKind.Animal, animal.m_ID);
        }

        public bool Equals(NetworkUnitId other)
        {
            return Kind == other.Kind && Id == other.Id;
        }

        public override bool Equals(object? obj)
        {
            return obj is NetworkUnitId other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return ((int)Kind * 397) ^ Id;
            }
        }

        public override string ToString()
        {
            return $"[{Kind}:{Id}]";
        }

        public static bool operator ==(NetworkUnitId left, NetworkUnitId right) => left.Equals(right);
        public static bool operator !=(NetworkUnitId left, NetworkUnitId right) => !left.Equals(right);
    }
}
