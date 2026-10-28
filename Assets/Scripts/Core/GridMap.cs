using System;
using System.Collections.Generic;

namespace CoMPaSS.Core
{
    [Serializable]
    public sealed class GridMap
    {
        private static readonly GridPosition[] CardinalOffsets =
        {
            new GridPosition(0, 1), new GridPosition(1, 0), new GridPosition(0, -1), new GridPosition(-1, 0)
        };
        private static readonly GridPosition[] CardinalAndWaitOffsets =
        {
            new GridPosition(0, 1), new GridPosition(1, 0), new GridPosition(0, -1), new GridPosition(-1, 0), new GridPosition(0, 0)
        };
        private readonly bool[] blocked;

        public int Width { get; }
        public int Height { get; }
        public int NeighborRotation { get; set; }

        public GridMap(int width, int height)
        {
            if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
            if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
            Width = width;
            Height = height;
            blocked = new bool[checked(width * height)];
        }

        public bool Contains(GridPosition position) =>
            position.X >= 0 && position.X < Width && position.Y >= 0 && position.Y < Height;

        public bool IsBlocked(GridPosition position)
        {
            if (!Contains(position)) return true;
            return blocked[ToIndex(position)];
        }

        public void SetBlocked(GridPosition position, bool value)
        {
            if (!Contains(position)) throw new ArgumentOutOfRangeException(nameof(position));
            blocked[ToIndex(position)] = value;
        }

        public GridMap Copy()
        {
            var copy = new GridMap(Width, Height);
            copy.NeighborRotation = NeighborRotation;
            Array.Copy(blocked, copy.blocked, blocked.Length);
            return copy;
        }

        public IEnumerable<GridPosition> GetNeighbors(GridPosition position, bool includeWait = false)
        {
            // Keep the order fixed so equal-cost paths are reproducible.
            GridPosition[] offsets = includeWait ? CardinalAndWaitOffsets : CardinalOffsets;

            for (int i = 0; i < offsets.Length; i++)
            {
                int index = i == 4 ? 4 : (i + (NeighborRotation % 4 + 4) % 4) % 4;
                var next = new GridPosition(position.X + offsets[index].X, position.Y + offsets[index].Y);
                if (Contains(next) && !IsBlocked(next)) yield return next;
            }
        }

        private int ToIndex(GridPosition position) => position.Y * Width + position.X;
    }
}
