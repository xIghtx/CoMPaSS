using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using CoMPaSS.Algorithms;
using CoMPaSS.Core;
using CoMPaSS.Experiment;

namespace CoMPaSS.Persistence
{
    /// <summary>Portable, dependency-free report data and vector figures.</summary>
    public static class MapfReportExporter
    {
        private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
        public static string Escape(string value) => (value ?? "").Replace("&", "&amp;").Replace("<", "&lt;")
            .Replace(">", "&gt;").Replace("\"", "&quot;").Replace("'", "&#39;");
        private static string N(double value) => value.ToString("0.###", Invariant);
        private static string Csv(object value) => "\"" + Convert.ToString(value, Invariant).Replace("\"", "\"\"") + "\"";

        public static string CampaignCsv(IReadOnlyList<CampaignRecord> records)
        {
            var output = new StringBuilder("run_id,algorithm,width,height,agents,map_seed,placement_seed,template,requested_density,actual_density,opened_cells,routed,arrived,raw_success,execution_success,state,planning_ms,recovery_ms,original_cost,executed_cost,steps,waits,expanded,initial_conflicts,recoveries,failure,configuration,bottlenecks,passage_width,distribution,priority_ms,heuristic_ms,heap_peak_sample_bytes,planning_conflicts\r\n");
            foreach (CampaignRecord r in records)
            {
                CampaignCase c = r.scenario;
                object[] values = { r.runId, r.algorithm, c.width, c.height, c.agentCount, c.mapSeed, c.placementSeed,
                    c.template, c.requestedDensity, (double)r.blockedCells / (c.width * c.height), r.openedCells,
                    r.routedAgents, r.arrivedAgents, r.rawSuccess, r.assistedSuccess, r.terminalState, r.planningMs,
                    r.recoveryMs, r.originalCost, r.executedCost, r.steps, r.waits, r.expandedNodes, r.initialConflicts, r.recoveries, r.failureReason, r.configuration,c.bottleneckCount,c.passageWidth,c.distribution,r.priorityMs,r.heuristicMs,r.managedPeakBytes,r.planningConflicts };
                for (int i = 0; i < values.Length; i++) { if (i > 0) output.Append(','); output.Append(Csv(values[i])); }
                output.Append("\r\n");
            }
            return output.ToString();
        }

        public static string CampaignChart(IReadOnlyList<CampaignRecord> records)
        {
            var b = BeginSvg(1100, 680);
            Text(b, 42, 40, "CoMPaSS · 연속 실험", 24);
            int raw = 0, executed = 0, maxAgents = 1; double maxMs = 1;
            foreach (CampaignRecord r in records)
            { if (r.rawSuccess) raw++; if (r.assistedSuccess) executed++; maxAgents = Math.Max(maxAgents, r.scenario.agentCount); maxMs = Math.Max(maxMs, r.planningMs); }
            Text(b, 42, 75, $"전체 {records.Count}회 · 원래 계획 성공 {raw} · 복구 포함 실행 성공 {executed}", 17);
            Text(b, 42, 104, "실패·시간 초과 포함 · 점 하나 = 실험 1회 · 지도/밀도 조건은 CSV와 각 점의 설명 참조", 13);
            for (int i = 0; i <= 5; i++)
            {
                double y = 540 - i * 75;
                b.Append($"<path d='M90 {N(y)}H1030' stroke='#ddd'/>");
                Text(b, 12, y + 5, N(maxMs * i / 5), 12);
            }
            Text(b, 10, 140, "계획 시간 (ms)", 13);
            foreach (CampaignRecord r in records)
            {
                double x = 90 + r.scenario.agentCount * 920.0 / maxAgents, y = 540 - r.planningMs * 375 / maxMs;
                b.Append($"<circle cx='{N(x)}' cy='{N(y)}' r='5' fill='{(r.rawSuccess ? "#426b50" : r.assistedSuccess ? "#99722c" : "#aa5147")}' fill-opacity='.7'><title>{Escape($"{r.runId}: {r.scenario.width}x{r.scenario.height}, agents={r.scenario.agentCount}, density={N(r.scenario.requestedDensity)}, {N(r.planningMs)}ms, {r.terminalState}")}</title></circle>");
            }
            for (int i = 0; i <= 4; i++) Text(b, 85 + 920 * i / 4.0, 565, N(maxAgents * i / 4.0), 12);
            Text(b, 465, 596, "에이전트 수", 14);
            Text(b, 60, 630, "초록: 원래 계획 성공   ·   황토: 복구 후 성공   ·   적갈: 실행 실패", 14);
            Text(b, 60, 655, "성공률 분모에 실패 실험 포함. 조건이 다른 점들을 하나의 평균 성능으로 해석하지 마세요.", 12);
            return b.Append("</svg>").ToString();
        }

        public static string MapSvg(GridMap map, IReadOnlyList<AgentDefinition> agents, IReadOnlyList<AgentPath> paths,
            int step, string title, string caption, MapfConflict conflict = null)
        {
            var b = BeginSvg(1200, 1050);
            Text(b, 40, 44, title, 23);
            Figure(b, map, agents, paths, step, 45, 80, 1110, 840, conflict);
            Text(b, 45, 950, "S 시작 · G 목표 · 숫자 현재 위치 · 목표 도착 후 영구 점유 · 선은 계획 경로", 15);
            WrappedText(b, caption, 45, 978, 100);
            return b.Append("</svg>").ToString();
        }

        public static string ComparisonSvg(GridMap map, IReadOnlyList<AgentDefinition> agents,
            IReadOnlyList<IReadOnlyList<AgentPath>> paths, IReadOnlyList<string> labels, int step, string caption)
        {
            int panels = Math.Max(1, paths.Count), width = panels * 700;
            var b = BeginSvg(width, 900);
            for (int i = 0; i < paths.Count; i++)
            {
                Text(b, i * 700 + 30, 40, labels[i], 18);
                Figure(b, map, agents, paths[i], step, i * 700 + 30, 70, 640, 680, null);
            }
            Text(b, 30, 790, $"같은 지도 · 같은 축척 · t={step} · 선은 계획 경로, 숫자는 해당 경로의 위치", 17);
            WrappedText(b, caption, 30, 826, Math.Max(60, width / 12));
            return b.Append("</svg>").ToString();
        }

        private static void Figure(StringBuilder b, GridMap map, IReadOnlyList<AgentDefinition> agents,
            IReadOnlyList<AgentPath> paths, int step, double left, double top, double width, double height, MapfConflict conflict)
        {
            double cell = Math.Min(width / map.Width, height / map.Height);
            left += (width - cell * map.Width) / 2; top += (height - cell * map.Height) / 2;
            for (int y = 0; y < map.Height; y++)
            for (int x = 0; x < map.Width; x++)
            {
                string fill = map.IsBlocked(new GridPosition(x, y)) ? "#555555" : "#f4f4f4";
                b.Append($"<rect x='{N(left + x * cell)}' y='{N(top + (map.Height - 1 - y) * cell)}' width='{N(cell)}' height='{N(cell)}' fill='{fill}' stroke='#d3d3d3' stroke-width='.5'/>");
            }
            foreach (AgentPath path in paths)
            {
                if (path == null || path.IsEmpty) continue;
                b.Append($"<polyline fill='none' stroke='{AgentHex(path.AgentId)}' stroke-opacity='.6' stroke-width='{N(Math.Max(1, cell * .09))}' points='");
                foreach (GridPosition p in path.Positions) b.Append($"{N(left + (p.X + .5) * cell)},{N(top + (map.Height - p.Y - .5) * cell)} ");
                b.Append("'/>");
            }
            foreach (AgentDefinition agent in agents)
            {
                AgentPath path = null;
                foreach (AgentPath candidate in paths) if (candidate != null && candidate.AgentId == agent.Id) { path = candidate; break; }
                GridPosition current = path == null || path.IsEmpty ? agent.Start : path.PositionAt(step);
                double font = Math.Max(7, Math.Min(18, cell * .28));
                Text(b, left + agent.Start.X * cell + 1, top + (map.Height - agent.Start.Y - 1) * cell + font, "S" + agent.Id, font);
                Text(b, left + agent.Goal.X * cell + 1, top + (map.Height - agent.Goal.Y) * cell - 2, "G" + agent.Id, font);
                double cx = left + (current.X + .5) * cell, cy = top + (map.Height - current.Y - .5) * cell;
                b.Append($"<circle cx='{N(cx)}' cy='{N(cy)}' r='{N(cell * .29)}' fill='{AgentHex(agent.Id)}' stroke='#111'/>");
                b.Append($"<text x='{N(cx)}' y='{N(cy + font * .33)}' text-anchor='middle' fill='white' font-size='{N(font)}'>{agent.Id}</text>");
            }
            if (conflict != null)
            {
                foreach (GridPosition p in new[] { conflict.FirstPosition, conflict.SecondPosition })
                    b.Append($"<rect x='{N(left + p.X * cell)}' y='{N(top + (map.Height - p.Y - 1) * cell)}' width='{N(cell)}' height='{N(cell)}' fill='none' stroke='#b94b40' stroke-width='3'/>");
            }
        }

        public static string AgentHex(int id)
        {
            string[] colors = { "#555555", "#ab6258", "#9b772f", "#507855", "#4c7c6c", "#926571", "#707070", "#80704a" };
            return colors[(int)((uint)id % colors.Length)];
        }
        private static StringBuilder BeginSvg(int width, int height) => new StringBuilder(
            $"<svg xmlns='http://www.w3.org/2000/svg' width='{width}' height='{height}' viewBox='0 0 {width} {height}' font-family='Malgun Gothic,Segoe UI,sans-serif'><rect width='100%' height='100%' fill='white'/>");
        private static void Text(StringBuilder b, double x, double y, string value, double size) =>
            b.Append($"<text x='{N(x)}' y='{N(y)}' fill='#252525' font-size='{N(size)}'>{Escape(value)}</text>");
        private static void WrappedText(StringBuilder b, string text, int x, int y, int lineLength)
        {
            text = (text ?? "").Replace('\n', ' ');
            for (int start = 0; start < text.Length; start += lineLength)
                Text(b, x, y + start / lineLength * 21, text.Substring(start, Math.Min(lineLength, text.Length - start)), 13);
        }
    }
}
