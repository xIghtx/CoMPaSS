using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using CoMPaSS.Core;

namespace CoMPaSS.Algorithms
{
    public sealed class CooperativeSearchResult
    {
        public bool Succeeded { get; }
        public IReadOnlyList<AgentPath> Paths { get; }
        public int ExpandedNodes { get; }
        public int FailedAgentId { get; }
        public string FailureReason { get; }

        internal CooperativeSearchResult(bool succeeded, List<AgentPath> paths, int expandedNodes, int failedAgentId, string failureReason)
        {
            Succeeded = succeeded;
            Paths = paths;
            ExpandedNodes = expandedNodes;
            FailedAgentId = failedAgentId;
            FailureReason = failureReason;
        }
    }

    /// <summary>Sequential Cooperative A*: each accepted path reserves its vertices and edges for later agents.</summary>
    public sealed class CooperativeAStarPlanner
    {
        public CooperativeSearchResult Search(GridMap map, IReadOnlyList<AgentDefinition> agents,
            int maxTimeSteps = 256, int maxExpandedNodesPerAgent = 100000, int maxPlanningMilliseconds = int.MaxValue,
            CancellationToken cancellationToken = default, SearchProfile profile = null)
        {
            profile = profile ?? new SearchProfile();
            if (map == null) return Failure(null, 0, -1, "Map is missing.");
            if (agents == null) return Failure(null, 0, -1, "Agent list is missing.");
            if (maxTimeSteps < 0) return Failure(null, 0, -1, "Maximum time steps cannot be negative.");
            if (maxExpandedNodesPerAgent <= 0) return Failure(null, 0, -1, "Search limit must be positive.");

            var acceptedPaths = new List<AgentPath>(agents.Count);
            var reservations = new ReservationTable(maxTimeSteps);
            // Agents already at their goal occupy that cell regardless of planning priority.
            foreach (AgentDefinition fixedAgent in agents)
                if (fixedAgent != null && fixedAgent.Start == fixedAgent.Goal && !map.IsBlocked(fixedAgent.Start))
                    reservations.Reserve(new AgentPath(fixedAgent.Id, new[] { fixedAgent.Start }));
            int expandedTotal = 0;
            var timer = Stopwatch.StartNew();

            for (int i = 0; i < agents.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                AgentDefinition agent = agents[i];
                if (agent == null) return Failure(acceptedPaths, expandedTotal, -1, "Agent list contains a missing entry.");
                if (!map.Contains(agent.Start) || map.IsBlocked(agent.Start) ||
                    !map.Contains(agent.Goal) || map.IsBlocked(agent.Goal))
                    return Failure(acceptedPaths, expandedTotal, agent.Id, "Start or goal is outside the map or blocked.");
                if (agent.Start == agent.Goal)
                { acceptedPaths.Add(new AgentPath(agent.Id, new[] { agent.Start })); continue; }
                if (reservations.IsVertexReserved(agent.Start, 0))
                    return Failure(acceptedPaths, expandedTotal, agent.Id, "Start position is already occupied by an earlier agent at time 0.");

                SearchOutcome outcome = SearchOne(map, agent, reservations, maxTimeSteps, maxExpandedNodesPerAgent, timer, maxPlanningMilliseconds, cancellationToken, profile);
                expandedTotal += outcome.Expanded;
                if (outcome.Path == null)
                    return Failure(acceptedPaths, expandedTotal, agent.Id, outcome.FailureReason);

                acceptedPaths.Add(outcome.Path);
                reservations.Reserve(outcome.Path);
            }

            return new CooperativeSearchResult(true, acceptedPaths, expandedTotal, -1, null);
        }

        private static SearchOutcome SearchOne(GridMap map, AgentDefinition agent, ReservationTable reservations,
            int maxTimeSteps, int maxExpandedNodes, Stopwatch timer, int maxPlanningMilliseconds, CancellationToken cancellationToken, SearchProfile profile)
        {
            var open = new MinHeap();
            var cameFrom = new Dictionary<TimedPosition, TimedPosition>();
            var visited = new HashSet<TimedPosition>();
            int sequence = 0;
            var start = new TimedPosition(agent.Start, 0);
            open.Push(new OpenNode(start, profile.Estimate(agent.Start, agent.Goal), sequence++));
            visited.Add(start);
            int expanded = 0;

            while (open.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (timer.ElapsedMilliseconds >= maxPlanningMilliseconds)
                    return new SearchOutcome(null, expanded, "Maximum planning-time limit reached.");
                TimedPosition current = open.Pop().State;
                if (current.Position == agent.Goal)
                {
                    if (reservations.CanRemainAtGoal(agent.Goal, current.TimeStep))
                        return new SearchOutcome(BuildPath(agent.Id, cameFrom, start, current), expanded, null);
                    // Arrival is permanent: delay entry instead of leaving a reached goal later.
                    continue;
                }
                if (current.TimeStep >= maxTimeSteps) continue;
                if (expanded >= maxExpandedNodes)
                    return new SearchOutcome(null, expanded, "Maximum expanded-node limit reached.");
                expanded++;

                foreach (GridPosition nextPosition in map.GetNeighbors(current.Position, includeWait: profile.AllowWait))
                {
                    int nextTime = current.TimeStep + 1;
                    if (reservations.IsVertexReserved(nextPosition, nextTime) ||
                        reservations.IsReverseEdgeReserved(current.Position, nextPosition, nextTime)) continue;

                    var next = new TimedPosition(nextPosition, nextTime);
                    if (!visited.Add(next)) continue;
                    cameFrom[next] = current;
                    double estimate = nextTime + profile.Estimate(nextPosition, agent.Goal);
                    // Fractional tie preference is encoded separately from the admissible estimate.
                    open.Push(new OpenNode(next, estimate, sequence++, profile.TieBreak == SearchTieBreak.LowerHeuristic ? -nextTime : 0));
                }
            }

            return new SearchOutcome(null, expanded, "No route found within the time-step limit.");
        }

        private static AgentPath BuildPath(int agentId, Dictionary<TimedPosition, TimedPosition> cameFrom,
            TimedPosition start, TimedPosition goal)
        {
            var reverse = new List<GridPosition> { goal.Position };
            TimedPosition cursor = goal;
            while (!cursor.Equals(start))
            {
                cursor = cameFrom[cursor];
                reverse.Add(cursor.Position);
            }
            reverse.Reverse();
            return new AgentPath(agentId, reverse);
        }

        private static int Manhattan(GridPosition a, GridPosition b) => Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y);

        private static CooperativeSearchResult Failure(List<AgentPath> paths, int expanded, int agentId, string reason) =>
            new CooperativeSearchResult(false, paths ?? new List<AgentPath>(), expanded, agentId, reason);

        private readonly struct SearchOutcome
        {
            public readonly AgentPath Path;
            public readonly int Expanded;
            public readonly string FailureReason;
            public SearchOutcome(AgentPath path, int expanded, string failureReason)
            {
                Path = path;
                Expanded = expanded;
                FailureReason = failureReason;
            }
        }

        private readonly struct TimedPosition : IEquatable<TimedPosition>
        {
            public readonly GridPosition Position;
            public readonly int TimeStep;
            public TimedPosition(GridPosition position, int timeStep) { Position = position; TimeStep = timeStep; }
            public bool Equals(TimedPosition other) => Position == other.Position && TimeStep == other.TimeStep;
            public override bool Equals(object obj) => obj is TimedPosition other && Equals(other);
            public override int GetHashCode() => (Position.GetHashCode() * 397) ^ TimeStep;
        }

        private readonly struct OpenNode
        {
            public readonly TimedPosition State;
            public readonly double Estimate;
            public readonly int Sequence;
            public readonly int Tie;
            public OpenNode(TimedPosition state, double estimate, int sequence, int tie = 0) { State = state; Estimate = estimate; Sequence = sequence; Tie = tie; }
        }

        private sealed class MinHeap
        {
            private readonly List<OpenNode> items = new List<OpenNode>();
            public int Count => items.Count;
            public void Push(OpenNode node)
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
            public OpenNode Pop()
            {
                OpenNode root = items[0];
                OpenNode tail = items[items.Count - 1];
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
            private static int Compare(OpenNode a, OpenNode b)
            {
                int byEstimate = a.Estimate.CompareTo(b.Estimate);
                if (byEstimate != 0) return byEstimate;
                int tie = a.Tie.CompareTo(b.Tie);
                return tie != 0 ? tie : a.Sequence.CompareTo(b.Sequence);
            }
        }

        private sealed class ReservationTable
        {
            private readonly int horizon;
            private readonly HashSet<Reservation> vertices = new HashSet<Reservation>();
            private readonly HashSet<EdgeReservation> edges = new HashSet<EdgeReservation>();
            public ReservationTable(int horizon) { this.horizon = horizon; }
            public bool IsVertexReserved(GridPosition position, int time) => vertices.Contains(new Reservation(position, time));
            public bool CanRemainAtGoal(GridPosition position, int arrivalTime)
            {
                for (int time = arrivalTime; time <= horizon; time++)
                    if (IsVertexReserved(position, time)) return false;
                return true;
            }
            public bool IsReverseEdgeReserved(GridPosition from, GridPosition to, int arrivalTime) =>
                edges.Contains(new EdgeReservation(to, from, arrivalTime));

            public void Reserve(AgentPath path)
            {
                for (int time = 0; time <= horizon; time++)
                {
                    GridPosition position = path.PositionAt(time);
                    vertices.Add(new Reservation(position, time));
                    if (time > 0)
                        edges.Add(new EdgeReservation(path.PositionAt(time - 1), position, time));
                }
            }
        }

        private readonly struct Reservation : IEquatable<Reservation>
        {
            private readonly GridPosition position;
            private readonly int time;
            public Reservation(GridPosition position, int time) { this.position = position; this.time = time; }
            public bool Equals(Reservation other) => position == other.position && time == other.time;
            public override bool Equals(object obj) => obj is Reservation other && Equals(other);
            public override int GetHashCode() => (position.GetHashCode() * 397) ^ time;
        }

        private readonly struct EdgeReservation : IEquatable<EdgeReservation>
        {
            private readonly GridPosition from;
            private readonly GridPosition to;
            private readonly int arrivalTime;
            public EdgeReservation(GridPosition from, GridPosition to, int arrivalTime)
            { this.from = from; this.to = to; this.arrivalTime = arrivalTime; }
            public bool Equals(EdgeReservation other) => from == other.from && to == other.to && arrivalTime == other.arrivalTime;
            public override bool Equals(object obj) => obj is EdgeReservation other && Equals(other);
            public override int GetHashCode() => ((from.GetHashCode() * 397) ^ to.GetHashCode()) * 397 ^ arrivalTime;
        }
    }
}
