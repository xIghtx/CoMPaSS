using System;
using CoMPaSS.Core;
using CoMPaSS.Experiment;
using UnityEngine;
using UnityEngine.Rendering;

namespace CoMPaSS.UI
{
    public sealed partial class MapfWorkbenchController
    {
        private bool guidedMode=true, guidedDetails, editBasicMap;
        private static RenderPipelineAsset previousQualityPipeline, previousDefaultPipeline;
        private static bool renderingOverride;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSplashScreen)]
        private static void ConfigureWorkbenchRendering()
        {
            // The workbench draws its entire view with IMGUI. It requires no URP lights,
            // shadows or compute buffers. Restore the editor's pipeline when leaving Play.
            if(renderingOverride)return;
            previousQualityPipeline=QualitySettings.renderPipeline;
            previousDefaultPipeline=GraphicsSettings.defaultRenderPipeline;
            renderingOverride=true;
            QualitySettings.renderPipeline=null;
            GraphicsSettings.defaultRenderPipeline=null;
        }
        private static void RestoreWorkbenchRendering()
        {
            if(!renderingOverride)return;
#if UNITY_EDITOR
            QualitySettings.renderPipeline=previousQualityPipeline;
            GraphicsSettings.defaultRenderPipeline=previousDefaultPipeline;
#endif
            renderingOverride=false;
        }
        private void DrawGuidedSidebar()
        {
            GUILayout.BeginVertical(GUI.skin.box,GUILayout.Width(326),GUILayout.ExpandHeight(true));
            DrawOperationControls();
            sidebarScroll=GUILayout.BeginScrollView(sidebarScroll,GUILayout.ExpandHeight(true));
            bool enabled=GUI.enabled;GUI.enabled=!LockScenario;
            GUILayout.Label("1  무엇을 살펴볼까요?",BoldLabel(18));
            GUILayout.Label("처음이라면 첫 번째 예제로 시작하세요.",MutedLabel());
            if(GUILayout.Button("충돌을 어떻게 피할까?\n두 로봇의 정지 → 우회 → 도착",GUILayout.Height(56))) UseGuidedExample(0);
            if(GUILayout.Button("여러 로봇을 함께 이동하기\n8명 · 장애물 지도",GUILayout.Height(56))) UseGuidedExample(1);
            if(GUILayout.Button("CBS는 어떤 판단을 할까?\n충돌 → 제약 추가 → 다시 탐색",GUILayout.Height(56))) UseGuidedExample(2);
            GUILayout.Space(10);
            editBasicMap=GUILayout.Toggle(editBasicMap,"내 지도와 인원으로 바꾸기");
            if(editBasicMap)
            {
                GUILayout.BeginHorizontal();GUILayout.Label("가로");mapWidth=IntField("width",mapWidth,45);GUILayout.Label("세로");mapHeight=IntField("height",mapHeight,45);GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();GUILayout.Label("로봇 수 (최대 32명)");agentCount=IntField("count",agentCount,45);GUILayout.EndHorizontal();
                mapTemplate=(MapTemplate)GUILayout.SelectionGrid((int)mapTemplate,new[]{"빈 지도","랜덤 장애물","교차로","병목","좁은 통로"},2);
                GUILayout.Label($"장애물 {obstacleDensity:P0}",MutedLabel());obstacleDensity=GUILayout.HorizontalSlider(obstacleDensity,0,.45f);
                GUILayout.BeginHorizontal();if(GUILayout.Button("새 지도 만들기"))RandomizeScenario();if(GUILayout.Button("위치만 섞기"))RandomizeAgentPositions();GUILayout.EndHorizontal();
            }
            GUILayout.Space(10);GUILayout.Label("2  이동 방법 선택",BoldLabel(18));
            selectedAlgorithm=(MapfAlgorithmKind)GUILayout.SelectionGrid((int)selectedAlgorithm,new[]{"각자 최단 길","순서 예약","충돌 해결","학습 모델"},2,GUILayout.Width(292));
            string[] explanations={"독립 A*: 각자 가장 짧은 길을 찾습니다. 충돌이 예상되면 함께 멈추고 우회 경로를 찾습니다.","Cooperative A*: 다른 로봇의 예정 위치를 예약해 충돌을 피합니다. 처음 사용할 때 권장합니다.","CBS: 충돌 원인을 찾고 새로운 제약을 추가합니다. 실행 후 ‘CBS 탐색 과정’으로 판단을 볼 수 있습니다.","PRIMAL: 공개 학습 모델이 주변을 관찰해 행동을 고릅니다. 아래 연결 상태를 확인하세요."};
            GUILayout.Label(explanations[(int)selectedAlgorithm],MutedLabel());
            GUI.enabled=enabled;
            if(selectedAlgorithm==MapfAlgorithmKind.PrimalExternal)DrawPrimalConnection();
            GUILayout.Space(10);GUILayout.Label("3  결과를 남기세요",BoldLabel(18));
            GUILayout.Label("그림은 PNG, 확대용 그림은 SVG로 함께 저장됩니다.",MutedLabel());
            GUILayout.BeginHorizontal();if(GUILayout.Button("지도 그림 저장"))ReportAction(()=>ExportMap(ReportDirectory()));if(GUILayout.Button("저장 폴더"))Application.OpenURL(new Uri(ReportDirectory()).AbsoluteUri);GUILayout.EndHorizontal();
            GUI.enabled=lastRun!=null&&!IsBusy;
            if(GUILayout.Button("보고서 한 번에 저장",GUILayout.Height(32)))ReportAction(ExportReportBundle);
            GUI.enabled=enabled;
            if(GUILayout.Button("이미지·로그 저장 옵션"))showReports=!showReports;
            if(showReports)DrawReportControls();
            GUILayout.Space(10);
            if(GUILayout.Button("연속 실험·세부 설정 열기")){guidedMode=false;showCampaign=true;sidebarScroll=Vector2.zero;}
            GUILayout.EndScrollView();
            GUI.enabled=!LockScenario&&(selectedAlgorithm!=MapfAlgorithmKind.PrimalExternal||primalReady);
            if(GUILayout.Button("▶  시뮬레이션 시작",GUILayout.Height(44)))RunSelected();
            GUI.enabled=enabled;
            GUILayout.EndVertical();
        }
        private void UseGuidedExample(int kind)
        {
            if(LockScenario)return;
            ResetRun();numberEdits.Clear();selectedAgent=0;selectedPathOnly=false;followSelectedAgent=false;mapZoom=1;cameraPan=Vector2.zero;
            randomSeed=23;agentPlacementSeed=42;autoRecovery=true;allowCbsFallback=true;pauseOnEvent=true;autoPlayAfterPlan=true;replaySpeed=2;
            maxPlanningMilliseconds=15000;maxTimeSteps=128;maxCbsNodes=5000;maxExpandedNodes=50000;
            mapWidth=kind==1?12:7;mapHeight=kind==1?12:7;obstacleDensity=kind==1?.15f:0;mapTemplate=kind==1?MapTemplate.RandomObstacles:MapTemplate.Open;
            selectedAlgorithm=kind==0?MapfAlgorithmKind.IndependentAStar:kind==1?MapfAlgorithmKind.CooperativeAStar:MapfAlgorithmKind.ConflictBasedSearch;
            agents.Clear();map=ProceduralMapGenerator.Create(mapWidth,mapHeight,mapTemplate,obstacleDensity,randomSeed);
            if(kind==1)agents.AddRange(ProceduralMapGenerator.CreateRandomAgents(map,8,agentPlacementSeed,out connectedCellsOpened));
            else {agents.Add(new AgentDefinition(0,new GridPosition(1,3),new GridPosition(5,3)));agents.Add(new AgentDefinition(1,new GridPosition(5,3),new GridPosition(1,3)));connectedCellsOpened=0;}
            agentCount=agents.Count;RememberPreviewConfiguration();RecountBlockedCells();guidedDetails=false;
            statusMessage="예제 준비 완료 · 실험 설정에서 지도를 확인하고 실행하세요.";
        }
        private string FriendlyState()
        {
            if(IsBusy)return simulation?.State==SimulationState.Replanning?"우회 경로를 찾는 중":"경로를 계산하는 중";
            if(simulation==null)return "실행 전 미리보기";
            switch(simulation.State)
            {
                case SimulationState.Completed:return "모두 도착했습니다";
                case SimulationState.Failed:return "이 조건에서는 이동을 완료하지 못했습니다";
                case SimulationState.Cancelled:return "실행을 중지했습니다";
                case SimulationState.PausedForConflict:return "충돌 직전 정지 · 다른 길을 찾습니다";
                case SimulationState.Replanning:return "우회 경로를 찾는 중";
                default:return isPlaying?"목표로 이동하는 중":"재생을 기다리는 중";
            }
        }
        private void DrawGuidedToolbar()
        {
            GUILayout.BeginHorizontal();
            if(GUILayout.Button("전체 지도",GUILayout.Width(88))){mapZoom=1;cameraPan=Vector2.zero;followSelectedAgent=false;zoomToConflict=false;}
            followSelectedAgent=GUILayout.Toggle(followSelectedAgent,"선택 로봇 따라가기");
            GUILayout.FlexibleSpace();GUILayout.Label("로봇 클릭: 선택  ·  휠: 확대  ·  우클릭 드래그: 이동",MutedLabel());GUILayout.EndHorizontal();
        }
        private void DrawGuidedResults()
        {
            GUILayout.BeginVertical(GUI.skin.box,GUILayout.Height(145));
            GUILayout.BeginHorizontal();GUILayout.Label(FriendlyState(),BoldLabel(17));GUILayout.FlexibleSpace();if(GUILayout.Button("수치·판단 로그 자세히",GUILayout.Width(180)))guidedDetails=true;GUILayout.EndHorizontal();
            if(simulation!=null)
            {
                GUILayout.Label($"도착 {simulation.ArrivedCount} / {agents.Count}명     이동 단계 {simulation.Step}     경로 수정 {simulation.RecoveryCount}회",BoldLabel(15));
                GUILayout.Label($"처음 계획: {(lastRun.CollisionFree?"충돌 없는 경로 확보":"충돌 또는 미계획 경로 있음")}  ·  실제 이동은 매 단계 안전 검사 후 진행합니다.",MutedLabel());
                if(!string.IsNullOrEmpty(simulation.FailureReason))GUILayout.Label(simulation.FailureReason,ErrorLabel());
                else GUILayout.Label(simulation.IsTerminal?"왼쪽 ‘보고서 한 번에 저장’으로 그림과 실험 기록을 보관하세요.":"충돌 시 전원이 잠시 멈춥니다. 새 경로가 준비되면 자동으로 다시 이동합니다.",MutedLabel());
            }
            else GUILayout.Label("① 왼쪽에서 예제 선택   ② 시뮬레이션 시작   ③ 도착·충돌 과정 확인\n네모 안 숫자는 로봇 ID입니다. 목표에 도착한 로봇은 그 자리를 계속 차지합니다.",MutedLabel());
            GUILayout.EndVertical();
        }
    }
}
