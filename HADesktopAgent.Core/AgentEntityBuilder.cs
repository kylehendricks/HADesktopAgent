using HADesktopAgent.Core.Audio;
using HADesktopAgent.Core.Audio.Entity;
using HADesktopAgent.Core.Display;
using HADesktopAgent.Core.Display.Entity;
using HADesktopAgent.Core.Entity;
using HADesktopAgent.Core.Process;
using HADesktopAgent.Core.Process.Entity;
using HADesktopAgent.Core.Sleep;
using HADesktopAgent.Core.Sleep.Entity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HADesktopAgent.Core
{
    /// <summary>
    /// The entities and APIs registered with a host, returned so callers can hold
    /// references for their lifetime (and dispose the monitor switch manager).
    /// </summary>
    public sealed class AgentEntities : IDisposable
    {
        public required MonitorSwitchManager MonitorSwitchManager { get; init; }
        public required DisplayConfigurationApi DisplayConfigApi { get; init; }
        public required AudioSelect AudioSelect { get; init; }
        public required SleepButton SleepButton { get; init; }
        public required IReadOnlyList<ProcessSwitch> ProcessSwitches { get; init; }

        public void Dispose()
        {
            // MonitorSwitchManager disposes the per-monitor entities it created.
            MonitorSwitchManager.Dispose();
            AudioSelect.Dispose();
        }
    }

    /// <summary>
    /// Builds the agent's entity graph and registers it with an <see cref="IHaEntityHost"/>.
    /// </summary>
    /// <remarks>
    /// Shared by every host so the graph is defined once: the Linux worker, the Windows
    /// tray app, and the offline dev console all get identical entities. Platform
    /// differences are expressed by which services are registered in DI — notably
    /// <see cref="IRefreshRateController"/>, which is optional and currently only
    /// implemented on Windows.
    /// </remarks>
    public static class AgentEntityBuilder
    {
        public static async Task<AgentEntities> BuildAsync(IServiceProvider services, IHaEntityHost host)
        {
            var loggerFactory = services.GetRequiredService<ILoggerFactory>();
            var logger = loggerFactory.CreateLogger(typeof(AgentEntityBuilder));
            var displayWatcher = services.GetRequiredService<IDisplayWatcher>();
            var monitorSwitcher = services.GetRequiredService<IMonitorSwitcher>();
            var audioManager = services.GetRequiredService<IAudioManager>();
            var sleepControl = services.GetRequiredService<ISleepControl>();
            var processSwitchConfig = services.GetRequiredService<IOptions<List<ProcessSwitchConfiguration>>>();
            var nameMappingConfig = services.GetRequiredService<IOptions<NameMappingConfiguration>>().Value;

            // Optional: only platforms that implement refresh rate control register this.
            var refreshRateController = services.GetService<IRefreshRateController>();

            // Log discovered monitor identifiers to help users configure name mappings
            foreach (var (name, info) in displayWatcher.MonitorDetails)
            {
                logger.LogInformation("Discovered monitor: '{Name}' (EDID: {EdidId})", name, info.EdidIdentifier ?? "unavailable");
            }

            // Per-monitor switch entities (with name mappings). Registers its own
            // entities as monitors appear and disappear.
            var monitorSwitchManager = new MonitorSwitchManager(
                loggerFactory.CreateLogger<MonitorSwitchManager>(),
                loggerFactory,
                displayWatcher,
                monitorSwitcher,
                host,
                nameMappingConfig.Monitors,
                refreshRateController);

            // Display configuration API (shares the live mapped-name dictionary from the monitor switch manager)
            var displayConfigApi = new DisplayConfigurationApi(
                loggerFactory.CreateLogger<DisplayConfigurationApi>(),
                displayWatcher,
                monitorSwitcher,
                monitorSwitchManager.MappedToOriginalNames);
            await host.RegisterApi(displayConfigApi);

            // Audio select entity (with name mappings)
            var audioSelect = new AudioSelect(loggerFactory.CreateLogger<AudioSelect>(), audioManager, nameMappingConfig.AudioDevices);
            await host.RegisterEntity(audioSelect);

            // Process switch entities
            var processSwitches = new List<ProcessSwitch>();
            foreach (var config in processSwitchConfig.Value)
            {
                var processSwitch = new ProcessSwitch(
                    loggerFactory.CreateLogger<ProcessSwitch>(),
                    config.PrettyName,
                    config.Name,
                    config.Icon,
                    config.ApplicationPath,
                    config.StartArgument,
                    config.StopArgument);
                processSwitches.Add(processSwitch);
                await host.RegisterEntity(processSwitch);
            }

            var sleepButton = new SleepButton(sleepControl);
            await host.RegisterEntity(sleepButton);

            return new AgentEntities
            {
                MonitorSwitchManager = monitorSwitchManager,
                DisplayConfigApi = displayConfigApi,
                AudioSelect = audioSelect,
                SleepButton = sleepButton,
                ProcessSwitches = processSwitches,
            };
        }
    }
}
