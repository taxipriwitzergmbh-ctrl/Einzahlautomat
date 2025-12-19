using System;

namespace Geldautomat
{
    // Globaler Snapshot der zuletzt ermittelten Summen aus KassenbestandForm
    public static class KassenSummary
    {
        private static readonly object _lock = new object();
        private static decimal? _sumAutomatEuro;
        private static decimal? _sumKassenEuro;
        private static DateTime? _lastUpdated;

        public static decimal? SumAutomatEuro { get { lock (_lock) { return _sumAutomatEuro; } } }
        public static decimal? SumKassenEuro { get { lock (_lock) { return _sumKassenEuro; } } }
        public static DateTime? LastUpdated { get { lock (_lock) { return _lastUpdated; } } }
        public static decimal? Difference
        {
            get
            {
                lock (_lock)
                {
                    if (_sumAutomatEuro.HasValue && _sumKassenEuro.HasValue)
                        return _sumAutomatEuro.Value - _sumKassenEuro.Value;
                    return null;
                }
            }
        }

        public static void Update(decimal sumAutomatEuro, decimal sumKassenEuro)
        {
            lock (_lock)
            {
                _sumAutomatEuro = sumAutomatEuro;
                _sumKassenEuro = sumKassenEuro;
                _lastUpdated = DateTime.UtcNow;
            }
        }

        public static void Clear()
        {
            lock (_lock)
            {
                _sumAutomatEuro = null;
                _sumKassenEuro = null;
                _lastUpdated = null;
            }
        }
    }
}
