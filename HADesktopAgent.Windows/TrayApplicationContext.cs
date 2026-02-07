using HADesktopAgent.Core.Audio;
using HADesktopAgent.Core.Audio.Entity;
using HADesktopAgent.Core.Display;
using HADesktopAgent.Core.Mqtt;
using HADesktopAgent.Core.Process;
using HADesktopAgent.Core.Process.Entity;
using HADesktopAgent.Core.Sleep;
using HADesktopAgent.Core.Sleep.Entity;
using Microsoft.Extensions.Options;

namespace HADesktopAgent.Windows
{
    public class TrayApplicationContext : ApplicationContext
    {
        private readonly NotifyIcon _trayIcon;
        private readonly IHost _host;

        // Entity references for state communication
        private MonitorSwitchManager? _monitorSwitchManager;
        private AudioSelect? _audioSelect;
        private List<ProcessSwitch> _processSwitches = new();
        private SleepButton? _sleepButton;

        public TrayApplicationContext(IHost host)
        {
            _host = host;

            _trayIcon = new NotifyIcon()
            {
                Icon = new Icon(GetType(), "Resources.ha-desktop-agent.ico"),
                ContextMenuStrip = CreateContextMenu(),
                Visible = true,
                Text = "HA Desktop Agent"
            };

            _trayIcon.DoubleClick += TrayIcon_DoubleClick;

            _ = InitializeAsync();
        }

        private async Task InitializeAsync()
        {
            await _host.StartAsync();

            // Get services from DI container
            var mqttHaManager = _host.Services.GetRequiredService<MqttHaManager>();
            var displayWatcher = _host.Services.GetRequiredService<IDisplayWatcher>();
            var monitorSwitcher = _host.Services.GetRequiredService<IMonitorSwitcher>();
            var audioManager = _host.Services.GetRequiredService<IAudioManager>();
            var sleepControl = _host.Services.GetRequiredService<ISleepControl>();
            var loggerFactory = _host.Services.GetRequiredService<ILoggerFactory>();
            var processSwitchConfig = _host.Services.GetRequiredService<IOptions<List<ProcessSwitchConfiguration>>>();

            // Create per-monitor switch entities
            _monitorSwitchManager = new MonitorSwitchManager(
                loggerFactory.CreateLogger<MonitorSwitchManager>(),
                loggerFactory,
                displayWatcher,
                monitorSwitcher,
                mqttHaManager);

            _audioSelect = new AudioSelect(loggerFactory.CreateLogger<AudioSelect>(), audioManager);
            await mqttHaManager.RegisterEntity(_audioSelect);

            // Create ProcessSwitch instances from configuration
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
                _processSwitches.Add(processSwitch);
                await mqttHaManager.RegisterEntity(processSwitch);
            }

            _sleepButton = new SleepButton(sleepControl);
            await mqttHaManager.RegisterEntity(_sleepButton);
        }

        private ContextMenuStrip CreateContextMenu()
        {
            var contextMenu = new ContextMenuStrip();

            var statusItem = new ToolStripMenuItem("HA Desktop Agent");
            statusItem.Enabled = false;
            contextMenu.Items.Add(statusItem);

            contextMenu.Items.Add(new ToolStripSeparator());

            var openLogsItem = new ToolStripMenuItem("Open Logs Folder");
            openLogsItem.Click += OpenLogs_Click;
            contextMenu.Items.Add(openLogsItem);

            contextMenu.Items.Add(new ToolStripSeparator());

            var exitItem = new ToolStripMenuItem("Exit");
            exitItem.Click += Exit_Click;
            contextMenu.Items.Add(exitItem);

            return contextMenu;
        }

        private void TrayIcon_DoubleClick(object? sender, EventArgs e)
        {
            MessageBox.Show(
                "HA Desktop Agent is running in the background.\n\n" +
                "This application monitors and controls your PC for Home Assistant.\n\n" +
                "Right-click the tray icon to exit or view logs.",
                "HA Desktop Agent",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }

        private void OpenLogs_Click(object? sender, EventArgs e)
        {
            var logsPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "HADesktopAgent",
                "logs");

            try
            {
                // Create the directory if it doesn't exist
                Directory.CreateDirectory(logsPath);

                // Open the folder in Windows Explorer
                System.Diagnostics.Process.Start("explorer.exe", logsPath);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Failed to open logs folder:\n{ex.Message}",
                    "Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        private async void Exit_Click(object? sender, EventArgs e)
        {
            _trayIcon.Visible = false;

            // Stop the hosted services gracefully
            await _host.StopAsync();

            Application.Exit();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _trayIcon?.Dispose();
                _host?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
