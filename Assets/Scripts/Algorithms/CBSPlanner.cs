using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using CoMPaSS.Core;
using CoMPaSS.Experiment;

namespace CoMPaSS.Algorithms
{
    public sealed class CBSOptions
    {
        public int MaxHighLevelNodes = 10000;
        public int MaxLowLevelExpandedNodes = 100000;
        public int MaxTimeSteps = 256;
        public int MaxPlanningMilliseconds = 60000;
        public CBSHeuristicMode Heuristic = CBSHeuristicMode.None;
        public CancellationToken CancellationToken;
        public bool RecordReplay = true;
        internal double HeuristicMilliseconds;
        internal int PlanningConflicts;
        internal readonly List<string> HeuristicLog = new List<string>();
    }

    public sealed class CBSResult
    {
        public bool Succeeded { get; }
        public IReadOnlyList<AgentPath> Paths { get; }
        public IReadOnlyList<MapfConflict> Conflicts { get; }
        public int ExpandedConstraintNodes { get; }
        public int GeneratedConstraints { get; }
        public int ReplanCount { get; }
        public int ExpandedLowLevelNodes { get; }
        public int ExpandedHeuristicNodes { get; }
        public int WaitActionCount { get; }
        public double HeuristicMilliseconds { get; internal set; }
        public int PlanningConflictCount { get; internal set; }
        public List<string> HeuristicLog { get; internal set; } = new List<string>();
        public int FailedAgentId { get; }
        public string FailureReason { get; }
        public IReadOnlyList<CBSReplayFrame> ReplayFrames { get; }

        internal CBSResult(bool succeeded, List<AgentPath> paths, List<MapfConflict> conflicts,
            int expandedConstraintNodes, int generatedConstraints, int replanCount,
            int expandedLowLevelNodes, int expandedHeuristicNodes, int waitActionCount, int failedAgentId, string failureReason, List<CBSReplayFrame> replayFrames = null)
        {
            Succeeded = succeeded;
            Paths = paths;
            Conflicts = conflicts;
            ExpandedConstraintNodes = expandedConstraintNodes;
            GeneratedConstraints = generatedConstraints;
            ReplanCount = replanCount;
            ExpandedLowLevelNodes = expandedLowLevelNodes;
            ExpandedHeuristicNodes = expandedHeuristicNodes;
            WaitActionCount = waitActionCount;
            FailedAgentId = failedAgentId;
            FailureReason = failureReason;
            ReplayFrames = replayFrames ?? new List<CBSReplayFrame>();
        }
    }

    public sealed class CBSReplayFrame
    {
        public int NodeId { get; }
        public int ParentNodeId { get; }
        public int SumOfCosts { get; }
        public IReadOnlyList<AgentPath> Paths { get; }
        public MapfConflict Conflict { get; }
        public string AppliedConstraint { get; }
        public bool IsConstraintTrial { get; }
        public int ConstrainedAgentId { get; }
        public bool ReplanSucceeded { get; }
        public string ReplanFailureReason { get; }
        internal CBSReplayFrame(int nodeId, int parentNodeId, int sumOfCosts, List<AgentPath> paths,
            MapfConflict conflict, string appliedConstraint, bool isConstraintTrial = false, int constrainedAgentId = -1,
            bool replanSucceeded = true, string replanFailureReason = null)
        {
            NodeId = nodeId; ParentNodeId = parentNodeId; SumOfCosts = sumOfCosts; Paths = paths; Conflict = conflict;
            AppliedConstraint = appliedConstraint; IsConstraintTrial = isConstraintTrial; ConstrainedAgentId = constrainedAgentId;
            ReplanSucceeded = replanSucceeded; ReplanFailureReason = replanFailureReason;
        }
    }

    /// <summary>Standard optimal Conflict-Based Search using sum-of-costs and space-time A*.</summary>
    public sealed partial class CBSPlanner
    {
        public CBSResult Search(GridMap map, IReadOnlyList<AgentDefinition> agents, CBSOptions options = null)
        {
            options = options ?? new CBSOptions(); options.HeuristicMilliseconds = 0; options.PlanningConflicts = 0; options.HeuristicLog.Clear();
            CBSResult result = SearchCore(map, agents, options);
            result.HeuristicMilliseconds = options.HeuristicMilliseconds; result.PlanningConflictCount = options.PlanningConflicts;
            result.HeuristicLog = new List<string>(options.HeuristicLog); return result;
        }

        private CBSResult SearchCore(GridMap map, IReadOnlyList<AgentDefinition> agents, CBSOptions options)
        {
            options = options ?? new CBSOptions();
            if (map == null) return Fail(null, 0, 0, 0, 0, 0, -1, "Map is missing.");
            if (agents == null) return Fail(null, 0, 0, 0, 0, 0, -1, "Agent list is missing.");
            if (options.MaxHighLevelNodes <= 0 || options.MaxLowLevelExpandedNodes <= 0 || options.MaxTimeSteps < 0 || options.MaxPlanningMilliseconds <= 0)
                return Fail(null, 0, 0, 0, 0, 0, -1, "CBS limits must be positive, with a non-negative time-step limit.");

            var timer = Stopwatch.StartNew();
            var root = new ConstraintNode(agents.Count);
            int lowLevelExpanded = 0;
            int replans = 0;
            for (int i = 0; i < agents.Count; i++)
            {
                AgentDefinition agent = agents[i];
                if (agent == null) return Fail(root.Paths, 0, 0, replans, lowLevelExpanded, 0, -1, "Agent list contains a missing entry.");
                if (!ValidEndpoint(map, agent.Start) || !ValidEndpoint(map, agent.Goal))
                    return Fail(root.Paths, 0, 0, replans, lowLevelExpanded, 0, agent.Id, "Start or goal is outside the map or blocked.");

                LowLevelResult planned = PlanAgent(map, agent, root.Constraints[i], options.MaxTimeSteps, options.MaxLowLevelExpandedNodes, timer, options.MaxPlanningMilliseconds, options.CancellationToken);
                lowLevelExpanded += planned.Expanded;
                if (planned.Path == null)
                    return Fail(root.Paths, 0, 0, replans, lowLevelExpanded, 0, agent.Id, planned.FailureReason);
                root.Paths[i] = planned.Path;
            }

            root.Cost = SumCost(root.Paths);
            var open = new MinHeap();
            int sequence = 0;
            int nextNodeId = 1;
            open.Push(root, sequence++);
            int expandedHighLevel = 0;
            int constraintsGenerated = 0;
            int heuristicExpanded = 0;
            int waitActions = 0;
            var replayFrames = new List<CBSReplayFrame>();

            while (open.Count > 0)
            {
                options.CancellationToken.ThrowIfCancellationRequested();
                if (timer.ElapsedMilliseconds >= options.MaxPlanningMilliseconds)
                    return Fail(null, expandedHighLevel, constraintsGenerated, replans, lowLevelExpanded, heuristicExpanded, -1,
                        "Maximum planning-time limit reached.", replayFrames);
                if (expandedHighLevel >= options.MaxHighLevelNodes)
                    return Fail(null, expandedHighLevel, constraintsGenerated, replans, lowLevelExpanded, heuristicExpanded, -1,
                        "Maximum CBS constraint-node limit reached.", replayFrames);

                ConstraintNode current = open.Pop();
                expandedHighLevel++;
                List<MapfConflict> conflicts = ConflictDetector.FindConflicts(current.Paths, stayAtGoal: true);
                options.PlanningConflicts += conflicts.Count;
                if (conflicts.Count == 0)
                {
                    if (options.RecordReplay) replayFrames.Add(new CBSReplayFrame(current.NodeId, current.ParentNodeId, current.Cost,
                        new List<AgentPath>(current.Paths), null, current.AppliedConstraint));
                    return new CBSResult(true, current.Paths, conflicts, expandedHighLevel,
                        constraintsGenerated, replans, lowLevelExpanded, heuristicExpanded, waitActions, -1, null, replayFrames);
                }
                if (options.RecordReplay) replayFrames.Add(new CBSReplayFrame(current.NodeId, current.ParentNodeId, current.Cost,
                    new List<AgentPath>(current.Paths), conflicts[0], current.AppliedConstraint));

                MapfConflict conflict = conflicts[0];
                for (int branch = 0; branch < 2; branch++)
                {
                    int agentIndex = FindAgentIndex(agents, branch == 0 ? conflict.FirstAgentId : conflict.SecondAgentId);
                    if (agentIndex < 0) continue;
                    var constraint = CreateConstraint(conflict, branch);
                    ConstraintNode child = current.Clone();
                    if (child.Constraints[agentIndex].Contains(constraint))
                    {
                        if (options.RecordReplay) replayFrames.Add(new CBSReplayFrame(current.NodeId, current.NodeId, current.Cost,
                            new List<AgentPath>(current.Paths), conflict, constraint.ToString(), true,
                            agents[agentIndex].Id, false, "이미 같은 제약이 있어 분기를 생략했습니다."));
                        continue;
                    }
                    child.Constraints[agentIndex].Add(constraint);
                    child.NodeId = nextNodeId++;
                    child.ParentNodeId = current.NodeId;
                    child.AppliedConstraint = constraint.ToString();
                    constraintsGenerated++;

                    LowLevelResult planned = PlanAgent(map, agents[agentIndex], child.Constraints[agentIndex], options.MaxTimeSteps,
                        options.MaxLowLevelExpandedNodes, timer, options.MaxPlanningMilliseconds, options.CancellationToken);
                    lowLevelExpanded += planned.Expanded;
                    replans++;
                    if (planned.Path == null)
                    {
                        if (options.RecordReplay) replayFrames.Add(new CBSReplayFrame(child.NodeId, current.NodeId, child.Cost,
                            new List<AgentPath>(current.Paths), conflict, child.AppliedConstraint, true,
                            agents[agentIndex].Id, false, planned.FailureReason));
                        if(planned.FailureReason.StartsWith("Maximum",StringComparison.Ordinal))
                            return Fail(null,expandedHighLevel,constraintsGenerated,replans,lowLevelExpanded,heuristicExpanded,agents[agentIndex].Id,planned.FailureReason,replayFrames);
                        continue;
                    }

                    child.Paths[agentIndex] = planned.Path;
                    for (int step = 1; step < planned.Path.Positions.Count; step++)
                        if (planned.Path.Positions[step] == planned.Path.Positions[step - 1]) waitActions++;
                    child.Cost = SumCost(child.Paths);
                    List<MapfConflict> childConflicts = ConflictDetector.FindConflicts(child.Paths, stayAtGoal: true);
                    if (options.RecordReplay) replayFrames.Add(new CBSReplayFrame(child.NodeId, current.NodeId, child.Cost,
                        new List<AgentPath>(child.Paths), childConflicts.Count > 0 ? childConflicts[0] : null,
                        child.AppliedConstraint, true, agents[agentIndex].Id, true, null));
                    child.EstimatedCost = child.Cost + ComputeHeuristic(map, agents, child, childConflicts, options,
                        timer, ref heuristicExpanded);
                    open.Push(child, sequence++);
                }
            }

            return Fail(null, expandedHighLevel, constraintsGenerated, replans, lowLevelExpanded, heuristicExpanded, -1,
                "No collision-free solution exists within the configured time-step limit.", replayFrames);
        }

        private static long PairKey(int first, int second)
        {
            int a = Math.Min(first, second), b = Math.Max(first, second);
            return ((long)a << 32) | (uint)b;
        }

        private static bool ValidEndpoint(GridMap map, GridPosition position) => map.Contains(position) && !map.IsBlocked(position);

        private static int FindAgentIndex(IReadOnlyList<AgentDefinition> agents, int id)
        {
            for (int i = 0; i < agents.Count; i++)
                if (agents[i] != null && agents[i].Id == id) return i;
            return -1;
        }

        private static CBSConstraint CreateConstraint(MapfConflict conflict, int branch)
        {
            if (conflict.Type == ConflictType.Vertex)
            {
                int agentId = branch == 0 ? conflict.FirstAgentId : conflict.SecondAgentId;
                return CBSConstraint.Vertex(agentId, conflict.FirstPosition, conflict.TimeStep);
            }

            if (branch == 0)
                return CBSConstraint.Edge(conflict.FirstAgentId, conflict.FirstPosition, conflict.SecondPosition, conflict.TimeStep);
            return CBSConstraint.Edge(conflict.SecondAgentId, conflict.SecondPosition, conflict.FirstPosition, conflict.TimeStep);
        }

        private static LowLevelResult PlanAgent(GridMap map, AgentDefinition agent,
            List<CBSConstraint> constraints, int maxTimeSteps, int maxExpandedNodes, Stopwatch timer, int maxMilliseconds, CancellationToken cancellationToken)
        {
            for (int i = 0; i < constraints.Count; i++)
                if (constraints[i].Type == CBSConstraintType.Vertex && constraints[i].TimeStep == 0 && constraints[i].Position == agent.Start)
                    return new LowLevelResult(null, 0, "The start cell is constrained at time 0.");

            var open = new LowLevelHeap();
            var visited = new HashSet<TimeState>();
            var parents = new Dictionary<TimeState, TimeState>();
            var start = new TimeState(agent.Start, 0);
            int sequence = 0;
            open.Push(new LowLevelNode(start, Heuristic(agent.Start, agent.Goal), sequence++));
            visited.Add(start);
            int expanded = 0;

            while (open.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (timer.ElapsedMilliseconds >= maxMilliseconds)
                    return new LowLevelResult(null, expanded, "Maximum planning-time limit reached.");
                TimeState current = open.Pop().State;
                if (current.Position == agent.Goal)
                {
                    if (CanRemainAtGoal(current, constraints))
                        return new LowLevelResult(Reconstruct(agent.Id, start, current, parents), expanded, null);
                    continue;
                }
                if (current.TimeStep >= maxTimeSteps) continue;
                if (expanded >= maxExpandedNodes)
                    return new LowLevelResult(null, expanded, "Maximum low-level A* expanded-node limit reached.");
                expanded++;

                foreach (GridPosition nextPosition in map.GetNeighbors(current.Position, includeWait: true))
                {
                    int nextTime = current.TimeStep + 1;
                    if (ViolatesConstraint(constraints, current.Position, nextPosition, nextTime)) continue;
                    var next = new TimeState(nextPosition, nextTime);
                    if (!visited.Add(next)) continue;
                    parents[next] = current;
                    open.Push(new LowLevelNode(next, nextTime + Heuristic(nextPosition, agent.Goal), sequence++));
                }
            }

            return new LowLevelResult(null, expanded, "No path satisfies this agent's constraints within the time-step limit.");
        }

        private static bool ViolatesConstraint(List<CBSConstraint> constraints, GridPosition from, GridPosition to, int arrivalTime)
        {
            for (int i = 0; i < constraints.Count; i++)
            {
                CBSConstraint constraint = constraints[i];
                if (constraint.TimeStep != arrivalTime) continue;
                if (constraint.Type == CBSConstraintType.Vertex && constraint.Position == to) return true;
                if (constraint.Type == CBSConstraintType.Edge && constraint.From == from && constraint.To == to) return true;
            }
            return false;
        }

        private static bool CanRemainAtGoal(TimeState state, List<CBSConstraint> constraints)
        {
            for (int i = 0; i < constraints.Count; i++)
            {
                CBSConstraint constraint = constraints[i];
                if (constraint.Type == CBSConstraintType.Vertex && constraint.Position == state.Position && constraint.TimeStep >= state.TimeStep)
                    return false;
            }
            return true;
        }

        private static AgentPath Reconstruct(int agentId, TimeState start, TimeState goal, Dictionary<TimeState, TimeState> parents)
        {
            var reverse = new List<GridPosition> { goal.Position };
            TimeState cursor = goal;
            while (!cursor.Equals(start))
            {
                cursor = parents[cursor];
                reverse.Add(cursor.Position);
            }
            reverse.Reverse();
            return new AgentPath(agentId, reverse);
        }

        private static int Heuristic(GridPosition a, GridPosition b) => Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y);

        private static int SumCost(List<AgentPath> paths)
        {
            int cost = 0;
            for (int i = 0; i < paths.Count; i++) cost += paths[i].MoveCost;
            return cost;
        }

        private static CBSResult Fail(List<AgentPath> paths, int high, int constraints, int replans,
            int low, int heuristicExpanded, int failedAgent, string reason, List<CBSReplayFrame> replayFrames = null) => new CBSResult(false,
                paths ?? new List<AgentPath>(), new List<MapfConflict>(), high, constraints, replans, low, heuristicExpanded, 0, failedAgent, reason, replayFrames);

        private enum CBSConstraintType { Vertex, Edge }

        private readonly struct CBSConstraint : IEquatable<CBSConstraint>
        {
            public readonly CBSConstraintType Type;
            public readonly int AgentId;
            public readonly GridPosition Position;
            public readonly GridPosition From;
            public readonly GridPosition To;
            public readonly int TimeStep;

            private CBSConstraint(CBSConstraintType type, int agentId, GridPosition position, GridPosition from, GridPosition to, int timeStep)
            { Type = type; AgentId = agentId; Position = position; From = from; To = to; TimeStep = timeStep; }

            public static CBSConstraint Vertex(int agentId, GridPosition position, int timeStep) =>
                new CBSConstraint(CBSConstraintType.Vertex, agentId, position, default(GridPosition), default(GridPosition), timeStep);
            public static CBSConstraint Edge(int agentId, GridPosition from, GridPosition to, int timeStep) =>
                new CBSConstraint(CBSConstraintType.Edge, agentId, default(GridPosition), from, to, timeStep);
            public bool Equals(CBSConstraint other) => Type == other.Type && AgentId == other.AgentId &&
                Position == other.Position && From == other.From && To == other.To && TimeStep == other.TimeStep;
            public override bool Equals(object obj) => obj is CBSConstraint other && Equals(other);
            public override int GetHashCode() => (((((int)Type * 397) ^ AgentId) * 397 ^ Position.GetHashCode()) * 397 ^ From.GetHashCode()) * 397 ^ To.GetHashCode() ^ TimeStep;
            public override string ToString() => Type == CBSConstraintType.Vertex
                ? $"agent {AgentId}: vertex {Position} at t={TimeStep}"
                : $"agent {AgentId}: edge {From}->{To} at t={TimeStep}";
        }

        private sealed class ConstraintNode
        {
            public readonly List<AgentPath> Paths;
            public readonly List<CBSConstraint>[] Constraints;
            public int Cost;
            public int EstimatedCost;
            public int NodeId;
            public int ParentNodeId = -1;
            public string AppliedConstraint;
            public ConstraintNode(int agentCount)
            {
                Paths = new List<AgentPath>(new AgentPath[agentCount]);
                Constraints = new List<CBSConstraint>[agentCount];
                for (int i = 0; i < agentCount; i++) Constraints[i] = new List<CBSConstraint>();
            }
            private ConstraintNode(List<AgentPath> paths, List<CBSConstraint>[] constraints, int cost, int estimatedCost,
                int nodeId, int parentNodeId, string appliedConstraint)
            { Paths = paths; Constraints = constraints; Cost = cost; EstimatedCost = estimatedCost; NodeId = nodeId; ParentNodeId = parentNodeId; AppliedConstraint = appliedConstraint; }
            public ConstraintNode Clone()
            {
                var paths = new List<AgentPath>(Paths);
                var constraints = new List<CBSConstraint>[Constraints.Length];
                for (int i = 0; i < Constraints.Length; i++) constraints[i] = new List<CBSConstraint>(Constraints[i]);
                return new ConstraintNode(paths, constraints, Cost, EstimatedCost, NodeId, ParentNodeId, AppliedConstraint);
            }
        }

        private readonly struct TimeState : IEquatable<TimeState>
        {
            public readonly GridPosition Position;
            public readonly int TimeStep;
            public TimeState(GridPosition position, int timeStep) { Position = position; TimeStep = timeStep; }
            public bool Equals(TimeState other) => Position == other.Position && TimeStep == other.TimeStep;
            public override bool Equals(object obj) => obj is TimeState other && Equals(other);
            public override int GetHashCode() => Position.GetHashCode() * 397 ^ TimeStep;
        }

        private readonly struct LowLevelResult
        {
            public readonly AgentPath Path;
            public readonly int Expanded;
            public readonly string FailureReason;
            public LowLevelResult(AgentPath path, int expanded, string failureReason) { Path = path; Expanded = expanded; FailureReason = failureReason; }
        }

        private readonly struct LowLevelNode
        {
            public readonly TimeState State;
            public readonly int Estimate;
            public readonly int Sequence;
            public LowLevelNode(TimeState state, int estimate, int sequence) { State = state; Estimate = estimate; Sequence = sequence; }
        }

        private sealed class LowLevelHeap
        {
            private readonly List<LowLevelNode> items = new List<LowLevelNode>();
            public int Count => items.Count;
            public void Push(LowLevelNode node)
            {
                items.Add(node);
                int index = items.Count - 1;
                while (index > 0)
                {
                    int parent = (index - 1) / 2;
                    if (Compare(items[parent], node) <= 0) break;
                    items[index] = items[parent];
                    index = parent;
                }
                items[index] = node;
            }
            public LowLevelNode Pop()
            {
                LowLevelNode root = items[0], tail = items[items.Count - 1];
                items.RemoveAt(items.Count - 1);
                if (items.Count == 0) return root;
                int index = 0;
                while (true)
                {
                    int left = index * 2 + 1;
                    if (left >= items.Count) break;
                    int right = left + 1;
                    int child = right < items.Count && Compare(items[right], items[left]) < 0 ? right : left;
                    if (Compare(tail, items[child]) <= 0) break;
                    items[index] = items[child];
                    index = child;
                }
                items[index] = tail;
                return root;
            }
            private static int Compare(LowLevelNode a, LowLevelNode b)
            {
                int estimate = a.Estimate.CompareTo(b.Estimate);
                return estimate != 0 ? estimate : a.Sequence.CompareTo(b.Sequence);
            }
        }

        private readonly struct HighLevelNode
        {
            public readonly ConstraintNode Node;
            public readonly int Sequence;
            public HighLevelNode(ConstraintNode node, int sequence) { Node = node; Sequence = sequence; }
        }

        private sealed class MinHeap
        {
            private readonly List<HighLevelNode> items = new List<HighLevelNode>();
            public int Count => items.Count;
            public void Push(ConstraintNode node, int sequence)
            {
                var entry = new HighLevelNode(node, sequence);
                items.Add(entry);
                int index = items.Count - 1;
                while (index > 0)
                {
                    int parent = (index - 1) / 2;
                    if (Compare(items[parent], entry) <= 0) break;
                    items[index] = items[parent];
                    index = parent;
                }
                items[index] = entry;
            }
            public ConstraintNode Pop()
            {
                HighLevelNode root = items[0], tail = items[items.Count - 1];
                items.RemoveAt(items.Count - 1);
                if (items.Count > 0)
                {
                    int index = 0;
                    while (true)
                    {
                        int left = index * 2 + 1;
                        if (left >= items.Count) break;
                        int right = left + 1;
                        int child = right < items.Count && Compare(items[right], items[left]) < 0 ? right : left;
                        if (Compare(tail, items[child]) <= 0) break;
                        items[index] = items[child];
                        index = child;
                    }
                    items[index] = tail;
                }
                return root.Node;
            }
            private static int Compare(HighLevelNode a, HighLevelNode b)
            {
                int cost = a.Node.EstimatedCost.CompareTo(b.Node.EstimatedCost);
                return cost != 0 ? cost : a.Sequence.CompareTo(b.Sequence);
            }
        }
    }
}
