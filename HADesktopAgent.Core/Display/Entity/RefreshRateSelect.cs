using HADesktopAgent.Core.Entity;
using Microsoft.Extensions.Logging;

namespace HADesktopAgent.Core.Display.Entity
{
    /// <summary>
    /// A per-monitor HA select entity exposing the refresh rates available at the
    /// monitor's current resolution (e.g. "60 Hz", "120 Hz", "144 Hz").
    /// </summary>
    public class RefreshRateSelect : IHaStatefulEntity, IHaCommandableEntity, IHaSelectableEntity, IDisposable
    {
        public string Name { get; }
        public string PrettyName { get; }
        public string UniqueId { get; }
        public string Icon => "mdi:sine-wave";
        public string EntityType => "select";
        public bool Optimistic => false;

        public SortedSet<string> Options => _options;
        public string? State => _currentRate is int rate ? FormatRate(rate) : null;

        public event IHaStatefulEntity.StateUpdatedHandler? StateUpdated;
        public event IHaEntity.ConfigUpdatedHandler? ConfigUpdated;

        private readonly ILogger _logger;
        private readonly string _originalMonitorName;
        private readonly IRefreshRateController _refreshRateController;
        private readonly IDisplayWatcher _displayWatcher;
        private readonly object _updateLock = new();

        private SortedSet<string> _options;
        private int? _currentRate;

        public RefreshRateSelect(
            ILogger logger,
            string displayName,
            string originalMonitorName,
            IRefreshRateController refreshRateController,
            IDisplayWatcher displayWatcher)
        {
            _logger = logger;
            _originalMonitorName = originalMonitorName;
            _refreshRateController = refreshRateController;
            _displayWatcher = displayWatcher;

            Name = "refresh_rate_" + SanitizeName(displayName);
            PrettyName = $"{displayName} Refresh Rate";
            UniqueId = Name;

            _options = new SortedSet<string>(RateComparer.Instance);
            RefreshCore();

            _displayWatcher.DisplaySettingsUpdated += HandleDisplaySettingsUpdated;
        }

        public void HandleCommand(string command)
        {
            var rateToken = command.Split(' ')[0];
            if (!int.TryParse(rateToken, out var rate))
            {
                _logger.LogWarning("Invalid refresh rate command for {Entity}: {Command}", Name, command);
                return;
            }

            if (!_refreshRateController.SetRefreshRate(_originalMonitorName, rate))
            {
                _logger.LogWarning("Failed to set refresh rate {Rate}Hz on '{Monitor}'", rate, _originalMonitorName);
            }

            RefreshCore();
        }

        private void HandleDisplaySettingsUpdated()
        {
            // Fires on platform callback threads; an escaping exception would be lost.
            try
            {
                RefreshCore();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to refresh rates for '{Monitor}'", _originalMonitorName);
            }
        }

        private void RefreshCore()
        {
            lock (_updateLock)
            {
                var rates = _refreshRateController.GetAvailableRefreshRates(_originalMonitorName);
                var currentRate = _refreshRateController.GetCurrentRefreshRate(_originalMonitorName);

                // An inactive/disconnected monitor reports no rates; keep the last known
                // options so the select stays usable in HA, and only clear the state.
                if (rates.Count > 0)
                {
                    var newOptions = new SortedSet<string>(rates.Select(FormatRate), RateComparer.Instance);
                    if (!_options.SetEquals(newOptions))
                    {
                        _options = newOptions;
                        ConfigUpdated?.Invoke(this);
                    }
                }

                if (_currentRate != currentRate)
                {
                    _currentRate = currentRate;
                    StateUpdated?.Invoke(this);
                }
            }
        }

        private static string FormatRate(int rate) => $"{rate} Hz";

        private static string SanitizeName(string name)
        {
            return name.ToLowerInvariant()
                .Replace(' ', '_')
                .Replace('-', '_');
        }

        /// <summary>
        /// Orders "N Hz" options numerically so 60 Hz sorts before 120 Hz.
        /// </summary>
        private sealed class RateComparer : IComparer<string>
        {
            public static readonly RateComparer Instance = new();

            public int Compare(string? x, string? y)
            {
                var xParsed = int.TryParse(x?.Split(' ')[0], out var xRate);
                var yParsed = int.TryParse(y?.Split(' ')[0], out var yRate);

                if (xParsed && yParsed)
                    return xRate.CompareTo(yRate);

                return string.CompareOrdinal(x, y);
            }
        }

        public void Dispose()
        {
            _displayWatcher.DisplaySettingsUpdated -= HandleDisplaySettingsUpdated;
        }
    }
}
