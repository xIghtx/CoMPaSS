using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using CoMPaSS.Algorithms;
using CoMPaSS.Core;
using CoMPaSS.Experiment;
using UnityEngine;

namespace CoMPaSS.UI
{
    public sealed partial class MapfWorkbenchController
    {
        private static string verificationDirectory;
        // Opt-in player integration check. Normal launches never execute or write this fixture.
        private IEnumerator Start()
        {
            string[] arguments=Environment.GetCommandLineArgs();int flag=Array.IndexOf(arguments,"--compass-verify");
            if(flag<0||flag+1>=arguments.Length)yield break;
            verificationDirectory=Path.GetFullPath(arguments[flag+1]);Directory.CreateDirectory(verificationDirectory);
            var errors=new List<string>();
            Application.LogCallback collect=(message,stack,type)=>{if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert)errors.Add(message+"\n"+stack);};
            Application.logMessageReceived+=collect;
            Screen.SetResolution(1600,1000,FullScreenMode.Windowed);
            while(!UnityEngine.Rendering.SplashScreen.isFinished)yield return null;
            yield return new WaitForSecondsRealtime(.7f);
            yield return new WaitForEndOfFrame();SaveVerificationScreen("00-guided-start.png");
            map=new GridMap(5,5);mapWidth=5;mapHeight=5;mapTemplate=MapTemplate.Open;obstacleDensity=0;selectedAlgorithm=MapfAlgorithmKind.IndependentAStar;
            agents.Clear();agents.Add(new AgentDefinition(0,new GridPosition(0,2),new GridPosition(4,2)));
            agents.Add(new AgentDefinition(1,new GridPosition(4,2),new GridPosition(0,2)));agentCount=2;selectedAgent=0;
            RememberPreviewConfiguration();RecountBlockedCells();NewWorkCancellation();runSettings=CurrentSettings();runSettings.CancellationToken=workCancellation.Token;
            currentCase=CurrentCase();BeginLiveRun(new MapfExperimentRunner().Run(map,agents,MapfAlgorithmKind.IndependentAStar,runSettings));isPlaying=false;
            while(simulation.PrepareStep())simulation.CommitStep();
            timelineStep=simulation.Step;MirrorSimulationEvents();statusMessage="검증 예제 · 다음 칸 충돌을 이동 전에 차단";
            yield return new WaitForEndOfFrame();SaveVerificationScreen("01-collision-1600.png");
            isPlaying=true;autoRecovery=true;allowCbsFallback=true;pauseOnEvent=false;replaySpeed=100;recoveryReadyAt=0;
            float deadline=Time.realtimeSinceStartup+20;
            while(!simulation.IsTerminal&&Time.realtimeSinceStartup<deadline)yield return null;
            if(simulation.State!=SimulationState.Completed)errors.Add("Crossing recovery did not complete: "+simulation.State);
            yield return null;
            showReports=true;figureNote="Player verification: two-agent crossing";
            yield return new WaitForEndOfFrame();SaveVerificationScreen("02-completed-1600.png");
            try
            {
                ExportMap(verificationDirectory);ExportBeforeAfter(verificationDirectory);
                File.WriteAllText(Path.Combine(verificationDirectory,"execution.json"),ExecutionJson(simulation));
                File.WriteAllText(Path.Combine(verificationDirectory,"events.json"),EventsJson(simulation));
            }
            catch(Exception error){errors.Add(error.ToString());}
            ResetRun();mapWidth=64;mapHeight=64;mapTemplate=MapTemplate.RandomObstacles;agentCount=32;obstacleDensity=.2f;randomSeed=23;agentPlacementSeed=42;
            map=ProceduralMapGenerator.Create(64,64,mapTemplate,.2,23);agents.Clear();
            agents.AddRange(ProceduralMapGenerator.CreateRandomAgents(map,32,42,out connectedCellsOpened));
            selectedAgent=31;RememberPreviewConfiguration();RecountBlockedCells();
            Screen.SetResolution(1280,720,FullScreenMode.Windowed);showReports=false;sidebarScroll=Vector2.zero;inspectorScroll=Vector2.zero;
            yield return new WaitForSecondsRealtime(.4f);
            yield return new WaitForEndOfFrame();SaveVerificationScreen("03-preview-32-agents-1280.png");
            selectedAlgorithm=MapfAlgorithmKind.CooperativeAStar;campaignSizes="8x8,10x10";campaignCounts="2,4";campaignDensities="0.1";campaignRepeats=1;
            StartCampaign();yield return null;StopExecution();
            deadline=Time.realtimeSinceStartup+20;while(IsBusy&&Time.realtimeSinceStartup<deadline)yield return null;
            ResumeCampaign();deadline=Time.realtimeSinceStartup+40;
            while(campaignRunning&&Time.realtimeSinceStartup<deadline)yield return null;
            if(campaign==null||campaign.nextIndex!=4||campaign.records.Count!=4)errors.Add("Sequential campaign/resume did not persist all four cases.");
            else if(campaign.records.Exists(r=>r.algorithm!=MapfAlgorithmKind.CooperativeAStar))errors.Add("Campaign algorithm changed.");
            DetectPrimalConnection();
            if(Array.IndexOf(arguments,"--compass-verify-primal")>=0)
            {
                if(!primalReady)errors.Add("Installed public PRIMAL model was not detected.");
                else
                {
                    ProbePrimalConnection();deadline=Time.realtimeSinceStartup+65;
                    while(primalProbe!=null&&Time.realtimeSinceStartup<deadline)yield return null;
                    if(primalProbe!=null||!primalReady)errors.Add("Real PRIMAL inference failed: "+primalStatus);
                    else File.WriteAllText(Path.Combine(verificationDirectory,"primal-check.txt"),primalStatus);
                }
            }
            File.WriteAllText(Path.Combine(verificationDirectory,"player-check.txt"),errors.Count==0?"PASS: Unity GUI, safe recovery, map/paired export, 32-agent preview at 1280x720, four-case campaign cancellation/resume/checkpoint.":string.Join("\n\n",errors));
            Application.logMessageReceived-=collect;
            Application.Quit(errors.Count==0?0:1);
        }
        private void SaveVerificationScreen(string name)
        {
            Texture2D image=ScreenCapture.CaptureScreenshotAsTexture();
            try
            {
                var pixels=image.GetPixels32();int visible=0;for(int i=0;i<pixels.Length;i+=997)if(pixels[i].r+pixels[i].g+pixels[i].b>45)visible++;
                if(visible<10)Debug.LogError("Verification screenshot appears blank: "+name);
                File.WriteAllBytes(Path.Combine(verificationDirectory,name),image.EncodeToPNG());
            }
            finally{Destroy(image);}
        }
    }
}
