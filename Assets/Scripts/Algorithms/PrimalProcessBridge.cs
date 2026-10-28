using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CoMPaSS.Core;
using CoMPaSS.Experiment;
using UnityEngine;

namespace CoMPaSS.Algorithms
{
    [Serializable]
    public sealed class PrimalInputDocument
    {
        public int protocolVersion = 1;
        public int width;
        public int height;
        public string blockedCells;
        public int seed=23,maxTimeSteps=256;
        public PrimalAgentInput[] agents = Array.Empty<PrimalAgentInput>();
    }

    [Serializable]
    public sealed class PrimalAgentInput
    {
        public int id;
        public int startX;
        public int startY;
        public int goalX;
        public int goalY;
    }

    [Serializable]
    public sealed class PrimalOutputDocument
    {
        public int protocolVersion;
        public bool success;
        public string failureReason;
        public string modelSource;
        public bool directlyTrained;
        public double inferenceMilliseconds;
        public PrimalPathOutput[] paths = Array.Empty<PrimalPathOutput>();
        public string[] actionLogs = Array.Empty<string>();
        public PrimalDecision[] decisions = Array.Empty<PrimalDecision>();
    }
    [Serializable] public sealed class PrimalDecision
    {
        public int agentId,step,action,size;
        public float[] probabilities,channels,goalVector;
        public string note;
    }

    [Serializable]
    public sealed class PrimalPathOutput
    {
        public int agentId;
        public int[] x = Array.Empty<int>();
        public int[] y = Array.Empty<int>();
    }

    public sealed class PrimalBridgeResult
    {
        public bool Succeeded;
        public List<AgentPath> Paths = new List<AgentPath>();
        public List<MapfConflict> Conflicts = new List<MapfConflict>();
        public List<AgentRunLog> AgentLogs = new List<AgentRunLog>();
        public double PlanningMilliseconds;
        public string FailureReason;
        public List<PrimalDecision> Decisions = new List<PrimalDecision>();
    }

    /// <summary>Runs an external, pre-trained PRIMAL-compatible model using a line-delimited JSON stdin/stdout protocol.</summary>
    public static class PrimalProcessBridge
    {
        public static PrimalBridgeResult Run(GridMap map, IReadOnlyList<AgentDefinition> agents,
            string pythonExecutable, string scriptPath, int timeoutMilliseconds = 60000, CancellationToken cancellationToken = default,int maxTimeSteps=256,int seed=23)
        {
            var result = new PrimalBridgeResult();
            cancellationToken.ThrowIfCancellationRequested();
            if (map == null || agents == null) { result.FailureReason = "Map or agent list is missing."; return result; }
            if (string.IsNullOrWhiteSpace(pythonExecutable) || string.IsNullOrWhiteSpace(scriptPath))
            { result.FailureReason = "Set the Python executable and PRIMAL adapter script path first."; return result; }
            if (!File.Exists(scriptPath)) { result.FailureReason = "PRIMAL adapter script was not found."; return result; }

            var request = new PrimalInputDocument { width = map.Width, height = map.Height, blockedCells = EncodeMap(map),seed=seed,maxTimeSteps=maxTimeSteps };
            request.agents = new PrimalAgentInput[agents.Count];
            for (int i = 0; i < agents.Count; i++)
            {
                AgentDefinition agent = agents[i];
                if (agent == null) { result.FailureReason = "Agent list contains a missing entry."; return result; }
                request.agents[i] = new PrimalAgentInput
                {
                    id = agent.Id,
                    startX = agent.Start.X,
                    startY = agent.Start.Y,
                    goalX = agent.Goal.X,
                    goalY = agent.Goal.Y
                };
            }

            var info = new ProcessStartInfo
            {
                FileName = pythonExecutable,
                Arguments = "\"" + scriptPath.Replace("\"", "\\\"") + "\" --mapf-json-stdin",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            var timer = Stopwatch.StartNew();
            try
            {
                using (var process = new Process { StartInfo = info })
                {
                    if (!process.Start()) { result.FailureReason = "Python process could not be started."; return result; }
                    Task<string> stdout = process.StandardOutput.ReadToEndAsync();
                    Task<string> stderr = process.StandardError.ReadToEndAsync();
                    process.StandardInput.WriteLine(JsonUtility.ToJson(request));
                    process.StandardInput.Close();
                    while (!process.WaitForExit(50))
                    {
                        if (cancellationToken.IsCancellationRequested || timer.ElapsedMilliseconds >= timeoutMilliseconds)
                        {
                            try { process.Kill(); } catch (InvalidOperationException) { }
                            cancellationToken.ThrowIfCancellationRequested();
                            result.FailureReason = "PRIMAL inference exceeded the time limit.";
                            return result;
                        }
                    }
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!Task.WaitAll(new Task[] { stdout, stderr }, Math.Max(1, timeoutMilliseconds - (int)timer.ElapsedMilliseconds), cancellationToken))
                    { result.FailureReason = "PRIMAL output read exceeded the time limit."; return result; }
                    string output = stdout.GetAwaiter().GetResult();
                    string error = stderr.GetAwaiter().GetResult();
                    result.PlanningMilliseconds = timer.Elapsed.TotalMilliseconds;
                    if (process.ExitCode != 0)
                    {
                        result.FailureReason = "PRIMAL adapter exited with code " + process.ExitCode + ": " + Trim(error);
                        return result;
                    }
                    PrimalOutputDocument response = JsonUtility.FromJson<PrimalOutputDocument>(output);
                    if (response == null || response.protocolVersion != 1)
                    { result.FailureReason = "PRIMAL adapter returned invalid protocol version or JSON."; return result; }
                    if(response.decisions!=null) foreach(PrimalDecision decision in response.decisions)
                    {
                        if(decision==null||decision.size<1||decision.size>32||decision.channels==null||decision.channels.Length!=4*decision.size*decision.size)continue;
                        result.Decisions.Add(decision);
                        result.AgentLogs.Add(new AgentRunLog { AgentId=decision.agentId,Message=$"t={decision.step} 모델 행동 {decision.action} · {decision.note}" });
                    }
                    result.AgentLogs.Add(new AgentRunLog { AgentId=-1,Message=$"모델 출처: {response.modelSource??"unspecified"} · 직접 학습: {response.directlyTrained}" });
                    if(response.actionLogs!=null) foreach(string log in response.actionLogs) result.AgentLogs.Add(new AgentRunLog { AgentId=-1,Message=log });
                    if (!response.success)
                    { result.FailureReason = string.IsNullOrEmpty(response.failureReason) ? "PRIMAL reported failure." : response.failureReason; return result; }

                    result.Paths = DecodePaths(map, agents, response.paths);
                    if (result.Paths.Count != agents.Count)
                    { result.FailureReason = "PRIMAL did not return a path for every agent."; return result; }
                    result.Conflicts = ConflictDetector.FindConflicts(result.Paths);
                    result.Succeeded = result.Conflicts.Count == 0;
                    result.PlanningMilliseconds = response.inferenceMilliseconds > 0 ? response.inferenceMilliseconds : result.PlanningMilliseconds;
                    string source = string.IsNullOrWhiteSpace(response.modelSource) ? "unspecified model" : response.modelSource;
                    string origin = response.directlyTrained ? "직접 학습 모델" : "공개 사전 학습 모델";
                    result.AgentLogs.Add(new AgentRunLog { AgentId = -1, Message = $"{origin} · 출처: {source}" });
                    if (!result.Succeeded) result.FailureReason = "PRIMAL 경로에 충돌이 있습니다.";
                    return result;
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception exception)
            {
                result.PlanningMilliseconds = timer.Elapsed.TotalMilliseconds;
                result.FailureReason = exception.Message;
                return result;
            }
        }

        private static List<AgentPath> DecodePaths(GridMap map, IReadOnlyList<AgentDefinition> agents, PrimalPathOutput[] output)
        {
            var byId = new Dictionary<int, AgentDefinition>();
            for (int i = 0; i < agents.Count; i++) byId[agents[i].Id] = agents[i];
            var paths = new List<AgentPath>();
            if (output == null) return paths;
            for (int i = 0; i < output.Length; i++)
            {
                PrimalPathOutput item = output[i];
                if (item == null || item.x == null || item.y == null || item.x.Length != item.y.Length || item.x.Length == 0 ||
                    !byId.TryGetValue(item.agentId, out AgentDefinition agent)) continue;
                var positions = new List<GridPosition>(item.x.Length);
                bool valid = true;
                bool arrived = false;
                for (int step = 0; step < item.x.Length; step++)
                {
                    var cell = new GridPosition(item.x[step], item.y[step]);
                    if (!map.Contains(cell) || map.IsBlocked(cell) || (step > 0 && !IsLegalStep(positions[step - 1], cell)) || (arrived && cell != agent.Goal))
                    { valid = false; break; }
                    positions.Add(cell);
                    if (cell == agent.Goal) arrived = true;
                }
                if (!valid || positions[0] != agent.Start || positions[positions.Count - 1] != agent.Goal) continue;
                paths.Add(new AgentPath(item.agentId, positions));
                byId.Remove(item.agentId);
            }
            return paths;
        }

        private static bool IsLegalStep(GridPosition a, GridPosition b) =>
            Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y) <= 1;

        private static string EncodeMap(GridMap map)
        {
            var chars = new char[map.Width * map.Height];
            for (int y = 0; y < map.Height; y++)
            for (int x = 0; x < map.Width; x++) chars[y * map.Width + x] = map.IsBlocked(new GridPosition(x, y)) ? '1' : '0';
            return new string(chars);
        }

        private static string Trim(string value) => string.IsNullOrWhiteSpace(value) ? "No error details." :
            value.Length > 500 ? value.Substring(0, 500) : value.Trim();
    }
}
