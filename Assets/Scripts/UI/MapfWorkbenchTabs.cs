using UnityEngine;

namespace CoMPaSS.UI
{
    public sealed partial class MapfWorkbenchController
    {
        private enum WorkbenchTab { Start, Setup, Playback, Results, Help }
        private WorkbenchTab activeWorkbenchTab = WorkbenchTab.Start;
        private Vector2 startTabScroll;
        private Vector2 resultsTabScroll;
        private Vector2 helpTabScroll;

        private static readonly string[] WorkbenchTabLabels =
        {
            "시작 · 프리셋", "실험 설정", "실행 · 리플레이", "결과", "사용법"
        };

        private bool IsCompactWorkbench => Screen.width < 1120;

        private void DrawWorkbenchNavigation()
        {
            if (IsCompactWorkbench)
            {
                DrawTabRow(0, 3);
                DrawTabRow(3, 2);
                return;
            }

            DrawTabRow(0, WorkbenchTabLabels.Length);
        }

        private void DrawTabRow(int start, int count)
        {
            GUILayout.BeginHorizontal(GUILayout.Height(42));
            for (int i = start; i < start + count; i++)
            {
                WorkbenchTab tab = (WorkbenchTab)i;
                Color oldBackground = GUI.backgroundColor;
                Color oldContent = GUI.contentColor;
                GUI.backgroundColor = activeWorkbenchTab == tab
                    ? new Color(0.57f, 0.51f, 0.40f)
                    : new Color(0.27f, 0.28f, 0.29f);
                GUI.contentColor = new Color(0.93f, 0.93f, 0.91f);
                float width = IsCompactWorkbench ? (Screen.width - 24f) / count : 0f;
                bool clicked = width > 0f
                    ? GUILayout.Button(WorkbenchTabLabels[i], GUILayout.Width(width), GUILayout.Height(38))
                    : GUILayout.Button(WorkbenchTabLabels[i], GUILayout.ExpandWidth(true), GUILayout.Height(38));
                if (clicked)
                {
                    if (activeWorkbenchTab == WorkbenchTab.Setup && tab != WorkbenchTab.Setup) CommitNumberInputs();
                    activeWorkbenchTab = tab;
                }
                GUI.backgroundColor = oldBackground;
                GUI.contentColor = oldContent;
            }
            GUILayout.EndHorizontal();
        }

        private void DrawWorkbenchTab()
        {
            switch (activeWorkbenchTab)
            {
                case WorkbenchTab.Start: DrawStartTab(); break;
                case WorkbenchTab.Setup: DrawSetupTab(); break;
                case WorkbenchTab.Playback: DrawPlaybackTab(); break;
                case WorkbenchTab.Results: DrawResultsTab(); break;
                case WorkbenchTab.Help: DrawHelpTab(); break;
            }
        }

        private void DrawStartTab()
        {
            GUILayout.BeginVertical(GUI.skin.box, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            GUILayout.Space(10);
            GUILayout.Label("MAPF 실험을 바로 시작하세요", BoldLabel(26));
            GUILayout.Label("예제를 고르면 지도와 로봇이 준비됩니다. 설정을 확인하고 실행한 뒤, 이동 과정과 결과를 살펴보세요.", MutedLabel());
            GUILayout.Space(14);
            startTabScroll = GUILayout.BeginScrollView(startTabScroll, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            GUILayout.Label("먼저 살펴볼 예제", BoldLabel(18));

            if (IsCompactWorkbench)
            {
                DrawPresetCard(0, "충돌을 어떻게 피할까?", "두 로봇이 마주치는 상황에서 멈추고 우회하는 과정을 확인합니다.", "2대 · 열린 지도 · Independent A*");
                DrawPresetCard(1, "여러 로봇을 함께 이동하기", "장애물이 있는 지도에서 여러 로봇의 경로 예약을 살펴봅니다.", "8대 · 무작위 장애물 · Cooperative A*");
                DrawPresetCard(2, "CBS는 어떻게 충돌을 해결할까?", "충돌을 찾고 제약을 추가해 다시 탐색하는 과정을 확인합니다.", "2대 · 교차 경로 · CBS");
            }
            else
            {
                GUILayout.BeginHorizontal();
                DrawPresetCard(0, "충돌을 어떻게 피할까?", "두 로봇이 마주치는 상황에서 멈추고 우회하는 과정을 확인합니다.", "2대 · 열린 지도 · Independent A*");
                DrawPresetCard(1, "여러 로봇을 함께 이동하기", "장애물이 있는 지도에서 여러 로봇의 경로 예약을 살펴봅니다.", "8대 · 무작위 장애물 · Cooperative A*");
                DrawPresetCard(2, "CBS는 어떻게 충돌을 해결할까?", "충돌을 찾고 제약을 추가해 다시 탐색하는 과정을 확인합니다.", "2대 · 교차 경로 · CBS");
                GUILayout.EndHorizontal();
            }

            GUILayout.Space(12);
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label("처음 사용한다면", BoldLabel(17));
            GUILayout.Label("1  예제를 선택하세요     2  실험 설정에서 지도를 확인하세요     3  실행하고 결과를 살펴보세요", BoldLabel(14));
            GUILayout.Label("무작위 실험이 필요하면 실험 설정에서 ‘새 랜덤 지도 + 배치’를 누르세요.", MutedLabel());
            GUILayout.EndVertical();
            GUILayout.EndScrollView();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("무작위 지도와 로봇으로 시작", GUILayout.Height(42)))
            {
                if (!LockScenario) { RandomizeScenario(); activeWorkbenchTab = WorkbenchTab.Setup; }
            }
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("현재 예제 설정 열기  →", GUILayout.Height(42), GUILayout.Width(250)))
                activeWorkbenchTab = WorkbenchTab.Setup;
            GUILayout.EndHorizontal();
            GUILayout.Space(8);
            GUILayout.EndVertical();
        }

        private void DrawPresetCard(int kind, string title, string description, string facts)
        {
            GUILayout.BeginVertical(GUI.skin.box, GUILayout.ExpandWidth(true), GUILayout.MinHeight(190));
            GUILayout.Label("예제 " + (kind + 1), MutedLabel());
            GUILayout.Label(title, BoldLabel(17));
            GUILayout.Label(description, MutedLabel());
            GUILayout.FlexibleSpace();
            GUILayout.Label(facts, BoldLabel(12));
            bool enabled = GUI.enabled;
            GUI.enabled = enabled && !LockScenario;
            if (GUILayout.Button("이 예제 불러오기", GUILayout.Height(36)))
            {
                UseGuidedExample(kind);
                activeWorkbenchTab = WorkbenchTab.Setup;
            }
            GUI.enabled = enabled;
            GUILayout.EndVertical();
        }

        private void DrawSetupTab()
        {
            if (IsCompactWorkbench)
            {
                GUILayout.BeginVertical(GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
                DrawSidebar(true);
                DrawSetupPreview(true);
                GUILayout.EndVertical();
                return;
            }

            GUILayout.BeginHorizontal(GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            DrawSidebar();
            DrawSetupPreview(false);
            GUILayout.EndHorizontal();
        }

        private void DrawSetupPreview(bool compact)
        {
            GUILayout.BeginVertical(GUI.skin.box, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            GUILayout.BeginHorizontal();
            GUILayout.Label("지도 미리보기", BoldLabel(19));
            GUILayout.FlexibleSpace();
            GUILayout.Label($"{map.Width} × {map.Height}   ·   로봇 {agents.Count}대", MutedLabel());
            GUILayout.EndHorizontal();
            GUILayout.Label("지도를 눌러 장애물·시작점·목표점을 편집하세요. 변경 내용은 바로 반영됩니다.", MutedLabel());
            GUILayout.BeginHorizontal();
            editTool = (EditTool)GUILayout.SelectionGrid((int)editTool, new[] { "장애물", "시작점", "목표점" }, 3);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            showPaths = GUILayout.Toggle(showPaths, "경로 표시");
            showConflicts = GUILayout.Toggle(showConflicts, "충돌 표시");
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("전체 지도", GUILayout.Width(100))) { mapZoom = 1f; cameraPan = Vector2.zero; followSelectedAgent = false; zoomToConflict = false; }
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal(GUILayout.ExpandHeight(true));
            DrawGrid();
            if (!compact && Screen.width >= 1500) DrawLiveInspector();
            GUILayout.EndHorizontal();
            if (compact || Screen.width < 1500)
                GUILayout.Label($"{MapTemplateName(mapTemplate)} · 장애물 {blockedCellCount}칸 · 자유 칸 {map.Width * map.Height - blockedCellCount}칸 · 지도 시드 {randomSeed} · 배치 시드 {agentPlacementSeed}", MutedLabel());
            GUILayout.Label(statusMessage, MutedLabel());
            GUILayout.EndVertical();
        }

        private void DrawPlaybackTab()
        {
            GUILayout.BeginVertical(GUI.skin.box, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            DrawOperationControls();
            if (lastRun != null) DrawPlaybackBar();
            else
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("실행 전 미리보기", BoldLabel(18));
                GUILayout.Label("실험 설정에서 조건을 바꾸거나 지금 바로 시작할 수 있습니다.", MutedLabel());
                GUILayout.FlexibleSpace();
                bool enabled = GUI.enabled;
                GUI.enabled = enabled && !LockScenario && (selectedAlgorithm != MapfAlgorithmKind.PrimalExternal || primalReady);
                if (GUILayout.Button("▶ 지금 실행", GUILayout.Width(150), GUILayout.Height(34))) RunSelected();
                GUI.enabled = enabled;
                GUILayout.EndHorizontal();
            }

            if (lastRun != null) DrawResearchToolbar();
            else DrawGuidedToolbar();
            GUILayout.BeginHorizontal();
            showPaths = GUILayout.Toggle(showPaths, "경로 표시");
            selectedPathOnly = GUILayout.Toggle(selectedPathOnly, "선택 로봇만");
            showConflicts = GUILayout.Toggle(showConflicts, "충돌 표시");
            zoomToConflict = GUILayout.Toggle(zoomToConflict, "충돌 지점 확대");
            GUILayout.FlexibleSpace();
            GUILayout.Label($"{map.Width} × {map.Height}   ·   로봇 {agents.Count}대", MutedLabel());
            GUILayout.EndHorizontal();

            if (IsCompactWorkbench)
            {
                GUILayout.BeginVertical(GUILayout.ExpandHeight(true));
                DrawGrid();
                DrawLiveInspector(true);
                GUILayout.EndVertical();
            }
            else
            {
                GUILayout.BeginHorizontal(GUILayout.ExpandHeight(true));
                DrawGrid();
                DrawLiveInspector();
                GUILayout.EndHorizontal();
            }
            GUILayout.BeginHorizontal();
            GUILayout.Label(statusMessage, MutedLabel());
            GUILayout.FlexibleSpace();
            if (lastRun != null && GUILayout.Button("결과 보기  →", GUILayout.Width(150))) activeWorkbenchTab = WorkbenchTab.Results;
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
        }

        private void DrawResultsTab()
        {
            resultsTabScroll = GUILayout.BeginScrollView(resultsTabScroll, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            GUILayout.BeginVertical(GUILayout.ExpandWidth(true));
            GUILayout.Label("실험 결과", BoldLabel(23));
            GUILayout.Label("실행 요약과 충돌·복구 기록을 확인하고, 보고서와 이미지를 저장하세요.", MutedLabel());
            bool hasRunSummary = lastRun != null || lastBatch != null || comparisons.Count > 0;
            if (!hasRunSummary)
            {
                GUILayout.BeginVertical(GUI.skin.box);
                GUILayout.Label("아직 결과가 없습니다", BoldLabel(18));
                GUILayout.Label("시작 탭에서 예제를 고르거나 실험 설정에서 알고리즘을 실행하면 결과가 여기에 쌓입니다.", MutedLabel());
                if (GUILayout.Button("예제 선택으로 이동", GUILayout.Width(220), GUILayout.Height(36))) activeWorkbenchTab = WorkbenchTab.Start;
                GUILayout.EndVertical();
            }
            else DrawResults();
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label("연속 실험 진행 상황", BoldLabel(17));
            DrawCampaignProgress();
            GUILayout.EndVertical();
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label("이미지와 보고서 저장", BoldLabel(17));
            DrawReportControls();
            GUILayout.EndVertical();
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label("시나리오 파일", BoldLabel(17));
            DrawPersistenceControls();
            if (GUILayout.Button("결과 CSV 내보내기", GUILayout.Width(220))) ExportCsv();
            GUILayout.EndVertical();
            GUILayout.EndVertical();
            GUILayout.EndScrollView();
        }

        private void DrawHelpTab()
        {
            helpTabScroll = GUILayout.BeginScrollView(helpTabScroll, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            GUILayout.BeginVertical(GUI.skin.box, GUILayout.ExpandWidth(true));
            GUILayout.Label("사용법", BoldLabel(24));
            GUILayout.Label("처음 사용하는 분은 이 순서대로 진행하면 됩니다.", MutedLabel());
            HelpSection("01 · 예제 선택", "시작·프리셋 탭에서 보고 싶은 상황을 고릅니다. 무작위 실험은 실험 설정의 ‘새 랜덤 지도 + 배치’를 사용하세요.");
            HelpSection("02 · 지도와 로봇 설정", "가로·세로 크기, 지도 유형, 장애물 밀도, 시드와 로봇 수를 정합니다. 설정 오른쪽 지도에서 칸을 눌러 장애물과 시작·목표 위치를 편집할 수 있습니다.");
            HelpSection("03 · 알고리즘 실행", "알고리즘을 고른 뒤 ‘선택 알고리즘 실행’을 누릅니다. 실행·리플레이에서 재생, 한 단계 이동, 속도, 선택 로봇 추적을 조작합니다.");
            HelpSection("04 · 결과 저장", "결과 탭에서 요약과 충돌·CBS 시행착오 기록을 확인하고 PNG, SVG, CSV, JSON, HTML 보고서를 저장합니다. SVG는 확대해도 선명합니다.");
            HelpSection("알고리즘 간단 설명", "Independent A*는 각 로봇이 따로 경로를 찾습니다. Cooperative A*는 앞서 계획된 로봇의 위치를 예약합니다. CBS는 충돌을 찾아 제약을 추가하고 경로를 다시 탐색합니다. PRIMAL은 외부 공개 모델 연결이 필요합니다.");
            HelpSection("지도 조작과 범례", "마우스 휠로 확대·축소하고 우클릭 드래그로 이동합니다. 실행 탭의 ‘선택 로봇 따라가기’를 켜면 해당 로봇과 판단을 따라갑니다. V는 같은 칸 충돌, E는 서로 지나치는 간선 충돌입니다.");
            GUILayout.EndVertical();
            GUILayout.EndScrollView();
        }

        private void HelpSection(string title, string body)
        {
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label(title, BoldLabel(16));
            GUILayout.Label(body, MutedLabel());
            GUILayout.EndVertical();
        }
    }
}
