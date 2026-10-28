using System;
using System.Collections.Generic;

namespace CoMPaSS.Core
{
    [Serializable]
    public sealed class AgentPath
    {
        private readonly List<GridPosition> positions;

        public int AgentId { get; }
        public IReadOnlyList<GridPosition> Positions => positions;
        public int MoveCost => Math.Max(0, positions.Count - 1);
        public bool IsEmpty => positions.Count == 0;

        public AgentPath(int agentId, IEnumerable<GridPosition> positions)
        {
            if (positions == null) throw new ArgumentNullException(nameof(positions));
            AgentId = agentId;
            this.positions = new List<GridPosition>(positions);
        }

        public GridPosition PositionAt(int timeStep, bool stayAtGoal = true)
        {
            if (positions.Count == 0) throw new InvalidOperationException("An empty path has no position.");
            if (timeStep < 0) throw new ArgumentOutOfRangeException(nameof(timeStep));
            if (timeStep < positions.Count) return positions[timeStep];
            if (stayAtGoal) return positions[positions.Count - 1];
            throw new ArgumentOutOfRangeException(nameof(timeStep));
        }
    }
}
