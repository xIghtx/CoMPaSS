using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using CoMPaSS.Core;

namespace CoMPaSS.Algorithms
{
    /// <summary>Deterministic four-connected A* with Manhattan distance and unit edge costs.</summary>
    public sealed class AStarPlanner
    {
        public PathSearchResult Search(GridMap map, AgentDefinition agent, int maxExpandedNodes = int.MaxValue, int maxMilliseconds = int.MaxValue,
            CancellationToken cancellationToken = default, SearchProfile profile = null)
        {
            profile = profile ?? new SearchProfile();
            if (map == null) return Invalid("Map is missing.");
            if (agent == null) return Invalid("Agent is missing.");
            if (maxExpandedNodes <= 0) return Invalid("Search limit must be positive.");
            if (!map.Contains(agent.Start) || map.IsBlocked(agent.Start)) return Invalid("Start is outside the map or blocked.");
            if (!map.Contains(agent.Goal) || map.IsBlocked(agent.Goal)) return Invalid("Goal is outside the map or blocked.");

            var open = new MinHeap(profile.TieBreak);
            var cameFrom = new Dictionary<GridPosition, GridPosition>();
            var bestCost = new Dictionary<GridPosition, int> { [agent.Start] = 0 };
            int sequence = 0;
            var timer = Stopwatch.StartNew();
            open.Push(new OpenNode(agent.Start, 0, profile.Estimate(agent.Start, agent.Goal), sequence++));
            int expanded = 0;

            while (open.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (timer.ElapsedMilliseconds >= maxMilliseconds)
                    return new PathSearchResult(PathSearchStatus.SearchLimitReached, null, expanded, "Maximum planning-time limit reached.");
                OpenNode current = open.Pop();
                if (!bestCost.TryGetValue(current.Position, out int knownCost) || knownCost != current.Cost) continue;
                if (current.Position == agent.Goal)
                    return new PathSearchResult(PathSearchStatus.Success, BuildPath(agent.Id, cameFrom, agent.Start, agent.Goal), expanded, null);
                if (expanded >= maxExpandedNodes)
                    return new PathSearchResult(PathSearchStatus.SearchLimitReached, null, expanded, "Maximum expanded-node limit reached.");

                expanded++;
                foreach (GridPosition next in map.GetNeighbors(current.Position))
                {
                    int candidateCost = current.Cost + 1;
                    if (bestCost.TryGetValue(next, out int oldCost) && candidateCost >= oldCost) continue;
                    bestCost[next] = candidateCost;
                    cameFrom[next] = current.Position;
                    open.Push(new OpenNode(next, candidateCost, candidateCost + profile.Estimate(next, agent.Goal), sequence++));
                }
            }

            return new PathSearchResult(PathSearchStatus.NoPath, null, expanded, "No route connects the start and goal.");
        }

        private static int Manhattan(GridPosition a, GridPosition b) => Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y);

        private static AgentPath BuildPath(int agentId, Dictionary<GridPosition, GridPosition> cameFrom, GridPosition start, GridPosition goal)
        {
            var reverse = new List<GridPosition> { goal };
            GridPosition cursor = goal;
            while (cursor != start)
            {
                cursor = cameFrom[cursor];
                reverse.Add(cursor);
            }
            reverse.Reverse();
            return new AgentPath(agentId, reverse);
        }

        private static PathSearchResult Invalid(string reason) =>
            new PathSearchResult(PathSearchStatus.InvalidInput, null, 0, reason);

        private readonly struct OpenNode
        {
            public readonly GridPosition Position;
            public readonly int Cost;
            public readonly double Estimate;
            public readonly int Sequence;

            public OpenNode(GridPosition position, int cost, double estimate, int sequence)
            {
                Position = position;
                Cost = cost;
                Estimate = estimate;
                Sequence = sequence;
            }
        }

        // Small binary heap avoids Unity/.NET-version dependencies on PriorityQueue<T>.
        private sealed class MinHeap
        {
            private readonly SearchTieBreak tieBreak;
            public MinHeap(SearchTieBreak tieBreak) { this.tieBreak = tieBreak; }
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

            private int Compare(OpenNode a, OpenNode b)
            {
                int byEstimate = a.Estimate.CompareTo(b.Estimate);
                if (byEstimate != 0) return byEstimate;
                if (tieBreak == SearchTieBreak.InsertionOrder) return a.Sequence.CompareTo(b.Sequence);
                int byHeuristic = (a.Estimate - a.Cost).CompareTo(b.Estimate - b.Cost);
                return byHeuristic != 0 ? byHeuristic : a.Sequence.CompareTo(b.Sequence);
            }
        }
    }
}
