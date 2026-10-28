using System.Collections.Generic;
using System.Threading;
using CoMPaSS.Algorithms;
using CoMPaSS.Core;

namespace CoMPaSS.Experiment
{
    public enum MapfAlgorithmKind
    {
        IndependentAStar,
        CooperativeAStar,
        ConflictBasedSearch,
        PrimalExternal
    }

    public enum AgentPriorityMode
    {
        InputOrder,
        LongestDistanceFirst,
        MostConstrainedFirst
    }

    public enum CBSHeuristicMode
    {
        None,
        DependencyGraph,
        WeightedDependencyGraph
    }

    [System.Serializable]
    public sealed class MapfPlannerSettings
    {
        public int MaxTimeSteps = 256;
        public int MaxExpandedNodesPerAgent = 100000;
        public int MaxHighLevelNodes = 10000;
        public int MaxPlanningMilliseconds = 60000;
        public AgentPriorityMode PriorityMode = AgentPriorityMode.InputOrder;
        public CBSHeuristicMode CbsHeuristic = CBSHeuristicMode.None;
        public string PythonExecutable;
        public string PrimalScriptPath;
        [System.NonSerialized] public CancellationToken CancellationToken;
        public bool RecordReplay = true;
        public SearchProfile Profile = new SearchProfile();
        public MapfPlannerSettings Copy() { var copy = (MapfPlannerSettings)MemberwiseClone(); copy.Profile = Profile?.Copy() ?? new SearchProfile(); return copy; }
    }

    public sealed class MapfRunSummary
    {
        public MapfAlgorithmKind Algorithm { get; internal set; }
        public string Configuration { get; internal set; }
        public string Variant { get; internal set; }
        public MapfPlannerSettings SettingsSnapshot { get; internal set; }
        public bool AllAgentsRouted { get; internal set; }
        public int TotalAgents { get; internal set; }
        public int RoutedAgents { get { int count = 0; foreach (AgentPath path in Paths) if (path != null && !path.IsEmpty) count++; return count; } }
        public bool CollisionFree => AllAgentsRouted && Conflicts.Count == 0;
        public List<AgentPath> Paths { get; internal set; } = new List<AgentPath>();
        public List<MapfConflict> Conflicts { get; internal set; } = new List<MapfConflict>();
        public double PlanningMilliseconds { get; internal set; }
        public double PriorityMilliseconds { get; internal set; }
        public double HeuristicMilliseconds { get; internal set; }
        public int PlanningConflictCount { get; internal set; }
        public long SampledManagedPeakBytes { get; internal set; }
        public double AveragePathLength => RoutedAgents == 0 ? 0 : (double)TotalMoveCost / RoutedAgents;
        public List<string> HeuristicLog { get; internal set; } = new List<string>();
        public List<PrimalDecision> PrimalDecisions { get; internal set; } = new List<PrimalDecision>();
        public long ManagedMemoryDeltaBytes { get; internal set; }
        public int TotalMoveCost { get; internal set; }
        public int CompletionTime { get; internal set; }
        public int ExpandedNodes { get; internal set; }
        public int HighLevelNodes { get; internal set; }
        public int GeneratedConstraints { get; internal set; }
        public int ReplanCount { get; internal set; }
        public int HeuristicExpandedNodes { get; internal set; }
        public int WaitActionCount { get; internal set; }
        public int FailedAgentId { get; internal set; } = -1;
        public string FailureReason { get; internal set; }
        public List<AgentPriorityDecision> PriorityDecisions { get; internal set; } = new List<AgentPriorityDecision>();
        public List<CBSReplayFrame> CbsReplayFrames { get; internal set; } = new List<CBSReplayFrame>();
        public List<AgentRunLog> AgentLogs { get; } = new List<AgentRunLog>();
    }

    public sealed class AgentPriorityDecision
    {
        public int AgentId { get; internal set; }
        public int Rank { get; internal set; }
        public double Score { get; internal set; }
        public string Reason { get; internal set; }
        public int DistanceComponent { get; internal set; }
        public int CongestionComponent { get; internal set; }
        public int NarrownessComponent { get; internal set; }
    }

    public sealed class AgentRunLog
    {
        public int AgentId { get; internal set; }
        public string Message { get; internal set; }
    }
}
