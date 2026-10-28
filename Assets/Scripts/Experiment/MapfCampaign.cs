using System;
using System.Collections.Generic;
using System.Globalization;
using CoMPaSS.Core;

namespace CoMPaSS.Experiment
{
    [Serializable]
    public sealed class CampaignCase
    {
        public int index, width, height, agentCount, mapSeed, placementSeed;
        public MapTemplate template;
        public float requestedDensity;
        public int bottleneckCount=1, passageWidth=3;
        public AgentDistribution distribution;
    }

    [Serializable]
    public sealed class CampaignRecord
    {
        public string runId;
        public string configuration;
        public CampaignCase scenario;
        public MapfAlgorithmKind algorithm;
        public int routedAgents, arrivedAgents, openedCells, blockedCells;
        public bool rawSuccess, assistedSuccess;
        public string terminalState, failureReason;
        public double planningMs, recoveryMs;
        public double priorityMs,heuristicMs;
        public long managedPeakBytes;
        public int planningConflicts;
        public int originalCost, executedCost, steps, waits, expandedNodes, initialConflicts, recoveries;
        public SimulationEvent[] events;
    }

    [Serializable]
    public sealed class CampaignManifest
    {
        public int version = 1;
        public string id;
        public MapfAlgorithmKind algorithm;
        public MapfPlannerSettings settings;
        public bool autoRecovery = true;
        public bool allowFallback;
        public int recoveryLimit=6;
        public int nextIndex;
        public List<CampaignCase> cases = new List<CampaignCase>();
        public List<CampaignRecord> records = new List<CampaignRecord>();
    }

    public sealed class PlannedCampaignCase
    {
        public CampaignCase Case;
        public GridMap Map;
        public List<AgentDefinition> Agents;
        public MapfRunSummary Summary;
        public int OpenedCells;
    }

    public static class MapfCampaign
    {
        public static List<CampaignCase> BuildCases(string sizes, string counts, string densities, int repetitions,
            int firstSeed, int placementSeed, MapTemplate template, string bottleneckCounts = "1", string passageWidths = "3", AgentDistribution distribution = AgentDistribution.Random)
        {
            if (repetitions < 1 || repetitions > 100) throw new ArgumentException("반복 횟수는 1~100이어야 합니다.");
            var result = new List<CampaignCase>();
            var unique = new HashSet<string>();
            foreach (string rawSize in sizes.Split(','))
            {
                string[] size = rawSize.Trim().ToLowerInvariant().Replace('×', 'x').Split('x');
                if (!int.TryParse(size[0], out int width) || size.Length > 2) throw new ArgumentException("지도 크기 예: 12x12,20x14,32x32");
                int height = width;
                if (size.Length == 2 && !int.TryParse(size[1], out height)) throw new ArgumentException("지도 높이가 숫자가 아닙니다.");
                if (width < 4 || height < 4 || width > 64 || height > 64) throw new ArgumentException("지도 각 변은 4~64 범위입니다.");
                foreach (string rawCount in counts.Split(','))
                {
                    if (!int.TryParse(rawCount.Trim(), out int count) || count < 1 || count > 32 || count * 2 > width * height)
                        throw new ArgumentException("에이전트 수는 1~32이고, 지도에 시작·목표 공간이 있어야 합니다.");
                    foreach (string rawDensity in densities.Split(','))
                    {
                        if (!float.TryParse(rawDensity.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float density) ||
                            float.IsNaN(density) || density < 0 || density > 0.45f) throw new ArgumentException("밀도는 0~0.45입니다. 예: 0.1,0.2,0.3");
                        if(template==MapTemplate.Open||template==MapTemplate.NarrowCorridors)density=0;
                        foreach(string rawBottleneck in bottleneckCounts.Split(','))
                        foreach(string rawPassage in passageWidths.Split(','))
                        {
                        if(!int.TryParse(rawBottleneck,out int bottlenecks)||bottlenecks<1||bottlenecks>8||!int.TryParse(rawPassage,out int passage)||passage<1||passage>8)
                            throw new ArgumentException("병목 수와 통로 폭은 1~8입니다.");
                        bottlenecks=Math.Min(bottlenecks,width-2);passage=Math.Min(passage,height);
                        if(template!=MapTemplate.Bottleneck)bottlenecks=1;
                        if(template!=MapTemplate.Bottleneck&&template!=MapTemplate.NarrowCorridors)passage=3;
                        string key = $"{width}/{height}/{count}/{density.ToString(CultureInfo.InvariantCulture)}/{bottlenecks}/{passage}";
                        if (!unique.Add(key)) continue;
                        for (int repeat = 0; repeat < repetitions; repeat++)
                        {
                            if (result.Count >= 2000) throw new ArgumentException("한 실험 묶음은 최대 2,000회입니다.");
                            result.Add(new CampaignCase { index = result.Count, width = width, height = height, agentCount = count,
                                mapSeed = unchecked(firstSeed + repeat), placementSeed = unchecked(placementSeed + repeat),
                                template = template, requestedDensity = density, bottleneckCount=bottlenecks,passageWidth=passage,distribution=distribution });
                        }
                        }
                    }
                }
            }
            return result;
        }

        public static PlannedCampaignCase Plan(CampaignCase scenario, MapfAlgorithmKind algorithm, MapfPlannerSettings settings)
        {
            ValidateCase(scenario);
            settings=settings.Copy();settings.Profile.Seed=scenario.mapSeed;
            settings.CancellationToken.ThrowIfCancellationRequested();
            GridMap map = ProceduralMapGenerator.Create(scenario.width, scenario.height, scenario.template, scenario.requestedDensity, scenario.mapSeed,scenario.bottleneckCount,scenario.passageWidth);
            List<AgentDefinition> agents = ProceduralMapGenerator.CreateRandomAgents(map, scenario.agentCount, scenario.placementSeed, out int opened,scenario.distribution);
            return new PlannedCampaignCase { Case = scenario, Map = map, Agents = agents, OpenedCells = opened,
                Summary = new MapfExperimentRunner().Run(map, agents, algorithm, settings) };
        }

        public static void ValidateCase(CampaignCase c)
        {
            if (c == null || c.width < 4 || c.width > 64 || c.height < 4 || c.height > 64 || c.agentCount < 1 ||
                c.agentCount > 32 || c.agentCount * 2 > c.width * c.height || float.IsNaN(c.requestedDensity) ||
                c.requestedDensity < 0 || c.requestedDensity > .45f || c.bottleneckCount<1||c.bottleneckCount>8||c.passageWidth<1||c.passageWidth>8||
                !Enum.IsDefined(typeof(AgentDistribution),c.distribution)||!Enum.IsDefined(typeof(MapTemplate), c.template))
                throw new ArgumentException("저장된 실험의 지도 크기·밀도·에이전트 수가 올바르지 않습니다.");
        }

        public static CampaignRecord Record(string id, CampaignCase scenario, MapfSimulation simulation, int openedCells)
        {
            int blocked = 0;
            for (int y = 0; y < simulation.Map.Height; y++)
            for (int x = 0; x < simulation.Map.Width; x++) if (simulation.Map.IsBlocked(new GridPosition(x, y))) blocked++;
            var events = new List<SimulationEvent>();
            foreach (SimulationEvent e in simulation.Events) events.Add(new SimulationEvent { step = e.step, kind = e.kind,
                firstAgent = e.firstAgent, secondAgent = e.secondAgent, message = e.message, method = e.method, milliseconds = e.milliseconds });
            return new CampaignRecord { runId = id, scenario = scenario, algorithm = simulation.Original.Algorithm,
                configuration=simulation.Original.Configuration,priorityMs=simulation.Original.PriorityMilliseconds,heuristicMs=simulation.Original.HeuristicMilliseconds,
                managedPeakBytes=simulation.Original.SampledManagedPeakBytes,planningConflicts=simulation.Original.PlanningConflictCount,
                routedAgents = simulation.Original.RoutedAgents, arrivedAgents = simulation.ArrivedCount,
                openedCells = openedCells, blockedCells = blocked, rawSuccess = simulation.Original.CollisionFree,
                assistedSuccess = simulation.State == SimulationState.Completed, terminalState = simulation.State.ToString(),
                failureReason = simulation.FailureReason ?? simulation.Original.FailureReason,
                planningMs = simulation.Original.PlanningMilliseconds, recoveryMs = simulation.RecoveryMilliseconds,
                originalCost = simulation.Original.TotalMoveCost, executedCost = simulation.ExecutedCost, steps = simulation.Step,
                waits = simulation.WaitActions, expandedNodes = simulation.Original.ExpandedNodes,
                initialConflicts = simulation.Original.Conflicts.Count, recoveries = simulation.RecoveryCount,
                events = events.ToArray() };
        }
    }
}
