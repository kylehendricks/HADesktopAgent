using HAWindowsAgent.Entity;

namespace HAWindowsAgent.Process.Entity
{
    public class ProcessSwitch : IHaStatefulEntity, IHaCommandableEntity, IHaClassifiableDevice
    {
        public string? State => _state switch
        {
            true => IHaStatefulEntity.ON_STATE,
            false => IHaStatefulEntity.OFF_STATE,
            null => null
        };
        public string Name { get; init; }
        public string PrettyName { get; init; }
        public string UniqueId { get; init; }
        public string Icon { get; init; }
        public string EntityType => "switch";
        public string DeviceClass => "switch";
        public bool Optimistic => false;

        public event IHaStatefulEntity.StateUpdatedHandler? StateUpdated;
        public event IHaEntity.ConfigUpdatedHandler? ConfigUpdated { add { } remove { } }

        private readonly string _applicationPath;
        private readonly string? _stopArgument;
        private readonly string? _startArgument;
        private readonly ProcessMonitor _processMonitor;
        private readonly ILogger<ProcessSwitch> _logger;

        private bool? _state;

        public ProcessSwitch(ILogger<ProcessSwitch> logger, string prettyName, string name, string icon, string applicationPath, string? startArgument = null, string? stopArgument = null)
        {
            Name = name;
            UniqueId = name;
            PrettyName = prettyName;
            Icon = icon;
            _applicationPath = applicationPath;
            _logger = logger;
            _stopArgument = stopArgument;
            _startArgument = startArgument;

            _processMonitor = new ProcessMonitor(applicationPath);
            _processMonitor.ProcessStateChanged += ProcessStateChanged;
            _state = _processMonitor.IsRunning;
        }

        public void HandleCommand(string command)
        {
            switch (command)
            {
                case IHaStatefulEntity.ON_STATE:
                    try
                    {
                        ApplicationLauncher.Launch(_applicationPath, _startArgument);
                    }
                    catch (Exception e)
                    {
                        _logger.LogError(e, "Failed to launch application {Name} with path {Path}", Name, _applicationPath);
                    }
                    return;
                case IHaStatefulEntity.OFF_STATE:
                    try
                    {
                        if (_stopArgument != null)
                        {
                            ApplicationLauncher.Launch(_applicationPath, _stopArgument);
                        }
                        else
                        {
                            ApplicationLauncher.Exit(_applicationPath);
                        }
                    }
                    catch (Exception e)
                    {
                        _logger.LogError(e, "Failed to exit application {Name} with path {Path}", Name, _applicationPath);
                    }
                    return;
                default:
                    _logger.LogWarning("Invalid command for {Entity}: {Command}", Name, command);
                    break;
            }
            ;
        }

        private void ProcessStateChanged(bool isRunning)
        {
            if (_state == isRunning)
            {
                return;
            }

            _state = isRunning;
            StateUpdated?.Invoke(this);
        }
    }
}
