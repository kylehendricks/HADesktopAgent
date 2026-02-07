using HAWindowsAgent;
using HAWindowsAgent.Audio;
using HAWindowsAgent.Display;
using HAWindowsAgent.Mqtt;
using HAWindowsAgent.PowerState;
using HAWindowsAgent.Process;
using Microsoft.Extensions.Options;
using Serilog;
using System.Text.Json;

Application.EnableVisualStyles();
Application.SetCompatibleTextRenderingDefault(false);
Application.SetHighDpiMode(HighDpiMode.SystemAware);

var appDataPath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "HAWindowsAgent");

var logPath = Path.Combine(appDataPath, "logs", "app-.log");
var configPath = Path.Combine(appDataPath, "config.json");

// Create default config if it doesn't exist
if (!File.Exists(configPath))
{
    Directory.CreateDirectory(appDataPath);
    var defaultConfig = new
    {
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
builder.Services.AddSingleton<IPowerState>(new SystemEventsPowerState());
builder.Services.AddSingleton<MqttManager>();
builder.Services.AddSingleton<IDisplayWatcher>(new DesktopManagerDisplayWatcher(new DesktopManager.MonitorWatcher()));
builder.Services.AddSingleton<AudioDeviceManager>();
builder.Services.AddSingleton(sp =>
{
    var logger = sp.GetRequiredService<ILogger<MqttHaManager>>();
    var mqttManager = sp.GetRequiredService<MqttManager>();
    return new MqttHaManager(logger, mqttManager, "homeassistant", "ha_windows_agent", "gaming_pc", "Gaming PC");
});

var host = builder.Build();

try
{
    Log.Information("HA Windows Agent starting...");

    Application.Run(new TrayApplicationContext(host));
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    Log.Information("HA Windows Agent shutting down...");
    Log.CloseAndFlush();
}
