using System;
using System.Collections.Concurrent;
using System.Threading;

namespace GolfStatsApp.Server.Services.Prediction
{
    /// <summary>Caps concurrent simulation runs per server and per client (NFR-5).</summary>
    public class SimulationConcurrencyLimiter
    {
        private readonly PredictiveModelingOptions _options;
        private readonly ConcurrentDictionary<string, int> _perClient = new();
        private int _active;

        public SimulationConcurrencyLimiter(PredictiveModelingOptions options) => _options = options;

        public int Active => Volatile.Read(ref _active);

        public IDisposable? TryAcquire(string clientKey)
        {
            if (Interlocked.Increment(ref _active) > _options.MaxConcurrentRuns)
            {
                Interlocked.Decrement(ref _active);
                return null;
            }

            var count = _perClient.AddOrUpdate(clientKey, 1, (_, c) => c + 1);
            if (count > _options.MaxConcurrentRunsPerClient)
            {
                Release(clientKey);
                return null;
            }
            return new Lease(this, clientKey);
        }

        private void Release(string clientKey)
        {
            Interlocked.Decrement(ref _active);
            var remaining = _perClient.AddOrUpdate(clientKey, 0, (_, c) => Math.Max(0, c - 1));
            if (remaining == 0) _perClient.TryRemove(new System.Collections.Generic.KeyValuePair<string, int>(clientKey, 0));
        }

        private sealed class Lease : IDisposable
        {
            private SimulationConcurrencyLimiter? _owner;
            private readonly string _key;

            public Lease(SimulationConcurrencyLimiter owner, string key)
            {
                _owner = owner;
                _key = key;
            }

            public void Dispose() => Interlocked.Exchange(ref _owner, null)?.Release(_key);
        }
    }
}
