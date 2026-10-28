using System.Collections.Generic;
using CoMPaSS.Core;

namespace CoMPaSS.Algorithms
{
    public enum ConflictType
    {
        Vertex,
        Edge
    }

    public sealed class MapfConflict
    {
        public ConflictType Type { get; }
        public int FirstAgentId { get; }
        public int SecondAgentId { get; }
        public int TimeStep { get; }
        public GridPosition FirstPosition { get; }
        public GridPosition SecondPosition { get; }

        internal MapfConflict(ConflictType type, int firstAgentId, int secondAgentId, int timeStep,
            GridPosition firstPosition, GridPosition secondPosition)
        {
            Type = type;
            FirstAgentId = firstAgentId;
            SecondAgentId = secondAgentId;
            TimeStep = timeStep;
            FirstPosition = firstPosition;
            SecondPosition = secondPosition;
        }
    }

    public static class ConflictDetector
    {
        /// <summary>Detects vertex conflicts and opposite-direction edge swaps. Short paths remain at their goal.</summary>
        public static List<MapfConflict> FindConflicts(IReadOnlyList<AgentPath> paths, bool stayAtGoal = true)
        {
            var conflicts = new List<MapfConflict>();
            if (paths == null || paths.Count < 2) return conflicts;

            int lastTime = 0;
            for (int i = 0; i < paths.Count; i++)
                if (paths[i] != null && !paths[i].IsEmpty && paths[i].Positions.Count - 1 > lastTime)
                    lastTime = paths[i].Positions.Count - 1;

            for (int time = 0; time <= lastTime; time++)
            {
                for (int i = 0; i < paths.Count; i++)
                {
                    AgentPath first = paths[i];
                    if (first == null || first.IsEmpty || (!stayAtGoal && time >= first.Positions.Count)) continue;
                    GridPosition firstCurrent = first.PositionAt(time, stayAtGoal);
                    GridPosition firstPrevious = time > 0 ? first.PositionAt(time - 1, stayAtGoal) : firstCurrent;

                    for (int j = i + 1; j < paths.Count; j++)
                    {
                        AgentPath second = paths[j];
                        if (second == null || second.IsEmpty || (!stayAtGoal && time >= second.Positions.Count)) continue;
                        GridPosition secondCurrent = second.PositionAt(time, stayAtGoal);
                        GridPosition secondPrevious = time > 0 ? second.PositionAt(time - 1, stayAtGoal) : secondCurrent;

                        if (firstCurrent == secondCurrent)
                            conflicts.Add(new MapfConflict(ConflictType.Vertex, first.AgentId, second.AgentId, time, firstCurrent, secondCurrent));
                        else if (time > 0 && firstPrevious == secondCurrent && secondPrevious == firstCurrent)
                            conflicts.Add(new MapfConflict(ConflictType.Edge, first.AgentId, second.AgentId, time, firstPrevious, firstCurrent));
                    }
                }
            }
            return conflicts;
        }
    }
}
