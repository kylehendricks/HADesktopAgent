using HADesktopAgent.Linux.Audio;
using HADesktopAgent.Linux.Display;
using HADesktopAgent.Linux.PowerState;
using HADesktopAgent.Linux.Sleep;
using Microsoft.Extensions.Logging;
using HADesktopAgent.Core;
using HADesktopAgent.Core.Audio;
using HADesktopAgent.Core.Audio.Entity;
using HADesktopAgent.Core.Display;
using HADesktopAgent.Core.Display.Entity;
using HADesktopAgent.Core.Mqtt;
using HADesktopAgent.Core.PowerState;
using HADesktopAgent.Core.Process;
using HADesktopAgent.Core.Process.Entity;
using HADesktopAgent.Core.Sleep;
using HADesktopAgent.Core.Sleep.Entity;
using Microsoft.Extensions.Options;
using Serilog;
using System.Text.Json;

var appDataPath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "HADesktopAgent");

var logPath = Path.Combine(appDataPath, "logs", "app-.log");
var configPath = Path.Combine(appDataPath, "config.json");

// Create default config if it doesn't exist
if (!File.Exists(configPath))
{
    Directory.CreateDirectory(appDataPath);
    var defaultConfig = new
    {
        Agent = new AgentConfiguration(),
        Mqtt = new MqttConfiguration(),
        ProcessSwitches = Array.Empty<ProcessSwitchConfiguration>(),
        NameMappings = new NameMappingConfiguration()
    };
    var json = JsonSerializer.Serialize(defaultConfig, new JsonSerializerOptions { WriteIndented = true });
    File.WriteAllText(configPath, json);
}

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

// Add user config from AppData
builder.Configuration.AddJsonFile(configPath, optional: false, reloadOnChange: true);

// Configure Agent options with validation
builder.Services.AddOptions<AgentConfiguration>()
    .Bind(builder.Configuration.GetSection("Agent"))
    .ValidateDataAnnotations()
    .ValidateOnStart();

// Configure MQTT options with validation
builder.Services.AddOptions<MqttConfiguration>()
    .Bind(builder.Configuration.GetSection("Mqtt"))
    .ValidateDataAnnotations()
    .ValidateOnStart();

// Register custom validator
builder.Services.AddSingleton<IValidateOptions<MqttConfiguration>, MqttConfigurationValidator>();

// Configure ProcessSwitch options with validation
builder.Services.AddOptions<List<ProcessSwitchConfiguration>>()
    .Bind(builder.Configuration.GetSection("ProcessSwitches"))
    .ValidateDataAnnotations()
    .ValidateOnStart();

// Register custom validator
builder.Services.AddSingleton<IValidateOptions<List<ProcessSwitchConfiguration>>, ProcessSwitchConfigurationValidator>();

// Configure NameMapping options
builder.Services.AddOptions<NameMappingConfiguration>()
    .Bind(builder.Configuration.GetSection("NameMappings"));

builder.Services.AddLogging(loggingBuilder =>
{
    loggingBuilder.ClearProviders();
    loggingBuilder.AddSerilog(Log.Logger, dispose: true);
});

builder.Services.AddSingleton<IPowerState, LogindPowerState>();
builder.Services.AddSingleton<IDisplayWatcher, KScreenDisplayWatcher>();
builder.Services.AddSingleton<IMonitorSwitcher, KScreenMonitorSwitcher>();
builder.Services.AddSingleton<IAudioManager, PulseAudioManager>();
builder.Services.AddSingleton<ISleepControl>(new SystemdSleepControl());
builder.Services.AddSingleton<MqttManager>();
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

    var mqttHaManager = host.Services.GetRequiredService<MqttHaManager>();
    var loggerFactory = host.Services.GetRequiredService<ILoggerFactory>();
    var displayWatcher = host.Services.GetRequiredService<IDisplayWatcher>();
    var monitorSwitcher = host.Services.GetRequiredService<IMonitorSwitcher>();
    var audioManager = host.Services.GetRequiredService<IAudioManager>();
    var sleepControl = host.Services.GetRequiredService<ISleepControl>();
    var processSwitchConfig = host.Services.GetRequiredService<IOptions<List<ProcessSwitchConfiguration>>>();
    var nameMappingConfig = host.Services.GetRequiredService<IOptions<NameMappingConfiguration>>().Value;

    // Log discovered monitor identifiers to help users configure name mappings
    foreach (var (name, info) in displayWatcher.MonitorDetails)
    {
        Log.Information("Discovered monitor: '{Name}' (EDID: {EdidId})", name, info.EdidIdentifier ?? "unavailable");
    }

    // Register per-monitor switch entities (with name mappings)
    var monitorSwitchManager = new MonitorSwitchManager(
        loggerFactory.CreateLogger<MonitorSwitchManager>(),
        loggerFactory,
        displayWatcher,
        monitorSwitcher,
        mqttHaManager,
        nameMappingConfig.Monitors);

    // Register display configuration API (shares the live mapped-name dictionary from the monitor switch manager)
    var displayConfigApi = new DisplayConfigurationApi(
        loggerFactory.CreateLogger<DisplayConfigurationApi>(),
        displayWatcher,
        monitorSwitcher,
        monitorSwitchManager.MappedToOriginalNames);
    await mqttHaManager.RegisterApi(displayConfigApi);

    // Register audio select entity (with name mappings)
    var audioSelect = new AudioSelect(loggerFactory.CreateLogger<AudioSelect>(), audioManager, nameMappingConfig.AudioDevices);
    await mqttHaManager.RegisterEntity(audioSelect);

    // Register sleep button entity
    var sleepButton = new SleepButton(sleepControl);
    await mqttHaManager.RegisterEntity(sleepButton);

    // Register process switch entities
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
        await mqttHaManager.RegisterEntity(processSwitch);
    }

    await host.RunAsync();
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
