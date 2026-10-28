using System;
using System.Collections.Generic;
using System.Diagnostics;
using CoMPaSS.Algorithms;
using CoMPaSS.Core;

namespace CoMPaSS.Experiment
{
    public sealed class MapfExperimentRunner
    {
        public MapfRunSummary Run(GridMap map, IReadOnlyList<AgentDefinition> agents,
            MapfAlgorithmKind algorithm, MapfPlannerSettings settings = null)
        {
            settings = settings == null ? new MapfPlannerSettings() : settings.Copy();
            settings.MaxPlanningMilliseconds = Math.Max(1, Math.Min(300000, settings.MaxPlanningMilliseconds));
            settings.MaxTimeSteps = Math.Max(1, Math.Min(1024, settings.MaxTimeSteps));
            settings.MaxExpandedNodesPerAgent = Math.Max(1, Math.Min(1000000, settings.MaxExpandedNodesPerAgent));
            settings.MaxHighLevelNodes = Math.Max(1, Math.Min(100000, settings.MaxHighLevelNodes));
            var summary = new MapfRunSummary { Algorithm = algorithm, TotalAgents = agents == null ? 0 : agents.Count };
            summary.SettingsSnapshot=settings.Copy();
            summary.Configuration = $"T{settings.MaxTimeSteps}/N{settings.MaxExpandedNodesPerAgent}/CT{settings.MaxHighLevelNodes}/ms{settings.MaxPlanningMilliseconds}/P{settings.PriorityMode}/CBS{settings.CbsHeuristic}/h{settings.Profile.Heuristic}/w{settings.Profile.Weight.ToString(System.Globalization.CultureInfo.InvariantCulture)}/order{settings.Profile.Neighbors}/tie{settings.Profile.TieBreak}/wait{settings.Profile.AllowWait}/weights{settings.Profile.DistanceWeight}:{settings.Profile.CongestionWeight}:{settings.Profile.NarrownessWeight}:{settings.Profile.WaitWeight}:{settings.Profile.RecoveryWeight}";
            settings.CancellationToken.ThrowIfCancellationRequested();
            if (map == null || agents == null)
            {
                summary.FailureReason = map == null ? "Map is missing." : "Agent list is missing.";
                return summary;
            }

            var ids = new HashSet<int>();
            var starts = new HashSet<GridPosition>();
            var goals = new HashSet<GridPosition>();
            foreach (AgentDefinition agent in agents)
            {
                if (agent == null || !ids.Add(agent.Id) || !starts.Add(agent.Start) || !goals.Add(agent.Goal))
                { summary.FailureReason = "에이전트 ID, 시작점, 목표점이 중복되거나 비어 있습니다."; return summary; }
                if (map.IsBlocked(agent.Start) || map.IsBlocked(agent.Goal))
                { summary.FailedAgentId = agent.Id; summary.FailureReason = "시작점 또는 목표점이 장애물 위에 있거나 지도 밖입니다."; return summary; }
            }
            long memoryBefore = GC.GetTotalMemory(false);
            using var memory = new ManagedMemorySampler();
            map = map.Copy(); map.NeighborRotation = (int)settings.Profile.Neighbors;
            var stopwatch = Stopwatch.StartNew();
            int totalBudget = settings.MaxPlanningMilliseconds;
            switch (algorithm)
            {
                case MapfAlgorithmKind.IndependentAStar:
                    RunIndependent(map, agents, settings, summary);
                    break;
                case MapfAlgorithmKind.CooperativeAStar:
                    var priorityTimer = Stopwatch.StartNew();
                    List<AgentDefinition> ordered = OrderAgents(map, agents, settings.PriorityMode, summary.PriorityDecisions, settings);
                    summary.PriorityMilliseconds = priorityTimer.Elapsed.TotalMilliseconds;
                    settings.MaxPlanningMilliseconds = totalBudget - (int)stopwatch.ElapsedMilliseconds;
                    if (settings.MaxPlanningMilliseconds > 0) RunCooperative(map, ordered, settings, summary);
                    else summary.FailureReason = "우선순위 계산 중 전체 시간 제한에 도달했습니다.";
                    break;
                case MapfAlgorithmKind.ConflictBasedSearch:
                    RunCbs(map, agents, settings, summary);
                    break;
                case MapfAlgorithmKind.PrimalExternal:
                    RunPrimal(map, agents, settings, summary);
                    break;
                default:
                    summary.FailureReason = "Unknown algorithm.";
                    break;
            }
            stopwatch.Stop();
            summary.PlanningMilliseconds = stopwatch.Elapsed.TotalMilliseconds;
            summary.ManagedMemoryDeltaBytes = Math.Max(0, GC.GetTotalMemory(false) - memoryBefore);
            summary.SampledManagedPeakBytes = memory.PeakBytes;
            if (algorithm != MapfAlgorithmKind.ConflictBasedSearch) summary.PlanningConflictCount = summary.Conflicts.Count;
            CalculatePathMetrics(summary);
            if (summary.PlanningMilliseconds > totalBudget)
            {
                summary.AllAgentsRouted = false;
                summary.FailureReason = $"실행 시간 제한 {totalBudget}ms 초과";
            }
            RecordAgentLogs(summary);
            return summary;
        }

        private static void RunIndependent(GridMap map, IReadOnlyList<AgentDefinition> agents,
            MapfPlannerSettings settings, MapfRunSummary summary)
        {
            var planner = new AStarPlanner();
            summary.AllAgentsRouted = true;
            var budget = Stopwatch.StartNew();
            for (int i = 0; i < agents.Count; i++)
            {
                settings.CancellationToken.ThrowIfCancellationRequested();
                int remaining = Math.Max(1, settings.MaxPlanningMilliseconds - (int)budget.ElapsedMilliseconds);
                if (budget.ElapsedMilliseconds >= settings.MaxPlanningMilliseconds)
                { summary.AllAgentsRouted = false; summary.FailureReason = "전체 탐색 시간 제한 도달"; break; }
                PathSearchResult result = planner.Search(map, agents[i], settings.MaxExpandedNodesPerAgent, remaining, settings.CancellationToken, settings.Profile);
                summary.ExpandedNodes += result.ExpandedNodes;
                if (!result.Succeeded)
                {
                    summary.AllAgentsRouted = false;
                    summary.FailedAgentId = agents[i] == null ? -1 : agents[i].Id;
                    summary.FailureReason = result.FailureReason;
                    continue;
                }
                summary.Paths.Add(result.Path);
            }
            summary.Conflicts = ConflictDetector.FindConflicts(summary.Paths);
        }

        private static void RunCooperative(GridMap map, IReadOnlyList<AgentDefinition> agents,
            MapfPlannerSettings settings, MapfRunSummary summary)
        {
            CooperativeSearchResult result = new CooperativeAStarPlanner().Search(map, agents,
                settings.MaxTimeSteps, settings.MaxExpandedNodesPerAgent, settings.MaxPlanningMilliseconds, settings.CancellationToken, settings.Profile);
            summary.AllAgentsRouted = result.Succeeded;
            summary.Paths = new List<AgentPath>(result.Paths);
            summary.ExpandedNodes = result.ExpandedNodes;
            summary.FailedAgentId = result.FailedAgentId;
            summary.FailureReason = result.FailureReason;
            summary.Conflicts = ConflictDetector.FindConflicts(summary.Paths);
        }

        private static void RunCbs(GridMap map, IReadOnlyList<AgentDefinition> agents,
            MapfPlannerSettings settings, MapfRunSummary summary)
        {
            CBSResult result = new CBSPlanner().Search(map, agents, new CBSOptions
            {
                MaxTimeSteps = settings.MaxTimeSteps,
                MaxPlanningMilliseconds = settings.MaxPlanningMilliseconds,
                MaxHighLevelNodes = settings.MaxHighLevelNodes,
                MaxLowLevelExpandedNodes = settings.MaxExpandedNodesPerAgent,
                Heuristic = settings.CbsHeuristic,
                CancellationToken = settings.CancellationToken,
                RecordReplay = settings.RecordReplay
            });
            summary.AllAgentsRouted = result.Succeeded;
            summary.Paths = new List<AgentPath>(result.Paths);
            if (result.Succeeded) summary.Conflicts = new List<MapfConflict>(result.Conflicts);
            else if (result.ReplayFrames.Count > 0 && result.ReplayFrames[result.ReplayFrames.Count - 1].Conflict != null)
                summary.Conflicts = new List<MapfConflict> { result.ReplayFrames[result.ReplayFrames.Count - 1].Conflict };
            else summary.Conflicts = ConflictDetector.FindConflicts(summary.Paths);
            summary.ExpandedNodes = result.ExpandedLowLevelNodes;
            summary.HighLevelNodes = result.ExpandedConstraintNodes;
            summary.GeneratedConstraints = result.GeneratedConstraints;
            summary.ReplanCount = result.ReplanCount;
            summary.HeuristicExpandedNodes = result.ExpandedHeuristicNodes;
            summary.WaitActionCount = result.WaitActionCount;
            summary.CbsReplayFrames = new List<CBSReplayFrame>(result.ReplayFrames);
            summary.HeuristicMilliseconds = result.HeuristicMilliseconds; summary.HeuristicLog = result.HeuristicLog;
            summary.PlanningConflictCount = result.PlanningConflictCount;
            summary.FailedAgentId = result.FailedAgentId;
            summary.FailureReason = result.FailureReason;
        }

        private static void RunPrimal(GridMap map, IReadOnlyList<AgentDefinition> agents,
            MapfPlannerSettings settings, MapfRunSummary summary)
        {
            PrimalBridgeResult result = PrimalProcessBridge.Run(map, agents, settings.PythonExecutable, settings.PrimalScriptPath,
                settings.MaxPlanningMilliseconds, settings.CancellationToken,settings.MaxTimeSteps,settings.Profile.Seed);
            summary.AllAgentsRouted = result.Succeeded;
            summary.Paths = result.Paths;
            summary.Conflicts = result.Conflicts;
            summary.PlanningMilliseconds = result.PlanningMilliseconds;
            summary.FailureReason = result.FailureReason;
            summary.AgentLogs.AddRange(result.AgentLogs);
            summary.PrimalDecisions=result.Decisions;
        }

        private static List<AgentDefinition> OrderAgents(GridMap map, IReadOnlyList<AgentDefinition> agents,
            AgentPriorityMode mode, List<AgentPriorityDecision> decisions, MapfPlannerSettings settings)
        {
            var ordered = new List<AgentDefinition>(agents.Count);
            for (int i = 0; i < agents.Count; i++) ordered.Add(agents[i]);
            if (mode == AgentPriorityMode.InputOrder)
            {
                for (int i = 0; i < ordered.Count; i++)
                    decisions.Add(new AgentPriorityDecision { AgentId = ordered[i].Id, Rank = i + 1, Score = 0, Reason = "사용자 입력 순서를 유지" });
                return ordered;
            }

            Dictionary<int, PriorityScore> scores = CalculatePriorityScores(map, agents, mode, settings);
            // Stable insertion sort keeps original order when priority scores tie.
            for (int i = 1; i < ordered.Count; i++)
            {
                AgentDefinition item = ordered[i];
                int itemScore = GetScore(scores, item).Total;
                int j = i - 1;
                while (j >= 0 && GetScore(scores, ordered[j]).Total < itemScore)
                {
                    ordered[j + 1] = ordered[j];
                    j--;
                }
                ordered[j + 1] = item;
            }
            for (int i = 0; i < ordered.Count; i++)
                decisions.Add(new AgentPriorityDecision
                {
                    AgentId = ordered[i].Id,
                    Rank = i + 1,
                    Score = GetScore(scores, ordered[i]).Total,
                    DistanceComponent = GetScore(scores, ordered[i]).Distance,
                    CongestionComponent = GetScore(scores, ordered[i]).Congestion,
                    NarrownessComponent = GetScore(scores, ordered[i]).Narrowness,
                    Reason = mode == AgentPriorityMode.LongestDistanceFirst ? "맨해튼 거리 점수" : $"거리×{settings.Profile.DistanceWeight} + 경로 중첩×{settings.Profile.CongestionWeight} + 협소도×{settings.Profile.NarrownessWeight}"
                });
            return ordered;
        }

        private static Dictionary<int, PriorityScore> CalculatePriorityScores(GridMap map, IReadOnlyList<AgentDefinition> agents, AgentPriorityMode mode, MapfPlannerSettings settings)
        {
            var scores = new Dictionary<int, PriorityScore>();
            var paths = new Dictionary<int, AgentPath>();
            var usage = new Dictionary<GridPosition, int>();
            var planner = new AStarPlanner();
            for (int i = 0; i < agents.Count; i++)
            {
                AgentDefinition agent = agents[i];
                if (agent == null) continue;
                int distance = Math.Abs(agent.Start.X - agent.Goal.X) + Math.Abs(agent.Start.Y - agent.Goal.Y);
                if (mode == AgentPriorityMode.LongestDistanceFirst)
                { scores[agent.Id] = new PriorityScore(distance, distance, 0, 0); continue; }
                PathSearchResult path = planner.Search(map, agent, Math.Min(50000, settings.MaxExpandedNodesPerAgent), Math.Max(1, settings.MaxPlanningMilliseconds / Math.Max(1, agents.Count)), settings.CancellationToken);
                if (path.Succeeded)
                {
                    paths[agent.Id] = path.Path;
                    for (int step = 0; step < path.Path.Positions.Count; step++)
                    {
                        GridPosition cell = path.Path.Positions[step];
                        usage[cell] = usage.TryGetValue(cell, out int count) ? count + 1 : 1;
                    }
                }
                scores[agent.Id] = new PriorityScore(mode == AgentPriorityMode.LongestDistanceFirst ? distance : 0,
                    distance, 0, 0);
            }

            if (mode == AgentPriorityMode.MostConstrainedFirst)
            {
                for (int i = 0; i < agents.Count; i++)
                {
                    AgentDefinition agent = agents[i];
                    if (agent == null) continue;
                    int overlap = 0;
                    if (paths.TryGetValue(agent.Id, out AgentPath path))
                    {
                        var counted = new HashSet<GridPosition>();
                        for (int step = 0; step < path.Positions.Count; step++)
                        {
                            GridPosition cell = path.Positions[step];
                            if (counted.Add(cell) && usage.TryGetValue(cell, out int count)) overlap += Math.Max(0, count - 1);
                        }
                    }
                    int degree = 0;
                    foreach (GridPosition _ in map.GetNeighbors(agent.Start)) degree++;
                    foreach (GridPosition _ in map.GetNeighbors(agent.Goal)) degree++;
                    int distance = Math.Abs(agent.Start.X - agent.Goal.X) + Math.Abs(agent.Start.Y - agent.Goal.Y);
                    int narrowness = Math.Max(0, 8 - degree);
                    int distanceScore = distance * Math.Max(0, Math.Min(100, settings.Profile.DistanceWeight));
                    int congestionScore = overlap * Math.Max(0, Math.Min(100, settings.Profile.CongestionWeight));
                    int narrowScore = narrowness * Math.Max(0, Math.Min(100, settings.Profile.NarrownessWeight));
                    scores[agent.Id] = new PriorityScore(congestionScore + narrowScore + distanceScore,
                        distanceScore, congestionScore, narrowScore);
                }
            }
            return scores;
        }

        private static PriorityScore GetScore(Dictionary<int, PriorityScore> scores, AgentDefinition agent) =>
            agent != null && scores.TryGetValue(agent.Id, out PriorityScore score) ? score : new PriorityScore(int.MinValue, 0, 0, 0);

        private readonly struct PriorityScore
        {
            public readonly int Total, Distance, Congestion, Narrowness;
            public PriorityScore(int total, int distance, int congestion, int narrowness)
            { Total = total; Distance = distance; Congestion = congestion; Narrowness = narrowness; }
        }

        private static void CalculatePathMetrics(MapfRunSummary summary)
        {
            summary.WaitActionCount = 0;
            int completion = 0;
            int cost = 0;
            for (int i = 0; i < summary.Paths.Count; i++)
            {
                if (summary.Paths[i] == null || summary.Paths[i].IsEmpty) continue;
                cost += summary.Paths[i].MoveCost;
                completion = Math.Max(completion, summary.Paths[i].MoveCost);
                IReadOnlyList<GridPosition> positions = summary.Paths[i].Positions;
                for (int step = 1; step < positions.Count; step++)
                    if (positions[step] == positions[step - 1]) summary.WaitActionCount++;
            }
            summary.TotalMoveCost = cost;
            summary.CompletionTime = completion;
        }

        private static void RecordAgentLogs(MapfRunSummary summary)
        {
            for (int i = 0; i < summary.Paths.Count; i++)
            {
                AgentPath path = summary.Paths[i];
                if (path == null) continue;
                summary.AgentLogs.Add(new AgentRunLog
                {
                    AgentId = path.AgentId,
                    Message = $"시간 단계 경로 {path.Positions.Count}칸, 이동 비용 {path.MoveCost}, 도착 t={path.MoveCost}"
                });
            }
            for (int i = 0; i < summary.Conflicts.Count; i++)
            {
                MapfConflict conflict = summary.Conflicts[i];
                summary.AgentLogs.Add(new AgentRunLog
                {
                    AgentId = conflict.FirstAgentId,
                    Message = $"t={conflict.TimeStep} {conflict.Type} 충돌 · Agent {conflict.SecondAgentId}"
                });
            }
        }
    }
}
