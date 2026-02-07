using HADesktopAgent.Core.PowerState;
using HADesktopAgent.Linux.DBus;
using Microsoft.Extensions.Logging;
using Tmds.DBus;

namespace HADesktopAgent.Linux.PowerState
{
    public sealed class LogindPowerState : IPowerState, IDisposable
    {
        public event IPowerState.OnSuspendHandler? OnSuspend;
        public event IPowerState.OnResumeHandler? OnResume;

        private readonly ILogger<LogindPowerState> _logger;
        private IDisposable? _signalSubscription;

        public LogindPowerState(ILogger<LogindPowerState> logger)
        {
            _logger = logger;
            _ = SubscribeAsync();
        }

        private async Task SubscribeAsync()
        {
            try
            {
                var logindManager = Connection.System.CreateProxy<ILogindManager>(
                    "org.freedesktop.login1",
                    "/org/freedesktop/login1");

                _signalSubscription = await logindManager.WatchPrepareForSleepAsync(
                    goingToSleep =>
                    {
                        if (goingToSleep)
                        {
                            _logger.LogInformation("System suspending");
                            OnSuspend?.Invoke();
                        }
                        else
                        {
                            _logger.LogInformation("System resuming");
                            OnResume?.Invoke();
                        }
                    },
                    ex => _logger.LogError(ex, "Error in PrepareForSleep signal watcher"));

                _logger.LogDebug("Subscribed to PrepareForSleep D-Bus signal");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to subscribe to PrepareForSleep D-Bus signal");
            }
        }

        public void Dispose()
        {
            _signalSubscription?.Dispose();
        }
    }
}
