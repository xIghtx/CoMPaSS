using System;
using System.Collections.Generic;
using CoMPaSS.Core;

namespace CoMPaSS.Experiment
{
    public sealed class MapfAlgorithmAggregate
    {
        public MapfAlgorithmKind Algorithm { get; internal set; }
        public int Runs { get; internal set; }
        public int Successes { get; internal set; }
        public int Failures => Runs - Successes;
        public double SuccessRate => Runs == 0 ? 0 : (double)Successes / Runs;
        public double MeanTimeMilliseconds { get; internal set; }
        public double TimeStandardDeviationMilliseconds { get; internal set; }
        public double MinimumTimeMilliseconds { get; internal set; }
        public double MaximumTimeMilliseconds { get; internal set; }
        public double MeanMoveCost { get; internal set; }
        public double MoveCostStandardDeviation { get; internal set; }
        public List<MapfRunSummary> Samples { get; } = new List<MapfRunSummary>();
    }

    public sealed class MapfBatchSummary
    {
        public int FirstSeed { get; internal set; }
        public int Repetitions { get; internal set; }
        public GridMap PreviewMap { get; internal set; }
        public List<AgentDefinition> PreviewAgents { get; internal set; } = new List<AgentDefinition>();
        public List<MapfAlgorithmAggregate> Algorithms { get; } = new List<MapfAlgorithmAggregate>();
    }

    public sealed class MapfExperimentBatchRunner
    {
        private readonly MapfExperimentRunner runner = new MapfExperimentRunner();

        public MapfBatchSummary RunSeedSweep(int width, int height, int agentCount, MapTemplate template,
            double obstacleDensity, int firstSeed, int repetitions, MapfPlannerSettings settings = null,
            int placementSeed = int.MinValue)
        {
            if (width < 4 || height < 4) throw new ArgumentOutOfRangeException("Map dimensions must be at least 4.");
            if (agentCount <= 0 || agentCount * 2 > width * height) throw new ArgumentOutOfRangeException(nameof(agentCount));
            if (repetitions <= 0 || repetitions > 100) throw new ArgumentOutOfRangeException(nameof(repetitions));
            settings = settings ?? new MapfPlannerSettings();

            var summary = new MapfBatchSummary { FirstSeed = firstSeed, Repetitions = repetitions };
            var algorithms = new[] { MapfAlgorithmKind.IndependentAStar, MapfAlgorithmKind.CooperativeAStar, MapfAlgorithmKind.ConflictBasedSearch };
            foreach (MapfAlgorithmKind algorithm in algorithms)
                summary.Algorithms.Add(new MapfAlgorithmAggregate { Algorithm = algorithm });

            for (int repetition = 0; repetition < repetitions; repetition++)
            {
                int seed = unchecked(firstSeed + repetition);
                GridMap map = ProceduralMapGenerator.Create(width, height, template, obstacleDensity, seed);
                int layoutSeed = placementSeed == int.MinValue ? seed : unchecked(placementSeed + repetition);
                List<AgentDefinition> agents = ProceduralMapGenerator.CreateRandomAgents(map, agentCount, layoutSeed, out _);
                if (repetition == 0)
                {
                    summary.PreviewMap = map;
                    summary.PreviewAgents = new List<AgentDefinition>(agents);
                }
                for (int algorithmIndex = 0; algorithmIndex < summary.Algorithms.Count; algorithmIndex++)
                {
                    MapfAlgorithmAggregate aggregate = summary.Algorithms[algorithmIndex];
                    MapfRunSummary sample = runner.Run(map, agents, aggregate.Algorithm, settings);
                    aggregate.Samples.Add(sample);
                }
            }

            for (int i = 0; i < summary.Algorithms.Count; i++) CalculateStatistics(summary.Algorithms[i]);
            return summary;
        }

        private static void CalculateStatistics(MapfAlgorithmAggregate aggregate)
        {
            aggregate.Runs = aggregate.Samples.Count;
            if (aggregate.Runs == 0) return;
            double sumTime = 0;
            double sumCost = 0;
            double sumCostSquared = 0;
            double sumTimeSquared = 0;
            aggregate.MinimumTimeMilliseconds = double.MaxValue;
            aggregate.MaximumTimeMilliseconds = double.MinValue;
            int successfulCostSamples = 0;

            for (int i = 0; i < aggregate.Samples.Count; i++)
            {
                MapfRunSummary sample = aggregate.Samples[i];
                sumTime += sample.PlanningMilliseconds;
                sumTimeSquared += sample.PlanningMilliseconds * sample.PlanningMilliseconds;
                aggregate.MinimumTimeMilliseconds = Math.Min(aggregate.MinimumTimeMilliseconds, sample.PlanningMilliseconds);
                aggregate.MaximumTimeMilliseconds = Math.Max(aggregate.MaximumTimeMilliseconds, sample.PlanningMilliseconds);
                if (sample.CollisionFree) aggregate.Successes++;
                if (sample.AllAgentsRouted)
                {
                    sumCost += sample.TotalMoveCost;
                    sumCostSquared += sample.TotalMoveCost * sample.TotalMoveCost;
                    successfulCostSamples++;
                }
            }

            aggregate.MeanTimeMilliseconds = sumTime / aggregate.Runs;
            aggregate.TimeStandardDeviationMilliseconds = Math.Sqrt(Math.Max(0, sumTimeSquared / aggregate.Runs - aggregate.MeanTimeMilliseconds * aggregate.MeanTimeMilliseconds));
            if (successfulCostSamples > 0)
            {
                aggregate.MeanMoveCost = sumCost / successfulCostSamples;
                aggregate.MoveCostStandardDeviation = Math.Sqrt(Math.Max(0, sumCostSquared / successfulCostSamples - aggregate.MeanMoveCost * aggregate.MeanMoveCost));
            }
        }

    }
}
