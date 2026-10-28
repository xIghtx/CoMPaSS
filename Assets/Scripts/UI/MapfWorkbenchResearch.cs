using System;
using System.Collections.Generic;
using System.IO;
using CoMPaSS.Algorithms;
using CoMPaSS.Core;
using CoMPaSS.Experiment;
using CoMPaSS.Persistence;
using UnityEngine;

namespace CoMPaSS.UI
{
    public sealed partial class MapfWorkbenchController
    {
        private SearchProfile searchProfile = new SearchProfile();
        private bool showReservations, showConstraints = true, showHeatmap, lowMotion, playSearch;
        private bool autoPlayAfterPlan = true, excludeDecisionTime, showHelp;
        private float mapZoom = 1, searchAccumulator;
        private Vector2 cameraPan;
        private int reservationLookahead = 2, comparisonTab;
        private string reachabilityMessage = "";
        private readonly Dictionary<GridPosition, int> conflictHeat = new Dictionary<GridPosition, int>();
        private string importScenarioPath = "";
        private int bottleneckCount=1,passageWidth=3,previewBottlenecks=1,previewPassage=3;
        private AgentDistribution distribution,previewDistribution;
        private string campaignBottlenecks="1,2",campaignPassages="1,3";
        private int recoveryLimit=6;
        private int observationStep,observationChannel;

        private void DrawScenarioHelp()
        {
            showHelp = GUILayout.Toggle(showHelp, "사용 순서 / 지표 도움말");
            if (showHelp) GUILayout.Label("① 지도·배치 설정 → ② 알고리즘 실행 → ③ 이동·CBS 과정 확인 → ④ 결과·그림 저장\n계획 시간: 짧을수록 빠릅니다. 경로 비용: 도착 전 이동+대기 합. 성공한 같은 조건끼리 비교하세요.\n충돌은 다음 이동 전에 차단됩니다. 도착한 에이전트는 목표를 계속 점유합니다. 시간·노드 제한은 실패 사유와 함께 저장됩니다.", MutedLabel());
            if (GUILayout.Button("시작→목표 연결 검사"))
            {
                var unreachable = ScenarioValidator.UnreachableAgents(map, agents);
                reachabilityMessage = unreachable.Count == 0 ? "모든 시작·목표가 연결되어 있습니다. 상호 충돌 없는 해는 별도로 계산합니다."
                    : "도달 불가 Agent " + string.Join(", ",unreachable) + " · 장애물을 제거하거나 시작·목표를 옮기세요.";
            }
            if (!string.IsNullOrEmpty(reachabilityMessage)) GUILayout.Label(reachabilityMessage, MutedLabel());
            long states = (long)map.Width * map.Height * agents.Count * maxTimeSteps;
            if (states > 10000000) GUILayout.Label($"최대 상태 조합 {states:N0}. 실제 메모리는 탐색에 따라 달라집니다. 시간·노드 제한을 낮추거나 애니메이션 간소화를 사용하세요.", ErrorLabel());
        }

        private void DrawFileImport()
        {
            GUILayout.Label("저장한 시나리오 JSON 경로", MutedLabel());
            importScenarioPath = GUILayout.TextField(importScenarioPath);
            if (GUILayout.Button("지정 파일 / 실제 이동 기록 불러오기")) ReportAction(() =>
            {
                if (string.IsNullOrWhiteSpace(importScenarioPath)) throw new InvalidDataException("시나리오 JSON 파일 경로를 입력하세요.");
                string target = Path.GetFullPath(importScenarioPath.Trim().Trim('"'));
                // Load through the same validated parser without overwriting the user's last saved session.
                if(!LoadScenarioFile(target)) return;
                string execution = target.EndsWith(".scenario.json",StringComparison.OrdinalIgnoreCase)
                    ? target.Substring(0,target.Length-".scenario.json".Length)+".execution.json" : Path.Combine(Path.GetDirectoryName(target),"execution.json");
                if (File.Exists(execution) && lastRun != null)
                {
                    ExecutionRecord record = JsonUtility.FromJson<ExecutionRecord>(File.ReadAllText(execution));
                    var paths = new List<AgentPath>();
                    foreach(ScenarioPathDocument p in record.paths)
                    {
                        if(p.x==null||p.y==null||p.x.Length!=p.y.Length) throw new InvalidDataException("실행 경로 좌표가 올바르지 않습니다.");
                        var positions=new List<GridPosition>();for(int i=0;i<p.x.Length;i++) positions.Add(new GridPosition(p.x[i],p.y[i]));
                        paths.Add(new AgentPath(p.agentId,positions));
                    }
                    simulation = MapfSimulation.FromHistory(map, agents, lastRun, paths);
                    executedReplay = simulation.ExecutedPaths();executedReplayStep=simulation.Step;terminalReported=true;timelineStep = 0; reviewingTimeline = true; isPlaying = false;
                    statusMessage = "실제 이동 기록을 불러왔습니다. 재생 버튼으로 확인하세요.";
                }
            });
        }

        private void DrawResearchProfile()
        {
            GUILayout.Label("탐색 구성 프로필", BoldLabel(14));
            searchProfile.Heuristic = (GridHeuristic)GUILayout.SelectionGrid((int)searchProfile.Heuristic, new[] { "맨해튼", "유클리드", "Dijkstra" }, 3);
            GUILayout.Label($"A*/Cooperative 휴리스틱 가중치 {searchProfile.Weight:F1} · 1 초과 시 최단 경로 보장 안 됨", MutedLabel());
            searchProfile.Weight = GUILayout.HorizontalSlider(searchProfile.Weight, 0, 3);
            GUILayout.Label("이웃 탐색 시작 방향 (이후 시계 방향)", MutedLabel());
            searchProfile.Neighbors = (NeighborOrder)GUILayout.SelectionGrid((int)searchProfile.Neighbors, new[] { "위", "오른쪽", "아래", "왼쪽" }, 4);
            searchProfile.TieBreak = (SearchTieBreak)GUILayout.SelectionGrid((int)searchProfile.TieBreak, new[] { "동률: 목표 가까이", "동률: 삽입 순서" }, 2);
            searchProfile.AllowWait = GUILayout.Toggle(searchProfile.AllowWait, "Cooperative 경로 내 대기 허용");
            GUILayout.Label("CBS는 최적성 비교를 위해 가중치 1·맨해튼·대기 허용을 사용합니다.", MutedLabel());
            GUILayout.BeginHorizontal();
            GUILayout.Label("거리 / 혼잡 / 협소 가중치", MutedLabel());
            searchProfile.DistanceWeight = Mathf.Clamp(IntField("distanceWeight", searchProfile.DistanceWeight, 35), 0, 100);
            searchProfile.CongestionWeight = Mathf.Clamp(IntField("congestionWeight", searchProfile.CongestionWeight, 35), 0, 100);
            searchProfile.NarrownessWeight = Mathf.Clamp(IntField("narrowWeight", searchProfile.NarrownessWeight, 35), 0, 100);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal(); GUILayout.Label("복구: 누적 대기 / 재계획 가중치", MutedLabel());
            searchProfile.WaitWeight = Mathf.Clamp(IntField("waitWeight", searchProfile.WaitWeight, 35), 0, 100);
            searchProfile.RecoveryWeight = Mathf.Clamp(IntField("recoveryWeight", searchProfile.RecoveryWeight, 35), 0, 100);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("프로필 저장")) ReportAction(() => File.WriteAllText(Path.Combine(DataDirectory(), "profile.json"), JsonUtility.ToJson(CurrentSettings(), true)));
            if (GUILayout.Button("프로필 불러오기")) ReportAction(() => ApplySettings(JsonUtility.FromJson<MapfPlannerSettings>(File.ReadAllText(Path.Combine(DataDirectory(), "profile.json")))));
            GUILayout.EndHorizontal();
            GUILayout.Label("최대 시간 단계 = 공간·시간 탐색 창. 창 밖까지 예약을 보장하지 않는 부분 실행은 수행하지 않습니다.", MutedLabel());
            if(GUILayout.Button("우선순위 구성요소 제거 비교")) StartAblation();
        }

        private void StartAblation()
        {
            if(LockScenario)return;CommitNumberInputs();RefreshPreviewIfNeeded();NewWorkCancellation();
            GridMap snapshot=map.Copy();var definition=new List<AgentDefinition>(agents);
            runSettings=CurrentSettings();runSettings.CancellationToken=workCancellation.Token;
            MapfPlannerSettings settings=runSettings.Copy();lastRun=null;simulation=null;isPlaying=false;comparisons.Clear();
            comparisonTask=System.Threading.Tasks.Task.Run(()=>
            {
                var results=new List<MapfRunSummary>();
                string[] names={"고정 순서","혼잡+협소+거리","혼잡 제외","협소 제외","거리 제외"};
                for(int i=0;i<names.Length;i++)
                {
                    var profile=settings.Copy();profile.PriorityMode=i==0?AgentPriorityMode.InputOrder:AgentPriorityMode.MostConstrainedFirst;
                    if(i==2)profile.Profile.CongestionWeight=0;if(i==3)profile.Profile.NarrownessWeight=0;if(i==4)profile.Profile.DistanceWeight=0;
                    var result=new MapfExperimentRunner().Run(snapshot,definition,MapfAlgorithmKind.CooperativeAStar,profile);result.Variant=names[i];results.Add(result);
                }
                return results;
            },settings.CancellationToken);
            statusMessage="동일 지도·배치에서 구성요소 제거 비교 중";
        }

        private void ApplySettings(MapfPlannerSettings settings)
        {
            if (settings == null) throw new InvalidDataException("프로필이 비어 있습니다.");
            searchProfile = settings.Profile?.Copy() ?? new SearchProfile();
            priorityMode = (AgentPriorityMode)Mathf.Clamp((int)settings.PriorityMode, 0, 2);
            cbsHeuristic = (CBSHeuristicMode)Mathf.Clamp((int)settings.CbsHeuristic, 0, 2);
            maxTimeSteps = Mathf.Clamp(settings.MaxTimeSteps, 1, 1024); maxExpandedNodes = Mathf.Clamp(settings.MaxExpandedNodesPerAgent, 100, 1000000);
            maxCbsNodes = Mathf.Clamp(settings.MaxHighLevelNodes, 100, 100000); maxPlanningMilliseconds = Mathf.Clamp(settings.MaxPlanningMilliseconds, 10, 300000);
            pythonExecutable = settings.PythonExecutable ?? "python"; primalScriptPath = settings.PrimalScriptPath ?? "";
            numberEdits.Clear(); statusMessage = "알고리즘 구성 프로필을 불러왔습니다.";
        }

        private void DrawResearchToolbar()
        {
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("전체 지도", GUILayout.Width(82))) { mapZoom = 1; cameraPan = Vector2.zero; followSelectedAgent = false; zoomToConflict = false; }
            GUILayout.Label("확대", GUILayout.Width(35)); mapZoom = GUILayout.HorizontalSlider(mapZoom, .5f, 8, GUILayout.Width(80));
            showReservations = GUILayout.Toggle(showReservations, "예약표"); showConstraints = GUILayout.Toggle(showConstraints, "새 제약");
            showHeatmap = GUILayout.Toggle(showHeatmap, "충돌 누적"); bool reduced = GUILayout.Toggle(lowMotion, "애니메이션 간소화");
            if(reduced!=lowMotion){lowMotion=reduced;QualitySettings.vSyncCount=0;Application.targetFrameRate=lowMotion?30:60;}
            GUILayout.FlexibleSpace(); GUILayout.Label("휠 확대 · 우클릭 드래그 이동", MutedLabel());
            GUILayout.EndHorizontal();
            if (showReservations)
            {
                GUILayout.BeginHorizontal(); GUILayout.Label("현재 시점부터 예약 표시 범위", GUILayout.Width(195));
                reservationLookahead = Mathf.RoundToInt(GUILayout.HorizontalSlider(reservationLookahead, 1, 12, GUILayout.Width(100)));
                GUILayout.Label(reservationLookahead + "단계 · ID@t · 선은 간선 예약", MutedLabel()); GUILayout.EndHorizontal();
            }
        }
        private void HandleCamera(Rect board)
        {
            Event e = Event.current;
            if (!new Rect(0, 0, board.width, board.height).Contains(e.mousePosition)) return;
            if (e.type == EventType.ScrollWheel)
            { mapZoom = Mathf.Clamp(mapZoom * Mathf.Pow(1.12f, -e.delta.y), .5f, 12); followSelectedAgent = false; zoomToConflict = false; e.Use(); }
            if (e.type == EventType.MouseDrag && (e.button == 1 || e.button == 2))
            { cameraPan += e.delta; followSelectedAgent = false; zoomToConflict = false; e.Use(); }
        }
        private void BuildConflictHeat()
        {
            conflictHeat.Clear(); if (lastRun == null || !showHeatmap) return;
            void Add(MapfConflict c) { if (c == null) return; conflictHeat.TryGetValue(c.FirstPosition, out int count); conflictHeat[c.FirstPosition] = count + 1; }
            foreach (MapfConflict c in lastRun.Conflicts) Add(c);
            foreach (CBSReplayFrame frame in lastRun.CbsReplayFrames) if (!frame.IsConstraintTrial) Add(frame.Conflict);
        }
        private void DrawResearchOverlay(Rect board, IReadOnlyList<AgentPath> paths, float cell, float x, float y)
        {
            if (Event.current.type != EventType.Repaint) return;
            Rect Cell(GridPosition p) => new Rect(x + p.X * cell, y + (map.Height - p.Y - 1) * cell, cell, cell);
            if (showReservations)
            foreach (AgentPath path in paths)
            {
                if (path == null || path.IsEmpty || (selectedPathOnly && path.AgentId != agents[selectedAgent].Id)) continue;
                int last=reviewingTimeline?Math.Min(timelineStep+reservationLookahead,GetLastTimeStep()):timelineStep+reservationLookahead;
                for (int t = timelineStep; t <= last; t++)
                {
                    Rect r = Cell(path.PositionAt(t)); if (!r.Overlaps(new Rect(0,0,board.width,board.height))) continue;
                    DrawOutline(r, new Color(.84f,.72f,.45f), 1);
                    if (cell >= 28) GUI.Label(r, path.AgentId + "@" + t, gridCellStyle);
                    if (t > timelineStep) DrawMapLine(Cell(path.PositionAt(t - 1)).center, r.center, new Color(.84f,.72f,.45f), 2);
                }
            }
            if (showConstraints && viewSearchReplay && lastRun != null && lastRun.CbsReplayFrames.Count > 0)
            {
                CBSReplayFrame frame = lastRun.CbsReplayFrames[cbsFrameIndex];
                foreach (CBSReplayFrame parent in lastRun.CbsReplayFrames)
                {
                    if (parent.NodeId != frame.ParentNodeId || parent.IsConstraintTrial || parent.Conflict == null) continue;
                    DrawOutline(Cell(parent.Conflict.FirstPosition), ConflictCell, 3);
                    DrawOutline(Cell(parent.Conflict.SecondPosition), ConflictCell, 3);
                    GUI.Label(new Rect(8, board.height - 42, board.width - 16, 38), "제약: " + frame.AppliedConstraint, BoldLabel(12)); break;
                }
            }
            if (agents.Count > 0)
            {
                AgentDefinition a = agents[selectedAgent]; Vector2 p = AgentDisplayPosition(a);
                DrawOutline(new Rect(x+(p.x+.05f)*cell,y+(map.Height-p.y-.95f)*cell,cell*.9f,cell*.9f), Color.white, 2);
            }
        }
        private static void DrawOutline(Rect r, Color color, float thickness)
        {
            GUI.color = color;
            GUI.DrawTexture(new Rect(r.x,r.y,r.width,thickness),Texture2D.whiteTexture); GUI.DrawTexture(new Rect(r.x,r.yMax-thickness,r.width,thickness),Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(r.x,r.y,thickness,r.height),Texture2D.whiteTexture); GUI.DrawTexture(new Rect(r.xMax-thickness,r.y,thickness,r.height),Texture2D.whiteTexture);
            GUI.color = Color.white;
        }
        private static void DrawMapLine(Vector2 from, Vector2 to, Color color, float thickness)
        {
            Matrix4x4 saved = GUI.matrix; GUI.color = color;
            GUIUtility.RotateAroundPivot(Mathf.Atan2(to.y-from.y,to.x-from.x)*Mathf.Rad2Deg,from);
            GUI.DrawTexture(new Rect(from.x,from.y-thickness/2,Vector2.Distance(from,to),thickness),Texture2D.whiteTexture);
            GUI.matrix = saved; GUI.color = Color.white;
        }

        private void DrawObservation()
        {
            if (lastRun?.Algorithm != MapfAlgorithmKind.PrimalExternal || agents.Count == 0) return;
            if(lastRun.PrimalDecisions.Count>0)
            {
                GUILayout.Label("PRIMAL 실제 입력·행동",BoldLabel(13));
                int maximum=0;foreach(var d in lastRun.PrimalDecisions)maximum=Math.Max(maximum,d.step);
                observationStep=Mathf.RoundToInt(GUILayout.HorizontalSlider(observationStep,0,Math.Max(1,maximum)));
                observationChannel=GUILayout.SelectionGrid(observationChannel,new[]{"점유","내 목표","다른 목표","장애물"},2);
                PrimalDecision selected=null;foreach(var d in lastRun.PrimalDecisions)if(d.agentId==agents[selectedAgent].Id&&d.step==observationStep){selected=d;break;}
                if(selected!=null)
                {
                    GUILayout.Label($"t={selected.step} · 행동 {selected.action} · {selected.note}",MutedLabel());
                    if(selected.probabilities!=null)GUILayout.Label("행동 확률: "+string.Join(" / ",Array.ConvertAll(selected.probabilities,p=>p.ToString("F2"))),MutedLabel());
                    Rect box=GUILayoutUtility.GetRect(180,180);float observationCell=180f/selected.size;
                    for(int row=0;row<selected.size;row++)for(int col=0;col<selected.size;col++)
                    {GUI.color=selected.channels[observationChannel*selected.size*selected.size+row*selected.size+col]>.5f?Color.white:new Color(.3f,.3f,.3f);GUI.DrawTexture(new Rect(box.x+col*observationCell,box.y+row*observationCell,observationCell-1,observationCell-1),Texture2D.whiteTexture);}
                    GUI.color=Color.white;
                }
                else GUILayout.Label("선택 에이전트의 이 시점 관측이 없습니다.",MutedLabel());
                return;
            }
            GUILayout.Label("국소 관측 · 11×11 재구성", BoldLabel(13));
            GUILayout.Label("장애물·현재 점유·목표. 모델의 원시 텐서와는 구분합니다.", MutedLabel());
            Rect region = GUILayoutUtility.GetRect(200, 200); Vector2 center = AgentDisplayPosition(agents[selectedAgent]); float size = 16;
            for (int dy = -5; dy <= 5; dy++) for (int dx = -5; dx <= 5; dx++)
            {
                var p = new GridPosition(Mathf.RoundToInt(center.x)+dx,Mathf.RoundToInt(center.y)+dy);
                Color color = map.IsBlocked(p) ? ObstacleCell : EmptyCell;
                foreach (AgentDefinition a in agents) { Vector2 ap = AgentDisplayPosition(a); if (Mathf.RoundToInt(ap.x)==p.X && Mathf.RoundToInt(ap.y)==p.Y) color=AgentColor(a.Id); }
                GUI.color=color; GUI.DrawTexture(new Rect(region.x+(dx+5)*size,region.y+(5-dy)*size,size-1,size-1),Texture2D.whiteTexture); GUI.color=Color.white;
            }
        }
        private void DrawResearchResults()
        {
            if (lastRun == null) return;
            excludeDecisionTime = GUILayout.Toggle(excludeDecisionTime, "표시 시간에서 우선순위·휴리스틱 계산 제외 (원시 시간은 보존)");
            double shown = lastRun.PlanningMilliseconds - (excludeDecisionTime ? lastRun.PriorityMilliseconds + lastRun.HeuristicMilliseconds : 0);
            GUILayout.Label($"계획 {Math.Max(0,shown):F2}ms · 우선순위 {lastRun.PriorityMilliseconds:F2}ms · 휴리스틱 {lastRun.HeuristicMilliseconds:F2}ms\n평균 경로 {lastRun.AveragePathLength:F2} · 계획 중 충돌 발견 {lastRun.PlanningConflictCount} · 관리 힙 최고 표본 {lastRun.SampledManagedPeakBytes/1048576.0:F1} MiB", MutedLabel());
            GUILayout.Label("힙은 실행 프로세스 전체를 20ms 간격으로 표본 측정합니다. 알고리즘 단독 최대 메모리가 아닙니다.", MutedLabel());
            if (showAgentLogDetails) foreach (string entry in lastRun.HeuristicLog) GUILayout.Label(entry, MutedLabel());
            if (comparisons.Count == 0) return;
            string[] labels = new string[comparisons.Count]; for(int i=0;i<labels.Length;i++) labels[i]=comparisons[i].Variant??MapfCsvExporter.Name(comparisons[i].Algorithm);
            bool enabled=GUI.enabled; GUI.enabled=!campaignRunning&&!IsBusy;
            int tab=GUILayout.SelectionGrid(comparisonTab,labels,labels.Length); GUI.enabled=enabled;
            if(tab!=comparisonTab){ comparisonTab=tab; BeginLiveRun(comparisons[tab]); isPlaying=false; }
            double maximum=1;foreach(var r in comparisons) maximum=Math.Max(maximum,r.PlanningMilliseconds);
            foreach(var r in comparisons)
            {
                GUILayout.BeginHorizontal();GUILayout.Label(r.Variant??MapfCsvExporter.Name(r.Algorithm),GUILayout.Width(155));
                Rect bar=GUILayoutUtility.GetRect(150,18,GUILayout.ExpandWidth(true)); GUI.color=new Color(.65f,.65f,.65f);
                GUI.DrawTexture(new Rect(bar.x,bar.y,bar.width*(float)(r.PlanningMilliseconds/maximum),bar.height),Texture2D.whiteTexture);GUI.color=Color.white;
                GUILayout.Label($"{r.PlanningMilliseconds:F1}ms · 비용 {r.TotalMoveCost}",GUILayout.Width(160));GUILayout.EndHorizontal();
            }
        }
    }
}
