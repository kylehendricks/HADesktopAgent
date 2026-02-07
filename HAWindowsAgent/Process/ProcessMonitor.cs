using Timer = System.Threading.Timer;

namespace HAWindowsAgent.Process
{
    public class ProcessMonitor : IDisposable
    {
        private readonly string _processName;
        private readonly Timer _pollTimer;
        private bool _isRunning;
        private readonly Lock _lock = new();

        public delegate void ProcessStateChangedHandler(bool isRunning);
        public event ProcessStateChangedHandler? ProcessStateChanged;

        /// <summary>
        /// Gets whether the monitored process is currently running
        /// </summary>
        public bool IsRunning
        {
            get
            {
                lock (_lock)
                {
                    return _isRunning;
                }
            }
        }

        /// <summary>
        /// Creates a new ProcessMonitor for the specified executable using polling
        /// </summary>
        /// <param name="exePath">Full path to the executable to monitor</param>
        /// <param name="pollIntervalMs">How often to check if the process is running (default: 1000ms)</param>
        public ProcessMonitor(string exePath, int pollIntervalMs = 1000)
        {
            if (string.IsNullOrWhiteSpace(exePath))
            {
                throw new ArgumentException("Executable path cannot be null or empty", nameof(exePath));
            }

            // Extract process name from path (without .exe extension)
            _processName = Path.GetFileNameWithoutExtension(exePath);

            // Check initial state
            _isRunning = CheckIfProcessIsRunning();

            // Start polling timer
            _pollTimer = new Timer(CheckProcessState, null, pollIntervalMs, pollIntervalMs);
        }

        private void CheckProcessState(object? state)
        {
            bool currentlyRunning = CheckIfProcessIsRunning();

            lock (_lock)
            {
                if (currentlyRunning != _isRunning)
                {
                    _isRunning = currentlyRunning;

                    // Fire event on a separate task to avoid blocking the timer
                    _ = Task.Run(() => ProcessStateChanged?.Invoke(_isRunning));
                }
            }
        }

        private bool CheckIfProcessIsRunning()
        {
            try
            {
                var processes = System.Diagnostics.Process.GetProcessesByName(_processName);
                return processes.Length > 0;
            }
            catch
            {
                return false;
            }
        }

        public void Dispose()
        {
            _pollTimer?.Dispose();
        }
    }
}
