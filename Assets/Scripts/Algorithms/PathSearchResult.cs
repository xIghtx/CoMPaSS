using CoMPaSS.Core;

namespace CoMPaSS.Algorithms
{
    public enum PathSearchStatus
    {
        Success,
        NoPath,
        InvalidInput,
        SearchLimitReached
    }

    public sealed class PathSearchResult
    {
        public PathSearchStatus Status { get; }
        public AgentPath Path { get; }
        public int ExpandedNodes { get; }
        public string FailureReason { get; }

        public bool Succeeded => Status == PathSearchStatus.Success;

        internal PathSearchResult(PathSearchStatus status, AgentPath path, int expandedNodes, string failureReason)
        {
            Status = status;
            Path = path;
            ExpandedNodes = expandedNodes;
            FailureReason = failureReason;
        }
    }
}
