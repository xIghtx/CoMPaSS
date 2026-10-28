using System.Globalization;
using System.Xml.Linq;
using CoMPaSS.Algorithms;
using CoMPaSS.Core;
using CoMPaSS.Experiment;
using CoMPaSS.Persistence;

static class Program
{
    static int assertions, scenarios;
    static readonly MapfExperimentRunner Runner = new();
    static void Check(bool condition, string message)
    { assertions++; if (!condition) throw new Exception(message); }
    static GridPosition P(int x, int y) => new(x, y);
    static AgentDefinition A(int id, int x, int y, int gx, int gy) => new(id, P(x,y), P(gx,gy));
    static MapfPlannerSettings Settings() => new() { MaxTimeSteps = 72, MaxExpandedNodesPerAgent = 30000, MaxHighLevelNodes = 600, MaxPlanningMilliseconds = 1200 };
    static void Main()
    {
        try { RunAll(); }
        catch (Exception error) { Console.Error.WriteLine(error); Environment.ExitCode = 1; }
    }
    static void RunAll()
    {
        GoalReservations(); CollisionRecovery(); PartialPopulation(); ImpossibleSwap(); StartAtGoal();
        CbsTrials(); Serialization(); CampaignConditions(); Cancellation(); ProfilesAndMaps(); CbsOptimality(); RandomizedRuns(); Reports(); Examples();
        Console.WriteLine($"PASS: {assertions} assertions, {scenarios} executed scenarios. Unity UI/clipboard are not exercised by this standalone harness.");
    }
    static void GoalReservations()
    {
        var map = new GridMap(5, 3); var agents = new[] { A(0,0,1,4,1), A(1,2,0,2,1) };
        var result = Runner.Run(map, agents, MapfAlgorithmKind.CooperativeAStar, Settings());
        Check(result.CollisionFree, "Cooperative future goal reservation");
        Check(result.Paths.Find(p => p.AgentId == 1).MoveCost >= 3, "Arrival must be delayed until after a future crossing");
        foreach (AgentPath path in result.Paths) CheckPermanentPath(path, agents.First(a => a.Id == path.AgentId));
        RunSimulation(map, agents, result, true);
    }
    static MapfSimulation RunSimulation(GridMap map, IReadOnlyList<AgentDefinition> agents, MapfRunSummary result, bool mustComplete = false)
    {
        scenarios++; var simulation = new MapfSimulation(map, agents, result); int guard = 5000;
        var arrived = new HashSet<int>();
        while (!simulation.IsTerminal && --guard > 0)
        {
            var previous = agents.ToDictionary(a => a.Id, a => simulation.Position(a.Id));
            if (!simulation.PrepareStep())
            {
                Check(agents.All(a => previous[a.Id] == simulation.Position(a.Id)), "No movement during stop");
                if (simulation.IsTerminal) break;
                RecoveryRequest request = simulation.BeginRecovery(Settings(), true);
                if (request == null) break;
                foreach (AgentDefinition a in agents)
                    if (simulation.HasArrived(a.Id)) Check(request.Map.IsBlocked(a.Goal), "Arrived goal blocked for recovery");
                simulation.ApplyRecovery(MapfRecoveryPlanner.Plan(request));
                continue;
            }
            simulation.CommitStep();
            var occupied = new HashSet<GridPosition>();
            foreach (AgentDefinition a in agents)
            {
                GridPosition p = simulation.Position(a.Id);
                Check(occupied.Add(p), "Executed vertex conflict");
                Check(!map.IsBlocked(p), "Executed obstacle collision");
                Check(Math.Abs(p.X-previous[a.Id].X)+Math.Abs(p.Y-previous[a.Id].Y)<=1, "Skipped a logical step");
                if (arrived.Contains(a.Id)) Check(p == a.Goal, "Arrived agent moved");
                if (p == a.Goal) arrived.Add(a.Id);
                foreach (AgentDefinition b in agents)
                    if (a.Id != b.Id) Check(!(p == previous[b.Id] && simulation.Position(b.Id) == previous[a.Id]), "Executed edge swap");
            }
        }
        Check(guard > 0 && simulation.IsTerminal, "Execution must terminate");
        Check(simulation.Agents.Count == agents.Count && simulation.Paths.Count == agents.Count, "All agents remain represented");
        Check(ConflictDetector.FindConflicts(simulation.ExecutedPaths()).Count == 0, "Execution history conflict-free");
        if (mustComplete) Check(simulation.State == SimulationState.Completed, "Expected completed scenario: " + simulation.FailureReason);
        return simulation;
    }
    static void CheckPermanentPath(AgentPath path, AgentDefinition agent)
    {
        bool arrived = false;
        foreach (GridPosition p in path.Positions) { if (arrived) Check(p == agent.Goal, "Path leaves a reached goal"); if (p == agent.Goal) arrived = true; }
        Check(arrived, "Path reaches goal");
    }
    static void CollisionRecovery()
    {
        var map = new GridMap(5, 5); var agents = new[] { A(0,0,2,4,2), A(1,4,2,0,2) };
        var result = Runner.Run(map, agents, MapfAlgorithmKind.IndependentAStar, Settings());
        Check(result.Conflicts.Count > 0, "Independent A* must expose raw collision");
        MapfSimulation simulation = RunSimulation(map, agents, result, true);
        Check(simulation.Events.Any(e => e.kind == "stop") && simulation.Events.Any(e => e.kind == "attempt") && simulation.Events.Any(e => e.kind == "resume"), "Recovery trace complete");
        Check(simulation.RecoveryCount > 0 && simulation.Original.Algorithm == MapfAlgorithmKind.IndependentAStar, "Original algorithm preserved");
        var record = MapfCampaign.Record("cross", new CampaignCase { width=5,height=5,agentCount=2 }, simulation, 0);
        Check(!record.rawSuccess && record.assistedSuccess, "Raw and assisted metrics distinct");
    }
    static void PartialPopulation()
    {
        var map = new GridMap(12, 12); var agents = ProceduralMapGenerator.CreateRandomAgents(map, 32, 345, out _);
        var summary = new MapfRunSummary { Algorithm = MapfAlgorithmKind.CooperativeAStar, TotalAgents = 32 };
        for (int i = 0; i < 5; i++) summary.Paths.Add(new AStarPlanner().Search(map, agents[i]).Path);
        var simulation = new MapfSimulation(map, agents, summary);
        Check(simulation.Paths.Count == 32 && summary.RoutedAgents == 5, "Partial routes cannot hide 27 agents");
        Check(!simulation.PrepareStep() && simulation.Step == 0, "Unplanned agents stop everyone before movement");
        Check(agents.All(a => simulation.Position(a.Id) == a.Start), "Every unplanned agent remains at start");
        var tiny = Settings(); tiny.MaxExpandedNodesPerAgent = 1;
        var partial = Runner.Run(map, agents, MapfAlgorithmKind.CooperativeAStar, tiny);
        Check(new MapfSimulation(map, agents, partial).Paths.Count == 32, "Real failed planner keeps all 32 agents visible");
    }
    static void ImpossibleSwap()
    {
        var map = new GridMap(2,1); var agents = new[] { A(0,0,0,1,0), A(1,1,0,0,0) };
        var simulation = RunSimulation(map, agents, Runner.Run(map,agents,MapfAlgorithmKind.IndependentAStar,Settings()));
        Check(simulation.State == SimulationState.Failed && simulation.Step == 0, "Impossible edge swap fails without overlap");
    }
    static void StartAtGoal()
    {
        var map = new GridMap(4,4); var agents = new[] { A(0,1,1,1,1), A(1,0,1,3,1) };
        var simulation = RunSimulation(map,agents,Runner.Run(map,agents,MapfAlgorithmKind.IndependentAStar,Settings()),true);
        Check(simulation.Position(0) == P(1,1), "Initial arrival fixed forever");
        Check(simulation.ExecutedPaths()[0].Positions.All(p => p == P(1,1)), "Initial arrived history fixed");
        var reversed = new[] { agents[1], agents[0] };
        var coop = Runner.Run(map, reversed, MapfAlgorithmKind.CooperativeAStar, Settings());
        Check(coop.CollisionFree, "Start-at-goal agent is reserved even with last priority");
    }
    static void CbsTrials()
    {
        var map = new GridMap(5,5); var agents = new[] { A(0,0,2,4,2), A(1,4,2,0,2) };
        foreach (CBSHeuristicMode heuristic in Enum.GetValues<CBSHeuristicMode>())
        {
            var settings = Settings(); settings.CbsHeuristic = heuristic;
            var result = Runner.Run(map,agents,MapfAlgorithmKind.ConflictBasedSearch,settings);
            Check(result.CollisionFree, "CBS collision-free " + heuristic);
            Check(result.CbsReplayFrames.Any(f => f.IsConstraintTrial && !string.IsNullOrEmpty(f.AppliedConstraint)), "CBS new constraints visible");
            foreach (var path in result.Paths) CheckPermanentPath(path,agents.First(a => a.Id == path.AgentId));
            RunSimulation(map,agents,result,true);
            settings.RecordReplay=false;
            var compact=Runner.Run(map,agents,MapfAlgorithmKind.ConflictBasedSearch,settings);
            Check(compact.CollisionFree && compact.CbsReplayFrames.Count==0, "Campaign avoids retaining trial frames");
        }
    }
    static void Serialization()
    {
        var map=new GridMap(6,6); var agents=new[] { A(0,0,0,5,5), A(1,5,0,0,5) };
        var summary=Runner.Run(map,agents,MapfAlgorithmKind.CooperativeAStar,Settings());
        string json=MapfScenarioSerializer.ToJson(map,agents,new ScenarioDocument { placementSeed=0,algorithm=(int)summary.Algorithm },summary);
        var loaded=MapfScenarioSerializer.FromJson(json);
        Check(loaded.Agents.Count==2 && loaded.Replay.TotalAgents==2 && loaded.Settings.placementSeed==0, "Roundtrip count and zero seed");
        Check(loaded.Replay.AgentLogs.Select(l=>l.Message).SequenceEqual(summary.AgentLogs.Select(l=>l.Message)), "Replay retains agent decision logs");
        Check(loaded.Replay.PriorityDecisions.Select(d=>(d.AgentId,d.Rank,d.Score,d.Reason)).SequenceEqual(summary.PriorityDecisions.Select(d=>(d.AgentId,d.Rank,d.Score,d.Reason))), "Replay retains priority explanations");
        var profileSettings=Settings();profileSettings.Profile.CongestionWeight=0;
        var variant=Runner.Run(map,agents,MapfAlgorithmKind.CooperativeAStar,profileSettings);
        profileSettings.Profile.CongestionWeight=9;
        Check(variant.SettingsSnapshot.Profile.CongestionWeight==0,"Each comparison preserves its own independent profile");
        summary.Paths.Add(null);
        Check(MapfScenarioSerializer.FromJson(MapfScenarioSerializer.ToJson(map,agents,null,summary)).Agents.Count==2,"Partial/null CBS paths serialize");
        var manifest=new CampaignManifest { settings=Settings(),cases=MapfCampaign.BuildCases("8","4","0.1",1,0,0,MapTemplate.RandomObstacles) };
        var restored=UnityEngine.JsonUtility.FromJson<CampaignManifest>(UnityEngine.JsonUtility.ToJson(manifest));
        Check(restored.cases.Count==1 && restored.settings.MaxTimeSteps==72, "Checkpoint preserves settings");
    }
    static void CampaignConditions()
    {
        var cases=MapfCampaign.BuildCases("8x8,12x12","4,8","0.1,0.3",2,23,99,MapTemplate.RandomObstacles);
        Check(cases.Count==16,"Condition Cartesian product");
        foreach(var c in cases)
        {
            var first=MapfCampaign.Plan(c,MapfAlgorithmKind.CooperativeAStar,Settings());
            var second=MapfCampaign.Plan(c,MapfAlgorithmKind.CooperativeAStar,Settings());
            Check(first.Summary.Algorithm==MapfAlgorithmKind.CooperativeAStar && first.Agents.Count==c.agentCount,"Fixed campaign algorithm and requested population");
            Check(first.Agents.Select(a=>a.Start).SequenceEqual(second.Agents.Select(a=>a.Start)) && first.Agents.Select(a=>a.Goal).SequenceEqual(second.Agents.Select(a=>a.Goal)),"Deterministic placement");
            for(int y=0;y<c.height;y++) for(int x=0;x<c.width;x++) Check(first.Map.IsBlocked(P(x,y))==second.Map.IsBlocked(P(x,y)),"Deterministic map");
        }
        bool rejected=false; try { MapfCampaign.BuildCases("4","32","0.1",1,0,0,MapTemplate.Open); } catch(ArgumentException) { rejected=true; }
        Check(rejected,"Reject impossible requested population");
        rejected=false; try { MapfCampaign.BuildCases("8","4","NaN",1,0,0,MapTemplate.Open); } catch(ArgumentException) { rejected=true; }
        Check(rejected,"Reject NaN density");
    }
    static void Cancellation()
    {
        var source=new CancellationTokenSource(); source.Cancel();
        foreach(var algorithm in new[] { MapfAlgorithmKind.IndependentAStar,MapfAlgorithmKind.CooperativeAStar,MapfAlgorithmKind.ConflictBasedSearch })
        {
            bool cancelled=false; var settings=Settings(); settings.CancellationToken=source.Token;
            try { Runner.Run(new GridMap(8,8),new[] {A(0,0,0,7,7)},algorithm,settings); } catch(OperationCanceledException) { cancelled=true; }
            Check(cancelled,"Cancellation " + algorithm);
        }
    }
    static void RandomizedRuns()
    {
        foreach(var algorithm in new[] { MapfAlgorithmKind.IndependentAStar,MapfAlgorithmKind.CooperativeAStar,MapfAlgorithmKind.ConflictBasedSearch })
        foreach(int count in new[] {4,8,16,32})
        foreach(int seed in new[] {0,23,921})
        {
            var c=new CampaignCase { width=12,height=12,agentCount=count,requestedDensity=.2f,mapSeed=seed,placementSeed=seed+42,template=MapTemplate.RandomObstacles };
            var item=MapfCampaign.Plan(c,algorithm,Settings());
            var sim=RunSimulation(item.Map,item.Agents,item.Summary);
            Console.WriteLine($"scenario {algorithm,-21} n={count,2} seed={seed,3}: {sim.State}, raw={item.Summary.CollisionFree}, t={sim.Step}, recovery={sim.RecoveryCount}");
        }
    }
    static void Reports()
    {
        var record=new CampaignRecord {runId="comma,quote\"",scenario=new CampaignCase {width=8,height=8,agentCount=4},algorithm=MapfAlgorithmKind.CooperativeAStar,planningMs=12.5,terminalState="Failed",failureReason="a,b\n<c>",events=Array.Empty<SimulationEvent>()};
        CultureInfo old=CultureInfo.CurrentCulture; CultureInfo.CurrentCulture=new CultureInfo("fr-FR");
        string csv=MapfReportExporter.CampaignCsv(new[] {record});
        Check(csv.Contains("\"12.5\"") && csv.Contains("\"comma,quote\"\"\""),"Invariant and escaped CSV");
        XDocument.Parse(MapfReportExporter.CampaignChart(new[] {record}));
        var map=new GridMap(8,8);var agents=new[] {A(0,0,0,7,7)};var paths=new[] {new AStarPlanner().Search(map,agents[0]).Path};
        string svg=MapfReportExporter.MapSvg(map,agents,paths,1,"<title>","& caption");XDocument.Parse(svg);
        Check(svg.Contains("&lt;title&gt;") && !svg.Contains("NaN"),"Valid escaped SVG");
        XDocument.Parse(MapfReportExporter.ComparisonSvg(map,agents,new[] {paths,paths},new[] {"before","after"},0,"comparison"));
        CultureInfo.CurrentCulture=old;
        string artifacts=Path.Combine(Environment.CurrentDirectory,".validation","figures");Directory.CreateDirectory(artifacts);
        File.WriteAllText(Path.Combine(artifacts,"map.svg"),svg);
        File.WriteAllText(Path.Combine(artifacts,"chart.svg"),MapfReportExporter.CampaignChart(new[] {record}));
        Check(MapfStatistics.ConditionSummaryCsv(new[]{record}).Contains("variance_population"),"Variance and standard deviation export");
    }
    static void ProfilesAndMaps()
    {
        foreach(GridHeuristic heuristic in Enum.GetValues<GridHeuristic>())
        foreach(NeighborOrder order in Enum.GetValues<NeighborOrder>())
        foreach(SearchTieBreak tie in Enum.GetValues<SearchTieBreak>())
        {
            var settings=Settings();settings.Profile.Heuristic=heuristic;settings.Profile.Neighbors=order;settings.Profile.TieBreak=tie;
            var result=Runner.Run(new GridMap(6,6),new[]{A(0,0,0,5,5)},MapfAlgorithmKind.IndependentAStar,settings);
            Check(result.AllAgentsRouted&&result.TotalMoveCost==10,"Admissible configured A* remains optimal");
        }
        var cases=MapfCampaign.BuildCases("12","4","0",1,23,24,MapTemplate.Bottleneck,"1,2","1,3",AgentDistribution.OpposingSides);
        Check(cases.Count==4,"Bottleneck count and width Cartesian product");
        foreach(var c in cases)
        {
            var item=MapfCampaign.Plan(c,MapfAlgorithmKind.CooperativeAStar,Settings());
            Check(ScenarioValidator.UnreachableAgents(item.Map,item.Agents).Count==0,"Generated placements connected");
            Check(item.Agents.SelectMany(a=>new[]{a.Start,a.Goal}).Distinct().Count()==8,"Opposed endpoints unique");
        }
        var corridor=ProceduralMapGenerator.Create(12,12,MapTemplate.NarrowCorridors,0,23,1,1);
        var agents=ProceduralMapGenerator.CreateRandomAgents(corridor,8,17,out _,AgentDistribution.CornerClusters);
        Check(ScenarioValidator.UnreachableAgents(corridor,agents).Count==0,"Narrow corridor connected");
        var map=new GridMap(4,4);var pair=new[]{A(0,0,0,3,3),A(1,3,0,0,3)};
        var original=Runner.Run(map,pair,MapfAlgorithmKind.CooperativeAStar,Settings());
        var sim=RunSimulation(map,pair,original,true);
        var replay=MapfSimulation.FromHistory(map,pair,original,sim.ExecutedPaths());
        Check(replay.Step==sim.Step&&replay.ArrivedCount==2&&replay.ExecutedCost==sim.ExecutedCost,"Execution history restored exactly");
    }
    static void CbsOptimality()
    {
        foreach(int seed in new[]{3,11,23,41})
        {
            var map=new GridMap(4,4);var agents=ProceduralMapGenerator.CreateRandomAgents(map,2,seed,out _);
            int optimum=JointOracle(map,agents);
            foreach(CBSHeuristicMode heuristic in Enum.GetValues<CBSHeuristicMode>())
            {
                var settings=Settings();settings.CbsHeuristic=heuristic;settings.MaxPlanningMilliseconds=5000;
                var result=Runner.Run(map,agents,MapfAlgorithmKind.ConflictBasedSearch,settings);
                Check(result.CollisionFree&&result.TotalMoveCost==optimum,"CBS/DG/WDG matches exhaustive two-agent optimum");
            }
        }
    }
    static void Examples()
    {
        string directory=Path.Combine(Environment.CurrentDirectory,".validation","examples");Directory.CreateDirectory(directory);
        var crossingMap=new GridMap(5,5);var pair=new[]{A(0,0,2,4,2),A(1,4,2,0,2)};
        var settings=Settings();var crossing=Runner.Run(crossingMap,pair,MapfAlgorithmKind.IndependentAStar,settings);
        File.WriteAllText(Path.Combine(directory,"01-crossing.json"),MapfScenarioSerializer.ToJson(crossingMap,pair,new ScenarioDocument{algorithm=(int)MapfAlgorithmKind.IndependentAStar,seed=23,placementSeed=42,mapTemplate=(int)MapTemplate.Open,obstacleDensity=0,maxTimeSteps=settings.MaxTimeSteps,maxExpandedNodesPerAgent=settings.MaxExpandedNodesPerAgent,maxHighLevelNodes=settings.MaxHighLevelNodes,maxPlanningMilliseconds=settings.MaxPlanningMilliseconds},crossing));
        foreach(var item in new[]{("02-bottleneck.json",12,8,MapTemplate.Bottleneck), ("03-random-32.json",32,32,MapTemplate.RandomObstacles)})
        {
            var map=ProceduralMapGenerator.Create(item.Item2,item.Item2,item.Item4,.2,23,2,3);
            var agents=ProceduralMapGenerator.CreateRandomAgents(map,item.Item3,42,out _);
            string json=MapfScenarioSerializer.ToJson(map,agents,new ScenarioDocument{algorithm=(int)MapfAlgorithmKind.CooperativeAStar,seed=23,placementSeed=42,mapTemplate=(int)item.Item4,obstacleDensity=.2f,bottleneckCount=2,passageWidth=3});
            Check(MapfScenarioSerializer.FromJson(json).Agents.Count==item.Item3,"Delivered example is loadable");
            File.WriteAllText(Path.Combine(directory,item.Item1),json);
        }
        File.WriteAllText(Path.Combine(directory,"profile.json"),UnityEngine.JsonUtility.ToJson(new MapfPlannerSettings{PriorityMode=AgentPriorityMode.MostConstrainedFirst},true));
        var summaries=new List<MapfRunSummary>();
        foreach(var algorithm in new[]{MapfAlgorithmKind.IndependentAStar,MapfAlgorithmKind.CooperativeAStar,MapfAlgorithmKind.ConflictBasedSearch})
            summaries.Add(Runner.Run(crossingMap,pair,algorithm,settings));
        File.WriteAllText(Path.Combine(directory,"crossing-results.csv"),MapfCsvExporter.Export(summaries),new System.Text.UTF8Encoding(true));
    }
    static int JointOracle(GridMap map,List<AgentDefinition> agents)
    {
        var start=(agents[0].Start,agents[1].Start);var queue=new PriorityQueue<(GridPosition,GridPosition),int>();
        var best=new Dictionary<(GridPosition,GridPosition),int>{{start,0}};queue.Enqueue(start,0);
        while(queue.TryDequeue(out var current,out int cost))
        {
            if(best[current]!=cost)continue;
            if(current.Item1==agents[0].Goal&&current.Item2==agents[1].Goal)return cost;
            IEnumerable<GridPosition> first=current.Item1==agents[0].Goal?new[]{current.Item1}:map.GetNeighbors(current.Item1,true);
            IEnumerable<GridPosition> second=current.Item2==agents[1].Goal?new[]{current.Item2}:map.GetNeighbors(current.Item2,true);
            foreach(var a in first)foreach(var b in second)
            {
                if(a==b||(a==current.Item2&&b==current.Item1))continue;
                int nextCost=cost+(current.Item1==agents[0].Goal?0:1)+(current.Item2==agents[1].Goal?0:1);var next=(a,b);
                if(best.TryGetValue(next,out int old)&&old<=nextCost)continue;best[next]=nextCost;queue.Enqueue(next,nextCost);
            }
        }
        return -1;
    }
}
