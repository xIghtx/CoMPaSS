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
    public sealed partial class MapfWorkbenchController : MonoBehaviour
    {
        private enum EditTool { Obstacle, Start, Goal }

        private const int MinimumDimension = 4;
        private const int MaximumDimension = 64;
        private static readonly Color Background = new Color(0.13f, 0.135f, 0.14f);
        private static readonly Color Panel = new Color(0.20f, 0.205f, 0.21f);
        private static readonly Color EmptyCell = new Color(0.39f, 0.40f, 0.40f);
        private static readonly Color ObstacleCell = new Color(0.25f, 0.26f, 0.27f);
        private static readonly Color PathCell = new Color(0.72f, 0.72f, 0.70f);
        private static readonly Color ConflictCell = new Color(0.91f, 0.45f, 0.36f);
        private static readonly Color EdgeConflictCell = new Color(0.91f, 0.68f, 0.36f);
        private static readonly Color SuccessAccent = new Color(0.53f, 0.72f, 0.59f);
        private static readonly Color MutedText = new Color(0.75f, 0.76f, 0.77f);
        private static readonly Color[] AgentColors =
        {
            new Color(0.96f, 0.94f, 0.89f), new Color(0.88f, 0.51f, 0.43f),
            new Color(0.91f, 0.72f, 0.38f), new Color(0.53f, 0.70f, 0.56f),
            new Color(0.69f, 0.68f, 0.63f), new Color(0.78f, 0.56f, 0.58f),
            new Color(0.77f, 0.79f, 0.78f), new Color(0.70f, 0.63f, 0.48f)
        };

        private GridMap map;
        private readonly List<AgentDefinition> agents = new List<AgentDefinition>();
        private readonly MapfExperimentRunner runner = new MapfExperimentRunner();
        private readonly List<MapfRunSummary> comparisons = new List<MapfRunSummary>();
        private readonly List<string> eventLog = new List<string>();
        private MapfRunSummary lastRun;
        private MapfBatchSummary lastBatch;
        private MapTemplate mapTemplate = MapTemplate.Open;
        private MapfAlgorithmKind selectedAlgorithm = MapfAlgorithmKind.CooperativeAStar;
        private AgentPriorityMode priorityMode = AgentPriorityMode.InputOrder;
        private CBSHeuristicMode cbsHeuristic = CBSHeuristicMode.None;
        private EditTool editTool = EditTool.Obstacle;
        private int mapWidth = 20;
        private int mapHeight = 14;
        private int agentCount = 4;
        private int selectedAgent;
        private int randomSeed = 23;
        private int agentPlacementSeed = 31415;
        private int maxTimeSteps = 128;
        private int maxExpandedNodes = 50000;
        private int maxCbsNodes = 5000;
        private int maxPlanningMilliseconds = 60000;
        private int timelineStep;
        private int cbsFrameIndex;
        private float replaySpeed = 3f;
        private float obstacleDensity = 0.12f;
        private float playbackAccumulator;
        private bool isPlaying;
        private bool showPaths = true;
        private bool showConflicts = true;
        private bool selectedPathOnly;
        private bool showAgentLogDetails;
        private bool showAdvancedSettings;
        private string pythonExecutable = "python";
        private string primalScriptPath = "";
        private string statusMessage = "지도와 에이전트를 설정한 뒤 알고리즘을 실행하세요.";
        private Vector2 resultsScroll;
        private Vector2 sidebarScroll;
        private Vector2 inspectorScroll;
        private bool integerFieldFocused;
        private bool followSelectedAgent;
        private bool zoomToConflict = true;
        private GUIStyle agentMarkerStyle;
        private GUIStyle gridCellStyle;
        private GUISkin monochromeSkin;
        private Font generatedUiFont;
        private Texture2D blackTexture;
        private Texture2D panelTexture;
        private Texture2D controlTexture;
        private Texture2D hoverTexture;
        private Texture2D whiteTexture;
        private bool previewConfigInitialized;
        private float uiScale = 1f;
        private int previewWidth;
        private int previewHeight;
        private int previewAgentCount;
        private int previewSeed;
        private int previewPlacementSeed;
        private MapTemplate previewTemplate;
        private float previewDensity;
        private int blockedCellCount;
        private int connectedCellsOpened;

        public static MapfWorkbenchController Active { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void CreateWorkbench()
        {
            if (FindAnyObjectByType<MapfWorkbenchController>() != null) return;
            var host = new GameObject("CoMPaSS MAPF Workbench");
            DontDestroyOnLoad(host);
            host.AddComponent<MapfWorkbenchController>();
        }

        private void Awake()
        {
            if (Active != null && Active != this)
            {
                Destroy(gameObject);
                return;
            }
            Active = this;
            map = ProceduralMapGenerator.Create(mapWidth, mapHeight, mapTemplate, obstacleDensity, randomSeed,bottleneckCount,passageWidth);
            CreateDefaultAgents();
            RememberPreviewConfiguration();
            RecountBlockedCells();
            DetectPrimalConnection();
            UseGuidedExample(0);
            Log("MAPF 실험 도구를 시작했습니다.");
        }

        private void OnDestroy()
        {
            workCancellation?.Cancel();
            primalProbeCancellation?.Cancel();
            if (Active == this) { Active = null; RestoreWorkbenchRendering(); }
            if (monochromeSkin != null) Destroy(monochromeSkin);
            if (generatedUiFont != null) Destroy(generatedUiFont);
            DestroyTexture(blackTexture);
            DestroyTexture(panelTexture);
            DestroyTexture(controlTexture);
            DestroyTexture(hoverTexture);
            DestroyTexture(whiteTexture);
        }

        private void Update() { UpdateExecution(); PollPrimalConnection(); }

        private void OnGUI()
        {
            integerFieldFocused = false;
            EnsureMonochromeSkin();
            GUISkin previousSkin = GUI.skin;
            Color previousBackground = GUI.backgroundColor;
            Color previousContent = GUI.contentColor;
            uiScale = Screen.dpi > 40f
                ? Mathf.Clamp(Screen.dpi / 96f, 0.95f, 1.35f)
                : Mathf.Clamp(Screen.height / 900f, 0.95f, 1.35f);
            ApplyUiScale();
            GUI.skin = monochromeSkin;
            GUI.backgroundColor = Color.white;
            GUI.color = Color.white;
            GUI.contentColor = new Color(0.94f, 0.94f, 0.94f);
            GUILayout.BeginArea(new Rect(0, 0, Screen.width, Screen.height), GUI.skin.box);
            DrawHeader();
            DrawWorkbenchNavigation();
            RefreshPreviewIfNeeded();
            DrawWorkbenchTab();
            GUILayout.EndArea();
            GUI.skin = previousSkin;
            GUI.backgroundColor = previousBackground;
            GUI.contentColor = previousContent;
        }

        private void DrawHeader()
        {
            GUI.backgroundColor = Color.white;
            GUILayout.BeginHorizontal(GUI.skin.box, GUILayout.Height(Screen.width < 700 ? 46 : 52));
            GUILayout.Label("CoMPaSS", BoldLabel(22), GUILayout.Width(Screen.width < 700 ? 130 : 150));
            if (Screen.width >= 700)
            {
                GUILayout.Label("MAPF 연구 워크벤치", BoldLabel(17), GUILayout.Width(220));
                GUILayout.Label("예제 선택  ·  실험  ·  분석", MutedLabel());
            }
            GUILayout.FlexibleSpace();
            GUI.contentColor = IsBusy ? EdgeConflictCell : lastRun != null ? SuccessAccent : MutedText;
            GUILayout.Label(IsBusy ? "● 계산 중" : lastRun == null ? "● 준비됨" : "● 결과 있음", BoldLabel(13), GUILayout.MaxWidth(160));
            GUI.contentColor = Color.white;
            GUILayout.EndHorizontal();
            GUI.backgroundColor = Color.white;
        }

        private void DrawSidebar(bool compact = false)
        {
            GUI.backgroundColor = Color.white;
            if (compact)
                GUILayout.BeginVertical(GUI.skin.box, GUILayout.Width(Mathf.Max(280f, Screen.width - 24f)), GUILayout.Height(Mathf.Clamp(Screen.height * 0.43f, 300f, 460f)));
            else
                GUILayout.BeginVertical(GUI.skin.box, GUILayout.Width(Mathf.Clamp(Screen.width * 0.24f, 310f * uiScale, 400f * uiScale)), GUILayout.ExpandHeight(true));
            sidebarScroll = GUILayout.BeginScrollView(sidebarScroll, GUILayout.ExpandHeight(true));
            bool previousEnabled = GUI.enabled;
            GUI.enabled = !LockScenario;
            GUILayout.Label("설정", BoldLabel(18));
            GUILayout.Label("지도 시드는 장애물을, 위치 시드는 시작·목표를 재현합니다.", MutedLabel());
            GUILayout.Label("지도 크기", MutedLabel());
            GUILayout.BeginHorizontal();
            mapWidth = Mathf.RoundToInt(GUILayout.HorizontalSlider(mapWidth, MinimumDimension, MaximumDimension));
            mapWidth = IntField("width", mapWidth, 48);
            mapHeight = Mathf.RoundToInt(GUILayout.HorizontalSlider(mapHeight, MinimumDimension, MaximumDimension));
            mapHeight = IntField("height", mapHeight, 48);
            GUILayout.EndHorizontal();

            GUILayout.Label("지도 유형", MutedLabel());
            mapTemplate = (MapTemplate)GUILayout.SelectionGrid((int)mapTemplate,
                new[] { "열린 공간", "무작위 장애물", "교차로", "병목", "좁은 통로" }, 2);
            if(mapTemplate==MapTemplate.Bottleneck||mapTemplate==MapTemplate.NarrowCorridors)
            {
                GUILayout.BeginHorizontal();GUILayout.Label("병목 수 / 통로 폭",MutedLabel());
                bottleneckCount=Mathf.Clamp(IntField("bottlenecks",bottleneckCount,45),1,Mathf.Min(8,mapWidth-2));
                passageWidth=Mathf.Clamp(IntField("passageWidth",passageWidth,45),1,Mathf.Min(8,mapHeight));GUILayout.EndHorizontal();
            }
            distribution=(AgentDistribution)GUILayout.SelectionGrid((int)distribution,new[]{"랜덤 배치","양쪽 교차","모서리 밀집"},3);
            GUILayout.BeginHorizontal();
            GUILayout.Label($"장애물 밀도 {obstacleDensity:P0}", GUILayout.Width(145));
            obstacleDensity = GUILayout.HorizontalSlider(obstacleDensity, 0f, 0.45f);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label("시드", GUILayout.Width(54));
            randomSeed = IntField("mapSeed", randomSeed, 85);
            GUILayout.Label("에이전트", GUILayout.Width(72));
            agentCount = IntField("count", agentCount, 42);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label("위치 시드", GUILayout.Width(66));
            agentPlacementSeed = IntField("placementSeed", agentPlacementSeed, 85);
            if (GUILayout.Button("위치 다시 섞기")) RandomizeAgentPositions();
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("시드로 다시 생성")) GenerateMap();
            if (GUILayout.Button("새 랜덤 지도 + 배치")) RandomizeScenario();
            GUILayout.EndHorizontal();

            GUILayout.Space(8);
            GUILayout.Label("알고리즘", MutedLabel());
            selectedAlgorithm = (MapfAlgorithmKind)GUILayout.SelectionGrid((int)selectedAlgorithm,
                new[] { "독립 A*", "Cooperative A*", "CBS", "PRIMAL 외부" }, 2);
            if (selectedAlgorithm == MapfAlgorithmKind.CooperativeAStar)
            {
                GUILayout.Label("우선순위 규칙", MutedLabel());
                priorityMode = (AgentPriorityMode)GUILayout.SelectionGrid((int)priorityMode,
                    new[] { "입력 순서", "먼 거리 우선", "혼잡 구간 우선" }, 1);
            }

            GUILayout.Label("탐색 제한", MutedLabel());
            GUILayout.BeginHorizontal();
            GUILayout.Label("시간 단계", GUILayout.Width(75));
            maxTimeSteps = Mathf.Clamp(IntField("horizon", maxTimeSteps, 65), 1, 1024);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label("노드 제한", GUILayout.Width(75));
            maxExpandedNodes = Mathf.Clamp(IntField("nodes", maxExpandedNodes, 65), 100, 1000000);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label("시간 제한(ms)", GUILayout.Width(90));
            maxPlanningMilliseconds = Mathf.Clamp(IntField("milliseconds", maxPlanningMilliseconds, 65), 10, 300000);
            GUILayout.EndHorizontal();
            if (selectedAlgorithm == MapfAlgorithmKind.ConflictBasedSearch)
            {
                GUILayout.Label("CBS 상위 탐색 휴리스틱", MutedLabel());
                cbsHeuristic = (CBSHeuristicMode)GUILayout.SelectionGrid((int)cbsHeuristic,
                    new[] { "기본", "DG 최소 정점 덮개", "WDG 최소 정점 덮개" }, 3);
                GUILayout.BeginHorizontal();
                GUILayout.Label("CBS 노드", GUILayout.Width(75));
                maxCbsNodes = Mathf.Clamp(IntField("cbsNodes", maxCbsNodes, 65), 100, 100000);
                GUILayout.EndHorizontal();
            }
            if (selectedAlgorithm == MapfAlgorithmKind.PrimalExternal)
            {
                DrawPrimalConnection();
                GUILayout.BeginHorizontal();
                GUILayout.Label("Python", GUILayout.Width(54));
                pythonExecutable = GUILayout.TextField(pythonExecutable);
                GUILayout.EndHorizontal();
                GUILayout.Label("어댑터 .py 경로", MutedLabel());
                primalScriptPath = GUILayout.TextField(primalScriptPath);
            }

            showAdvancedSettings = GUILayout.Toggle(showAdvancedSettings, "고급 설정 / 구성 프로필");
            if (showAdvancedSettings)
            {
                GUILayout.Label("규칙 기반 구성: Cooperative A* 우선순위, CBS 휴리스틱, 탐색·시간 제한을 선택합니다.", MutedLabel());
                long estimatedStates = (long)mapWidth * mapHeight * agentCount * Mathf.Max(1, maxTimeSteps);
                DrawResearchProfile();
                GUILayout.Label($"대략적 시간 상태 수: {estimatedStates:N0} · 실제 계산량 예측값이 아닌 상한 지표입니다.", MutedLabel());
            }

            DrawAgentEditor();
            DrawScenarioHelp();
            GUI.enabled = previousEnabled;
            DrawCampaignSetupControls();
            GUI.enabled = previousEnabled && !LockScenario;
            DrawPersistenceControls();
            GUI.enabled = previousEnabled;
            GUILayout.EndScrollView();
            GUILayout.BeginHorizontal();
            GUI.enabled = !LockScenario && (selectedAlgorithm != MapfAlgorithmKind.PrimalExternal || primalReady);
            if (GUILayout.Button("▶  선택 알고리즘 실행", GUILayout.Height(40)))
            { activeWorkbenchTab = WorkbenchTab.Playback; RunSelected(); }
            GUI.enabled = !LockScenario;
            if (GUILayout.Button("알고리즘 비교", GUILayout.Height(40)))
            { activeWorkbenchTab = WorkbenchTab.Playback; RunComparison(); }
            GUI.enabled = previousEnabled;
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
            GUI.backgroundColor = Color.white;
        }

        private void DrawAgentEditor()
        {
            GUILayout.Space(7);
            GUILayout.Label("에이전트", BoldLabel(15));
            if (agents.Count > 0)
            {
                selectedAgent = Mathf.Clamp(selectedAgent, 0, agents.Count - 1);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("‹", GUILayout.Width(42))) selectedAgent = PositiveModulo(selectedAgent - 1, agents.Count);
                GUILayout.Label($"Agent {agents[selectedAgent].Id}  ·  {selectedAgent + 1}/{agents.Count}", BoldLabel(13), GUILayout.ExpandWidth(true));
                if (GUILayout.Button("›", GUILayout.Width(42))) selectedAgent = (selectedAgent + 1) % agents.Count;
                GUILayout.EndHorizontal();
                AgentDefinition selected = agents[selectedAgent];
                GUILayout.Label($"{selected.Start} → {selected.Goal}", MutedLabel());
            }
            GUILayout.Label("지도에서 선택한 도구로 칸을 눌러 편집", MutedLabel());
            editTool = (EditTool)GUILayout.SelectionGrid((int)editTool,
                new[] { "장애물", "시작점", "목표점" }, 3);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("에이전트 추가") && agents.Count < 32) AddAgent();
            if (GUILayout.Button("선택 삭제") && agents.Count > 1) RemoveSelectedAgent();
            GUILayout.EndHorizontal();
        }

        private void DrawPersistenceControls()
        {
            GUILayout.Space(7);
            GUILayout.Label("저장 및 불러오기", BoldLabel(15));
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("실험 저장")) SaveScenario();
            if (GUILayout.Button("최근 실험 불러오기")) LoadScenario();
            GUILayout.EndHorizontal();
            DrawFileImport();
        }

        private void DrawWorkspace()
        {
            GUI.backgroundColor = Color.white;
            GUILayout.BeginVertical(GUI.skin.box, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            if (lastRun != null) DrawPlaybackBar();
            else
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("실행 전 미리보기", BoldLabel(18));
                GUILayout.FlexibleSpace();
                GUILayout.Label("설정 변경 사항이 실시간 반영 중", MutedLabel());
                GUILayout.EndHorizontal();
            }
            GUILayout.BeginHorizontal();
            showPaths = GUILayout.Toggle(showPaths, "경로 표시");
            selectedPathOnly = GUILayout.Toggle(selectedPathOnly, "선택 에이전트만");
            showConflicts = GUILayout.Toggle(showConflicts, "충돌 표시");
            zoomToConflict = GUILayout.Toggle(zoomToConflict, "충돌 지점 확대");
            GUILayout.FlexibleSpace();
            GUILayout.Label($"{map.Width} × {map.Height}    에이전트 {agents.Count}명    시드 {randomSeed}", MutedLabel());
            GUILayout.EndHorizontal();
            if(guidedMode)DrawGuidedToolbar();else DrawResearchToolbar();
            GUILayout.BeginHorizontal();
            DrawGrid();
            DrawLiveInspector();
            GUILayout.EndHorizontal();
            GUILayout.Label(statusMessage, MutedLabel());
            DrawResults();
            GUILayout.EndVertical();
            GUI.backgroundColor = Color.white;
        }

        private void DrawLiveInspector(bool compact = false)
        {
            GUILayout.BeginVertical(GUI.skin.box, GUILayout.Width(compact ? Mathf.Max(240f, Screen.width - 40f) : 250f * uiScale), GUILayout.ExpandHeight(true));
            inspectorScroll = GUILayout.BeginScrollView(inspectorScroll, GUILayout.ExpandHeight(true));
            GUILayout.Label("현재 설정", BoldLabel(16));
            GUILayout.Label("지도", MutedLabel());
            GUILayout.Label($"{map.Width} × {map.Height} · {MapTemplateName(mapTemplate)}");
            GUILayout.Label($"장애물 {blockedCellCount}칸 ({(float)blockedCellCount / Mathf.Max(1, map.Width * map.Height):P0})");
            GUILayout.Label($"지도 시드 {randomSeed} · 위치 시드 {agentPlacementSeed}", MutedLabel());
            GUILayout.Label($"자유 칸 {map.Width * map.Height - blockedCellCount}", MutedLabel());
            if (connectedCellsOpened > 0)
                GUILayout.Label($"도달 가능한 배치를 위해 장애물 {connectedCellsOpened}칸을 열었습니다.", MutedLabel());
            GUILayout.Space(10);
            GUILayout.Label("에이전트", MutedLabel());
            if (agents.Count > 0)
            {
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("‹", GUILayout.Width(32))) selectedAgent = PositiveModulo(selectedAgent - 1, agents.Count);
                GUILayout.Label($"Agent {agents[selectedAgent].Id} · {selectedAgent + 1}/{agents.Count}");
                if (GUILayout.Button("›", GUILayout.Width(32))) selectedAgent = (selectedAgent + 1) % agents.Count;
                GUILayout.EndHorizontal();
            }
            GUILayout.Label($"{agents.Count}개 · 밀도 {(float)agents.Count / Mathf.Max(1, map.Width * map.Height):P1}");
            if (agents.Count > 0) GUILayout.Label($"시작 {agents[selectedAgent].Start} → 목표 {agents[selectedAgent].Goal}", MutedLabel());
            if (lastRun != null) GUILayout.Label($"전체 {agents.Count} · 계획 {lastRun.RoutedAgents} · 도착 {simulation?.ArrivedCount ?? 0}", BoldLabel(13));
            GUILayout.Space(10);
            followSelectedAgent = GUILayout.Toggle(followSelectedAgent, "선택 에이전트 따라가기");
            if (followSelectedAgent) selectedPathOnly = true;
            GUILayout.Space(8);
            GUILayout.Label("선택 알고리즘", MutedLabel());
            GUILayout.Label(MapfCsvExporter.Name(lastRun?.Algorithm ?? selectedAlgorithm), BoldLabel(14));
            if (selectedAlgorithm == MapfAlgorithmKind.CooperativeAStar)
                GUILayout.Label("우선순위: " + PriorityModeName(priorityMode), MutedLabel());
            if (selectedAlgorithm == MapfAlgorithmKind.ConflictBasedSearch)
                GUILayout.Label("휴리스틱: " + CbsHeuristicName(cbsHeuristic), MutedLabel());
            GUILayout.Space(10);
            GUILayout.Label("선택 에이전트 상태", MutedLabel());
            if (simulation != null && agents.Count > 0)
            {
                int id = agents[selectedAgent].Id;
                GUILayout.Label(simulation.HasArrived(id) ? "도착 · 목표 칸 영구 점유" : !simulation.HasRoute(id) ? "미계획 · 시작 칸에서 대기" : simulation.State.ToString(), BoldLabel(13));
            }
            DrawSelectedAgentState();
            DrawCbsReplayDetails();
            DrawObservation();
            GUILayout.Space(8);
            GUILayout.Label("범례", MutedLabel());
            LegendRow(EmptyCell, "이동 가능");
            LegendRow(ObstacleCell, "장애물");
            LegendRow(PathCell, "경로");
            LegendRow(ConflictCell, "V  정점 충돌");
            LegendRow(EdgeConflictCell, "E  간선 충돌");
            GUILayout.FlexibleSpace();
            if (lastRun == null)
                GUILayout.Label("아직 실행 전입니다. 설정을 바꾸면 지도와 배치가 이 화면에서 바로 갱신됩니다.", MutedLabel());
            else
                GUILayout.Label($"최근 실행 · {lastRun.PlanningMilliseconds:F1} ms\n경로 비용 {lastRun.TotalMoveCost} · 충돌 {lastRun.Conflicts.Count}", MutedLabel());
            GUILayout.EndScrollView();
            GUILayout.EndVertical();
        }

        private void DrawCbsReplayDetails()
        {
            if (!viewSearchReplay || lastRun == null || lastRun.CbsReplayFrames.Count == 0) return;
            CBSReplayFrame frame = lastRun.CbsReplayFrames[Mathf.Clamp(cbsFrameIndex, 0, lastRun.CbsReplayFrames.Count - 1)];
            GUILayout.Space(8);
            GUILayout.Label("CBS 시행착오", MutedLabel());
            if (!frame.IsConstraintTrial)
            {
                GUILayout.Label($"CT 노드 {frame.NodeId} 확장 · 누적 비용 {frame.SumOfCosts}", BoldLabel(12));
                GUILayout.Label(frame.Conflict == null ? "남은 충돌이 없어 해를 찾았습니다." :
                    $"충돌 발견: Agent {frame.Conflict.FirstAgentId} / {frame.Conflict.SecondAgentId}, t={frame.Conflict.TimeStep}", MutedLabel());
                return;
            }
            GUILayout.Label($"분기 {frame.ParentNodeId} → {frame.NodeId} · Agent {frame.ConstrainedAgentId}", BoldLabel(12));
            GUILayout.Label("추가 제약: " + (string.IsNullOrEmpty(frame.AppliedConstraint) ? "(정보 없음)" : frame.AppliedConstraint), MutedLabel());
            GUILayout.Label(frame.ReplanSucceeded ? $"재탐색 성공 · 비용 {frame.SumOfCosts}" : "재탐색 실패 · 해당 분기 폐기",
                frame.ReplanSucceeded ? MutedLabel() : ErrorLabel());
            if (!frame.ReplanSucceeded && !string.IsNullOrEmpty(frame.ReplanFailureReason))
                GUILayout.Label(frame.ReplanFailureReason, ErrorLabel());
            if (frame.ReplanSucceeded && frame.Conflict != null)
                GUILayout.Label($"다음 충돌: {frame.Conflict.Type} · Agent {frame.Conflict.FirstAgentId}/{frame.Conflict.SecondAgentId} · t={frame.Conflict.TimeStep}", MutedLabel());
            else if (frame.ReplanSucceeded)
                GUILayout.Label("이 분기에서 추가 충돌이 발견되지 않았습니다.", MutedLabel());
        }

        private void LegendRow(Color color, string label)
        {
            GUILayout.BeginHorizontal();
            Rect swatch = GUILayoutUtility.GetRect(18, 18, GUILayout.Width(18));
            GUI.color = color; GUI.DrawTexture(swatch, Texture2D.whiteTexture); GUI.color = Color.white;
            GUILayout.Label(label, MutedLabel());
            GUILayout.EndHorizontal();
        }

        private void RecountBlockedCells()
        {
            blockedCellCount = 0;
            for (int y = 0; y < map.Height; y++)
            for (int x = 0; x < map.Width; x++)
                if (map.IsBlocked(new GridPosition(x, y))) blockedCellCount++;
        }

        private void DrawPlaybackBar()
        {
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(isPlaying ? "일시정지" : "재생", GUILayout.Width(78)))
            {
                viewSearchReplay = false; campaignPaused = false;
                if (!isPlaying && simulation != null && simulation.IsTerminal && timelineStep >= simulation.Step)
                { reviewingTimeline = true; timelineStep = 0; playbackAccumulator = 0; }
                if (simulation != null && simulation.IsTerminal) reviewingTimeline = true;
                isPlaying = !isPlaying;
                if (campaignRunning) campaignPaused = !isPlaying;
            }
            if (GUILayout.Button("이동 +1", GUILayout.Width(72))) StepOnce();
            if (GUILayout.Button("처음 보기", GUILayout.Width(82)))
            { isPlaying = false; reviewingTimeline = true; timelineStep = 0; playbackAccumulator = 0; }
            GUILayout.Label("속도", GUILayout.Width(36));
            replaySpeed = Mathf.Pow(10f, GUILayout.HorizontalSlider(Mathf.Log10(replaySpeed), -0.60206f, 2f, GUILayout.Width(100)));
            foreach (int speed in new[] { 1, 10, 100 })
                if (GUILayout.Button(speed + "×", GUILayout.Width(45))) replaySpeed = speed;
            GUILayout.Label(replaySpeed.ToString("F1") + "×", GUILayout.Width(55));
            GUILayout.FlexibleSpace();
            GUILayout.Label(simulation == null ? "저장된 계획" : $"도착 {simulation.ArrivedCount}/{agents.Count}");
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label(viewSearchReplay ? "탐색 경로 t" : "실행 기록 t", GUILayout.Width(90));
            int step = Mathf.RoundToInt(GUILayout.HorizontalSlider(timelineStep, 0, Mathf.Max(1, GetLastTimeStep())));
            if (step != timelineStep)
            { timelineStep = Mathf.Min(step, GetLastTimeStep()); isPlaying = false; reviewingTimeline = !viewSearchReplay; playbackAccumulator = 0; }
            GUILayout.Label($"{timelineStep} / {GetLastTimeStep()}", GUILayout.Width(90));
            GUILayout.EndHorizontal();
            if (lastRun.CbsReplayFrames.Count == 0) return;
            bool search = GUILayout.Toggle(viewSearchReplay, "CBS 시행착오 살펴보기 (실제 이동과 별도)");
            if (search != viewSearchReplay)
            { viewSearchReplay = search; isPlaying = false; if (campaignRunning) campaignPaused = true; timelineStep = search ? 0 : simulation?.Step ?? 0; }
            if (!viewSearchReplay) return;
            playSearch = GUILayout.Toggle(playSearch, "CBS 전체 과정 연속 재생");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("‹", GUILayout.Width(30))) SelectCbsFrame(cbsFrameIndex - 1);
            int frameIndex = Mathf.RoundToInt(GUILayout.HorizontalSlider(cbsFrameIndex, 0, lastRun.CbsReplayFrames.Count - 1));
            if (frameIndex != cbsFrameIndex) SelectCbsFrame(frameIndex);
            if (GUILayout.Button("›", GUILayout.Width(30))) SelectCbsFrame(cbsFrameIndex + 1);
            GUILayout.Label($"시행착오 {cbsFrameIndex + 1}/{lastRun.CbsReplayFrames.Count}", GUILayout.Width(165));
            GUILayout.EndHorizontal();
        }

        private void DrawGrid()
        {
            GUILayout.BeginVertical(GUI.skin.box, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            Rect board = GUILayoutUtility.GetRect(160, 100, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            if (board.width <= 0 || board.height <= 0) { GUILayout.EndVertical(); return; }
            GUI.BeginGroup(board);
            HandleCamera(board); BuildConflictHeat();
            float baseCellSize = Mathf.Max(1f, Mathf.Min((board.width - 12f) / map.Width, (board.height - 12f) / map.Height));
            MapfConflict currentConflict = showConflicts ? GetCurrentConflict() : null;
            bool zoomToFocus = followSelectedAgent || (zoomToConflict && currentConflict != null);
            float cellSize = baseCellSize * mapZoom * (zoomToFocus ? 2f : 1f);
            float offsetX = (board.width - cellSize * map.Width) * 0.5f + cameraPan.x;
            float offsetY = (board.height - cellSize * map.Height) * 0.5f + cameraPan.y;
            if (zoomToFocus && agents.Count > 0)
            {
                GridPosition center = followSelectedAgent ? GetFollowPosition() : ConflictFocus(currentConflict);
                offsetX = board.width * 0.5f - (center.X + 0.5f) * cellSize;
                offsetY = board.height * 0.5f - (map.Height - center.Y - 0.5f) * cellSize;
            }
            IReadOnlyList<AgentPath> paths = GetDisplayedPaths();
            displayedPathCells.Clear();
            if (showPaths) foreach (AgentPath path in paths)
            {
                if (path == null || (selectedPathOnly && agents.Count > 0 && path.AgentId != agents[selectedAgent].Id)) continue;
                foreach (GridPosition cell in path.Positions) displayedPathCells.Add(cell);
            }
            if (Event.current.type == EventType.MouseDown && Event.current.button == 0)
            {
                Vector2 mouse = Event.current.mousePosition;
                var cell = new GridPosition(Mathf.FloorToInt((mouse.x - offsetX) / cellSize), map.Height - 1 - Mathf.FloorToInt((mouse.y - offsetY) / cellSize));
                if (map.Contains(cell) && mouse.x >= 0 && mouse.y >= 0 && mouse.x < board.width && mouse.y < board.height)
                {
                    bool selected = false;
                    if (lastRun != null) foreach (AgentDefinition agent in agents)
                    {
                        Vector2 p = AgentDisplayPosition(agent);
                        if (Mathf.RoundToInt(p.x) != cell.X || Mathf.RoundToInt(p.y) != cell.Y) continue;
                        selectedAgent = agents.IndexOf(agent); selected = true; break;
                    }
                    if (!selected && !LockScenario && activeWorkbenchTab == WorkbenchTab.Setup) ApplyCellEdit(cell);
                    Event.current.Use();
                }
            }

            if (Event.current.type == EventType.Repaint)
            for (int y = 0; y < map.Height; y++)
            for (int x = 0; x < map.Width; x++)
            {
                var position = new GridPosition(x, y);
                Rect cell = new Rect(offsetX + x * cellSize, offsetY + (map.Height - 1 - y) * cellSize, cellSize - 1, cellSize - 1);
                if (!cell.Overlaps(new Rect(0, 0, board.width, board.height))) continue;
                AgentDefinition startsAt = FindEndpoint(position, true);
                AgentDefinition goalAt = FindEndpoint(position, false);
                MapfConflict conflict = showConflicts ? ConflictAt(position, timelineStep) : null;
                Color oldColor = GUI.backgroundColor;
                Color oldContent = GUI.contentColor;
                GUI.backgroundColor = GetCellColor(position, -1, startsAt, goalAt, conflict, paths);
                if (conflict != null) GUI.contentColor = Color.black;
                string label = GetCellLabel(position, -1, startsAt, goalAt, conflict);
                if (cellSize < 23f)
                {
                    if (conflict != null) label = "!";
                    else if (startsAt != null) label = "S";
                    else if (goalAt != null) label = "G";
                    else label = "";
                }
                Color cellColor = GUI.backgroundColor;
                GUI.color = cellColor; GUI.DrawTexture(cell, Texture2D.whiteTexture); GUI.color = Color.white;
                GUI.backgroundColor = Color.clear;
                if (gridCellStyle == null) gridCellStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 10 };
                GUI.contentColor = cellColor.grayscale > 0.55f ? Color.black : Color.white;
                GUI.Label(cell, label, gridCellStyle);
                GUI.backgroundColor = oldColor;
                GUI.contentColor = oldContent;
            }
            if (lastRun != null) DrawMovingAgents(paths, cellSize, offsetX, offsetY);
            DrawResearchOverlay(board, paths, cellSize, offsetX, offsetY);
            if (currentConflict != null) DrawConflictBanner(currentConflict, board, cellSize, offsetX, offsetY);
            GUI.EndGroup();
            GUILayout.EndVertical();
        }

        private void DrawConflictBanner(MapfConflict conflict, Rect board, float cellSize, float offsetX, float offsetY)
        {
            GridPosition center = ConflictFocus(conflict);
            float x = offsetX + (center.X + 0.5f) * cellSize;
            float y = offsetY + (map.Height - center.Y - 0.5f) * cellSize;
            const float width = 220f;
            const float height = 34f;
            Rect banner = new Rect(Mathf.Clamp(x - width * 0.5f, 4f, Mathf.Max(4f, board.width - width - 4f)),
                Mathf.Clamp(y - height - 6f, 4f, Mathf.Max(4f, board.height - height - 4f)), width, height);
            Color previousBackground = GUI.backgroundColor;
            Color previousContent = GUI.contentColor;
            GUI.backgroundColor = conflict.Type == ConflictType.Vertex ? ConflictCell : EdgeConflictCell;
            GUI.contentColor = Color.white;
            string type = conflict.Type == ConflictType.Vertex ? "정점 충돌" : "간선 교차 충돌";
            var bannerStyle = new GUIStyle(GUI.skin.box)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                fontSize = 12
            };
            GUI.Box(banner, $"충돌 예상  ·  {type}  ·  t={conflict.TimeStep}", bannerStyle);
            GUI.backgroundColor = previousBackground;
            GUI.contentColor = previousContent;
        }

        private MapfConflict GetCurrentConflict()
        {
            if (lastRun == null) return null;
            if (simulation != null && !viewSearchReplay) return simulation.PendingConflict;
            if (viewSearchReplay && lastRun.CbsReplayFrames.Count > 0)
                return lastRun.CbsReplayFrames[Mathf.Clamp(cbsFrameIndex, 0, lastRun.CbsReplayFrames.Count - 1)].Conflict;
            MapfConflict closest = null;
            for (int i = 0; i < lastRun.Conflicts.Count; i++)
            {
                int candidateDistance = Mathf.Abs(lastRun.Conflicts[i].TimeStep - timelineStep);
                if (candidateDistance == 0) { closest = lastRun.Conflicts[i]; break; }
            }
            return closest;
        }

        private static GridPosition ConflictFocus(MapfConflict conflict)
        {
            if (conflict == null) return new GridPosition(0, 0);
            if (conflict.Type == ConflictType.Vertex) return conflict.FirstPosition;
            return new GridPosition((conflict.FirstPosition.X + conflict.SecondPosition.X) / 2,
                (conflict.FirstPosition.Y + conflict.SecondPosition.Y) / 2);
        }

        private void SelectCbsFrame(int index)
        {
            if (lastRun == null || lastRun.CbsReplayFrames.Count == 0) return;
            viewSearchReplay = true; isPlaying = false;
            cbsFrameIndex = Mathf.Clamp(index, 0, lastRun.CbsReplayFrames.Count - 1);
            playbackAccumulator = 0f;
            MapfConflict conflict = lastRun.CbsReplayFrames[cbsFrameIndex].Conflict;
            if (conflict != null) timelineStep = Mathf.Clamp(conflict.TimeStep, 0, GetLastTimeStep());
        }

        private Vector2 AgentDisplayPosition(AgentDefinition agent)
        {
            if (simulation != null && !viewSearchReplay && !reviewingTimeline)
            {
                GridPosition from = simulation.Position(agent.Id), to = simulation.NextPosition(agent.Id);
                return Vector2.Lerp(new Vector2(from.X, from.Y), new Vector2(to.X, to.Y), (lowMotion ? 0 : Mathf.Clamp01(playbackAccumulator)));
            }
            AgentPath path = FindDisplayedPath(agent.Id);
            return path == null || path.IsEmpty ? new Vector2(agent.Start.X, agent.Start.Y) :
                InterpolatedPosition(path, timelineStep + (isPlaying ? Mathf.Clamp01(playbackAccumulator) : 0));
        }

        private void DrawMovingAgents(IReadOnlyList<AgentPath> paths, float cellSize, float offsetX, float offsetY)
        {
            MapfConflict conflict = showConflicts ? GetCurrentConflict() : null;
            foreach (AgentDefinition agent in agents)
            {
                Vector2 position = AgentDisplayPosition(agent);
                bool involved = conflict != null && (agent.Id == conflict.FirstAgentId || agent.Id == conflict.SecondAgentId);
                bool arrived = position.x == agent.Goal.X && position.y == agent.Goal.Y;
                float size = Mathf.Max(7f, cellSize * 0.78f);
                Rect marker = new Rect(offsetX + (position.x + 0.5f) * cellSize - size / 2,
                    offsetY + (map.Height - position.y - 0.5f) * cellSize - size / 2, size, size);
                GUI.color = involved ? ConflictCell : Color.black;
                GUI.DrawTexture(new Rect(marker.x - 1, marker.y - 1, size + 2, size + 2), Texture2D.whiteTexture);
                GUI.color = AgentColor(agent.Id); GUI.DrawTexture(marker, Texture2D.whiteTexture); GUI.color = Color.white;
                if (agentMarkerStyle == null) agentMarkerStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
                agentMarkerStyle.fontSize = Mathf.Clamp(Mathf.RoundToInt(size * 0.55f), 8, 22);
                GUI.contentColor = Color.black;
                if (cellSize >= 15) GUI.Label(marker, arrived ? "■" : agent.Id.ToString(), agentMarkerStyle);
                GUI.contentColor = Color.white;
            }
        }

        private void DrawSelectedAgentState()
        {
            if (agents.Count == 0) return;
            AgentDefinition selected = agents[Mathf.Clamp(selectedAgent, 0, agents.Count - 1)];
            GUILayout.Label($"Agent {selected.Id}  ·  {selected.Start} → {selected.Goal}", BoldLabel(13));
            AgentPath path = FindDisplayedPath(selected.Id);
            if (path == null || path.IsEmpty)
            {
                GUILayout.Label(lastRun == null ? "실행 전 · 설정된 시작점에서 대기" : "이 실행에서 경로가 만들어지지 않았습니다.", MutedLabel());
                return;
            }

            float time = timelineStep + (isPlaying ? Mathf.Clamp01(playbackAccumulator) : 0f);
            int step = Mathf.Clamp(Mathf.FloorToInt(time), 0, path.MoveCost);
            Vector2 position = AgentDisplayPosition(selected);
            GridPosition from = simulation != null && !viewSearchReplay && !reviewingTimeline ? simulation.Position(selected.Id) : path.PositionAt(step);
            GridPosition to = simulation != null && !viewSearchReplay && !reviewingTimeline ? simulation.NextPosition(selected.Id) : path.PositionAt(Mathf.Min(step + 1, path.MoveCost));
            string action = from == to ? "대기" : DescribeMove(from, to);
            GUILayout.Label($"현재 ({position.x:F1}, {position.y:F1}) · 전체 t={timelineStep} · 이 경로 도착 t={path.MoveCost}");
            GUILayout.Label($"다음 판단: {action}", BoldLabel(13));
            if(lastRun.Algorithm==MapfAlgorithmKind.IndependentAStar||lastRun.Algorithm==MapfAlgorithmKind.CooperativeAStar)
            {
                double estimate=(runSettings?.Profile??searchProfile).Estimate(from,selected.Goal);
                GUILayout.Label($"계획 경로상 g={step}, 가중 h={estimate:F1}, f={step+estimate:F1}",MutedLabel());
            }

            foreach (AgentPriorityDecision decision in lastRun.PriorityDecisions)
            {
                if (decision.AgentId != selected.Id) continue;
                GUILayout.Label($"우선순위 {decision.Rank}위 · 점수 {decision.Score:F0}", MutedLabel());
                GUILayout.Label(decision.Reason, MutedLabel());
                break;
            }
            int shown = 0;
            foreach (AgentRunLog log in lastRun.AgentLogs)
            {
                if (log.AgentId != selected.Id && log.AgentId != -1) continue;
                GUILayout.Label(log.Message, MutedLabel());
                if (++shown >= 3) break;
            }
        }

        private void DrawResults()
        {
            GUILayout.BeginVertical(GUI.skin.box, GUILayout.ExpandWidth(true));
            GUILayout.Label("실행 결과", BoldLabel(15));
            if (comparisons.Count > 0)
            {
                DrawSummaryHeader();
                foreach (MapfRunSummary item in comparisons) DrawSummaryRow(item);
            }
            else if (lastBatch != null)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("알고리즘", BoldLabel(12), GUILayout.Width(165));
                GUILayout.Label("성공률", GUILayout.Width(75));
                GUILayout.Label("실패", GUILayout.Width(55));
                GUILayout.Label("평균 시간 ± SD", GUILayout.Width(145));
                GUILayout.Label("최솟값–최댓값", GUILayout.Width(140));
                GUILayout.Label("평균 이동 비용 ± SD", GUILayout.Width(175));
                GUILayout.EndHorizontal();
                foreach (MapfAlgorithmAggregate item in lastBatch.Algorithms) DrawAggregateRow(item);
                GUILayout.Label($"실험 조건: {mapWidth}×{mapHeight}, {agentCount} 에이전트, seed {lastBatch.FirstSeed}–{lastBatch.FirstSeed + lastBatch.Repetitions - 1}", MutedLabel());
            }
            else if (lastRun != null) { DrawSummaryHeader(); DrawSummaryRow(lastRun); }
            else GUILayout.Label("아직 실행 결과가 없습니다.", MutedLabel());

            if (lastRun != null && !string.IsNullOrEmpty(lastRun.FailureReason))
                GUILayout.Label($"실패 사유: 에이전트 {lastRun.FailedAgentId} · {lastRun.FailureReason}", ErrorLabel());
            if (lastRun != null && lastRun.PriorityDecisions.Count > 0)
            GUILayout.Label("우선순위: " + string.Join("  |  ", PriorityLabels(lastRun.PriorityDecisions)), MutedLabel());
            if (simulation != null) GUILayout.Label($"실제 실행 {simulation.State} · 도착 {simulation.ArrivedCount}/{agents.Count} · 비용 {simulation.ExecutedCost} · t={simulation.Step} · 대기 {simulation.WaitActions} · 복구 {simulation.RecoveryCount}회 ({simulation.RecoveryMilliseconds:F1}ms)", MutedLabel());
            if (lastRun != null)
                GUILayout.Label($"원래 계획: 완료 시간 {lastRun.CompletionTime} · 대기 {lastRun.WaitActionCount} · 최종 경로 충돌 {lastRun.Conflicts.Count} · 탐색 중 충돌 발견 {lastRun.PlanningConflictCount}", MutedLabel());
            showAgentLogDetails = GUILayout.Toggle(showAgentLogDetails, "선택 에이전트 판단 로그");
            if (showAgentLogDetails && lastRun != null && agents.Count > 0)
            {
                int selectedId = agents[Mathf.Clamp(selectedAgent, 0, agents.Count - 1)].Id;
                foreach (AgentPriorityDecision decision in lastRun.PriorityDecisions)
                    if (decision.AgentId == selectedId) GUILayout.Label($"순위 {decision.Rank} · 점수 {decision.Score:F0} · {decision.Reason}", MutedLabel());
                int details = 0;
                foreach (AgentRunLog log in lastRun.AgentLogs)
                {
                    if (log.AgentId != selectedId) continue;
                    GUILayout.Label(log.Message, MutedLabel());
                    if (++details >= 8) break;
                }
            }
            if (lastRun != null && lastRun.Conflicts.Count > 0)
            {
                int visible = 0;
                foreach (MapfConflict conflict in lastRun.Conflicts)
                {
                    if (visible++ >= 5) break;
                    GUILayout.Label($"t={conflict.TimeStep} {conflict.Type} 충돌 · Agent {conflict.FirstAgentId} / {conflict.SecondAgentId}", ErrorLabel());
                }
            }
            GUILayout.Space(5);
            DrawResearchResults();
            GUILayout.Label("실행 로그", BoldLabel(12));
            int logCount = Mathf.Min(4, eventLog.Count);
            for (int i = 0; i < logCount; i++) GUILayout.Label(eventLog[i], MutedLabel());
            GUILayout.EndVertical();
        }

        private void DrawSummaryHeader()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("원래 계획", BoldLabel(12), GUILayout.Width(165));
            GUILayout.Label("전원 경로", GUILayout.Width(70));
            GUILayout.Label("충돌 없음", GUILayout.Width(85));
            GUILayout.Label("계산(ms)", GUILayout.Width(90));
            GUILayout.Label("경로 비용", GUILayout.Width(75));
            GUILayout.Label("완료 t", GUILayout.Width(75));
            GUILayout.Label("확장 노드", GUILayout.Width(90));
            GUILayout.EndHorizontal();
        }

        private void DrawSummaryRow(MapfRunSummary item)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(item.Variant??MapfCsvExporter.Name(item.Algorithm), GUILayout.Width(165));
            GUILayout.Label(item.AllAgentsRouted ? "예" : "아니요", GUILayout.Width(70));
            GUILayout.Label(item.CollisionFree ? "예" : "아니요", GUILayout.Width(85));
            GUILayout.Label(item.PlanningMilliseconds.ToString("F2"), GUILayout.Width(90));
            GUILayout.Label(item.TotalMoveCost.ToString(), GUILayout.Width(75));
            GUILayout.Label(item.CompletionTime.ToString(), GUILayout.Width(75));
            GUILayout.Label(item.ExpandedNodes.ToString(), GUILayout.Width(90));
            GUILayout.EndHorizontal();
            if (item.Algorithm == MapfAlgorithmKind.ConflictBasedSearch)
                GUILayout.Label($"CBS 상세: CT 노드 {item.HighLevelNodes} · 제약 {item.GeneratedConstraints} · 재탐색 {item.ReplanCount} · 휴리스틱 확장 {item.HeuristicExpandedNodes}", MutedLabel());
        }

        private void DrawAggregateRow(MapfAlgorithmAggregate item)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(MapfCsvExporter.Name(item.Algorithm), GUILayout.Width(165));
            GUILayout.Label($"{item.SuccessRate:P0} ({item.Successes}/{item.Runs})", GUILayout.Width(75));
            GUILayout.Label(item.Failures.ToString(), GUILayout.Width(55));
            GUILayout.Label($"{item.MeanTimeMilliseconds:F2} ± {item.TimeStandardDeviationMilliseconds:F2}", GUILayout.Width(145));
            GUILayout.Label($"{item.MinimumTimeMilliseconds:F2}–{item.MaximumTimeMilliseconds:F2}", GUILayout.Width(140));
            GUILayout.Label($"{item.MeanMoveCost:F1} ± {item.MoveCostStandardDeviation:F1}", GUILayout.Width(175));
            GUILayout.EndHorizontal();
        }

        private void RefreshPreviewIfNeeded()
        {
            if (integerFieldFocused || LockScenario) return;
            mapWidth = Mathf.Clamp(mapWidth, MinimumDimension, MaximumDimension);
            mapHeight = Mathf.Clamp(mapHeight, MinimumDimension, MaximumDimension);
            agentCount = Mathf.Clamp(agentCount, 1, Mathf.Min(32, mapWidth * mapHeight / 2));
            obstacleDensity = Mathf.Clamp(obstacleDensity, 0f, 0.45f);
            if (previewConfigInitialized && previewWidth == mapWidth && previewHeight == mapHeight &&
                previewAgentCount == agentCount && previewSeed == randomSeed && previewPlacementSeed == agentPlacementSeed && previewTemplate == mapTemplate &&
                Mathf.Approximately(previewDensity, obstacleDensity) && previewBottlenecks==bottleneckCount && previewPassage==passageWidth && previewDistribution==distribution) return;

            map = ProceduralMapGenerator.Create(mapWidth, mapHeight, mapTemplate, obstacleDensity, randomSeed,bottleneckCount,passageWidth);
            agents.Clear();
            CreateDefaultAgents();
            RememberPreviewConfiguration();
            RecountBlockedCells();
            ResetRun();
            statusMessage = "설정 변경 사항을 미리보기에 적용했습니다.";
        }

        private void RememberPreviewConfiguration()
        {
            previewConfigInitialized = true;
            previewWidth = mapWidth;
            previewHeight = mapHeight;
            previewAgentCount = agentCount;
            previewSeed = randomSeed;
            previewPlacementSeed = agentPlacementSeed;
            previewTemplate = mapTemplate;
            previewDensity = obstacleDensity;previewBottlenecks=bottleneckCount;previewPassage=passageWidth;previewDistribution=distribution;
        }

        private static string MapTemplateName(MapTemplate value)
        {
            switch (value)
            {
                case MapTemplate.RandomObstacles: return "무작위 장애물";
                case MapTemplate.Crossroads: return "교차로";
                case MapTemplate.Bottleneck: return "병목";
                case MapTemplate.NarrowCorridors: return "좁은 통로";
                default: return "열린 공간";
            }
        }

        private static string PriorityModeName(AgentPriorityMode value)
        {
            switch (value)
            {
                case AgentPriorityMode.LongestDistanceFirst: return "먼 거리 우선";
                case AgentPriorityMode.MostConstrainedFirst: return "혼잡 구간 우선";
                default: return "입력 순서";
            }
        }

        private static string CbsHeuristicName(CBSHeuristicMode value)
        {
            switch (value)
            {
                case CBSHeuristicMode.DependencyGraph: return "DG 최소 정점 덮개";
                case CBSHeuristicMode.WeightedDependencyGraph: return "WDG 최소 정점 덮개";
                default: return "기본";
            }
        }

        private void GenerateMap()
        {
            CommitNumberInputs();
            mapWidth = Mathf.Clamp(mapWidth, MinimumDimension, MaximumDimension);
            mapHeight = Mathf.Clamp(mapHeight, MinimumDimension, MaximumDimension);
            try
            {
                map = ProceduralMapGenerator.Create(mapWidth, mapHeight, mapTemplate, obstacleDensity, randomSeed,bottleneckCount,passageWidth);
                agents.Clear();
                CreateDefaultAgents();
                RememberPreviewConfiguration();
                RecountBlockedCells();
                ResetRun();
                statusMessage = $"시드로 지도와 에이전트 배치를 재현했습니다. 지도 {randomSeed} · 위치 {agentPlacementSeed}";
                Log($"{mapWidth}×{mapHeight} {mapTemplate} 지도 / {agents.Count}개 에이전트 생성 (seed {randomSeed}, 배치 {agentPlacementSeed}).");
            }
            catch (Exception exception) { SetError(exception.Message); }
        }

        private void RandomizeScenario()
        {
            CommitNumberInputs();
            mapTemplate = MapTemplate.RandomObstacles;
            randomSeed = UnityEngine.Random.Range(-1000000000, 1000000000);
            agentPlacementSeed = UnityEngine.Random.Range(-1000000000, 1000000000);
            GenerateMap();
        }

        private void RandomizeAgentPositions()
        {
            try
            {
                CommitNumberInputs(); RefreshPreviewIfNeeded();
                agentPlacementSeed = UnityEngine.Random.Range(-1000000000, 1000000000);
                agents.Clear();
                agents.AddRange(ProceduralMapGenerator.CreateRandomAgents(map, agentCount, agentPlacementSeed, out connectedCellsOpened,distribution));
                selectedAgent = 0;
                RememberPreviewConfiguration();
                RecountBlockedCells();
                ResetRun();
                statusMessage = connectedCellsOpened > 0
                    ? $"이동 가능한 연결 영역 확보를 위해 장애물 {connectedCellsOpened}칸을 열고 에이전트를 다시 배치했습니다. 위치 시드 {agentPlacementSeed}"
                    : $"에이전트 위치를 다시 섞었습니다. 위치 시드 {agentPlacementSeed}";
                Log(statusMessage);
            }
            catch (Exception exception) { SetError("에이전트 재배치 실패: " + exception.Message); }
        }

        private void CreateDefaultAgents()
        {
            int count = Mathf.Clamp(agentCount, 1, Mathf.Min(32, map.Width * map.Height / 2));
            agents.AddRange(ProceduralMapGenerator.CreateRandomAgents(map, count, agentPlacementSeed, out connectedCellsOpened,distribution));
            agentCount = agents.Count; selectedAgent = Mathf.Clamp(selectedAgent, 0, agents.Count - 1);
        }

        private void AddAgent()
        {
            if (agents.Count >= 32) { SetError("최대 32명까지 지원합니다."); return; }
            var open = new List<GridPosition>();
            for (int y = 0; y < map.Height; y++)
            for (int x = 0; x < map.Width; x++)
            {
                var cell = new GridPosition(x, y);
                if (!map.IsBlocked(cell) && FindEndpoint(cell, true) == null && FindEndpoint(cell, false) == null) open.Add(cell);
            }
            if (open.Count < 2) { SetError("에이전트를 추가할 빈 칸이 부족합니다."); return; }
            GridPosition start = open[PositiveModulo(randomSeed + agents.Count * 17, open.Count)];
            GridPosition goal = open[PositiveModulo(randomSeed * 3 + agents.Count * 31 + open.Count / 2, open.Count)];
            if (start == goal) goal = open[(open.IndexOf(start) + 1) % open.Count];
            agents.Add(new AgentDefinition(NextAgentId(), start, goal));
            agentCount = agents.Count; RememberPreviewConfiguration();
            selectedAgent = agents.Count - 1;
            ResetRun();
        }

        private void RemoveSelectedAgent()
        {
            if (agents.Count <= 1) return;
            agents.RemoveAt(selectedAgent);
            agentCount = agents.Count; RememberPreviewConfiguration();
            selectedAgent = Mathf.Clamp(selectedAgent, 0, agents.Count - 1);
            ResetRun();
        }

        private int NextAgentId()
        {
            int id = 0;
            for (int i = 0; i < agents.Count; i++) id = Mathf.Max(id, agents[i].Id + 1);
            return id;
        }

        private void ApplyCellEdit(GridPosition position)
        {
            if (agents.Count == 0) return;
            AgentDefinition selected = agents[selectedAgent];
            if (editTool == EditTool.Obstacle)
            {
                if (FindEndpoint(position, true) != null || FindEndpoint(position, false) != null)
                { SetError("시작점이나 목표점이 있는 칸은 장애물로 바꿀 수 없습니다."); return; }
                map.SetBlocked(position, !map.IsBlocked(position));
                blockedCellCount += map.IsBlocked(position) ? 1 : -1;
            }
            else if (editTool == EditTool.Start)
            {
                if (map.IsBlocked(position) || OccupiedByOtherEndpoint(position, selected.Id, true))
                { SetError("시작점은 빈 칸에만 둘 수 있습니다."); return; }
                agents[selectedAgent] = new AgentDefinition(selected.Id, position, selected.Goal);
            }
            else
            {
                if (map.IsBlocked(position) || OccupiedByOtherEndpoint(position, selected.Id, false))
                { SetError("목표점은 빈 칸에만 둘 수 있습니다."); return; }
                agents[selectedAgent] = new AgentDefinition(selected.Id, selected.Start, position);
            }
            connectedCellsOpened = 0;
            ResetRun();
        }

        private bool OccupiedByOtherEndpoint(GridPosition position, int agentId, bool startsOnly)
        {
            for (int i = 0; i < agents.Count; i++)
            {
                AgentDefinition agent = agents[i];
                if (agent.Id == agentId) continue;
                if (agent.Start == position || (!startsOnly && agent.Goal == position)) return true;
                if (startsOnly && agent.Goal == position) return true;
            }
            return false;
        }

        private void RunSelected() { StartSinglePlan(); }

        private void RunComparison() { StartComparison(); }

        private MapfPlannerSettings CurrentSettings() => new MapfPlannerSettings
        {
            MaxTimeSteps = maxTimeSteps,
            MaxPlanningMilliseconds = maxPlanningMilliseconds,
            MaxExpandedNodesPerAgent = maxExpandedNodes,
            MaxHighLevelNodes = maxCbsNodes,
            PriorityMode = priorityMode,
            CbsHeuristic = cbsHeuristic,
            PythonExecutable = pythonExecutable,
            PrimalScriptPath = primalScriptPath,
            Profile = ProfileForRun()
        };

        private SearchProfile ProfileForRun(){var profile=searchProfile.Copy();profile.Seed=randomSeed;return profile;}

        private void RunSeedSweep() { showCampaign = true; }

        private void StepOnce()
        {
            if (simulation == null || IsBusy) return;
            if (simulation.IsTerminal || (reviewingTimeline && timelineStep < simulation.Step))
            { reviewingTimeline = true; viewSearchReplay = false; isPlaying = false; timelineStep = Mathf.Min(simulation.Step, timelineStep + 1); playbackAccumulator = 0; return; }
            viewSearchReplay = false; reviewingTimeline = false; isPlaying = false;
            if (simulation.PrepareStep()) { simulation.CommitStep(); timelineStep = simulation.Step; MirrorSimulationEvents(); }
            else { OnSimulationStopped(); if (autoRecovery && !simulation.IsTerminal) { isPlaying = true; StartRecovery(); } }
            playbackAccumulator = 0;
        }

        private IReadOnlyList<AgentPath> GetDisplayedPaths()
        {
            if (lastRun == null) return Array.Empty<AgentPath>();
            if (viewSearchReplay && lastRun.CbsReplayFrames.Count > 0)
                return lastRun.CbsReplayFrames[Mathf.Clamp(cbsFrameIndex, 0, lastRun.CbsReplayFrames.Count - 1)].Paths;
            if (simulation != null && reviewingTimeline && !viewSearchReplay)
            {
                if(executedReplay==null||executedReplayStep!=simulation.Step){executedReplay=simulation.ExecutedPaths();executedReplayStep=simulation.Step;}
                return executedReplay;
            }
            return simulation != null ? simulation.Paths : lastRun.Paths;
        }

        private GridPosition GetFollowPosition()
        {
            if (agents.Count == 0) return new GridPosition(map.Width / 2, map.Height / 2);
            AgentDefinition agent = agents[Mathf.Clamp(selectedAgent, 0, agents.Count - 1)];
            AgentPath path = FindDisplayedPath(agent.Id);
            if (path == null || path.IsEmpty) return agent.Start;
            Vector2 position = AgentDisplayPosition(agent);
            return new GridPosition(Mathf.RoundToInt(position.x), Mathf.RoundToInt(position.y));
        }

        private AgentPath FindDisplayedPath(int agentId)
        {
            IReadOnlyList<AgentPath> paths = GetDisplayedPaths();
            for (int i = 0; i < paths.Count; i++)
            {
                AgentPath path = paths[i];
                if (path != null && path.AgentId == agentId) return path;
            }
            return null;
        }

        private static Vector2 InterpolatedPosition(AgentPath path, float time)
        {
            if (path == null || path.IsEmpty) return Vector2.zero;
            float boundedTime = Mathf.Clamp(time, 0f, path.MoveCost);
            int step = Mathf.FloorToInt(boundedTime);
            float fraction = boundedTime - step;
            GridPosition from = path.PositionAt(step);
            GridPosition to = path.PositionAt(Mathf.Min(step + 1, path.MoveCost));
            return Vector2.Lerp(new Vector2(from.X, from.Y), new Vector2(to.X, to.Y), fraction);
        }

        private static string DescribeMove(GridPosition from, GridPosition to)
        {
            int dx = to.X - from.X, dy = to.Y - from.Y;
            if (dx > 0) return "오른쪽 칸으로 이동";
            if (dx < 0) return "왼쪽 칸으로 이동";
            if (dy > 0) return "위쪽 칸으로 이동";
            if (dy < 0) return "아래쪽 칸으로 이동";
            return "대기";
        }

        private Color GetCellColor(GridPosition position, int occupant, AgentDefinition start, AgentDefinition goal,
            MapfConflict conflict, IReadOnlyList<AgentPath> paths)
        {
            if (conflict != null) return conflict.Type == ConflictType.Vertex ? ConflictCell : EdgeConflictCell;
            if (occupant >= 0) return AgentColor(occupant);
            if (start != null) return AgentColor(start.Id);
            if (goal != null) return Color.Lerp(AgentColor(goal.Id), Color.white, 0.38f);
            if (showHeatmap && conflictHeat.TryGetValue(position, out int heat)) return Color.Lerp(EmptyCell, ConflictCell, Mathf.Clamp01(heat / 6f));
            if (showPaths && displayedPathCells.Contains(position)) return PathCell;
            return map.IsBlocked(position) ? ObstacleCell : EmptyCell;
        }

        private string GetCellLabel(GridPosition position, int occupant, AgentDefinition start, AgentDefinition goal, MapfConflict conflict)
        {
            if (conflict != null) return conflict.Type == ConflictType.Vertex ? "V!" : "E!";
            if (occupant >= 0) return occupant.ToString();
            if (start != null) return "S" + start.Id;
            if (goal != null) return "G" + goal.Id;
            return map.IsBlocked(position) ? "" : "";
        }

        private AgentDefinition FindEndpoint(GridPosition position, bool start)
        {
            for (int i = 0; i < agents.Count; i++)
                if ((start ? agents[i].Start : agents[i].Goal) == position) return agents[i];
            return null;
        }

        private bool IsPathCell(IReadOnlyList<AgentPath> paths, GridPosition position)
        {
            for (int i = 0; i < paths.Count; i++)
            {
                AgentPath path = paths[i];
                if (path == null || (selectedPathOnly && path.AgentId != agents[selectedAgent].Id)) continue;
                for (int step = 0; step < path.Positions.Count; step++) if (path.Positions[step] == position) return true;
            }
            return false;
        }

        private MapfConflict ConflictAt(GridPosition position, int time)
        {
            if (lastRun == null) return null;
            if (simulation != null && !viewSearchReplay)
            {
                MapfConflict pending = simulation.PendingConflict;
                return pending != null && (pending.FirstPosition == position || pending.SecondPosition == position) ? pending : null;
            }
            if (viewSearchReplay && lastRun.CbsReplayFrames.Count > 0)
            {
                MapfConflict frameConflict = lastRun.CbsReplayFrames[Mathf.Clamp(cbsFrameIndex, 0, lastRun.CbsReplayFrames.Count - 1)].Conflict;
                return frameConflict != null && frameConflict.TimeStep == time &&
                    (frameConflict.FirstPosition == position || frameConflict.SecondPosition == position) ? frameConflict : null;
            }
            for (int i = 0; i < lastRun.Conflicts.Count; i++)
            {
                MapfConflict conflict = lastRun.Conflicts[i];
                if (conflict.TimeStep == time && (conflict.FirstPosition == position || conflict.SecondPosition == position)) return conflict;
            }
            return null;
        }

        private readonly HashSet<GridPosition> displayedPathCells = new HashSet<GridPosition>();
        private static Color AgentColor(int id) => AgentColors[(int)((uint)id % AgentColors.Length)];

        private int GetLastTimeStep()
        {
            if (simulation != null && !viewSearchReplay) return simulation.Step;
            int last = 0;
            IReadOnlyList<AgentPath> paths = GetDisplayedPaths();
            for (int i = 0; i < paths.Count; i++) if (paths[i] != null) last = Mathf.Max(last, paths[i].MoveCost);
            return last;
        }

        private void SaveScenario()
        {
            try
            {
                CommitNumberInputs(); RefreshPreviewIfNeeded();
                string directory = DataDirectory();
                string path = Path.Combine(directory, "last-session.json");
                var document = CurrentDocument();
                document.mapTemplate = (int)mapTemplate;
                File.WriteAllText(path, MapfScenarioSerializer.ToJson(map, agents, document, lastRun));
                statusMessage = "실험 설정과 지도를 저장했습니다.";
                Log("저장: " + path);
            }
            catch (Exception exception) { SetError("저장 실패: " + exception.Message); }
        }

        private void LoadScenario() { LoadScenarioFile(Path.Combine(DataDirectory(), "last-session.json")); }

        private bool LoadScenarioFile(string path)
        {
            try
            {
                LoadedScenario loaded = MapfScenarioSerializer.FromJson(File.ReadAllText(path));
                map = loaded.Map;
                RecountBlockedCells();
                agents.Clear();
                agents.AddRange(loaded.Agents);
                ScenarioDocument document = loaded.Settings;
                mapWidth = map.Width; mapHeight = map.Height; randomSeed = document.seed;
                agentPlacementSeed = document.placementSeed;
                agentCount = agents.Count; connectedCellsOpened = 0;
                mapTemplate = (MapTemplate)Mathf.Clamp(document.mapTemplate, 0, 4);
                bottleneckCount=Mathf.Clamp(document.bottleneckCount,1,8);passageWidth=Mathf.Clamp(document.passageWidth,1,8);
                distribution=(AgentDistribution)Mathf.Clamp(document.distribution,0,2);searchProfile=document.profile??new SearchProfile();
                obstacleDensity = float.IsNaN(document.obstacleDensity) ? .12f : Mathf.Clamp(document.obstacleDensity, 0, .45f);
                selectedAlgorithm = (MapfAlgorithmKind)Mathf.Clamp(document.algorithm, 0, 3);
                priorityMode = (AgentPriorityMode)Mathf.Clamp(document.priorityMode, 0, 2);
                cbsHeuristic = (CBSHeuristicMode)Mathf.Clamp(document.cbsHeuristic, 0, 2);
                pythonExecutable = string.IsNullOrWhiteSpace(document.pythonExecutable) ? "python" : document.pythonExecutable;
                primalScriptPath = document.primalScriptPath ?? "";
                maxTimeSteps = Mathf.Clamp(document.maxTimeSteps, 1, 1024);
                maxPlanningMilliseconds = Mathf.Clamp(document.maxPlanningMilliseconds, 10, 600000);
                maxExpandedNodes = Mathf.Clamp(document.maxExpandedNodesPerAgent, 100, 1000000);
                maxCbsNodes = Mathf.Clamp(document.maxHighLevelNodes, 100, 100000);
                selectedAgent = 0;
                RememberPreviewConfiguration();
                lastRun = loaded.Replay; simulation = null;
                comparisons.Clear();
                ResetTimeline();
                if (lastRun != null) { NewWorkCancellation(); runSettings = CurrentSettings(); runSettings.CancellationToken = workCancellation.Token; currentCase = CurrentCase(); BeginLiveRun(lastRun); isPlaying = false; }
                statusMessage = "실험 파일을 불러왔습니다.";
                Log("불러오기: " + path);return true;
            }
            catch (Exception exception) { SetError("불러오기 실패: " + exception.Message);return false; }
        }

        private void ExportCsv()
        {
            try
            {
                string csv;
                if (lastBatch != null) csv = MapfCsvExporter.Export(lastBatch);
                else
                {
                    var rows = comparisons.Count > 0 ? comparisons : lastRun == null ? new List<MapfRunSummary>() : new List<MapfRunSummary> { lastRun };
                    csv = MapfCsvExporter.Export(rows);
                }
                string path = Path.Combine(DataDirectory(), "results.csv");
                File.WriteAllText(path, csv);
                statusMessage = "결과 CSV를 내보냈습니다.";
                Log("CSV 내보내기: " + path);
            }
            catch (Exception exception) { SetError("CSV 내보내기 실패: " + exception.Message); }
        }

        private ScenarioDocument CurrentDocument(MapfRunSummary result=null)
        {
            MapfRunSummary basis=result??lastRun;
            MapfPlannerSettings settings = basis?.SettingsSnapshot ?? (lastRun != null && runSettings != null ? runSettings : CurrentSettings());
            return new ScenarioDocument
            {
                seed = randomSeed, placementSeed = agentPlacementSeed, algorithm = (int)(basis?.Algorithm ?? selectedAlgorithm),
                mapTemplate = (int)mapTemplate, bottleneckCount=bottleneckCount,passageWidth=passageWidth,distribution=(int)distribution,profile=settings.Profile.Copy(),maxTimeSteps = settings.MaxTimeSteps, maxPlanningMilliseconds = settings.MaxPlanningMilliseconds,
                maxExpandedNodesPerAgent = settings.MaxExpandedNodesPerAgent, maxHighLevelNodes = settings.MaxHighLevelNodes,
                priorityMode = (int)settings.PriorityMode, obstacleDensity = obstacleDensity, cbsHeuristic = (int)settings.CbsHeuristic,
                pythonExecutable = settings.PythonExecutable, primalScriptPath = settings.PrimalScriptPath
            };
        }

        private static string DataDirectory()
        {
            string directory = verificationDirectory == null ? Path.Combine(Application.persistentDataPath, "CoMPaSS") : Path.Combine(verificationDirectory,"Data");
            Directory.CreateDirectory(directory);
            return directory;
        }

        private void ResetRun()
        {
            simulation = null;
            executedReplay = null; runSettings = null;
            lastRun = null;
            lastBatch = null;
            comparisons.Clear();
            ResetTimeline();
        }

        private void ResetTimeline()
        {
            timelineStep = 0;
            cbsFrameIndex = 0;
            playbackAccumulator = 0;
            isPlaying = false;
        }

        private void Log(string message)
        {
            eventLog.Insert(0, DateTime.Now.ToString("HH:mm:ss") + "  " + message);
            if (eventLog.Count > 20) eventLog.RemoveAt(eventLog.Count - 1);
        }

        private void SetError(string message)
        {
            statusMessage = message;
            Log("오류: " + message);
        }

        private static string[] PriorityLabels(List<AgentPriorityDecision> decisions)
        {
            var lines = new string[decisions.Count];
            for (int i = 0; i < decisions.Count; i++) lines[i] = $"{decisions[i].Rank}위 Agent {decisions[i].AgentId} ({decisions[i].Score:F0}: 거리 {decisions[i].DistanceComponent}, 혼잡 {decisions[i].CongestionComponent}, 협소 {decisions[i].NarrownessComponent})";
            return lines;
        }

        private void EnsureMonochromeSkin()
        {
            if (monochromeSkin != null) return;
            blackTexture = MakeSolidTexture(Background);
            panelTexture = MakeSolidTexture(Panel);
            controlTexture = MakeSolidTexture(new Color(0.28f, 0.29f, 0.30f));
            hoverTexture = MakeSolidTexture(new Color(0.38f, 0.39f, 0.40f));
            whiteTexture = MakeSolidTexture(new Color(0.88f, 0.88f, 0.86f));
            monochromeSkin = Instantiate(GUI.skin);
            monochromeSkin.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                generatedUiFont = Font.CreateDynamicFontFromOSFont(new[] { "Malgun Gothic", "Segoe UI", "Arial" }, 15);
            }
            catch (Exception) { generatedUiFont = null; }
            if (generatedUiFont != null) monochromeSkin.font = generatedUiFont;

            SetStyle(monochromeSkin.box, panelTexture, controlTexture, hoverTexture, Color.white);
            SetStyle(monochromeSkin.button, controlTexture, hoverTexture, whiteTexture, Color.white, Color.black);
            SetStyle(monochromeSkin.toggle, controlTexture, hoverTexture, whiteTexture, Color.white, Color.black);
            SetStyle(monochromeSkin.textField, blackTexture, panelTexture, controlTexture, Color.white);
            SetStyle(monochromeSkin.textArea, blackTexture, panelTexture, controlTexture, Color.white);
            monochromeSkin.textArea.font = monochromeSkin.font;
            monochromeSkin.textArea.wordWrap = true;
            SetStyle(monochromeSkin.label, null, null, null, new Color(0.94f, 0.94f, 0.94f));
            SetStyle(monochromeSkin.horizontalSlider, blackTexture, controlTexture, hoverTexture, Color.white);
            SetStyle(monochromeSkin.horizontalSliderThumb, controlTexture, hoverTexture, whiteTexture, Color.white, Color.black);
            SetStyle(monochromeSkin.verticalSlider, blackTexture, controlTexture, hoverTexture, Color.white);
            SetStyle(monochromeSkin.verticalSliderThumb, controlTexture, hoverTexture, whiteTexture, Color.white, Color.black);
            SetStyle(monochromeSkin.scrollView, blackTexture, panelTexture, panelTexture, Color.white);
            SetStyle(monochromeSkin.horizontalScrollbar, blackTexture, controlTexture, hoverTexture, Color.white);
            SetStyle(monochromeSkin.horizontalScrollbarThumb, controlTexture, hoverTexture, whiteTexture, Color.white, Color.black);
            SetStyle(monochromeSkin.verticalScrollbar, blackTexture, controlTexture, hoverTexture, Color.white);
            SetStyle(monochromeSkin.verticalScrollbarThumb, controlTexture, hoverTexture, whiteTexture, Color.white, Color.black);
            if (monochromeSkin.font != null)
            {
                GUIStyle[] fontStyles = { monochromeSkin.box, monochromeSkin.button, monochromeSkin.toggle,
                    monochromeSkin.textField, monochromeSkin.label, monochromeSkin.horizontalSlider,
                    monochromeSkin.horizontalSliderThumb, monochromeSkin.verticalSlider,
                    monochromeSkin.verticalSliderThumb, monochromeSkin.scrollView, monochromeSkin.horizontalScrollbar,
                    monochromeSkin.horizontalScrollbarThumb, monochromeSkin.verticalScrollbar,
                    monochromeSkin.verticalScrollbarThumb };
                for (int i = 0; i < fontStyles.Length; i++) if (fontStyles[i] != null) fontStyles[i].font = monochromeSkin.font;
            }
            monochromeSkin.label.fontSize=14;monochromeSkin.button.fontSize=14;monochromeSkin.toggle.fontSize=14;
            monochromeSkin.textField.fontSize=14;monochromeSkin.textArea.fontSize=14;
            monochromeSkin.button.wordWrap=true;
        }

        private void ApplyUiScale()
        {
            if (monochromeSkin == null) return;
            int baseSize = Mathf.RoundToInt(14f * uiScale);
            monochromeSkin.label.fontSize = baseSize;
            monochromeSkin.button.fontSize = baseSize;
            monochromeSkin.toggle.fontSize = baseSize;
            monochromeSkin.textField.fontSize = baseSize;
            monochromeSkin.textArea.fontSize = baseSize;
        }

        private static Texture2D MakeSolidTexture(Color color)
        {
            var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            texture.SetPixel(0, 0, color);
            texture.Apply();
            return texture;
        }

        private static void SetStyle(GUIStyle style, Texture2D normal, Texture2D hover, Texture2D active,
            Color textColor, Color? selectedTextColor = null)
        {
            if (style == null) return;
            style.normal.background = normal;
            style.hover.background = hover;
            style.active.background = active;
            style.focused.background = hover;
            style.onNormal.background = active;
            style.onHover.background = active;
            style.onActive.background = active;
            style.onFocused.background = active;
            style.normal.textColor = textColor;
            style.hover.textColor = textColor;
            style.active.textColor = selectedTextColor ?? textColor;
            style.focused.textColor = textColor;
            style.onNormal.textColor = selectedTextColor ?? textColor;
            style.onHover.textColor = selectedTextColor ?? textColor;
            style.onActive.textColor = selectedTextColor ?? textColor;
            style.onFocused.textColor = selectedTextColor ?? textColor;
            if (style.font == null && GUI.skin.font != null) style.font = GUI.skin.font;
        }

        private void DestroyTexture(Texture2D texture)
        {
            if (texture != null) Destroy(texture);
        }

        private int IntField(string key, int value, int width)
        {
            string controlName = "compass-number-" + key;
            if (!numberEdits.TryGetValue(key, out NumberEdit edit)) { edit = new NumberEdit(); numberEdits[key] = edit; }
            bool focused = GUI.GetNameOfFocusedControl() == controlName;
            bool enter = focused && Event.current.type == EventType.KeyDown && (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter);
            if (!focused && edit.Editing)
            {
                if (int.TryParse(edit.Text, out int committed)) value = committed;
                edit.Editing = false;
            }
            if (!focused && !edit.Editing) edit.Text = value.ToString();
            GUI.SetNextControlName(controlName);
            edit.Text = GUILayout.TextField(edit.Text, GUILayout.Width(width * uiScale));
            if (GUI.GetNameOfFocusedControl() == controlName)
            {
                edit.Editing = true; integerFieldFocused = true;
                if (enter)
                {
                    if (int.TryParse(edit.Text, out int committed)) value = committed;
                    edit.Text = value.ToString(); edit.Editing = false; integerFieldFocused = false;
                    GUI.FocusControl(string.Empty); Event.current.Use();
                }
            }
            return value;
        }
        private sealed class NumberEdit { public string Text = ""; public bool Editing; }
        private readonly Dictionary<string, NumberEdit> numberEdits = new Dictionary<string, NumberEdit>();

        private int CommitNumber(string key, int value)
        {
            if (numberEdits.TryGetValue(key, out NumberEdit edit) && edit.Editing)
            { if (int.TryParse(edit.Text, out int result)) value = result; edit.Editing = false; }
            return value;
        }
        private void CommitNumberInputs()
        {
            mapWidth = Mathf.Clamp(CommitNumber("width", mapWidth), 4, 64);
            mapHeight = Mathf.Clamp(CommitNumber("height", mapHeight), 4, 64);
            agentCount = Mathf.Clamp(CommitNumber("count", agentCount), 1, Mathf.Min(32, mapWidth * mapHeight / 2));
            randomSeed = CommitNumber("mapSeed", randomSeed); agentPlacementSeed = CommitNumber("placementSeed", agentPlacementSeed);
            maxTimeSteps = Mathf.Clamp(CommitNumber("horizon", maxTimeSteps), 1, 1024);
            maxExpandedNodes = Mathf.Clamp(CommitNumber("nodes", maxExpandedNodes), 100, 1000000);
            maxCbsNodes = Mathf.Clamp(CommitNumber("cbsNodes", maxCbsNodes), 100, 100000);
            maxPlanningMilliseconds = Mathf.Clamp(CommitNumber("milliseconds", maxPlanningMilliseconds), 10, 300000);
            campaignRepeats = CommitNumber("campaignRepeats", campaignRepeats);
            bottleneckCount=Mathf.Clamp(CommitNumber("bottlenecks",bottleneckCount),1,Mathf.Min(8,mapWidth-2));
            passageWidth=Mathf.Clamp(CommitNumber("passageWidth",passageWidth),1,Mathf.Min(8,mapHeight));
            recoveryLimit=Mathf.Clamp(CommitNumber("recoveryLimit",recoveryLimit),1,50);
            searchProfile.DistanceWeight = Mathf.Clamp(CommitNumber("distanceWeight", searchProfile.DistanceWeight), 0, 100);
            searchProfile.CongestionWeight = Mathf.Clamp(CommitNumber("congestionWeight", searchProfile.CongestionWeight), 0, 100);
            searchProfile.NarrownessWeight = Mathf.Clamp(CommitNumber("narrowWeight", searchProfile.NarrownessWeight), 0, 100);
            searchProfile.WaitWeight = Mathf.Clamp(CommitNumber("waitWeight", searchProfile.WaitWeight), 0, 100);
            searchProfile.RecoveryWeight = Mathf.Clamp(CommitNumber("recoveryWeight", searchProfile.RecoveryWeight), 0, 100);
            if(Event.current!=null)GUI.FocusControl(""); integerFieldFocused = false;
        }

        private static int PositiveModulo(int value, int modulus) => ((value % modulus) + modulus) % modulus;

        private GUIStyle BoldLabel(int fontSize)
        {
            var style = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(fontSize * uiScale), fontStyle = FontStyle.Bold, wordWrap = true };
            return style;
        }

        private static GUIStyle MutedLabel()
        {
            var style = new GUIStyle(GUI.skin.label) { wordWrap = true };
            style.normal.textColor = MutedText;
            return style;
        }

        private static GUIStyle ErrorLabel()
        {
            var style = new GUIStyle(GUI.skin.label) { wordWrap = true };
            style.normal.textColor = ConflictCell;
            return style;
        }
    }
}
