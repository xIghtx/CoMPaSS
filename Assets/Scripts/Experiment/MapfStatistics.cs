using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace CoMPaSS.Experiment
{
    public static class MapfStatistics
    {
        public static string ConditionSummaryCsv(IReadOnlyList<CampaignRecord> records)
        {
            var groups=new Dictionary<string,List<CampaignRecord>>();
            foreach(CampaignRecord r in records)
            {
                CampaignCase c=r.scenario;
                string key=$"{r.algorithm}|{c.width}x{c.height}|n={c.agentCount}|{c.template}|density={c.requestedDensity.ToString(CultureInfo.InvariantCulture)}|bottlenecks={c.bottleneckCount}|width={c.passageWidth}|{c.distribution}|{r.configuration}";
                if(!groups.TryGetValue(key,out var group)){group=new List<CampaignRecord>();groups[key]=group;}group.Add(r);
            }
            var csv=new StringBuilder("condition,metric,samples,raw_success,execution_success,failures,mean,variance_population,stddev_population,min,max,p95\r\n");
            foreach(var group in groups)
            {
                int raw=0,executed=0;foreach(var r in group.Value){if(r.rawSuccess)raw++;if(r.assistedSuccess)executed++;}
                var metrics=new Dictionary<string,Func<CampaignRecord,double>> {
                    {"planning_ms_all",r=>r.planningMs},{"priority_ms_all",r=>r.priorityMs},{"heuristic_ms_all",r=>r.heuristicMs},
                    {"recovery_ms_all",r=>r.recoveryMs},{"expanded_nodes_all",r=>r.expandedNodes},{"heap_peak_sample_bytes_all",r=>r.managedPeakBytes},
                    {"executed_cost_success_only",r=>r.executedCost},{"steps_success_only",r=>r.steps},{"waits_success_only",r=>r.waits}
                };
                foreach(var metric in metrics)
                {
                    var values=new List<double>();bool successOnly=metric.Key.EndsWith("success_only",StringComparison.Ordinal);
                    foreach(var r in group.Value)if(!successOnly||r.assistedSuccess)values.Add(metric.Value(r));
                    values.Sort();double mean=0,variance=0;foreach(double x in values)mean+=x;if(values.Count>0)mean/=values.Count;
                    foreach(double x in values)variance+=(x-mean)*(x-mean);if(values.Count>0)variance/=values.Count;
                    csv.Append('"').Append(group.Key.Replace("\"","\"\"")).Append("\",").Append(metric.Key).Append(',').Append(values.Count)
                        .Append(',').Append(raw).Append(',').Append(executed).Append(',').Append(group.Value.Count-executed);
                    double[] stats=values.Count==0?Array.Empty<double>():new[]{mean,variance,Math.Sqrt(variance),values[0],values[values.Count-1],values[Math.Max(0,(int)Math.Ceiling(values.Count*.95)-1)]};
                    if(stats.Length==0)csv.Append(",,,,,,");else foreach(double x in stats)csv.Append(',').Append(x.ToString("0.######",CultureInfo.InvariantCulture));
                    csv.Append("\r\n");
                }
            }
            return csv.ToString();
        }
    }
}
