using HADesktopAgent.Linux.Audio;
using HADesktopAgent.Linux.Display;
using HADesktopAgent.Linux.PowerState;
using HADesktopAgent.Linux.Sleep;
using Microsoft.Extensions.Logging;
using HADesktopAgent.Core;
using HADesktopAgent.Core.Audio;
using HADesktopAgent.Core.Dev;
using HADesktopAgent.Core.Display;
using HADesktopAgent.Core.Mqtt;
using HADesktopAgent.Core.PowerState;
using HADesktopAgent.Core.Sleep;
using Microsoft.Extensions.Options;
using Serilog;
using Serilog.Events;

// Dev mode runs the whole entity graph with no broker: see what is detected, watch
// state change live, and drive commands by hand. Stripped from args before the host
// builder sees them, which only understands --key=value pairs.
var devMode = args.Any(a => a is "--dev" or "-d");
args = [.. args.Where(a => a is not ("--dev" or "-d"))];

// Config location, highest precedence first: --config <path>, $HADESKTOPAGENT_CONFIG,
// then the user-managed default under LocalApplicationData. A configuration manager
// points the first two at a file it generates; only the default is created if missing.
var (configPath, hostArgs) = AgentConfigurationSetup.ResolveConfigPath(args);
args = hostArgs;

AgentConfigurationSetup.EnsureDefaultConfig(configPath);

var logPath = AgentConfigurationSetup.LogPath;

var loggerConfiguration = new LoggerConfiguration()
    .MinimumLevel.Debug()
    .WriteTo.File(
        logPath,
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 7,
        shared: true,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}");

if (devMode)
{
    // Everything to stderr so the REPL on stdout stays readable (`2>/dev/null`).
    loggerConfiguration.WriteTo.Console(
        standardErrorFromLevel: LogEventLevel.Verbose,
        outputTemplate: "{Timestamp:HH:mm:ss} [{Level:u3}] {Message:lj}{NewLine}{Exception}");
}

Log.Logger = loggerConfiguration.CreateLogger();

var builder = Host.CreateApplicationBuilder(args);

// Binds and validates every config section, and resolves Mqtt.PasswordFile.
builder.AddAgentConfiguration(configPath);

builder.Services.AddLogging(loggingBuilder =>
{
    loggingBuilder.ClearProviders();
    loggingBuilder.AddSerilog(Log.Logger, dispose: true);

    // Serilog owns the rolling file log. The console provider exists so the journal
    // gets something too: AddSystemd below swaps its formatter for one that prefixes
    // the syslog level, which is what gives journalctl real priorities. In dev mode
    // Serilog is already writing to stderr, so a second provider would just double up.
    if (!devMode)
    {
        loggingBuilder.AddConsole();
    }
});

// No-op unless the process really is a systemd service (checks INVOCATION_ID), so
// this is safe in dev mode and when run by hand. Under systemd it supplies the
// Type=notify readiness ping and the journal log formatter.
builder.Services.AddSystemd();

builder.Services.AddSingleton<IPowerState, LogindPowerState>();
builder.Services.AddSingleton<IDisplayWatcher, KScreenDisplayWatcher>();
builder.Services.AddSingleton<IMonitorSwitcher, KScreenMonitorSwitcher>();
builder.Services.AddSingleton<IRefreshRateController, KScreenRefreshRateController>();
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

// Disposed so DI singletons are too: PulseAudioManager kills its `pactl subscribe`
// child from Dispose, and the dev path below does not go through RunAsync, which
// would otherwise dispose the host for us.
using var host = builder.Build();

try
{
    Log.Information("HA Desktop Agent starting...");

    if (devMode)
    {
        var agentConfig = host.Services.GetRequiredService<IOptions<AgentConfiguration>>().Value;
        var mqttConfig = host.Services.GetRequiredService<IOptions<MqttConfiguration>>().Value;

        // MqttManager starts its reconnect loop from its constructor, so simply never
        // resolving it (or MqttHaManager) is what keeps dev mode off the network.
        using var consoleHost = new ConsoleHaHost(
            mqttConfig.DiscoveryPrefix,
            "ha_desktop_agent",
            mqttConfig.StatusTopic,
            agentConfig.DeviceId,
            agentConfig.DeviceName);

        await host.StartAsync();
        using var entities = await AgentEntityBuilder.BuildAsync(host.Services, consoleHost);

        await new DevConsole(consoleHost, host.Services).RunAsync(host.Services
            .GetRequiredService<IHostApplicationLifetime>().ApplicationStopping);

        await host.StopAsync();
    }
    else
    {
        var mqttHaManager = host.Services.GetRequiredService<MqttHaManager>();
        using var entities = await AgentEntityBuilder.BuildAsync(host.Services, mqttHaManager);

        await host.RunAsync();
    }
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");

    // Non-zero so systemd's Restart=on-failure actually fires; falling off the end
    // here would exit 0 and leave the unit dead after a crash.
    Environment.ExitCode = 1;
}
finally
{
    Log.Information("HA Desktop Agent shutting down...");
    Log.CloseAndFlush();
}
