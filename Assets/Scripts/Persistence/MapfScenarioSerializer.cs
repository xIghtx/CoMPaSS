using System;
using System.Collections.Generic;
using System.IO;
using CoMPaSS.Algorithms;
using CoMPaSS.Core;
using CoMPaSS.Experiment;
using UnityEngine;

namespace CoMPaSS.Persistence
{
    [Serializable]
    public sealed class ScenarioDocument
    {
        public int schemaVersion = 1;
        public int width;
        public int height;
        public string blockedCells;
        public int seed;
        public int placementSeed;
        public int algorithm;
        public int maxTimeSteps = 256;
        public int maxExpandedNodesPerAgent = 100000;
        public int maxHighLevelNodes = 10000;
        public int maxPlanningMilliseconds = 60000;
        public int priorityMode;
        public int cbsHeuristic;
        public string pythonExecutable;
        public string primalScriptPath;
        public int mapTemplate;
        public int bottleneckCount=1, passageWidth=3;
        public int distribution;
        public SearchProfile profile=new SearchProfile();
        public float obstacleDensity = 0.12f;
        public ScenarioAgentDocument[] agents = Array.Empty<ScenarioAgentDocument>();
        public ScenarioReplayDocument replay;
    }

    [Serializable]
    public sealed class ScenarioAgentDocument
    {
        public int id;
        public int startX;
        public int startY;
        public int goalX;
        public int goalY;
    }

    [Serializable]
    public sealed class ScenarioPathDocument
    {
        public int agentId;
        public int[] x = Array.Empty<int>();
        public int[] y = Array.Empty<int>();
    }

    [Serializable]
    public sealed class ScenarioConflictDocument
    {
        public int type;
        public int firstAgentId;
        public int secondAgentId;
        public int timeStep;
        public int firstX;
        public int firstY;
        public int secondX;
        public int secondY;
    }

    [Serializable]
    public sealed class ScenarioCbsFrameDocument
    {
        public int nodeId;
        public int parentNodeId;
        public int sumOfCosts;
        public string appliedConstraint;
        public bool isConstraintTrial;
        public int constrainedAgentId = -1;
        public bool replanSucceeded = true;
        public string replanFailureReason;
        public ScenarioConflictDocument conflict;
        public ScenarioPathDocument[] paths = Array.Empty<ScenarioPathDocument>();
    }

    [Serializable]
    public sealed class ScenarioDecisionDocument
    { public int agentId,rank,distance,congestion,narrowness;public double score;public string reason; }
    [Serializable]
    public sealed class ScenarioLogDocument
    { public int agentId;public string message; }

    [Serializable]
    public sealed class ScenarioReplayDocument
    {
        public bool allAgentsRouted;
        public string configuration;
        public string variant;
        public double priorityMilliseconds, heuristicMilliseconds;
        public long sampledManagedPeakBytes;
        public int planningConflictCount;
        public string[] heuristicLog;
        public PrimalDecision[] primalDecisions;
        public ScenarioDecisionDocument[] priorities;
        public ScenarioLogDocument[] logs;
        public double planningMilliseconds;
        public long managedMemoryDeltaBytes;
        public int totalMoveCost;
        public int completionTime;
        public int expandedNodes;
        public int highLevelNodes;
        public int generatedConstraints;
        public int replanCount;
        public int heuristicExpandedNodes;
        public int waitActionCount;
        public int failedAgentId;
        public string failureReason;
        public ScenarioPathDocument[] paths = Array.Empty<ScenarioPathDocument>();
        public ScenarioConflictDocument[] conflicts = Array.Empty<ScenarioConflictDocument>();
        public ScenarioCbsFrameDocument[] cbsFrames = Array.Empty<ScenarioCbsFrameDocument>();
    }

    public sealed class LoadedScenario
    {
        public GridMap Map { get; }
        public List<AgentDefinition> Agents { get; }
        public ScenarioDocument Settings { get; }
        public MapfRunSummary Replay { get; }
        internal LoadedScenario(GridMap map, List<AgentDefinition> agents, ScenarioDocument settings, MapfRunSummary replay)
        { Map = map; Agents = agents; Settings = settings; Replay = replay; }
    }

    public static class MapfScenarioSerializer
    {
        public static string ToJson(GridMap map, IReadOnlyList<AgentDefinition> agents,
            ScenarioDocument settings = null, MapfRunSummary replay = null)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            settings = settings ?? new ScenarioDocument();
            settings.width = map.Width;
            settings.height = map.Height;
            settings.blockedCells = EncodeCells(map);
            settings.replay = replay == null ? null : EncodeReplay(replay);
            settings.agents = new ScenarioAgentDocument[agents == null ? 0 : agents.Count];
            for (int i = 0; i < settings.agents.Length; i++)
            {
                AgentDefinition agent = agents[i];
                if (agent == null) throw new InvalidDataException("Agent list contains a missing entry.");
                settings.agents[i] = new ScenarioAgentDocument
                {
                    id = agent.Id,
                    startX = agent.Start.X,
                    startY = agent.Start.Y,
                    goalX = agent.Goal.X,
                    goalY = agent.Goal.Y
                };
            }
            return JsonUtility.ToJson(settings, true);
        }

        public static LoadedScenario FromJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) throw new InvalidDataException("Scenario file is empty.");
            ScenarioDocument document = JsonUtility.FromJson<ScenarioDocument>(json);
            if (document == null || document.schemaVersion != 1) throw new InvalidDataException("Unsupported or invalid scenario format.");
            if (document.width < 4 || document.height < 4 || document.width > 64 || document.height > 64)
                throw new InvalidDataException("Map dimensions must be between 4 and 64.");
            int cellCount = checked(document.width * document.height);
            if (document.blockedCells == null || document.blockedCells.Length != cellCount)
                throw new InvalidDataException("Obstacle data does not match the map dimensions.");

            var map = new GridMap(document.width, document.height);
            for (int y = 0; y < document.height; y++)
            for (int x = 0; x < document.width; x++)
            {
                char value = document.blockedCells[y * document.width + x];
                if (value != '0' && value != '1') throw new InvalidDataException("Obstacle data contains invalid characters.");
                map.SetBlocked(new GridPosition(x, y), value == '1');
            }

            var agents = new List<AgentDefinition>();
            var ids = new HashSet<int>();
            var starts = new HashSet<GridPosition>();
            var goals = new HashSet<GridPosition>();
            if (document.agents != null)
            {
                for (int i = 0; i < document.agents.Length; i++)
                {
                    ScenarioAgentDocument agent = document.agents[i];
                    if (agent == null) throw new InvalidDataException("Scenario contains an empty agent record.");
                    var start = new GridPosition(agent.startX, agent.startY);
                    var goal = new GridPosition(agent.goalX, agent.goalY);
                    if (!map.Contains(start) || !map.Contains(goal) || map.IsBlocked(start) || map.IsBlocked(goal))
                        throw new InvalidDataException("An agent start or goal lies outside the map or on an obstacle.");
                    if (!ids.Add(agent.id)) throw new InvalidDataException("Scenario contains duplicate agent IDs.");
                    if (!starts.Add(start) || !goals.Add(goal)) throw new InvalidDataException("Agent starts or goals must be unique.");
                    agents.Add(new AgentDefinition(agent.id, start, goal));
                }
            }
            if (agents.Count == 0 || agents.Count > 32) throw new InvalidDataException("A scenario must contain between 1 and 32 agents.");
            MapfRunSummary summary = DecodeReplay(document);
            if (summary != null)
            {
                ValidateReplayPaths(map, agents, summary.Paths);
                foreach (CBSReplayFrame frame in summary.CbsReplayFrames) ValidateReplayPaths(map, agents, frame.Paths);
                summary.AllAgentsRouted = summary.AllAgentsRouted && summary.RoutedAgents == agents.Count;
                summary.Conflicts = ConflictDetector.FindConflicts(summary.Paths);
            }
            return new LoadedScenario(map, agents, document, summary);
        }

        private static ScenarioReplayDocument EncodeReplay(MapfRunSummary result)
        {
            var replay = new ScenarioReplayDocument
            {
                allAgentsRouted = result.AllAgentsRouted,
                variant=result.Variant,
                primalDecisions=result.PrimalDecisions.ToArray(),
                configuration=result.Configuration,priorityMilliseconds=result.PriorityMilliseconds,heuristicMilliseconds=result.HeuristicMilliseconds,
                sampledManagedPeakBytes=result.SampledManagedPeakBytes,planningConflictCount=result.PlanningConflictCount,heuristicLog=result.HeuristicLog.ToArray(),
                planningMilliseconds = result.PlanningMilliseconds,
                managedMemoryDeltaBytes = result.ManagedMemoryDeltaBytes,
                totalMoveCost = result.TotalMoveCost,
                completionTime = result.CompletionTime,
                expandedNodes = result.ExpandedNodes,
                highLevelNodes = result.HighLevelNodes,
                generatedConstraints = result.GeneratedConstraints,
                replanCount = result.ReplanCount,
                heuristicExpandedNodes = result.HeuristicExpandedNodes,
                waitActionCount = result.WaitActionCount,
                failedAgentId = result.FailedAgentId,
                failureReason = result.FailureReason,
                paths = EncodePaths(result.Paths),
                conflicts = EncodeConflicts(result.Conflicts)
            };
            replay.cbsFrames = new ScenarioCbsFrameDocument[result.CbsReplayFrames.Count];
            replay.priorities=new ScenarioDecisionDocument[result.PriorityDecisions.Count];
            for(int i=0;i<replay.priorities.Length;i++)
            {AgentPriorityDecision d=result.PriorityDecisions[i];replay.priorities[i]=new ScenarioDecisionDocument{agentId=d.AgentId,rank=d.Rank,score=d.Score,reason=d.Reason,distance=d.DistanceComponent,congestion=d.CongestionComponent,narrowness=d.NarrownessComponent};}
            replay.logs=new ScenarioLogDocument[result.AgentLogs.Count];
            for(int i=0;i<replay.logs.Length;i++)replay.logs[i]=new ScenarioLogDocument{agentId=result.AgentLogs[i].AgentId,message=result.AgentLogs[i].Message};
            for (int i = 0; i < replay.cbsFrames.Length; i++)
            {
                CBSReplayFrame frame = result.CbsReplayFrames[i];
                replay.cbsFrames[i] = new ScenarioCbsFrameDocument
                {
                    nodeId = frame.NodeId,
                    parentNodeId = frame.ParentNodeId,
                    sumOfCosts = frame.SumOfCosts,
                    appliedConstraint = frame.AppliedConstraint,
                    isConstraintTrial = frame.IsConstraintTrial,
                    constrainedAgentId = frame.ConstrainedAgentId,
                    replanSucceeded = frame.ReplanSucceeded,
                    replanFailureReason = frame.ReplanFailureReason,
                    conflict = EncodeConflict(frame.Conflict),
                    paths = EncodePaths(frame.Paths)
                };
            }
            return replay;
        }

        private static MapfRunSummary DecodeReplay(ScenarioDocument document)
        {
            ScenarioReplayDocument replay = document.replay;
            if (replay == null) return null;
            var summary = new MapfRunSummary
            {
                SettingsSnapshot=new MapfPlannerSettings { MaxTimeSteps=document.maxTimeSteps,MaxPlanningMilliseconds=document.maxPlanningMilliseconds,
                    MaxExpandedNodesPerAgent=document.maxExpandedNodesPerAgent,MaxHighLevelNodes=document.maxHighLevelNodes,PriorityMode=(AgentPriorityMode)document.priorityMode,
                    CbsHeuristic=(CBSHeuristicMode)document.cbsHeuristic,PythonExecutable=document.pythonExecutable,PrimalScriptPath=document.primalScriptPath,Profile=document.profile??new SearchProfile() },
                Algorithm = (MapfAlgorithmKind)Math.Max(0, Math.Min(3, document.algorithm)),
                TotalAgents = document.agents.Length,
                AllAgentsRouted = replay.allAgentsRouted,
                Variant=replay.variant,
                PrimalDecisions=new List<PrimalDecision>(replay.primalDecisions??Array.Empty<PrimalDecision>()),
                Configuration=replay.configuration,PriorityMilliseconds=replay.priorityMilliseconds,HeuristicMilliseconds=replay.heuristicMilliseconds,
                SampledManagedPeakBytes=replay.sampledManagedPeakBytes,PlanningConflictCount=replay.planningConflictCount,HeuristicLog=new List<string>(replay.heuristicLog??Array.Empty<string>()),
                PlanningMilliseconds = replay.planningMilliseconds,
                ManagedMemoryDeltaBytes = replay.managedMemoryDeltaBytes,
                TotalMoveCost = replay.totalMoveCost,
                CompletionTime = replay.completionTime,
                ExpandedNodes = replay.expandedNodes,
                HighLevelNodes = replay.highLevelNodes,
                GeneratedConstraints = replay.generatedConstraints,
                ReplanCount = replay.replanCount,
                HeuristicExpandedNodes = replay.heuristicExpandedNodes,
                WaitActionCount = replay.waitActionCount,
                FailedAgentId = replay.failedAgentId,
                FailureReason = replay.failureReason,
                Paths = DecodePaths(replay.paths),
                Conflicts = DecodeConflicts(replay.conflicts)
            };
            if (replay.cbsFrames != null)
            {
                for (int i = 0; i < replay.cbsFrames.Length; i++)
                {
                    ScenarioCbsFrameDocument frame = replay.cbsFrames[i];
                    if (frame == null) continue;
                    summary.CbsReplayFrames.Add(new CBSReplayFrame(frame.nodeId, frame.parentNodeId, frame.sumOfCosts,
                        DecodePaths(frame.paths), DecodeConflict(frame.conflict), frame.appliedConstraint,
                        frame.isConstraintTrial, frame.constrainedAgentId, frame.replanSucceeded, frame.replanFailureReason));
                }
            }
            if(replay.priorities!=null)foreach(var d in replay.priorities)if(d!=null)summary.PriorityDecisions.Add(new AgentPriorityDecision
            {AgentId=d.agentId,Rank=d.rank,Score=d.score,Reason=d.reason,DistanceComponent=d.distance,CongestionComponent=d.congestion,NarrownessComponent=d.narrowness});
            if(replay.logs!=null)foreach(var log in replay.logs)if(log!=null)summary.AgentLogs.Add(new AgentRunLog{AgentId=log.agentId,Message=log.message});
            return summary;
        }

        private static ScenarioPathDocument[] EncodePaths(IReadOnlyList<AgentPath> paths)
        {
            var documents = new ScenarioPathDocument[paths == null ? 0 : paths.Count];
            for (int i = 0; i < documents.Length; i++)
            {
                AgentPath path = paths[i];
                if (path == null) continue;
                var item = new ScenarioPathDocument { agentId = path.AgentId, x = new int[path.Positions.Count], y = new int[path.Positions.Count] };
                for (int step = 0; step < path.Positions.Count; step++)
                { item.x[step] = path.Positions[step].X; item.y[step] = path.Positions[step].Y; }
                documents[i] = item;
            }
            return documents;
        }

        private static List<AgentPath> DecodePaths(ScenarioPathDocument[] paths)
        {
            var decoded = new List<AgentPath>();
            if (paths == null) return decoded;
            for (int i = 0; i < paths.Length; i++)
            {
                ScenarioPathDocument path = paths[i];
                if (path == null || path.x == null || path.y == null || path.x.Length != path.y.Length) continue;
                var positions = new List<GridPosition>(path.x.Length);
                for (int step = 0; step < path.x.Length; step++) positions.Add(new GridPosition(path.x[step], path.y[step]));
                decoded.Add(new AgentPath(path.agentId, positions));
            }
            return decoded;
        }

        private static void ValidateReplayPaths(GridMap map, IReadOnlyList<AgentDefinition> agents, IReadOnlyList<AgentPath> paths)
        {
            var seen = new HashSet<int>();
            foreach (AgentPath path in paths)
            {
                if (path == null) continue;
                AgentDefinition agent = null;
                foreach (AgentDefinition item in agents) if (item.Id == path.AgentId) { agent = item; break; }
                if (agent == null || !seen.Add(path.AgentId) || path.IsEmpty || path.Positions.Count > 4097 || path.PositionAt(0) != agent.Start)
                    throw new InvalidDataException("저장된 경로의 ID·시작점·길이가 올바르지 않습니다.");
                bool arrived = false;
                for (int t = 0; t < path.Positions.Count; t++)
                {
                    GridPosition p = path.Positions[t], previous = path.PositionAt(Math.Max(0, t - 1));
                    if (map.IsBlocked(p) || Math.Abs(p.X - previous.X) + Math.Abs(p.Y - previous.Y) > 1 || (arrived && p != agent.Goal))
                        throw new InvalidDataException("저장된 경로에 잘못된 이동이 있습니다.");
                    if (p == agent.Goal) arrived = true;
                }
            }
        }

        private static ScenarioConflictDocument[] EncodeConflicts(IReadOnlyList<MapfConflict> conflicts)
        {
            var documents = new ScenarioConflictDocument[conflicts == null ? 0 : conflicts.Count];
            for (int i = 0; i < documents.Length; i++) documents[i] = EncodeConflict(conflicts[i]);
            return documents;
        }

        private static ScenarioConflictDocument EncodeConflict(MapfConflict conflict)
        {
            if (conflict == null) return null;
            return new ScenarioConflictDocument
            {
                type = (int)conflict.Type,
                firstAgentId = conflict.FirstAgentId,
                secondAgentId = conflict.SecondAgentId,
                timeStep = conflict.TimeStep,
                firstX = conflict.FirstPosition.X,
                firstY = conflict.FirstPosition.Y,
                secondX = conflict.SecondPosition.X,
                secondY = conflict.SecondPosition.Y
            };
        }

        private static List<MapfConflict> DecodeConflicts(ScenarioConflictDocument[] documents)
        {
            var conflicts = new List<MapfConflict>();
            if (documents == null) return conflicts;
            for (int i = 0; i < documents.Length; i++)
            {
                ScenarioConflictDocument item = documents[i];
                if (item == null) continue;
                conflicts.Add(new MapfConflict((ConflictType)Math.Max(0, Math.Min(1, item.type)), item.firstAgentId,
                    item.secondAgentId, item.timeStep, new GridPosition(item.firstX, item.firstY), new GridPosition(item.secondX, item.secondY)));
            }
            return conflicts;
        }

        private static MapfConflict DecodeConflict(ScenarioConflictDocument item)
        {
            if (item == null) return null;
            return new MapfConflict((ConflictType)Math.Max(0, Math.Min(1, item.type)), item.firstAgentId,
                item.secondAgentId, item.timeStep, new GridPosition(item.firstX, item.firstY), new GridPosition(item.secondX, item.secondY));
        }

        private static string EncodeCells(GridMap map)
        {
            var chars = new char[map.Width * map.Height];
            for (int y = 0; y < map.Height; y++)
            for (int x = 0; x < map.Width; x++)
                chars[y * map.Width + x] = map.IsBlocked(new GridPosition(x, y)) ? '1' : '0';
            return new string(chars);
        }
    }
}
