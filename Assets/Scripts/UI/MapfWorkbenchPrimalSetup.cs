using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using CoMPaSS.Core;
using CoMPaSS.Experiment;
using UnityEngine;

namespace CoMPaSS.UI
{
    public sealed partial class MapfWorkbenchController
    {
        private bool primalReady;
        private string primalStatus="모델 연결을 확인하세요.", primalTools;
        private Process primalSetupProcess;
        private Task<MapfRunSummary> primalProbe;
        private System.Threading.CancellationTokenSource primalProbeCancellation;
        private readonly ConcurrentQueue<string> primalSetupOutput=new ConcurrentQueue<string>();
        private static string PrimalRuntime => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"CoMPaSS","PrimalRuntime");
        private void DetectPrimalConnection()
        {
            primalTools=Path.GetFullPath(Path.Combine(Application.dataPath,"..","Tools","Primal"));
            string python=Path.Combine(PrimalRuntime,"python","python.exe"), adapter=Path.Combine(primalTools,"primal_adapter.py");
            primalReady=File.Exists(Path.Combine(PrimalRuntime,"ready.json"))&&File.Exists(python)&&File.Exists(adapter)&&File.Exists(Path.Combine(PrimalRuntime,"config.json"));
            if(primalReady){pythonExecutable=python;primalScriptPath=adapter;primalStatus="연결됨 · 공개 PRIMAL 모델 사용 가능";}
            else primalStatus="아직 연결되지 않았습니다. 아래 자동 준비를 한 번 실행하세요.";
        }
        private void DrawPrimalConnection()
        {
            GUILayout.Label("PRIMAL 연결",BoldLabel(15));
            GUILayout.Label(primalStatus,MutedLabel());
            bool enabled=GUI.enabled;GUI.enabled=enabled&&primalSetupProcess==null&&primalProbe==null&&!LockScenario;
            if(!primalReady)
            {
                GUILayout.Label("전용 Python·TensorFlow·공개 모델을 내려받습니다. 400MB 이상 다운로드와 약 2GB의 디스크 공간이 필요하며 몇 분 걸릴 수 있습니다.",MutedLabel());
                if(GUILayout.Button("공개 모델 자동 준비",GUILayout.Height(34)))StartPrimalSetup();
            }
            else if(GUILayout.Button("실제 모델 연결 검사"))ProbePrimalConnection();
            GUI.enabled=enabled;
            if(primalSetupProcess!=null||primalProbe!=null)GUILayout.Label("준비 중에도 다른 C# 알고리즘은 사용할 수 있습니다.",MutedLabel());
        }
        private void StartPrimalSetup()
        {
            try
            {
                string script=Path.Combine(primalTools,"setup_primal.ps1");
                if(!File.Exists(script))throw new FileNotFoundException("설치 도구가 없습니다. 프로그램 폴더 전체를 복사해 주세요.",script);
                var info=new ProcessStartInfo {FileName="powershell.exe",Arguments="-NoProfile -File \""+script+"\"",UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
                primalSetupProcess=new Process{StartInfo=info};
                primalSetupProcess.OutputDataReceived+=(s,e)=>{if(!string.IsNullOrWhiteSpace(e.Data))primalSetupOutput.Enqueue(e.Data);};
                primalSetupProcess.ErrorDataReceived+=(s,e)=>{if(!string.IsNullOrWhiteSpace(e.Data))primalSetupOutput.Enqueue(e.Data);};
                primalSetupProcess.Start();primalSetupProcess.BeginOutputReadLine();primalSetupProcess.BeginErrorReadLine();primalStatus="다운로드와 설치를 시작했습니다…";
            }
            catch(Exception error){primalSetupProcess?.Dispose();primalSetupProcess=null;primalStatus="준비 실패: "+error.Message;}
        }
        private void ProbePrimalConnection()
        {
            var settings=CurrentSettings();settings.MaxPlanningMilliseconds=60000;settings.MaxTimeSteps=32;
            primalProbeCancellation?.Dispose();primalProbeCancellation=new System.Threading.CancellationTokenSource();settings.CancellationToken=primalProbeCancellation.Token;
            var probeAgents=new[]{new AgentDefinition(0,new GridPosition(1,2),new GridPosition(3,2))};
            primalProbe=Task.Run(()=>new MapfExperimentRunner().Run(new GridMap(5,5),probeAgents,MapfAlgorithmKind.PrimalExternal,settings));
            primalStatus="공개 체크포인트로 실제 행동을 계산하는 중…";
        }
        private void PollPrimalConnection()
        {
            while(primalSetupOutput.TryDequeue(out string line)){primalStatus=line;Log("PRIMAL 준비: "+line);}
            if(primalSetupProcess!=null&&primalSetupProcess.HasExited)
            {
                int code=primalSetupProcess.ExitCode;primalSetupProcess.Dispose();primalSetupProcess=null;
                if(code==0)DetectPrimalConnection();else {primalReady=false;primalStatus="준비를 완료하지 못했습니다. 인터넷 연결과 디스크 공간을 확인한 후 자동 준비를 다시 눌러주세요. 마지막 메시지: "+primalStatus;}
            }
            if(primalProbe!=null&&primalProbe.IsCompleted)
            {
                try {var result=primalProbe.GetAwaiter().GetResult();primalReady=result.CollisionFree&&result.PrimalDecisions.Count>0;primalStatus=primalReady?"검사 통과 · 실제 학습 모델의 경로·관측·행동 확인":"연결 검사 실패: "+result.FailureReason;}
                catch(Exception error){primalReady=false;primalStatus="연결 검사 실패: "+error.Message;}
                finally{primalProbe=null;}
            }
        }
    }
}
