using System;
using System.Collections.Generic;
using System.Diagnostics;
using CoMPaSS.Core;
using CoMPaSS.Experiment;

namespace CoMPaSS.Algorithms
{
    public sealed partial class CBSPlanner
    {
        // Pairwise optimal cost increase, followed by (edge-weighted) minimum vertex cover.
        // Bounded auxiliary searches contribute zero or a matching lower bound, never an upper bound.
        private static int ComputeHeuristic(GridMap map, IReadOnlyList<AgentDefinition> agents, ConstraintNode node,
            List<MapfConflict> conflicts, CBSOptions options, Stopwatch timer, ref int expanded)
        {
            if (options.Heuristic == CBSHeuristicMode.None || conflicts.Count == 0) return 0;
            var watch = Stopwatch.StartNew();
            try
            {
                var processed = new HashSet<long>(); var edges = new List<DependencyEdge>();
                foreach (MapfConflict conflict in conflicts)
                {
                    long key = PairKey(conflict.FirstAgentId, conflict.SecondAgentId);
                    if (!processed.Add(key)) continue;
                    int a = FindAgentIndex(agents, conflict.FirstAgentId), b = FindAgentIndex(agents, conflict.SecondAgentId);
                    if (a < 0 || b < 0) continue;
                    int increase = PairCostIncrease(map, agents, node, a, b, options, timer, ref expanded);
                    if (increase <= 0) continue;
                    int weight = options.Heuristic == CBSHeuristicMode.DependencyGraph ? 1 : increase;
                    edges.Add(new DependencyEdge(a, b, weight));
                    if (options.HeuristicLog.Count < 128) options.HeuristicLog.Add($"CT {node.NodeId}: Agent {agents[a].Id}/{agents[b].Id} · 쌍 최적 추가 비용 {increase} · 간선 가중치 {weight}");
                }
                int bound = ExactCover(edges, agents.Count, options, timer, out bool exact);
                if (options.HeuristicLog.Count < 128) options.HeuristicLog.Add($"CT {node.NodeId}: h={bound} · {(exact ? "최소 정점 덮개" : "계산 제한 → 매칭 하한")}");
                return bound;
            }
            finally { options.HeuristicMilliseconds += watch.Elapsed.TotalMilliseconds; }
        }

        private static int PairCostIncrease(GridMap map, IReadOnlyList<AgentDefinition> agents, ConstraintNode node,
            int first, int second, CBSOptions options, Stopwatch timer, ref int expanded)
        {
            int[] indices = { first, second }; var pair = new[] { agents[first], agents[second] };
            var root = new ConstraintNode(2);
            for (int i = 0; i < 2; i++)
            { root.Paths[i] = node.Paths[indices[i]]; root.Constraints[i] = new List<CBSConstraint>(node.Constraints[indices[i]]); }
            root.Cost = SumCost(root.Paths); root.EstimatedCost = root.Cost;
            var open = new MinHeap(); int sequence = 0, count = 0; open.Push(root, sequence++);
            int limit = Math.Min(options.MaxHighLevelNodes, 2000);
            int deadline = Math.Min(options.MaxPlanningMilliseconds, (int)timer.ElapsedMilliseconds + 100);
            while (open.Count > 0 && count++ < limit && timer.ElapsedMilliseconds < deadline)
            {
                options.CancellationToken.ThrowIfCancellationRequested();
                ConstraintNode current = open.Pop(); var conflicts = ConflictDetector.FindConflicts(current.Paths);
                if (conflicts.Count == 0) return Math.Max(0, current.Cost - root.Cost);
                MapfConflict conflict = conflicts[0];
                for (int branch = 0; branch < 2; branch++)
                {
                    int index = pair[0].Id == (branch == 0 ? conflict.FirstAgentId : conflict.SecondAgentId) ? 0 : 1;
                    CBSConstraint constraint = CreateConstraint(conflict, branch);
                    if (current.Constraints[index].Contains(constraint)) continue;
                    ConstraintNode child = current.Clone(); child.Constraints[index].Add(constraint);
                    LowLevelResult result = PlanAgent(map, pair[index], child.Constraints[index], options.MaxTimeSteps,
                        options.MaxLowLevelExpandedNodes, timer, deadline, options.CancellationToken);
                    expanded += result.Expanded;
                    // A node-limit failure could hide a cheaper solution, so discard this auxiliary proof.
                    if (result.Path == null)
                    {
                        if (result.FailureReason.StartsWith("Maximum", StringComparison.Ordinal)) return 0;
                        continue;
                    }
                    child.Paths[index] = result.Path; child.Cost = SumCost(child.Paths); child.EstimatedCost = child.Cost;
                    open.Push(child, sequence++);
                }
            }
            return 0;
        }

        private readonly struct DependencyEdge
        {
            public readonly int A, B, Weight;
            public DependencyEdge(int a, int b, int weight) { A = a; B = b; Weight = weight; }
        }
        private static int ExactCover(List<DependencyEdge> edges, int vertices, CBSOptions options, Stopwatch timer, out bool exact)
        {
            exact = true; if (edges.Count == 0) return 0;
            edges.Sort((a,b) => b.Weight.CompareTo(a.Weight));
            var used = new HashSet<int>(); int matching = 0, upper = 0;
            foreach (DependencyEdge edge in edges)
            {
                upper += edge.Weight;
                if (used.Contains(edge.A) || used.Contains(edge.B)) continue;
                matching += edge.Weight; used.Add(edge.A); used.Add(edge.B);
            }
            int best = upper; var values = new int[vertices]; int visited = 0; bool bounded = false;
            int deadline = Math.Min(options.MaxPlanningMilliseconds, (int)timer.ElapsedMilliseconds + 40);
            void Search(int cost)
            {
                options.CancellationToken.ThrowIfCancellationRequested();
                if (++visited > 100000 || timer.ElapsedMilliseconds >= deadline) { bounded = true; return; }
                if (cost >= best) return;
                foreach (DependencyEdge edge in edges)
                {
                    int missing = edge.Weight - values[edge.A] - values[edge.B];
                    if (missing <= 0) continue;
                    if (cost + missing >= best) return;
                    for (int add = 0; add <= missing; add++)
                    {
                        values[edge.A] += add; values[edge.B] += missing - add;
                        Search(cost + missing);
                        values[edge.A] -= add; values[edge.B] -= missing - add;
                        if (bounded) return;
                    }
                    return;
                }
                best = cost;
            }
            Search(0); exact = !bounded; return bounded ? matching : best;
        }
    }
}
