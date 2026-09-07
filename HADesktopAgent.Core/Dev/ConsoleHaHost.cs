using HADesktopAgent.Core.Entity;
using HADesktopAgent.Core.Mqtt;

namespace HADesktopAgent.Core.Dev
{
    /// <summary>
    /// An <see cref="IHaEntityHost"/> that prints to the console instead of talking to a
    /// broker. Lets the agent run with no MQTT at all: you see what it detects, watch
    /// state change live, and drive commands by hand from <see cref="DevConsole"/>.
    /// </summary>
    public sealed class ConsoleHaHost : IHaEntityHost, IDisposable
    {
        private readonly string _appPrefix;
        private readonly string _statusTopic;
        private readonly string _deviceName;
        private readonly string _deviceId;
        private readonly string _haPrefix;

        private readonly List<IHaEntity> _entities = [];
        private readonly List<IMqttApi> _apis = [];

        // State updates arrive on display-watcher and pactl-subscribe threads while the
        // REPL reads on the main thread; guards the collections and console writes.
        private readonly object _lock = new();

        /// <summary>Suppresses per-event output during the initial bulk registration.</summary>
        private bool _quiet = true;

        public ConsoleHaHost(string haPrefix, string appPrefix, string statusTopic, string deviceId, string deviceName)
        {
            _haPrefix = haPrefix;
            _appPrefix = appPrefix;
            _statusTopic = statusTopic;
            _deviceId = deviceId;
            _deviceName = deviceName;
        }

        public IReadOnlyList<IHaEntity> Entities
        {
            get { lock (_lock) { return [.. _entities]; } }
        }

        public IReadOnlyList<IMqttApi> Apis
        {
            get { lock (_lock) { return [.. _apis]; } }
        }

        public string HaPrefix => _haPrefix;
        public string AppPrefix => _appPrefix;
        public string StatusTopic => _statusTopic;
        public string DeviceId => _deviceId;
        public string DeviceName => _deviceName;

        /// <summary>
        /// Starts reporting registrations and state changes as they happen. Called once
        /// the initial graph is built so startup doesn't scroll past.
        /// </summary>
        public void BeginLiveReporting()
        {
            lock (_lock)
            {
                _quiet = false;
            }
        }

        public Task RegisterEntity(IHaEntity entity)
        {
            lock (_lock)
            {
                _entities.Add(entity);
            }

            if (entity is IHaStatefulEntity stateful)
            {
                stateful.StateUpdated += HandleStateUpdated;
            }

            entity.ConfigUpdated += HandleConfigUpdated;

            Report(ConsoleColor.Green, "+ registered", Describe(entity));
            return Task.CompletedTask;
        }

        public Task UnregisterEntity(IHaEntity entity)
        {
            bool removed;
            lock (_lock)
            {
                removed = _entities.Remove(entity);
            }

            if (!removed)
            {
                return Task.CompletedTask;
            }

            if (entity is IHaStatefulEntity stateful)
            {
                stateful.StateUpdated -= HandleStateUpdated;
            }

            entity.ConfigUpdated -= HandleConfigUpdated;

            Report(ConsoleColor.Red, "- unregistered", Describe(entity));
            return Task.CompletedTask;
        }

        public Task RegisterApi(IMqttApi api)
        {
            lock (_lock)
            {
                _apis.Add(api);
            }

            Report(ConsoleColor.Green, "+ registered", $"api {api.Name}  <- {_appPrefix}/{_deviceId}/{api.Name}/command");
            return Task.CompletedTask;
        }

        public Task UnregisterApi(IMqttApi api)
        {
            lock (_lock)
            {
                _apis.Remove(api);
            }

            Report(ConsoleColor.Red, "- unregistered", $"api {api.Name}");
            return Task.CompletedTask;
        }

        private static string Describe(IHaEntity entity)
        {
            var state = entity is IHaStatefulEntity s ? $"  state={s.State ?? "<null>"}" : "";
            return $"{entity.EntityType,-7} {entity.Name}{state}";
        }

        private void HandleStateUpdated(IHaStatefulEntity entity)
        {
            Report(ConsoleColor.Cyan, "~ state", $"{entity.Name} = {entity.State ?? "<null>"}");
        }

        private void HandleConfigUpdated(IHaEntity entity)
        {
            var options = entity is IHaSelectableEntity selectable
                ? $" options=[{string.Join(", ", selectable.Options)}]"
                : "";
            Report(ConsoleColor.DarkYellow, "~ config", $"{entity.Name}{options}");
        }

        /// <summary>
        /// Writes an event line. Held under the same lock as the collections so
        /// concurrent watcher threads can't interleave mid-line.
        /// </summary>
        private void Report(ConsoleColor color, string tag, string message)
        {
            lock (_lock)
            {
                if (_quiet)
                {
                    return;
                }

                var previous = Console.ForegroundColor;
                Console.ForegroundColor = color;
                Console.Write($"[{DateTime.Now:HH:mm:ss}] {tag,-15} ");
                Console.ForegroundColor = previous;
                Console.WriteLine(message);
            }
        }

        public void Dispose()
        {
            foreach (var entity in Entities)
            {
                if (entity is IHaStatefulEntity stateful)
                {
                    stateful.StateUpdated -= HandleStateUpdated;
                }

                entity.ConfigUpdated -= HandleConfigUpdated;
            }
        }
    }
}
