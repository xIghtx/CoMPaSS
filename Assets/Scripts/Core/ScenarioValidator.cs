using System.Collections.Generic;
namespace CoMPaSS.Core
{
    public static class ScenarioValidator
    {
        public static List<int> UnreachableAgents(GridMap map, IReadOnlyList<AgentDefinition> agents)
        {
            var regions = new Dictionary<GridPosition,int>(); int region = 0;
            for (int y=0;y<map.Height;y++) for(int x=0;x<map.Width;x++)
            {
                var start=new GridPosition(x,y);if(map.IsBlocked(start)||regions.ContainsKey(start))continue;
                var queue=new Queue<GridPosition>();queue.Enqueue(start);regions[start]=++region;
                while(queue.Count>0) foreach(GridPosition p in map.GetNeighbors(queue.Dequeue()))
                    if(!regions.ContainsKey(p)){regions[p]=region;queue.Enqueue(p);}
            }
            var invalid=new List<int>();
            foreach(AgentDefinition a in agents)
                if(!regions.TryGetValue(a.Start,out int first)||!regions.TryGetValue(a.Goal,out int second)||first!=second) invalid.Add(a.Id);
            return invalid;
        }
    }
}
