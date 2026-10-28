using System;

namespace CoMPaSS.Core
{
    [Serializable]
    public sealed class AgentDefinition
    {
        public int Id { get; }
        public GridPosition Start { get; }
        public GridPosition Goal { get; }

        public AgentDefinition(int id, GridPosition start, GridPosition goal)
        {
            Id = id;
            Start = start;
            Goal = goal;
        }
    }
}
