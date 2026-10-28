using System.Collections.Generic;
using CoMPaSS.Core;
using CoMPaSS.Experiment;

namespace CoMPaSS.Algorithms
{
    /// <summary>Extension point for new algorithms: pure data in, shared metrics/paths out.</summary>
    public interface IMapfSolver
    {
        string Name { get; }
        MapfRunSummary Solve(GridMap map, IReadOnlyList<AgentDefinition> agents, MapfPlannerSettings settings);
    }
    public sealed class BuiltInMapfSolver : IMapfSolver
    {
        public MapfAlgorithmKind Kind { get; }
        public string Name => Kind.ToString();
        public BuiltInMapfSolver(MapfAlgorithmKind kind) { Kind = kind; }
        public MapfRunSummary Solve(GridMap map, IReadOnlyList<AgentDefinition> agents, MapfPlannerSettings settings) =>
            new MapfExperimentRunner().Run(map, agents, Kind, settings);
    }
}
