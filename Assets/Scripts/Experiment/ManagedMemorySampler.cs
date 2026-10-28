using System;
using System.Threading;

namespace CoMPaSS.Experiment
{
    // Process-wide managed heap sample, not an exact algorithm-exclusive memory peak.
    internal sealed class ManagedMemorySampler : IDisposable
    {
        private long peak;
        private readonly Timer timer;
        public long PeakBytes { get { Sample(null); return Interlocked.Read(ref peak); } }
        public ManagedMemorySampler() { peak = GC.GetTotalMemory(false); timer = new Timer(Sample, null, 20, 20); }
        private void Sample(object state)
        {
            long value = GC.GetTotalMemory(false), old = Interlocked.Read(ref peak);
            while (value > old) { long current = Interlocked.CompareExchange(ref peak, value, old); if (current == old) break; old = current; }
        }
        public void Dispose() { timer.Dispose(); }
    }
}
