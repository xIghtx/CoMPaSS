using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using CoMPaSS.Algorithms;
using CoMPaSS.Core;
using CoMPaSS.Experiment;
using CoMPaSS.Persistence;
using UnityEngine;

namespace CoMPaSS.UI
{
    public sealed partial class MapfWorkbenchController
    {
        private bool showReports, capturing;
        private string figureNote = "", latestPng;
        private int reportEventIndex = -1;
        private readonly List<string> captureHistory = new List<string>();
        [Serializable] private sealed class ReportEvents { public ReportEvent[] events; }
        [Serializable] private sealed class ExecutionRecord
        { public string state; public int step; public ScenarioPathDocument[] paths; }
        [Serializable] private sealed class ReportEvent
        {
            public SimulationEvent detail;
            public ScenarioPathDocument[] before, after;
        }

        private void DrawReportControls()
        {
            GUILayout.Space(8);
            GUILayout.Label("그림 설명과 관찰 메모를 적고, 원하는 형식으로 저장하세요.", MutedLabel());
            GUILayout.Label("그림 설명 / 관찰 메모", MutedLabel());
            figureNote = GUILayout.TextArea(figureNote, GUILayout.Height(52));
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("지도 PNG + SVG")) ReportAction(() => ExportMap(ReportDirectory()));
            bool enabled = GUI.enabled; GUI.enabled = !capturing;
            if (GUILayout.Button("화면 2× PNG")) StartCoroutine(CaptureScreen());
            GUI.enabled = enabled; GUILayout.EndHorizontal();
            if (GUILayout.Button("선택 CBS / 복구 전후 비교 SVG")) ReportAction(() => ExportBeforeAfter(ReportDirectory()));
            if (comparisons.Count > 0 && GUILayout.Button("알고리즘 비교 SVG")) ReportAction(() => ExportComparison(ReportDirectory()));
            if (simulation != null && simulation.Events.Count > 0)
            {
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("‹ 사건", GUILayout.Width(65))) reportEventIndex = Math.Max(0, (reportEventIndex < 0 ? simulation.Events.Count : reportEventIndex) - 1);
                if (GUILayout.Button("사건 ›", GUILayout.Width(65))) reportEventIndex = Math.Min(simulation.Events.Count - 1, reportEventIndex + 1);
                if (GUILayout.Button("이 시점 보기"))
                {
                    int index = Mathf.Clamp(reportEventIndex < 0 ? simulation.Events.Count - 1 : reportEventIndex, 0, simulation.Events.Count - 1);
                    timelineStep = Math.Min(simulation.Step, simulation.Events[index].step);
                    reviewingTimeline = true; viewSearchReplay = false; isPlaying = false;
                }
                GUILayout.EndHorizontal();
                int selected = Mathf.Clamp(reportEventIndex < 0 ? simulation.Events.Count - 1 : reportEventIndex, 0, simulation.Events.Count - 1);
                SimulationEvent entry = simulation.Events[selected];
                GUILayout.Label($"사건 {selected + 1}/{simulation.Events.Count} · t={entry.step}\n{entry.kind}: {entry.message}", MutedLabel());
            }
            if (GUILayout.Button("보고서 묶음 저장 (HTML · CSV · JSON)")) ReportAction(ExportReportBundle);
            GUILayout.BeginHorizontal();
            GUI.enabled = !string.IsNullOrEmpty(latestPng);
            if (GUILayout.Button("최근 PNG 복사")) ReportAction(() => CopyPngToClipboard(latestPng));
            GUI.enabled = enabled;
            if (GUILayout.Button("저장 폴더 열기")) Application.OpenURL(new Uri(ReportDirectory()).AbsoluteUri);
            GUILayout.EndHorizontal();
            for (int i = Math.Max(0, captureHistory.Count - 3); i < captureHistory.Count; i++)
                if (GUILayout.Button(Path.GetFileName(captureHistory[i]), GUI.skin.label)) Application.OpenURL(new Uri(captureHistory[i]).AbsoluteUri);
        }

        private string ReportDirectory()
        { string directory = Path.Combine(DataDirectory(), "Reports"); Directory.CreateDirectory(directory); return directory; }
        private string ReportStem(string type) => DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + "-" + type;
        private string FigureCaption() => $"{lastRun?.Algorithm.ToString() ?? "Preview"} · {map.Width}×{map.Height} · {agents.Count} agents · map seed {randomSeed} · placement seed {agentPlacementSeed} · t={timelineStep} · " +
            (viewSearchReplay ? $"CBS trial {cbsFrameIndex + 1}" : simulation == null ? "preview" : $"{simulation.State}, arrived {simulation.ArrivedCount}, recoveries {simulation.RecoveryCount}") + " · " + figureNote;
        private void ReportAction(Action action)
        { try { action(); } catch (Exception error) { SetError("내보내기 실패: " + error.Message); } }
        private void RememberCapture(string path, string caption)
        {
            captureHistory.Add(path);
            if (captureHistory.Count > 24) captureHistory.RemoveAt(0);
            if (path.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) latestPng = path;
            File.WriteAllText(path + ".caption.txt", caption, Encoding.UTF8);
            statusMessage = "저장됨: " + Path.GetFileName(path); Log("그림 저장: " + path);
        }

        private string ExportMap(string directory)
        {
            string stem = Path.Combine(directory, ReportStem("map")), caption = FigureCaption();
            IReadOnlyList<AgentPath> paths = GetDisplayedPaths();
            File.WriteAllText(stem + ".svg", MapfReportExporter.MapSvg(map, agents, paths, timelineStep, "CoMPaSS · MAPF", caption, GetCurrentConflict()), Encoding.UTF8);
            Texture2D texture = RasterMap(paths, timelineStep, 2560);
            try { File.WriteAllBytes(stem + ".png", texture.EncodeToPNG()); }
            finally { Destroy(texture); }
            RememberCapture(stem + ".png", caption);
            RememberCapture(stem + ".svg", caption);
            return Path.GetFileName(stem + ".svg");
        }

        private IEnumerator CaptureScreen()
        {
            capturing = true;
            yield return new WaitForEndOfFrame();
            Texture2D capture = null;
            try
            {
                int factor = (long)Screen.width * Screen.height <= 9000000 ? 2 : 1;
                capture = ScreenCapture.CaptureScreenshotAsTexture(factor);
                string path = Path.Combine(ReportDirectory(), ReportStem("screen") + ".png");
                File.WriteAllBytes(path, capture.EncodeToPNG()); RememberCapture(path, FigureCaption());
            }
            catch (Exception error) { SetError("화면 캡처 실패: " + error.Message); }
            finally { if (capture != null) Destroy(capture); capturing = false; }
        }

        private string ExportBeforeAfter(string directory)
        {
            IReadOnlyList<AgentPath> before = null, after = null;
            string label = ""; int step = timelineStep;
            if (viewSearchReplay && lastRun != null && lastRun.CbsReplayFrames.Count > 0)
            {
                CBSReplayFrame frame = lastRun.CbsReplayFrames[cbsFrameIndex];
                foreach (CBSReplayFrame candidate in lastRun.CbsReplayFrames)
                    if (!candidate.IsConstraintTrial && candidate.NodeId == frame.ParentNodeId) { before = candidate.Paths; break; }
                before = before ?? frame.Paths; after = frame.Paths;
                label = $"CBS node {frame.ParentNodeId} → {frame.NodeId} · {frame.AppliedConstraint} · success={frame.ReplanSucceeded}";
                step = frame.Conflict?.TimeStep ?? timelineStep;
            }
            else if (simulation != null)
            {
                int index = Mathf.Clamp(reportEventIndex < 0 ? simulation.Events.Count - 1 : reportEventIndex, 0, simulation.Events.Count - 1);
                for (int i = index; i >= 0; i--)
                {
                    SimulationEvent entry = simulation.Events[i];
                    if (entry.beforePaths == null) continue;
                    before = entry.beforePaths; after = entry.afterPaths ?? entry.beforePaths; step = entry.step;
                    label = $"t={step} · {entry.kind} · {entry.method} · {entry.message}"; break;
                }
            }
            if (before == null) throw new InvalidOperationException("CBS 시행착오 또는 충돌/복구 사건을 먼저 선택하세요.");
            string path = Path.Combine(directory, ReportStem("before-after") + ".svg");
            File.WriteAllText(path, MapfReportExporter.ComparisonSvg(map, agents, new[] { before, after },
                new[] { "변경 전 계획", "변경 후 계획 / 실패 시 이전 상태" }, step, label + " · " + figureNote), Encoding.UTF8);
            ExportRasterPanels(new[] { before, after }, step, Path.ChangeExtension(path, ".png"), label);
            RememberCapture(path, label); return Path.GetFileName(path);
        }

        private string ExportComparison(string directory)
        {
            var paths = new List<IReadOnlyList<AgentPath>>(); var labels = new List<string>();
            foreach (MapfRunSummary result in comparisons)
            {
                paths.Add(result.Paths);
                labels.Add($"{result.Variant??MapfCsvExporter.Name(result.Algorithm)} · {result.PlanningMilliseconds:F1}ms · {result.RoutedAgents}/{agents.Count}명 · 충돌 {result.Conflicts.Count}");
            }
            string path = Path.Combine(directory, ReportStem("comparison") + ".svg");
            File.WriteAllText(path, MapfReportExporter.ComparisonSvg(map, agents, paths, labels, timelineStep, FigureCaption()), Encoding.UTF8);
            ExportRasterPanels(paths, timelineStep, Path.ChangeExtension(path, ".png"), string.Join(" | ", labels) + " · " + FigureCaption());
            RememberCapture(path, FigureCaption()); return Path.GetFileName(path);
        }

        private static ScenarioPathDocument[] ReportPaths(IReadOnlyList<AgentPath> paths)
        {
            var result = new List<ScenarioPathDocument>();
            if (paths != null) foreach (AgentPath path in paths)
            {
                if (path == null) continue;
                var p = new ScenarioPathDocument { agentId = path.AgentId, x = new int[path.Positions.Count], y = new int[path.Positions.Count] };
                for (int i = 0; i < p.x.Length; i++) { p.x[i] = path.Positions[i].X; p.y[i] = path.Positions[i].Y; }
                result.Add(p);
            }
            return result.ToArray();
        }
        private static string EventsJson(MapfSimulation sim)
        {
            var result = new List<ReportEvent>();
            if (sim != null) foreach (SimulationEvent entry in sim.Events)
                result.Add(new ReportEvent { detail = entry, before = ReportPaths(entry.beforePaths), after = ReportPaths(entry.afterPaths) });
            return JsonUtility.ToJson(new ReportEvents { events = result.ToArray() }, true);
        }
        private static string ExecutionJson(MapfSimulation sim) => JsonUtility.ToJson(new ExecutionRecord
        { state = sim?.State.ToString() ?? "Preview", step = sim?.Step ?? 0, paths = ReportPaths(sim?.ExecutedPaths()) }, true);

        private void ExportReportBundle()
        {
            string id = ReportStem("report"), directory = Path.Combine(ReportDirectory(), id); Directory.CreateDirectory(directory);
            string figure = ExportMap(directory), caption = FigureCaption();
            File.WriteAllText(Path.Combine(directory, "scenario.json"), MapfScenarioSerializer.ToJson(map, agents, CurrentDocument(), lastRun), Encoding.UTF8);
            File.WriteAllText(Path.Combine(directory, "events.json"), EventsJson(simulation), Encoding.UTF8);
            File.WriteAllText(Path.Combine(directory, "execution.json"), ExecutionJson(simulation), Encoding.UTF8);
            File.WriteAllText(Path.Combine(directory,"comparisons.csv"),MapfCsvExporter.Export(comparisons),new UTF8Encoding(true));
            for(int i=0;i<comparisons.Count;i++)File.WriteAllText(Path.Combine(directory,$"comparison-{i+1}.json"),MapfScenarioSerializer.ToJson(map,agents,CurrentDocument(comparisons[i]),comparisons[i]),Encoding.UTF8);
            var records = new List<CampaignRecord>();
            bool savedCampaignCase = campaign != null && currentCase != null && campaign.cases.Contains(currentCase) && currentCase.index < campaign.nextIndex;
            if (simulation != null && !savedCampaignCase) records.Add(MapfCampaign.Record(id, currentCase ?? CurrentCase(), simulation, connectedCellsOpened));
            if (campaign != null) records.AddRange(campaign.records);
            File.WriteAllText(Path.Combine(directory, "results.csv"), MapfReportExporter.CampaignCsv(records), new UTF8Encoding(true));
            File.WriteAllText(Path.Combine(directory, "summary.csv"), MapfStatistics.ConditionSummaryCsv(records),new UTF8Encoding(true));
            File.WriteAllText(Path.Combine(directory, "charts.svg"), MapfReportExporter.CampaignChart(records), Encoding.UTF8);
            if (campaign != null) File.WriteAllText(Path.Combine(directory, "campaign.json"), JsonUtility.ToJson(campaign, true), Encoding.UTF8);
            var html = new StringBuilder("<!doctype html><html lang='ko'><meta charset='utf-8'><title>CoMPaSS 보고서</title><style>body{font-family:'Malgun Gothic','Segoe UI',sans-serif;max-width:1150px;margin:40px auto;padding:24px;color:#292929}img{max-width:100%;border:1px solid #ddd}td,th{border-bottom:1px solid #ddd;padding:8px;text-align:left}table{border-collapse:collapse;width:100%}p,li{line-height:1.7}@media print{figure{break-inside:avoid}}</style><h1>CoMPaSS MAPF 실험 보고서</h1>");
            html.Append("<p>Run ID: " + MapfReportExporter.Escape(id) + "<br>" + MapfReportExporter.Escape(caption) + "</p>");
            html.Append("<p>원래 계획 성공은 모든 에이전트의 충돌 없는 경로 확보 여부입니다. 실행 성공은 안전 정지와 복구를 포함한 도착 여부입니다. 부분 실행의 상태도 보존합니다.</p>");
            html.Append("<figure><img src='" + figure + "'><figcaption>" + MapfReportExporter.Escape(caption) + "</figcaption></figure>");
            if (comparisons.Count > 0) html.Append("<figure><img src='" + ExportComparison(directory) + "'><figcaption>같은 지도와 시작·목표 배치에서 계산한 원래 계획 비교</figcaption></figure>");
            bool eventAvailable = viewSearchReplay && lastRun?.CbsReplayFrames.Count > 0;
            if (simulation != null) foreach (SimulationEvent entry in simulation.Events) if (entry.beforePaths != null) eventAvailable = true;
            if (eventAvailable) html.Append("<figure><img src='" + ExportBeforeAfter(directory) + "'><figcaption>선택한 탐색 / 충돌 복구 사건</figcaption></figure>");
            html.Append("<img src='charts.svg'><h2>실행 기록</h2><table><tr><th>Run</th><th>알고리즘</th><th>원래 계획</th><th>도착</th><th>계획 / 복구 ms</th><th>상태</th></tr>");
            foreach (CampaignRecord r in records)
                html.Append($"<tr><td>{MapfReportExporter.Escape(r.runId)}</td><td>{r.algorithm}</td><td>{r.rawSuccess}</td><td>{r.arrivedAgents}/{r.scenario.agentCount}</td><td>{r.planningMs:F2} / {r.recoveryMs:F2}</td><td>{MapfReportExporter.Escape(r.terminalState)}</td></tr>");
            html.Append("</table><h2>사건 로그</h2><ul>");
            if (simulation != null) foreach (SimulationEvent entry in simulation.Events)
                html.Append($"<li>t={entry.step} · {MapfReportExporter.Escape(entry.kind + " · " + entry.message + " · " + entry.method)}</li>");
            html.Append("</ul><p><a href='results.csv'>실행 CSV</a> · <a href='summary.csv'>조건별 통계 CSV</a> · <a href='comparisons.csv'>알고리즘·요소 제거 비교 CSV</a> · <a href='scenario.json'>설정·지도·원래 계획 JSON</a> · <a href='events.json'>사건·재계획 전후 경로 JSON</a> · <a href='execution.json'>실제 이동 기록 JSON</a></p></html>");
            string report = Path.Combine(directory, "report.html"); File.WriteAllText(report, html.ToString(), Encoding.UTF8);
            File.WriteAllText(Path.Combine(directory, "report.md"), "# CoMPaSS MAPF\n\n" + caption + "\n\n![지도](" + figure + ")\n\n[상세 보고서](report.html) · [수치](results.csv) · [사건 기록](events.json)\n", Encoding.UTF8);
            RememberCapture(report, caption); Application.OpenURL(new Uri(report).AbsoluteUri);
        }

        private void ExportRasterPanels(IReadOnlyList<IReadOnlyList<AgentPath>> paths, int step, string destination, string caption)
        {
            var textures = new List<Texture2D>(); Texture2D combined = null;
            try
            {
                int width = 0, height = 0;
                foreach (IReadOnlyList<AgentPath> panel in paths)
                { Texture2D texture = RasterMap(panel, step, 1600); textures.Add(texture); width += texture.width; height = Math.Max(height, texture.height); }
                combined = new Texture2D(width, height, TextureFormat.RGB24, false);
                int offset = 0;
                foreach (Texture2D texture in textures)
                { combined.SetPixels32(offset, 0, texture.width, texture.height, texture.GetPixels32()); offset += texture.width; }
                combined.Apply(); File.WriteAllBytes(destination, combined.EncodeToPNG()); RememberCapture(destination, caption);
            }
            finally { foreach (Texture2D texture in textures) Destroy(texture); if (combined != null) Destroy(combined); }
        }

        // A map-only raster avoids screen resolution dependence; SVG contains the full caption.
        private Texture2D RasterMap(IReadOnlyList<AgentPath> paths, int step, int resolution)
        {
            int cell = Math.Max(10, (resolution - 80) / Math.Max(map.Width, map.Height));
            int width = map.Width * cell + 80, height = map.Height * cell + 80;
            var pixels = new Color32[width * height];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = Color.white;
            Action<int, int, int, int, Color32> rect = (x, y, w, h, color) =>
            { for (int yy = Math.Max(0, y); yy < Math.Min(height, y + h); yy++) for (int xx = Math.Max(0, x); xx < Math.Min(width, x + w); xx++) pixels[yy * width + xx] = color; };
            for (int y = 0; y < map.Height; y++) for (int x = 0; x < map.Width; x++)
                rect(40 + x * cell, 40 + y * cell, cell - 1, cell - 1, map.IsBlocked(new GridPosition(x, y)) ? new Color32(85, 85, 85, 255) : new Color32(237, 237, 237, 255));
            foreach (AgentPath path in paths)
            {
                if (path == null) continue;
                ColorUtility.TryParseHtmlString(MapfReportExporter.AgentHex(path.AgentId), out Color color);
                for (int i = 1; i < path.Positions.Count; i++)
                {
                    GridPosition a = path.Positions[i - 1], b = path.Positions[i]; int count = Math.Max(1, cell * (Math.Abs(b.X - a.X) + Math.Abs(b.Y - a.Y)));
                    for (int t = 0; t <= count; t++) rect(40 + a.X * cell + cell / 2 + (b.X - a.X) * cell * t / count,
                        40 + a.Y * cell + cell / 2 + (b.Y - a.Y) * cell * t / count, Math.Max(2, cell / 14), Math.Max(2, cell / 14), color);
                }
            }
            foreach (AgentDefinition agent in agents)
            {
                GridPosition position = agent.Start;
                foreach (AgentPath path in paths) if (path != null && !path.IsEmpty && path.AgentId == agent.Id) { position = path.PositionAt(step); break; }
                ColorUtility.TryParseHtmlString(MapfReportExporter.AgentHex(agent.Id), out Color color);
                RasterLabel(rect, "S" + agent.Id, 42 + agent.Start.X * cell, 42 + agent.Start.Y * cell, Math.Max(1, cell / 18), Color.black);
                RasterLabel(rect, "G" + agent.Id, 42 + agent.Goal.X * cell, 40 + agent.Goal.Y * cell + cell - cell / 3, Math.Max(1, cell / 18), Color.black);
                int size = cell * 3 / 5, x = 40 + position.X * cell + (cell - size) / 2, y = 40 + position.Y * cell + (cell - size) / 2;
                rect(x - 1, y - 1, size + 2, size + 2, Color.black); rect(x, y, size, size, color);
                int scale = Math.Max(1, cell / 16); string label = agent.Id.ToString();
                RasterLabel(rect, label, x + (size - label.Length * 4 * scale) / 2, y + (size - 5 * scale) / 2, scale, Color.white);
            }
            var texture = new Texture2D(width, height, TextureFormat.RGB24, false); texture.SetPixels32(pixels); texture.Apply(); return texture;
        }
        private static void RasterLabel(Action<int, int, int, int, Color32> rect, string text, int x, int y, int scale, Color32 color)
        {
            string[] glyphs = { "111101101101111", "010110010010111", "111001111100111", "111001111001111", "101101111001001", "111100111001111", "111100111101111", "111001001001001", "111101111101111", "111101111001111", "111100111001111", "111100101101111" };
            foreach (char c in text)
            {
                int index = c == 'S' ? 10 : c == 'G' ? 11 : c - '0'; if (index < 0 || index >= glyphs.Length) continue;
                for (int i = 0; i < 15; i++) if (glyphs[index][i] == '1') rect(x + i % 3 * scale, y + (4 - i / 3) * scale, scale, scale, color);
                x += 4 * scale;
            }
        }

        private void CopyPngToClipboard(string path)
        {
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            var texture = new Texture2D(2, 2);
            IntPtr memory = IntPtr.Zero; bool opened = false;
            try
            {
                if (!texture.LoadImage(File.ReadAllBytes(path))) throw new IOException("PNG를 읽지 못했습니다.");
                Color32[] pixels = texture.GetPixels32(); int bytes = checked(pixels.Length * 4); var dib = new byte[40 + bytes];
                Action<int, int> put = (offset, value) => Buffer.BlockCopy(BitConverter.GetBytes(value), 0, dib, offset, 4);
                put(0, 40); put(4, texture.width); put(8, texture.height); dib[12] = 1; dib[14] = 32; put(20, bytes);
                for (int i = 0; i < pixels.Length; i++) { dib[40 + i * 4] = pixels[i].b; dib[41 + i * 4] = pixels[i].g; dib[42 + i * 4] = pixels[i].r; dib[43 + i * 4] = 255; }
                memory = GlobalAlloc(0x42, (UIntPtr)dib.Length); if (memory == IntPtr.Zero) throw new IOException("클립보드 메모리 부족");
                IntPtr pointer = GlobalLock(memory); if (pointer == IntPtr.Zero) throw new IOException("클립보드 메모리 잠금 실패");
                try { Marshal.Copy(dib, 0, pointer, dib.Length); } finally { GlobalUnlock(memory); }
                opened = OpenClipboard(IntPtr.Zero); if (!opened) throw new IOException("다른 프로그램이 클립보드를 사용 중입니다. 다시 시도하세요.");
                if (!EmptyClipboard() || SetClipboardData(8, memory) == IntPtr.Zero) throw new IOException("이미지 복사 실패");
                memory = IntPtr.Zero; statusMessage = "이미지를 복사했습니다. 문서에서 Ctrl+V로 붙여 넣으세요.";
            }
            finally { if (opened) CloseClipboard(); if (memory != IntPtr.Zero) GlobalFree(memory); Destroy(texture); }
#else
            throw new PlatformNotSupportedException("이미지 클립보드 복사는 Windows에서 지원합니다.");
#endif
        }
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
        [DllImport("user32.dll")] private static extern bool OpenClipboard(IntPtr owner);
        [DllImport("user32.dll")] private static extern bool CloseClipboard();
        [DllImport("user32.dll")] private static extern bool EmptyClipboard();
        [DllImport("user32.dll")] private static extern IntPtr SetClipboardData(uint format, IntPtr memory);
        [DllImport("kernel32.dll")] private static extern IntPtr GlobalAlloc(uint flags, UIntPtr bytes);
        [DllImport("kernel32.dll")] private static extern IntPtr GlobalLock(IntPtr memory);
        [DllImport("kernel32.dll")] private static extern bool GlobalUnlock(IntPtr memory);
        [DllImport("kernel32.dll")] private static extern IntPtr GlobalFree(IntPtr memory);
#endif
    }
}
