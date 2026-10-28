using System.Collections.Generic;
using System.Globalization;
using System.Text;
using CoMPaSS.Experiment;

namespace CoMPaSS.Persistence
{
    public static class MapfCsvExporter
    {
        public static string Export(MapfBatchSummary batch)
        {
            if (batch == null) return Export((IEnumerable<MapfRunSummary>)null);
            var rows = new List<MapfRunSummary>();
            foreach (MapfAlgorithmAggregate aggregate in batch.Algorithms)
                rows.AddRange(aggregate.Samples);
            return Export(rows);
        }

        public static string Export(IEnumerable<MapfRunSummary> summaries)
        {
            var csv = new StringBuilder();
            csv.AppendLine("algorithm,all_agents_routed,collision_free,planning_ms,total_move_cost,completion_time,wait_actions,expanded_nodes,heuristic_expanded_nodes,high_level_nodes,constraints,replans,conflicts,managed_memory_delta_bytes,failure_reason,variant,configuration,priority_ms,heuristic_ms,managed_heap_peak_sample_bytes,planning_conflict_count,average_path_length");
            if (summaries == null) return csv.ToString();

            foreach (MapfRunSummary result in summaries)
            {
                if (result == null) continue;
                csv.Append(Name(result.Algorithm)).Append(',')
                    .Append(result.AllAgentsRouted).Append(',')
                    .Append(result.CollisionFree).Append(',')
                    .Append(result.PlanningMilliseconds.ToString("F3", CultureInfo.InvariantCulture)).Append(',')
                    .Append(result.TotalMoveCost).Append(',')
                    .Append(result.CompletionTime).Append(',')
                    .Append(result.WaitActionCount).Append(',')
                    .Append(result.ExpandedNodes).Append(',')
                    .Append(result.HeuristicExpandedNodes).Append(',')
                    .Append(result.HighLevelNodes).Append(',')
                    .Append(result.GeneratedConstraints).Append(',')
                    .Append(result.ReplanCount).Append(',')
                    .Append(result.Conflicts.Count).Append(',')
                    .Append(result.ManagedMemoryDeltaBytes).Append(',')
                    .Append(Quote(result.FailureReason ?? string.Empty)).Append(',').Append(Quote(result.Variant??"")).Append(',').Append(Quote(result.Configuration??""))
                    .Append(',').Append(result.PriorityMilliseconds.ToString("F3",CultureInfo.InvariantCulture)).Append(',').Append(result.HeuristicMilliseconds.ToString("F3",CultureInfo.InvariantCulture))
                    .Append(',').Append(result.SampledManagedPeakBytes).Append(',').Append(result.PlanningConflictCount).Append(',').Append(result.AveragePathLength.ToString("F3",CultureInfo.InvariantCulture)).AppendLine();
            }
            return csv.ToString();
        }

        public static string Name(MapfAlgorithmKind algorithm)
        {
            switch (algorithm)
            {
                case MapfAlgorithmKind.IndependentAStar: return "A* independent";
                case MapfAlgorithmKind.CooperativeAStar: return "Cooperative A*";
                case MapfAlgorithmKind.ConflictBasedSearch: return "CBS";
                case MapfAlgorithmKind.PrimalExternal: return "PRIMAL (외부 모델)";
                default: return algorithm.ToString();
            }
        }

        private static string Quote(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";
    }
}
