using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CoMPaSS.Algorithms;
using CoMPaSS.Core;
using CoMPaSS.Experiment;
using CoMPaSS.Persistence;
using UnityEngine;

namespace CoMPaSS.UI
{
    public sealed partial class MapfWorkbenchController
    {
        private MapfSimulation simulation;
        private Task<PlannedCampaignCase> planningTask;
        private Task<RecoveryResult> recoveryTask;
        private Task<List<MapfRunSummary>> comparisonTask;
        private CancellationTokenSource workCancellation;
        private MapfPlannerSettings runSettings;
        private CampaignCase currentCase;
        private bool autoRecovery = true, allowCbsFallback, pauseOnEvent = true;
        private bool viewSearchReplay, reviewingTimeline;
        private float recoveryReadyAt;
        private int mirroredEventCount;
        private bool campaignRunning, campaignPaused;
        private CampaignManifest campaign;
        private string campaignDirectory;
        private string campaignSizes = "12x12,20x20,32x32";
        private string campaignCounts = "4,8,16,32";
        private string campaignDensities = "0.1,0.2,0.3";
        private int campaignRepeats = 3;
        private bool showCampaign;
        private List<AgentPath> executedReplay;
        private int executedReplayStep=-1;
        private bool terminalReported;
        private bool IsBusy => planningTask != null || recoveryTask != null || comparisonTask != null;
        private bool LockScenario => IsBusy || campaignRunning || (simulation != null && !simulation.IsTerminal);

        private void StartSinglePlan()
        {
            if (IsBusy || campaignRunning) return;
            CommitNumberInputs(); RefreshPreviewIfNeeded();
            NewWorkCancellation();
            isPlaying = false; simulation = null; comparisons.Clear(); lastBatch = null; lastRun = null;
            runSettings = CurrentSettings(); runSettings.CancellationToken = workCancellation.Token;
            currentCase = CurrentCase();
            GridMap snapshot = map.Copy();
            var definition = new List<AgentDefinition>(agents);
            CampaignCase caseSnapshot = currentCase; int openedSnapshot = connectedCellsOpened;
            MapfAlgorithmKind algorithm = selectedAlgorithm;
            MapfPlannerSettings settings = runSettings.Copy();
            planningTask = Task.Run(() => new PlannedCampaignCase { Map = snapshot, Agents = definition, Case = caseSnapshot,
                OpenedCells = openedSnapshot, Summary = new MapfExperimentRunner().Run(snapshot, definition, algorithm, settings) }, settings.CancellationToken);
            statusMessage = "경로 계산 중 · 중지할 수 있습니다.";
        }

        private void StartComparison()
        {
            if (IsBusy || campaignRunning) return;
            CommitNumberInputs(); RefreshPreviewIfNeeded();
            NewWorkCancellation();
            isPlaying = false; simulation = null; lastBatch = null; lastRun = null; comparisons.Clear();
            GridMap snapshot = map.Copy(); var definition = new List<AgentDefinition>(agents);
            runSettings = CurrentSettings(); runSettings.CancellationToken = workCancellation.Token;
            var settings = runSettings.Copy();
            comparisonTask = Task.Run(() =>
            {
                var results = new List<MapfRunSummary>();
                foreach (MapfAlgorithmKind algorithm in new[] { MapfAlgorithmKind.IndependentAStar, MapfAlgorithmKind.CooperativeAStar, MapfAlgorithmKind.ConflictBasedSearch })
                {
                    settings.CancellationToken.ThrowIfCancellationRequested();
                    results.Add(new MapfExperimentRunner().Run(snapshot, definition, algorithm, settings));
                }
                return results;
            }, settings.CancellationToken);
            statusMessage = "동일 지도·배치로 세 알고리즘 계산 중";
        }

        private void NewWorkCancellation()
        {
            workCancellation?.Dispose();
            workCancellation = new CancellationTokenSource();
        }

        private CampaignCase CurrentCase() => new CampaignCase { width = map.Width, height = map.Height,
            agentCount = agents.Count, mapSeed = randomSeed, placementSeed = agentPlacementSeed,
            template = mapTemplate, requestedDensity = obstacleDensity,bottleneckCount=bottleneckCount,passageWidth=passageWidth,distribution=distribution };

        private void BeginLiveRun(MapfRunSummary summary)
        {
            if(workCancellation==null||workCancellation.IsCancellationRequested)NewWorkCancellation();
            if(summary.SettingsSnapshot!=null)runSettings=summary.SettingsSnapshot.Copy();
            if(runSettings==null)runSettings=CurrentSettings();
            runSettings.CancellationToken=workCancellation.Token;
            lastRun = summary;
            ResetTimeline(); viewSearchReplay = false; reviewingTimeline = false;
            simulation = new MapfSimulation(map, agents, summary){MaxRecoveries=recoveryLimit};
            executedReplay = null; executedReplayStep=-1;terminalReported=false;reportEventIndex = -1;
            mirroredEventCount = 0;
            isPlaying = !campaignPaused && (campaignRunning || autoPlayAfterPlan);
            statusMessage = $"전체 {agents.Count}명 · 경로 {summary.RoutedAgents}명 · 미계획 {agents.Count - summary.RoutedAgents}명";
            MirrorSimulationEvents();
        }

        private void UpdateExecution()
        {
            PollJobs();
            if (simulation == null) return;
            if (viewSearchReplay)
            {
                if (playSearch && lastRun.CbsReplayFrames.Count > 0)
                {
                    searchAccumulator += Time.unscaledDeltaTime * replaySpeed;
                    if (searchAccumulator >= 1)
                    {
                        int increment = Mathf.FloorToInt(searchAccumulator); searchAccumulator -= increment;
                        SelectCbsFrame(cbsFrameIndex + increment);
                        if (cbsFrameIndex >= lastRun.CbsReplayFrames.Count - 1) playSearch = false;
                    }
                }
                return;
            }
            if (simulation.IsTerminal)
            {
                MirrorSimulationEvents();
                bool firstTerminalFrame = !terminalReported;terminalReported=true;
                if (executedReplay == null || executedReplayStep!=simulation.Step) {executedReplay = simulation.ExecutedPaths();executedReplayStep=simulation.Step;}
                if (reviewingTimeline && isPlaying && !viewSearchReplay)
                {
                    playbackAccumulator += Time.unscaledDeltaTime * replaySpeed;
                    int ticks = Mathf.FloorToInt(playbackAccumulator);
                    timelineStep = Mathf.Min(simulation.Step, timelineStep + ticks); playbackAccumulator -= ticks;
                    if (timelineStep >= simulation.Step) { isPlaying = false; playbackAccumulator = 0; }
                    return;
                }
                isPlaying = false;
                if (firstTerminalFrame && !campaignRunning) statusMessage = simulation.State == SimulationState.Completed
                    ? $"전체 {agents.Count}명 도착 · 목표 칸 점유 유지 · 복구 {simulation.RecoveryCount}회"
                    : simulation.FailureReason ?? "실행이 중지되었습니다.";
                if (campaignRunning && !campaignPaused) FinishCampaignCase();
                return;
            }
            if (viewSearchReplay || !isPlaying || campaignPaused) return;
            if (simulation.State == SimulationState.PausedForConflict)
            {
                if (Time.unscaledTime >= recoveryReadyAt && !IsBusy) StartRecovery();
                return;
            }
            if (IsBusy) return;
            if (reviewingTimeline) { reviewingTimeline = false; timelineStep = simulation.Step; playbackAccumulator = 0f; }
            playbackAccumulator += Time.unscaledDeltaTime * replaySpeed;
            int budget = 512;
            // Prepare before interpolation as well as before integer ticks.
            while (budget-- > 0)
            {
                if (!simulation.PrepareStep()) { OnSimulationStopped(); break; }
                if (playbackAccumulator < 1f) break;
                playbackAccumulator -= 1f;
                simulation.CommitStep(); timelineStep = simulation.Step;
                MirrorSimulationEvents();
                if (simulation.IsTerminal) { playbackAccumulator = 0; break; }
            }
        }

        private void OnSimulationStopped()
        {
            playbackAccumulator = 0; timelineStep = simulation.Step;
            MirrorSimulationEvents();
            if (simulation.State == SimulationState.PausedForConflict)
            {
                statusMessage = "충돌/미계획 경로 감지 · 모든 에이전트 이동 정지";
                recoveryReadyAt = Time.unscaledTime + (pauseOnEvent ? 0.8f : 0f);
                if (!autoRecovery) simulation.Fail("자동 복구가 꺼져 있어 안전하게 정지했습니다.");
            }
        }

        private void StartRecovery()
        {
            RecoveryRequest request = simulation.BeginRecovery(runSettings, allowCbsFallback);
            MirrorSimulationEvents();
            if (request == null) return;
            statusMessage = "대안 경로 계산 중 · 도착 칸은 계속 점유됩니다.";
            recoveryTask = Task.Run(() => MapfRecoveryPlanner.Plan(request), request.Settings.CancellationToken);
        }

        private void PollJobs()
        {
            try
            {
                if (planningTask != null && planningTask.IsCompleted)
                {
                    Task<PlannedCampaignCase> done = planningTask; planningTask = null;
                    PlannedCampaignCase item = done.GetAwaiter().GetResult();
                    if (workCancellation.IsCancellationRequested) return;
                    map = item.Map; agents.Clear(); agents.AddRange(item.Agents); agentCount = agents.Count; selectedAgent = 0;
                    currentCase = item.Case; mapWidth = map.Width; mapHeight = map.Height;
                    randomSeed = currentCase.mapSeed; agentPlacementSeed = currentCase.placementSeed;
                    mapTemplate = currentCase.template; obstacleDensity = currentCase.requestedDensity;
                    bottleneckCount=currentCase.bottleneckCount;passageWidth=currentCase.passageWidth;distribution=currentCase.distribution;
                    connectedCellsOpened = item.OpenedCells; RecountBlockedCells(); RememberPreviewConfiguration();
                    BeginLiveRun(item.Summary);
                }
                if (recoveryTask != null && recoveryTask.IsCompleted)
                {
                    Task<RecoveryResult> done = recoveryTask; recoveryTask = null;
                    RecoveryResult result = done.GetAwaiter().GetResult();
                    if (workCancellation.IsCancellationRequested) return;
                    simulation.ApplyRecovery(result); MirrorSimulationEvents();
                    statusMessage = result.Succeeded ? "대안 경로 검증 완료 · 자동 재개" : simulation.FailureReason;
                }
                if (comparisonTask != null && comparisonTask.IsCompleted)
                {
                    Task<List<MapfRunSummary>> done = comparisonTask; comparisonTask = null;
                    List<MapfRunSummary> results = done.GetAwaiter().GetResult();
                    if (workCancellation.IsCancellationRequested) return;
                    comparisons.Clear(); comparisons.AddRange(results); currentCase = CurrentCase();
                    BeginLiveRun(comparisons.Find(item => item.Algorithm == selectedAlgorithm) ?? comparisons[0]);
                }
            }
            catch (OperationCanceledException) { statusMessage = "실행을 중지했습니다. 완료된 실험 결과는 보존됩니다."; }
            catch (Exception error)
            {
                simulation?.Fail(error.Message); isPlaying = false;
                if (campaignRunning) { campaignRunning = false; TrySaveCheckpoint(); }
                SetError(error.Message);
            }
        }

        private void MirrorSimulationEvents()
        {
            if (simulation == null) return;
            while (mirroredEventCount < simulation.Events.Count)
            {
                SimulationEvent entry = simulation.Events[mirroredEventCount++];
                Log($"t={entry.step} · {entry.kind} · {entry.message}");
            }
        }

        private void StopExecution()
        {
            workCancellation?.Cancel(); isPlaying = false;
            simulation?.Cancel(); MirrorSimulationEvents();
            if (campaignRunning) TrySaveCheckpoint();
            campaignRunning = false; campaignPaused = false;
            statusMessage = "중지됨 · 연속 실험은 저장된 다음 시나리오부터 이어갈 수 있습니다.";
        }

        private void DrawOperationControls()
        {
            if (!LockScenario) return;
            GUILayout.Label(IsBusy ? "계산 중…" : "실행 중", BoldLabel(14));
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(isPlaying ? "일시정지" : "계속"))
            { isPlaying = !isPlaying; campaignPaused = !isPlaying; viewSearchReplay = false; }
            if (GUILayout.Button("실행 중지")) StopExecution();
            GUILayout.EndHorizontal();
        }

        private void DrawCampaignSetupControls()
        {
            showCampaign = GUILayout.Toggle(showCampaign, "연속 실험 / 자동 복구 설정");
            if (!showCampaign && !campaignRunning) return;
            bool enabled = GUI.enabled;
            GUI.enabled = !LockScenario;
            autoRecovery = GUILayout.Toggle(autoRecovery, "충돌·미계획 시 자동 재계획");
            allowCbsFallback = GUILayout.Toggle(allowCbsFallback, "최종 대안으로 CBS 허용 (복구 방법 별도 기록)");
            pauseOnEvent = GUILayout.Toggle(pauseOnEvent, "충돌 시 0.8초 설명 정지");
            autoPlayAfterPlan = GUILayout.Toggle(autoPlayAfterPlan, "계산 완료 후 자동 재생 (해제하면 수동 리플레이)");
            GUILayout.BeginHorizontal();GUILayout.Label("최대 충돌 복구 횟수");recoveryLimit=Mathf.Clamp(IntField("recoveryLimit",recoveryLimit,45),1,50);GUILayout.EndHorizontal();
            GUILayout.Label("선택한 알고리즘 고정 · 조건 조합별 반복", MutedLabel());
            GUILayout.Label("지도 크기 (쉼표 구분)", MutedLabel()); campaignSizes = GUILayout.TextField(campaignSizes);
            GUILayout.Label("에이전트 수", MutedLabel()); campaignCounts = GUILayout.TextField(campaignCounts);
            GUILayout.Label("장애물 밀도", MutedLabel()); campaignDensities = GUILayout.TextField(campaignDensities);
            if(mapTemplate==MapTemplate.Bottleneck||mapTemplate==MapTemplate.NarrowCorridors)
            {GUILayout.Label("병목 수 목록 / 통로 폭 목록",MutedLabel());campaignBottlenecks=GUILayout.TextField(campaignBottlenecks);campaignPassages=GUILayout.TextField(campaignPassages);}
            GUILayout.BeginHorizontal(); GUILayout.Label("조건별 반복", GUILayout.Width(100));
            campaignRepeats = IntField("campaignRepeats", campaignRepeats, 55); GUILayout.EndHorizontal();
            if (GUILayout.Button("현재 알고리즘으로 연속 실험 시작"))
            { activeWorkbenchTab = WorkbenchTab.Playback; StartCampaign(); }
            GUI.enabled = enabled;
        }

        private void DrawCampaignProgress()
        {
            if (campaign != null)
            {
                int raw = 0, assisted = 0;
                foreach (CampaignRecord record in campaign.records) { if (record.rawSuccess) raw++; if (record.assistedSuccess) assisted++; }
                string campaignState = campaignRunning ? "진행 중" : campaign.nextIndex >= campaign.cases.Count ? "완료" : "중지됨";
                GUILayout.Label($"{campaign.algorithm} · {campaignState} · 완료 {campaign.nextIndex}/{campaign.cases.Count}\n원래 계획 성공 {raw} · 실행 도착 성공 {assisted}", MutedLabel());
                if (!campaignRunning && campaign.nextIndex < campaign.cases.Count && GUILayout.Button("최근 연속 실험 이어하기")) ResumeCampaign();
            }
            else GUILayout.Label("연속 실험을 시작하면 진행률과 요약이 여기에 표시됩니다.", MutedLabel());
        }

        private void StartCampaign()
        {
            if (LockScenario) return;
            try
            {
                CommitNumberInputs(); RefreshPreviewIfNeeded();
                List<CampaignCase> cases = MapfCampaign.BuildCases(campaignSizes, campaignCounts, campaignDensities,
                    campaignRepeats, randomSeed, agentPlacementSeed, mapTemplate,campaignBottlenecks,campaignPassages,distribution);
                string id = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
                campaign = new CampaignManifest { id = id, algorithm = selectedAlgorithm, settings = CurrentSettings(),
                    autoRecovery = autoRecovery, allowFallback = allowCbsFallback,recoveryLimit=recoveryLimit,cases = cases };
                campaignDirectory = Path.Combine(DataDirectory(), "Campaigns", id);
                Directory.CreateDirectory(campaignDirectory);
                File.WriteAllText(Path.Combine(DataDirectory(), "last-campaign.txt"), campaignDirectory);
                NewWorkCancellation(); campaignRunning = true; campaignPaused = false;
                SaveCampaignCheckpoint(); QueueCampaignCase();
            }
            catch (Exception error) { campaignRunning = false; SetError(error.Message); }
        }

        private void QueueCampaignCase()
        {
            if (campaign.nextIndex >= campaign.cases.Count)
            { campaignRunning = false; statusMessage = $"연속 실험 {campaign.cases.Count}회 완료 · CSV/JSON 저장됨"; return; }
            runSettings = campaign.settings.Copy(); runSettings.CancellationToken = workCancellation.Token;
            runSettings.RecordReplay = false;
            CampaignCase scenario = campaign.cases[campaign.nextIndex];
            MapfPlannerSettings settings = runSettings.Copy();
            MapfAlgorithmKind algorithm = campaign.algorithm;
            simulation = null; lastRun = null; isPlaying = false; comparisons.Clear();
            planningTask = Task.Run(() => MapfCampaign.Plan(scenario, algorithm, settings), settings.CancellationToken);
            statusMessage = $"연속 실험 {campaign.nextIndex + 1}/{campaign.cases.Count} 계산 중";
        }

        private void FinishCampaignCase()
        {
            MirrorSimulationEvents();
            try
            {
                string id = $"{campaign.id}-{currentCase.index + 1:D4}";
                CampaignRecord record = MapfCampaign.Record(id, currentCase, simulation, connectedCellsOpened);
                File.WriteAllText(Path.Combine(campaignDirectory, id + ".scenario.json"), MapfScenarioSerializer.ToJson(map, agents, CurrentDocument(), lastRun));
                File.WriteAllText(Path.Combine(campaignDirectory, id + ".events.json"), EventsJson(simulation));
                File.WriteAllText(Path.Combine(campaignDirectory, id + ".execution.json"), ExecutionJson(simulation));
                // Full snapshots are on disk; summary records need only compact event data.
                record.events = Array.Empty<SimulationEvent>();
                campaign.records.Add(record); campaign.nextIndex++;
                SaveCampaignCheckpoint();
                QueueCampaignCase();
            }
            catch (Exception error) { campaignRunning = false; SetError("실험 저장 실패: " + error.Message); }
        }

        private void SaveCampaignCheckpoint()
        {
            if (campaign == null || string.IsNullOrEmpty(campaignDirectory)) return;
            string target = Path.Combine(campaignDirectory, "campaign.json");
            File.WriteAllText(target + ".tmp", JsonUtility.ToJson(campaign, true));
            if (File.Exists(target)) File.Replace(target + ".tmp", target, target + ".bak");
            else File.Move(target + ".tmp", target);
            File.WriteAllText(Path.Combine(campaignDirectory, "results.csv"), MapfReportExporter.CampaignCsv(campaign.records), new System.Text.UTF8Encoding(true));
            File.WriteAllText(Path.Combine(campaignDirectory, "summary.csv"), MapfStatistics.ConditionSummaryCsv(campaign.records),new System.Text.UTF8Encoding(true));
            File.WriteAllText(Path.Combine(campaignDirectory, "charts.svg"), MapfReportExporter.CampaignChart(campaign.records));
        }

        private void TrySaveCheckpoint()
        { try { SaveCampaignCheckpoint(); } catch (Exception error) { SetError("체크포인트 저장 실패: " + error.Message); } }

        private void ResumeCampaign()
        {
            if (LockScenario) return;
            try
            {
                string root = Path.GetFullPath(Path.Combine(DataDirectory(), "Campaigns")) + Path.DirectorySeparatorChar;
                string directory = Path.GetFullPath(File.ReadAllText(Path.Combine(DataDirectory(), "last-campaign.txt")).Trim());
                if (!directory.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("실험 저장 경로가 올바르지 않습니다.");
                CampaignManifest loaded = JsonUtility.FromJson<CampaignManifest>(File.ReadAllText(Path.Combine(directory, "campaign.json")));
                if (loaded == null || loaded.version != 1 || loaded.settings == null || loaded.cases == null || loaded.nextIndex < 0 ||
                    loaded.nextIndex > loaded.cases.Count || loaded.cases.Count > 2000 || loaded.records == null)
                    throw new InvalidDataException("실험 파일 형식이 올바르지 않습니다.");
                if (!Enum.IsDefined(typeof(MapfAlgorithmKind), loaded.algorithm) || loaded.nextIndex != loaded.records.Count)
                    throw new InvalidDataException("실험 진행 정보가 일치하지 않습니다.");
                foreach (CampaignCase item in loaded.cases) MapfCampaign.ValidateCase(item);
                campaign = loaded; campaignDirectory = directory;
                selectedAlgorithm = campaign.algorithm; autoRecovery = campaign.autoRecovery; allowCbsFallback = campaign.allowFallback;
                recoveryLimit=Mathf.Clamp(campaign.recoveryLimit,1,50);ApplySettings(campaign.settings);
                NewWorkCancellation(); campaignRunning = true; campaignPaused = false;
                QueueCampaignCase();
            }
            catch (Exception error) { SetError("이어하기 실패: " + error.Message); }
        }
    }
}
