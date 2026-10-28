using System;
using System.Collections.Generic;

namespace CoMPaSS.Core
{
    public enum MapTemplate
    {
        Open,
        RandomObstacles,
        Crossroads,
        Bottleneck,
        NarrowCorridors
    }
    public enum AgentDistribution { Random, OpposingSides, CornerClusters }

    public static class ProceduralMapGenerator
    {
        public static GridMap Create(int width, int height, MapTemplate template, double obstacleDensity, int seed, int bottleneckCount = 1, int passageWidth = 3)
        {
            switch (template)
            {
                case MapTemplate.Open:
                    return new GridMap(width, height);
                case MapTemplate.RandomObstacles:
                    return CreateRandomObstacles(width, height, obstacleDensity, seed);
                case MapTemplate.Crossroads:
                    return CreateCrossroads(width, height, obstacleDensity, seed);
                case MapTemplate.Bottleneck:
                    return CreateBottleneck(width, height, obstacleDensity, seed, bottleneckCount, passageWidth);
                case MapTemplate.NarrowCorridors:
                    return CreateCorridors(width, height, passageWidth);
                default:
                    throw new ArgumentOutOfRangeException(nameof(template));
            }
        }

        /// <summary>Creates a reproducible random-obstacle map. Endpoints are assigned separately.</summary>
        public static GridMap CreateRandomObstacles(int width, int height, double obstacleDensity, int seed)
        {
            if (double.IsNaN(obstacleDensity) || obstacleDensity < 0.0 || obstacleDensity > 1.0)
                throw new ArgumentOutOfRangeException(nameof(obstacleDensity), "Density must be between 0 and 1.");

            var map = new GridMap(width, height);
            var random = new SeededRandom(unchecked((uint)seed));
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                map.SetBlocked(new GridPosition(x, y), random.NextUnitDouble() < obstacleDensity);
            return map;
        }

        /// <summary>Creates reproducible, distinct endpoints inside one connected walkable region.</summary>
        public static List<AgentDefinition> CreateRandomAgents(GridMap map, int count, int seed, out int openedCells, AgentDistribution distribution = AgentDistribution.Random)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            if (count < 1 || count * 2 > map.Width * map.Height)
                throw new ArgumentOutOfRangeException(nameof(count), "There must be room for distinct starts and goals.");

            openedCells = 0;
            List<GridPosition> region = FindLargestOpenRegion(map);
            var random = new SeededRandom(unchecked((uint)seed));
            if (region.Count == 0)
            {
                GridPosition first = new GridPosition(random.NextInt(map.Width), random.NextInt(map.Height));
                map.SetBlocked(first, false);
                region.Add(first);
                openedCells++;
            }
            while (region.Count < count * 2)
            {
                var frontier = new List<GridPosition>();
                var seen = new HashSet<GridPosition>();
                for (int i = 0; i < region.Count; i++)
                foreach (GridPosition neighbor in GetAdjacentCells(map, region[i]))
                    if (map.IsBlocked(neighbor) && seen.Add(neighbor)) frontier.Add(neighbor);
                if (frontier.Count == 0) throw new InvalidOperationException("랜덤 배치를 위한 연결된 이동 가능 영역을 만들 수 없습니다.");
                GridPosition opened = frontier[random.NextInt(frontier.Count)];
                map.SetBlocked(opened, false);
                // The opened cell may join an existing component. Include it before opening more cells.
                region = FindLargestOpenRegion(map);
                openedCells++;
            }

            for (int i = region.Count - 1; i > 0; i--)
            {
                int swap = random.NextInt(i + 1);
                GridPosition value = region[i];
                region[i] = region[swap];
                region[swap] = value;
            }

            var agents = new List<AgentDefinition>(count);
            if (distribution != AgentDistribution.Random)
            {
                // Stable sort retains the seeded shuffle among equal-position bands.
                for (int i=1;i<region.Count;i++)
                {
                    GridPosition p=region[i]; int score=distribution==AgentDistribution.OpposingSides?p.X:p.X+p.Y; int j=i-1;
                    while(j>=0 && (distribution==AgentDistribution.OpposingSides?region[j].X:region[j].X+region[j].Y)>score)
                    {region[j+1]=region[j];j--;}
                    region[j+1]=p;
                }
                for(int i=0;i<count;i++)
                {
                    GridPosition start=region[i],goal=region[region.Count-1-i];
                    if(distribution==AgentDistribution.OpposingSides && i%2==1){GridPosition swap=start;start=goal;goal=swap;}
                    agents.Add(new AgentDefinition(i,start,goal));
                }
                return agents;
            }
            for (int i = 0; i < count; i++)
                agents.Add(new AgentDefinition(i, region[i], region[count + i]));
            return agents;
        }

        private static List<GridPosition> FindLargestOpenRegion(GridMap map)
        {
            var visited = new HashSet<GridPosition>();
            var largest = new List<GridPosition>();
            for (int y = 0; y < map.Height; y++)
            for (int x = 0; x < map.Width; x++)
            {
                var start = new GridPosition(x, y);
                if (map.IsBlocked(start) || !visited.Add(start)) continue;
                var region = new List<GridPosition> { start };
                var queue = new Queue<GridPosition>();
                queue.Enqueue(start);
                while (queue.Count > 0)
                {
                    GridPosition current = queue.Dequeue();
                    foreach (GridPosition neighbor in map.GetNeighbors(current))
                        if (visited.Add(neighbor)) { region.Add(neighbor); queue.Enqueue(neighbor); }
                }
                if (region.Count > largest.Count) largest = region;
            }
            return largest;
        }

        private static IEnumerable<GridPosition> GetAdjacentCells(GridMap map, GridPosition cell)
        {
            GridPosition[] offsets = { new GridPosition(0, 1), new GridPosition(1, 0), new GridPosition(0, -1), new GridPosition(-1, 0) };
            for (int i = 0; i < offsets.Length; i++)
            {
                var neighbor = new GridPosition(cell.X + offsets[i].X, cell.Y + offsets[i].Y);
                if (map.Contains(neighbor)) yield return neighbor;
            }
        }

        private static GridMap CreateCrossroads(int width, int height, double density, int seed)
        {
            GridMap map = CreateRandomObstacles(width, height, density, seed);
            int centerX = width / 2;
            int centerY = height / 2;
            for (int x = 0; x < width; x++) map.SetBlocked(new GridPosition(x, centerY), false);
            for (int y = 0; y < height; y++) map.SetBlocked(new GridPosition(centerX, y), false);
            return map;
        }

        private static GridMap CreateBottleneck(int width, int height, double density, int seed, int count, int passageWidth)
        {
            GridMap map = CreateRandomObstacles(width, height, density, seed);
            count=Math.Max(1,Math.Min(Math.Min(8,width-2),count));passageWidth=Math.Max(1,Math.Min(height,passageWidth));
            for(int wall=1;wall<=count;wall++)
            {
                int wallX=wall*(width-1)/(count+1), start=Math.Max(0,(height-passageWidth)/2);
                for(int y=0;y<height;y++) map.SetBlocked(new GridPosition(wallX,y),y<start||y>=start+passageWidth);
            }
            return map;
        }
        private static GridMap CreateCorridors(int width,int height,int passageWidth)
        {
            var map=new GridMap(width,height);int passage=Math.Max(1,Math.Min(8,passageWidth));
            for(int y=0;y<height;y++)for(int x=0;x<width;x++)
                map.SetBlocked(new GridPosition(x,y),y%(passage+1)==passage && x!=((y/(passage+1))%2==0?width-1:0));
            return map;
        }

        // Fixed xorshift32 sequence makes map generation stable across .NET/Unity runtime versions.
        private struct SeededRandom
        {
            private uint state;

            public SeededRandom(uint seed)
            {
                state = seed == 0 ? 0x6D2B79F5u : seed;
            }

            public double NextUnitDouble()
            {
                uint value = state;
                value ^= value << 13;
                value ^= value >> 17;
                value ^= value << 5;
                state = value;
                return value / 4294967296.0;
            }

            public int NextInt(int exclusiveMaximum) => (int)(NextUnitDouble() * exclusiveMaximum);
        }
    }
}
