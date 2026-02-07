using HADesktopAgent.Linux.PowerState;
using Microsoft.Extensions.Logging;
using HADesktopAgent.Core;
using HADesktopAgent.Core.Mqtt;
using HADesktopAgent.Core.PowerState;
using HADesktopAgent.Core.Process;
using HADesktopAgent.Core.Process.Entity;
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
        ProcessSwitches = Array.Empty<ProcessSwitchConfiguration>()
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

builder.Services.AddLogging(loggingBuilder =>
{
    loggingBuilder.ClearProviders();
    loggingBuilder.AddSerilog(Log.Logger, dispose: true);
});

builder.Services.AddSingleton<IPowerState>(new NoOpPowerState());
builder.Services.AddSingleton<MqttManager>();
builder.Services.AddSingleton(sp =>
{
    var logger = sp.GetRequiredService<ILogger<MqttHaManager>>();
    var mqttManager = sp.GetRequiredService<MqttManager>();
    var agentConfig = sp.GetRequiredService<IOptions<AgentConfiguration>>().Value;
    return new MqttHaManager(logger, mqttManager, "homeassistant", "ha_desktop_agent", agentConfig.DeviceId, agentConfig.DeviceName);
});

var host = builder.Build();

try
{
    Log.Information("HA Desktop Agent starting...");

    // Register process switch entities
    var mqttHaManager = host.Services.GetRequiredService<MqttHaManager>();
    var loggerFactory = host.Services.GetRequiredService<ILoggerFactory>();
    var processSwitchConfig = host.Services.GetRequiredService<IOptions<List<ProcessSwitchConfiguration>>>();

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
