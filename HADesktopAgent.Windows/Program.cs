using HADesktopAgent.Windows;
using HADesktopAgent.Windows.Audio;
using HADesktopAgent.Windows.Display;
using HADesktopAgent.Windows.PowerState;
using HADesktopAgent.Windows.Sleep;
using HADesktopAgent.Core;
using HADesktopAgent.Core.Audio;
using HADesktopAgent.Core.Display;
using HADesktopAgent.Core.Mqtt;
using HADesktopAgent.Core.PowerState;
using HADesktopAgent.Core.Sleep;
using Microsoft.Extensions.Options;
using Serilog;

Application.EnableVisualStyles();
Application.SetCompatibleTextRenderingDefault(false);
Application.SetHighDpiMode(HighDpiMode.SystemAware);

// Config location, highest precedence first: --config <path>, %HADESKTOPAGENT_CONFIG%,
// then the user-managed default under LocalApplicationData. Only the default is
// created if missing.
var (configPath, hostArgs) = AgentConfigurationSetup.ResolveConfigPath(args);
args = hostArgs;

AgentConfigurationSetup.EnsureDefaultConfig(configPath);

var logPath = AgentConfigurationSetup.LogPath;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Debug()
    .WriteTo.File(
        logPath,
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 7,
        shared: true,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

var builder = Host.CreateApplicationBuilder(args);

// Binds and validates every config section, and resolves Mqtt.PasswordFile.
builder.AddAgentConfiguration(configPath);

builder.Services.AddLogging(loggingBuilder =>
{
    loggingBuilder.ClearProviders();
    loggingBuilder.AddSerilog(Log.Logger, dispose: true);
});
builder.Services.AddSingleton<IPowerState>(new SystemEventsPowerState());
builder.Services.AddSingleton<MqttManager>();
builder.Services.AddSingleton<IDisplayWatcher>(new DesktopManagerDisplayWatcher(new DesktopManager.MonitorWatcher()));
builder.Services.AddSingleton<IMonitorSwitcher>(new WindowsMonitorSwitcher());
builder.Services.AddSingleton<IRefreshRateController, WindowsRefreshRateController>();
builder.Services.AddSingleton<IAudioManager, AudioDeviceManager>();
builder.Services.AddSingleton<ISleepControl>(new WindowsSleepControl());
builder.Services.AddSingleton(sp =>
{
    var logger = sp.GetRequiredService<ILogger<MqttHaManager>>();
    var mqttManager = sp.GetRequiredService<MqttManager>();
    var agentConfig = sp.GetRequiredService<IOptions<AgentConfiguration>>().Value;
    var mqttConfig = sp.GetRequiredService<IOptions<MqttConfiguration>>().Value;
    return new MqttHaManager(logger, mqttManager, mqttConfig.DiscoveryPrefix, "ha_desktop_agent", agentConfig.DeviceId, agentConfig.DeviceName);
});

var host = builder.Build();

try
{
    Log.Information("HA Desktop Agent starting...");

    Application.Run(new TrayApplicationContext(host));
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    Log.Information("HA Desktop Agent shutting down...");
    Log.CloseAndFlush();
}
