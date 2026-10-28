using System;
using System.Collections.Generic;
using System.Diagnostics;
using CoMPaSS.Algorithms;
using CoMPaSS.Core;

namespace CoMPaSS.Experiment
{
    public enum SimulationState { Ready, Moving, PausedForConflict, Replanning, Completed, Failed, Cancelled }

    [Serializable]
    public sealed class SimulationEvent
    {
        public int step;
        public string kind;
        public int firstAgent = -1;
        public int secondAgent = -1;
        public string message;
        public string method;
        public double milliseconds;
        [NonSerialized] public List<AgentPath> beforePaths;
        [NonSerialized] public List<AgentPath> afterPaths;
    }

    /// <summary>Validates a simultaneous tick before any agent may move. Arrivals permanently occupy their goal.</summary>
    public sealed class MapfSimulation
    {
        public readonly GridMap Map;
        public readonly List<AgentDefinition> Agents;
        public readonly MapfRunSummary Original;
        public readonly List<SimulationEvent> Events = new List<SimulationEvent>();
        public List<AgentPath> Paths { get; private set; }
        public SimulationState State { get; private set; }
        public int Step { get; private set; }
        public int RecoveryCount { get; private set; }
        public double RecoveryMilliseconds { get; private set; }
        public int ExecutedCost { get; private set; }
        public int WaitActions { get; private set; }
        public string FailureReason { get; private set; }
        public MapfConflict PendingConflict { get; private set; }
        public int ArrivedCount => finished.Count;
        public bool IsTerminal => State == SimulationState.Completed || State == SimulationState.Failed || State == SimulationState.Cancelled;
        public int MaxSteps = 4096;
        public int MaxRecoveries = 6;
        private readonly Dictionary<int, GridPosition> positions = new Dictionary<int, GridPosition>();
        private readonly Dictionary<int, List<GridPosition>> history = new Dictionary<int, List<GridPosition>>();
        private readonly Dictionary<int, AgentPath> planned = new Dictionary<int, AgentPath>();
        private readonly HashSet<int> finished = new HashSet<int>();
        private readonly Dictionary<int,int> affectedCount = new Dictionary<int,int>();
        private Dictionary<int, GridPosition> next;

        public MapfSimulation(GridMap map, IReadOnlyList<AgentDefinition> agents, MapfRunSummary original)
        {
            Map = map.Copy(); Agents = new List<AgentDefinition>(agents); Original = original;
            Paths = new List<AgentPath>();
            foreach (AgentPath path in original.Paths)
                if (path != null && !path.IsEmpty) planned[path.AgentId] = path;
            foreach (AgentDefinition agent in Agents)
            {
                positions[agent.Id] = agent.Start;
                history[agent.Id] = new List<GridPosition> { agent.Start };
                if (agent.Start == agent.Goal) finished.Add(agent.Id);
                Paths.Add(planned.TryGetValue(agent.Id, out AgentPath path) ? path : new AgentPath(agent.Id, new[] { agent.Start }));
            }
            State = finished.Count == Agents.Count ? SimulationState.Completed : SimulationState.Ready;
            AddEvent("start", $"{Agents.Count}명 유지 · 경로 생성 {original.RoutedAgents}명 · 미계획 {Agents.Count - original.RoutedAgents}명");
        }

        public GridPosition Position(int id) => positions[id];
        public static MapfSimulation FromHistory(GridMap map, IReadOnlyList<AgentDefinition> agents, MapfRunSummary original, IReadOnlyList<AgentPath> paths)
        {
            if(paths.Count!=agents.Count || ConflictDetector.FindConflicts(paths).Count>0) throw new ArgumentException("실행 기록의 에이전트 수 또는 충돌 검증 실패");
            var result=new MapfSimulation(map,agents,original);result.finished.Clear();result.Paths=new List<AgentPath>(paths);
            var ids=new HashSet<int>();int last=-1;
            foreach(AgentPath path in paths)
            {
                AgentDefinition agent=result.Agents.Find(a=>a.Id==path.AgentId);
                if(path.IsEmpty||path.MoveCost>4096||agent==null||!ids.Add(path.AgentId)||path.PositionAt(0)!=agent.Start||(last>=0&&last!=path.MoveCost)) throw new ArgumentException("실행 기록의 ID·시작·길이가 잘못되었습니다.");
                last=path.MoveCost;bool arrived=agent.Start==agent.Goal;
                for(int t=1;t<path.Positions.Count;t++)
                {
                    GridPosition a=path.PositionAt(t-1),b=path.PositionAt(t);
                    if(map.IsBlocked(b)||Math.Abs(a.X-b.X)+Math.Abs(a.Y-b.Y)>1||(arrived&&b!=agent.Goal)) throw new ArgumentException("실행 기록에 잘못된 이동이 있습니다.");
                    if(!arrived){result.ExecutedCost++;if(a==b)result.WaitActions++;}if(b==agent.Goal)arrived=true;
                }
                result.history[agent.Id]=new List<GridPosition>(path.Positions);result.positions[agent.Id]=path.PositionAt(last);
                if(arrived) result.finished.Add(agent.Id);
            }
            result.Step=Math.Max(0,last);result.State=result.finished.Count==agents.Count?SimulationState.Completed:SimulationState.Cancelled;
            return result;
        }
        public GridPosition NextPosition(int id) => next != null && State == SimulationState.Moving ? next[id] : positions[id];
        public bool HasArrived(int id) => finished.Contains(id);
        public bool HasRoute(int id) => planned.ContainsKey(id);

        public bool PrepareStep()
        {
            if (IsTerminal || State == SimulationState.Replanning || State == SimulationState.PausedForConflict) return false;
            if (State == SimulationState.Moving) return true;
            if (Step >= MaxSteps) { Fail("최대 실행 단계에 도달했습니다."); return false; }
            next = new Dictionary<int, GridPosition>();
            foreach (AgentDefinition agent in Agents)
            {
                if (finished.Contains(agent.Id)) { next[agent.Id] = positions[agent.Id]; continue; }
                if (!planned.TryGetValue(agent.Id, out AgentPath path) || path.IsEmpty || path.PositionAt(0) != agent.Start)
                    return Pause(null, $"Agent {agent.Id}의 경로가 없어 전체 이동을 정지했습니다.", agent.Id);
                GridPosition target = path.PositionAt(Step + 1);
                GridPosition current = positions[agent.Id];
                if (Map.IsBlocked(target) || Math.Abs(target.X - current.X) + Math.Abs(target.Y - current.Y) > 1)
                    return Pause(null, $"Agent {agent.Id}의 잘못된 이동을 차단했습니다.", agent.Id);
                if (Step >= path.MoveCost && current != agent.Goal)
                    return Pause(null, $"Agent {agent.Id}의 경로가 목표 전에 끝났습니다.", agent.Id);
                next[agent.Id] = target;
            }
            for (int i = 0; i < Agents.Count; i++)
            for (int j = i + 1; j < Agents.Count; j++)
            {
                int a = Agents[i].Id, b = Agents[j].Id;
                if (next[a] == next[b])
                    return Pause(new MapfConflict(ConflictType.Vertex, a, b, Step + 1, next[a], next[b]), "같은 칸 진입을 이동 전에 차단했습니다.");
                if (next[a] == positions[b] && next[b] == positions[a])
                    return Pause(new MapfConflict(ConflictType.Edge, a, b, Step + 1, positions[a], next[a]), "서로 위치를 바꾸는 이동을 차단했습니다.");
            }
            State = SimulationState.Moving;
            return true;
        }

        public void CommitStep()
        {
            if (State != SimulationState.Moving || next == null) throw new InvalidOperationException("A checked tick is required.");
            Step++;
            foreach (AgentDefinition agent in Agents)
            {
                if (!finished.Contains(agent.Id))
                {
                    ExecutedCost++;
                    if (next[agent.Id] == positions[agent.Id]) WaitActions++;
                }
                positions[agent.Id] = next[agent.Id];
                history[agent.Id].Add(next[agent.Id]);
                if (next[agent.Id] == agent.Goal && finished.Add(agent.Id))
                    AddEvent("arrived", $"Agent {agent.Id} 도착 · 목표 칸 {agent.Goal} 영구 점유", agent.Id);
            }
            next = null;
            State = finished.Count == Agents.Count ? SimulationState.Completed : SimulationState.Ready;
            if (State == SimulationState.Completed) AddEvent("complete", $"전체 {Agents.Count}명 도착 · 복구 {RecoveryCount}회");
        }

        private bool Pause(MapfConflict conflict, string message, int agent = -1)
        {
            foreach(int id in new[]{conflict?.FirstAgentId??agent,conflict?.SecondAgentId??-1})
                if(id>=0){affectedCount.TryGetValue(id,out int count);affectedCount[id]=count+1;}
            PendingConflict = conflict; next = null; State = SimulationState.PausedForConflict;
            SimulationEvent entry = AddEvent("stop", message, conflict?.FirstAgentId ?? agent, conflict?.SecondAgentId ?? -1);
            entry.beforePaths = new List<AgentPath>(Paths);
            return false;
        }

        public RecoveryRequest BeginRecovery(MapfPlannerSettings settings, bool allowFallback)
        {
            if (State != SimulationState.PausedForConflict) return null;
            if (RecoveryCount >= MaxRecoveries) { Fail("재계획 횟수 제한에 도달했습니다."); return null; }
            State = SimulationState.Replanning;
            RecoveryCount++;
            var blockedMap = Map.Copy();
            var remaining = new List<AgentDefinition>();
            var boosts=new Dictionary<int,int>();
            foreach (AgentDefinition agent in Agents)
            {
                if (finished.Contains(agent.Id)) blockedMap.SetBlocked(agent.Goal, true);
                else
                {
                    remaining.Add(new AgentDefinition(agent.Id, positions[agent.Id], agent.Goal));int waits=0;
                    for(int t=1;t<history[agent.Id].Count;t++)if(history[agent.Id][t]==history[agent.Id][t-1])waits++;
                    affectedCount.TryGetValue(agent.Id,out int attempts);
                    boosts[agent.Id]=waits*Math.Max(0,Math.Min(100,settings.Profile.WaitWeight))+attempts*Math.Max(0,Math.Min(100,settings.Profile.RecoveryWeight));
                }
            }
            AddEvent("replanning", $"재계획 {RecoveryCount}/{MaxRecoveries} · 도착 {finished.Count}명은 고정 장애물로 유지");
            return new RecoveryRequest { Map = blockedMap, Agents = remaining, Algorithm = Original.Algorithm,
                Settings = settings.Copy(), AllowFallback = allowFallback,PriorityBoosts=boosts };
        }

        public void ApplyRecovery(RecoveryResult result)
        {
            if (State != SimulationState.Replanning) return;
            foreach (SimulationEvent entry in result.Events) { entry.step = Step; Events.Add(entry); }
            RecoveryMilliseconds += result.Milliseconds;
            if (!result.Succeeded) { Fail(result.FailureReason); return; }
            if (!ValidateRecovery(result.Paths)) { Fail("재계획 결과의 에이전트·경로·목표 점유 검증에 실패했습니다."); return; }
            var before = new List<AgentPath>(Paths);
            foreach (AgentPath suffix in result.Paths)
            {
                var merged = new List<GridPosition>(history[suffix.AgentId]);
                for (int i = 1; i < suffix.Positions.Count; i++) merged.Add(suffix.Positions[i]);
                planned[suffix.AgentId] = new AgentPath(suffix.AgentId, merged);
            }
            Paths.Clear();
            foreach (AgentDefinition agent in Agents)
                Paths.Add(planned.TryGetValue(agent.Id, out AgentPath path) ? path : new AgentPath(agent.Id, history[agent.Id]));
            PendingConflict = null; State = SimulationState.Ready;
            SimulationEvent resumed = AddEvent("resume", "새 경로 검증 완료 · 자동 재개");
            resumed.beforePaths = before; resumed.afterPaths = new List<AgentPath>(Paths); resumed.method = result.Method;
        }

        private bool ValidateRecovery(IReadOnlyList<AgentPath> paths)
        {
            var ids = new HashSet<int>(); var occupiedGoals = new HashSet<GridPosition>();
            foreach (AgentDefinition a in Agents) if (finished.Contains(a.Id)) occupiedGoals.Add(a.Goal);
            foreach (AgentPath path in paths)
            {
                if (path == null || path.IsEmpty || !ids.Add(path.AgentId) || finished.Contains(path.AgentId)) return false;
                AgentDefinition agent = Agents.Find(a => a.Id == path.AgentId);
                if (agent == null || path.PositionAt(0) != positions[agent.Id] || path.PositionAt(path.MoveCost) != agent.Goal) return false;
                bool arrived = false;
                for (int t = 0; t < path.Positions.Count; t++)
                {
                    GridPosition p = path.Positions[t], previous = path.PositionAt(Math.Max(0, t - 1));
                    if (Map.IsBlocked(p) || occupiedGoals.Contains(p) || Math.Abs(p.X - previous.X) + Math.Abs(p.Y - previous.Y) > 1 || (arrived && p != agent.Goal)) return false;
                    if (p == agent.Goal) arrived = true;
                }
            }
            return ids.Count == Agents.Count - finished.Count && ConflictDetector.FindConflicts(paths).Count == 0;
        }

        public void Fail(string reason) { State = SimulationState.Failed; FailureReason = reason; next = null; AddEvent("failed", reason); }
        public void Cancel() { State = SimulationState.Cancelled; next = null; AddEvent("cancelled", "사용자가 실행을 중지했습니다."); }
        public List<AgentPath> ExecutedPaths()
        {
            var result = new List<AgentPath>();
            foreach (AgentDefinition agent in Agents) result.Add(new AgentPath(agent.Id, history[agent.Id]));
            return result;
        }
        private SimulationEvent AddEvent(string kind, string message, int a = -1, int b = -1)
        {
            var entry = new SimulationEvent { step = Step, kind = kind, message = message, firstAgent = a, secondAgent = b };
            Events.Add(entry); return entry;
        }
    }

    public sealed class RecoveryRequest
    {
        public GridMap Map;
        public List<AgentDefinition> Agents;
        public MapfAlgorithmKind Algorithm;
        public MapfPlannerSettings Settings;
        public bool AllowFallback;
        public Dictionary<int,int> PriorityBoosts = new Dictionary<int,int>();
    }
    public sealed class RecoveryResult
    {
        public bool Succeeded;
        public string FailureReason;
        public string Method;
        public double Milliseconds;
        public List<AgentPath> Paths = new List<AgentPath>();
        public List<SimulationEvent> Events = new List<SimulationEvent>();
    }

    public static class MapfRecoveryPlanner
    {
        public static RecoveryResult Plan(RecoveryRequest request)
        {
            var result = new RecoveryResult();
            var timer = Stopwatch.StartNew();
            int attempts = request.Algorithm == MapfAlgorithmKind.ConflictBasedSearch ? 1 : (request.AllowFallback ? 4 : 3);
            if (request.Algorithm == MapfAlgorithmKind.PrimalExternal) attempts = request.AllowFallback?2:1;
            for (int attempt = 0; attempt < attempts; attempt++)
            {
                request.Settings.CancellationToken.ThrowIfCancellationRequested();
                int remaining = request.Settings.MaxPlanningMilliseconds - (int)timer.ElapsedMilliseconds;
                if (remaining <= 0) break;
                var settings = request.Settings.Copy();
                settings.RecordReplay = false;
                settings.MaxPlanningMilliseconds = Math.Max(1, remaining / (attempts - attempt));
                var agents = new List<AgentDefinition>(request.Agents);
                if(request.Settings.PriorityMode==AgentPriorityMode.MostConstrainedFirst)
                {
                    for(int i=1;i<agents.Count;i++)
                    {
                        var a=agents[i];request.PriorityBoosts.TryGetValue(a.Id,out int score);int j=i-1;
                        while(j>=0&&request.PriorityBoosts.TryGetValue(agents[j].Id,out int other)&&other<score){agents[j+1]=agents[j];j--;}
                        agents[j+1]=a;
                    }
                    if(attempt==0) foreach(var a in agents)result.Events.Add(new SimulationEvent{kind="priority",firstAgent=a.Id,message=$"Agent {a.Id} · 누적 대기·충돌 개입 점수 {request.PriorityBoosts[a.Id]}"});
                }
                if (attempt == 1) agents.Reverse();
                if (attempt == 2 && agents.Count > 1) { AgentDefinition first = agents[0]; agents.RemoveAt(0); agents.Add(first); }
                MapfAlgorithmKind algorithm = request.Algorithm;
                if (algorithm == MapfAlgorithmKind.IndependentAStar) algorithm = MapfAlgorithmKind.CooperativeAStar;
                if (attempt == 3) algorithm = MapfAlgorithmKind.ConflictBasedSearch;
                if(request.Algorithm==MapfAlgorithmKind.PrimalExternal&&attempt==1)algorithm=MapfAlgorithmKind.ConflictBasedSearch;
                settings.PriorityMode = AgentPriorityMode.InputOrder;
                string method = algorithm == request.Algorithm ? $"{algorithm} · 순서 시도 {attempt + 1}" : $"{request.Algorithm} + {algorithm} 복구";
                MapfRunSummary planned = new MapfExperimentRunner().Run(request.Map, agents, algorithm, settings);
                result.Events.Add(new SimulationEvent { kind = "attempt", method = method, milliseconds = planned.PlanningMilliseconds,
                    message = planned.CollisionFree ? method + " 성공" : method + " 실패: " + planned.FailureReason });
                if (!planned.CollisionFree || planned.RoutedAgents != agents.Count) continue;
                result.Succeeded = true; result.Paths = planned.Paths; result.Method = method;
                break;
            }
            result.Milliseconds = timer.Elapsed.TotalMilliseconds;
            if (!result.Succeeded) result.FailureReason = "제한 내에서 충돌 없는 대안을 찾지 못했습니다. 종료 상태와 시드를 저장했습니다.";
            return result;
        }
    }
}
