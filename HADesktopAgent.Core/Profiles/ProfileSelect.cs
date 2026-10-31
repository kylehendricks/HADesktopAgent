using HADesktopAgent.Core.Audio;
using HADesktopAgent.Core.Display;
using HADesktopAgent.Core.Entity;
using Microsoft.Extensions.Logging;

namespace HADesktopAgent.Core.Profiles
{
    /// <summary>
    /// A select whose options are the configured <see cref="ProfileConfiguration"/>s.
    /// Selecting one enables exactly its monitors, then sets its audio output and
    /// refresh rates. The state is whichever profile matches the active monitors right
    /// now, so it stays correct when displays are changed some other way.
    /// </summary>
    public sealed class ProfileSelect : IHaStatefulEntity, IHaCommandableEntity, IHaSelectableEntity, IDisposable
    {
        /// <summary>
        /// State payload for "no profile matches". An MQTT select clears its current
        /// option on <c>None</c>; an empty payload would just be ignored, leaving HA
        /// showing a profile that is no longer active.
        /// </summary>
        public const string NoProfileState = "None";

        /// <summary>
        /// How long audio and refresh-rate steps keep retrying after the monitors switch.
        /// A newly enabled display takes a moment to report its modes, and its HDMI audio
        /// sink only appears once the display is up.
        /// </summary>
        private const int FollowUpAttempts = 15;
        private static readonly TimeSpan FollowUpInterval = TimeSpan.FromSeconds(1);

        public string Name => "profile";
        public string PrettyName => "Profile";
        public string UniqueId => "profile";
        public string Icon => "mdi:monitor-multiple";
        public string EntityType => "select";
        public bool Optimistic => false;

        public SortedSet<string> Options { get; }
        public string? State => _activeProfile ?? NoProfileState;

        public event IHaStatefulEntity.StateUpdatedHandler? StateUpdated;
        public event IHaEntity.ConfigUpdatedHandler? ConfigUpdated { add { } remove { } }

        private readonly ILogger<ProfileSelect> _logger;
        private readonly IReadOnlyDictionary<string, ProfileConfiguration> _profiles;
        private readonly IDisplayWatcher _displayWatcher;
        private readonly IMonitorSwitcher _monitorSwitcher;
        private readonly MonitorNameResolver _monitorNames;
        private readonly IAudioManager _audioManager;
        private readonly AudioNameResolver _audioNames;
        private readonly IRefreshRateController? _refreshRateController;

        private readonly object _stateLock = new();
        private string? _activeProfile;

        // Applies run off the MQTT thread and one at a time; a newer selection cancels
        // the follow-up retries of the one before it.
        private readonly SemaphoreSlim _applyGate = new(1, 1);
        private readonly object _applyLock = new();
        private CancellationTokenSource? _applyCts;

        public ProfileSelect(
            ILogger<ProfileSelect> logger,
            IReadOnlyDictionary<string, ProfileConfiguration> profiles,
            IDisplayWatcher displayWatcher,
            IMonitorSwitcher monitorSwitcher,
            MonitorNameResolver monitorNames,
            IAudioManager audioManager,
            AudioNameResolver audioNames,
            IRefreshRateController? refreshRateController)
        {
            _logger = logger;
            _profiles = profiles;
            _displayWatcher = displayWatcher;
            _monitorSwitcher = monitorSwitcher;
            _monitorNames = monitorNames;
            _audioManager = audioManager;
            _audioNames = audioNames;
            _refreshRateController = refreshRateController;

            Options = new SortedSet<string>(profiles.Keys, StringComparer.Ordinal);

            UpdateState();

            _displayWatcher.ActiveMonitorsUpdated += HandleDisplaysChanged;
            _displayWatcher.AvailableMonitorsUpdated += HandleDisplaysChanged;
            _audioManager.DeviceChanged += HandleAudioChanged;
        }

        public void HandleCommand(string command)
        {
            if (!_profiles.TryGetValue(command, out var profile))
            {
                _logger.LogWarning("Unknown profile '{Profile}'", command);
                return;
            }

            CancellationToken token;
            lock (_applyLock)
            {
                _applyCts?.Cancel();
                _applyCts?.Dispose();
                _applyCts = new CancellationTokenSource();
                token = _applyCts.Token;
            }

            _ = Task.Run(() => ApplyAsync(command, profile, token));
        }

        private async Task ApplyAsync(string profileName, ProfileConfiguration profile, CancellationToken ct)
        {
            try
            {
                await _applyGate.WaitAsync(ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            try
            {
                _logger.LogInformation("Applying profile '{Profile}'", profileName);

                if (!ApplyMonitors(profileName, profile))
                    return;

                var steps = BuildFollowUpSteps(profileName, profile);
                for (var attempt = 0; steps.Count > 0 && attempt < FollowUpAttempts; attempt++)
                {
                    if (attempt > 0)
                        await Task.Delay(FollowUpInterval, ct);

                    steps.RemoveAll(step => step.Run() != StepResult.Retry);
                }

                foreach (var step in steps)
                {
                    _logger.LogWarning("Profile '{Profile}': gave up on {Step} after {Seconds}s",
                        profileName, step.Description, FollowUpAttempts * FollowUpInterval.TotalSeconds);
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Profile '{Profile}' superseded before it finished applying", profileName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to apply profile '{Profile}'", profileName);
            }
            finally
            {
                _applyGate.Release();
            }
        }

        private bool ApplyMonitors(string profileName, ProfileConfiguration profile)
        {
            var available = _monitorNames.GetAvailableByDisplayName();
            var enabled = new HashSet<string>();

            foreach (var monitor in profile.Monitors)
            {
                if (available.TryGetValue(monitor, out var original))
                    enabled.Add(original);
                else
                    _logger.LogWarning("Profile '{Profile}': monitor '{Monitor}' is not connected, skipping it",
                        profileName, monitor);
            }

            if (enabled.Count == 0)
            {
                _logger.LogWarning("Refusing to apply profile '{Profile}' — none of its monitors are connected", profileName);
                return false;
            }

            // Re-applying an identical layout still makes the screens blank on some setups.
            if (_displayWatcher.ActiveMonitors.SetEquals(enabled))
                return true;

            _logger.LogInformation("Profile '{Profile}': enabling [{Monitors}]", profileName, string.Join(", ", enabled));
            if (!_monitorSwitcher.ApplyConfiguration(enabled))
            {
                _logger.LogError("Profile '{Profile}': failed to apply the monitor layout", profileName);
                return false;
            }

            return true;
        }

        private enum StepResult { Done, Retry, Failed }

        private sealed record FollowUpStep(string Description, Func<StepResult> Run);

        private List<FollowUpStep> BuildFollowUpSteps(string profileName, ProfileConfiguration profile)
        {
            var steps = new List<FollowUpStep>();

            if (!string.IsNullOrWhiteSpace(profile.Audio))
            {
                var audio = profile.Audio;
                var confirmations = 0;
                steps.Add(new FollowUpStep($"audio output '{audio}'", () => SetAudio(audio, ref confirmations)));
            }

            if (profile.RefreshRates.Count > 0 && _refreshRateController == null)
            {
                _logger.LogWarning("Profile '{Profile}' sets refresh rates, but this platform can't change them", profileName);
            }
            else
            {
                foreach (var (monitor, rate) in profile.RefreshRates)
                {
                    steps.Add(new FollowUpStep($"{rate}Hz on '{monitor}'", () => SetRefreshRate(profileName, monitor, rate)));
                }
            }

            return steps;
        }

        /// <summary>
        /// Switching displays makes the GPU's HDMI audio device re-probe, which removes
        /// and recreates its sinks a moment later. A default set in that window is lost,
        /// so the output only counts as switched once it has stayed the default for
        /// <see cref="AudioConfirmations"/> consecutive checks.
        /// </summary>
        private const int AudioConfirmations = 3;

        private StepResult SetAudio(string audioName, ref int confirmations)
        {
            var device = _audioManager.GetAudioDevices().Find(d => _audioNames.Resolve(d) == audioName);
            if (device == null)
            {
                confirmations = 0;
                return StepResult.Retry;
            }

            if (device.IsActive)
                return ++confirmations >= AudioConfirmations ? StepResult.Done : StepResult.Retry;

            confirmations = 0;
            _logger.LogInformation("Setting audio output to '{Audio}'", audioName);
            _audioManager.SetActiveDevice(device.Id);
            return StepResult.Retry;
        }

        private StepResult SetRefreshRate(string profileName, string monitor, int rate)
        {
            if (!_monitorNames.GetAvailableByDisplayName().TryGetValue(monitor, out var original))
                return StepResult.Retry;

            if (_refreshRateController!.GetCurrentRefreshRate(original) == rate)
                return StepResult.Done;

            var available = _refreshRateController.GetAvailableRefreshRates(original);

            // An empty list means the monitor isn't active yet.
            if (available.Count == 0)
                return StepResult.Retry;

            if (!available.Contains(rate))
            {
                _logger.LogWarning("Profile '{Profile}': '{Monitor}' doesn't offer {Rate}Hz at its current resolution (has {Rates})",
                    profileName, monitor, rate, string.Join(", ", available));
                return StepResult.Failed;
            }

            return _refreshRateController.SetRefreshRate(original, rate) ? StepResult.Done : StepResult.Retry;
        }

        private void HandleDisplaysChanged() => SafeUpdateState();

        private void HandleAudioChanged(object? sender, EventArgs e) => SafeUpdateState();

        private void SafeUpdateState()
        {
            // Fires on platform callback threads; an escaping exception would be lost.
            try
            {
                UpdateState();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to refresh the active profile");
            }
        }

        private void UpdateState()
        {
            lock (_stateLock)
            {
                var match = FindActiveProfile();
                if (match == _activeProfile)
                    return;

                _activeProfile = match;
                StateUpdated?.Invoke(this);
            }
        }

        /// <summary>
        /// The profile whose monitors are exactly the active ones. Profiles that share a
        /// monitor set are told apart by their audio output.
        /// </summary>
        private string? FindActiveProfile()
        {
            var active = _displayWatcher.ActiveMonitors.Select(_monitorNames.Resolve).ToHashSet();

            var matches = Options.Where(name => active.SetEquals(_profiles[name].Monitors)).ToList();
            if (matches.Count <= 1)
                return matches.FirstOrDefault();

            var activeAudio = _audioManager.GetAudioDevices().Find(d => d.IsActive) is { } device
                ? _audioNames.Resolve(device)
                : null;

            return matches.FirstOrDefault(name => _profiles[name].Audio == activeAudio) ?? matches[0];
        }

        public void Dispose()
        {
            _displayWatcher.ActiveMonitorsUpdated -= HandleDisplaysChanged;
            _displayWatcher.AvailableMonitorsUpdated -= HandleDisplaysChanged;
            _audioManager.DeviceChanged -= HandleAudioChanged;

            lock (_applyLock)
            {
                _applyCts?.Cancel();
                _applyCts?.Dispose();
                _applyCts = null;
            }
        }
    }
}
