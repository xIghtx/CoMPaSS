using System;
using CoMPaSS.Core;

namespace CoMPaSS.Algorithms
{
    public enum GridHeuristic { Manhattan, Euclidean, Zero }
    public enum NeighborOrder { UpRightDownLeft, RightDownLeftUp, DownLeftUpRight, LeftUpRightDown }
    public enum SearchTieBreak { LowerHeuristic, InsertionOrder }
    [Serializable]
    public sealed class SearchProfile
    {
        public GridHeuristic Heuristic;
        public float Weight = 1;
        public NeighborOrder Neighbors;
        public SearchTieBreak TieBreak;
        public int DistanceWeight = 1, CongestionWeight = 3, NarrownessWeight = 2;
        public int WaitWeight = 2, RecoveryWeight = 3;
        public int Seed = 23;
        public bool AllowWait = true;
        public SearchProfile Copy() => (SearchProfile)MemberwiseClone();
        public double Estimate(GridPosition from, GridPosition goal)
        {
            int x = Math.Abs(from.X - goal.X), y = Math.Abs(from.Y - goal.Y);
            double h = Heuristic == GridHeuristic.Zero ? 0 : Heuristic == GridHeuristic.Euclidean ? Math.Sqrt(x*x+y*y) : x+y;
            float weight = float.IsNaN(Weight) ? 1 : Math.Max(0, Math.Min(5, Weight));
            return h * weight;
        }
    }
}
