using HADesktopAgent.Core.Mqtt;
using HADesktopAgent.Core.Process;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace HADesktopAgent.Core
{
    /// <summary>
    /// Locates config.json and wires it into the host. Shared by both platform hosts so
    /// the config surface can't drift between them.
    /// </summary>
    public static class AgentConfigurationSetup
    {
        /// <summary>Config file location, for hosts that can't pass <c>--config</c>.</summary>
        public const string ConfigPathEnvironmentVariable = "HADESKTOPAGENT_CONFIG";

        /// <summary>
        /// Writable per-user directory holding the default config and the logs.
        /// </summary>
        public static string AppDataPath { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "HADesktopAgent");

        public static string DefaultConfigPath { get; } = Path.Combine(AppDataPath, "config.json");

        /// <summary>Serilog rolling-file template; the date is inserted before the extension.</summary>
        public static string LogPath { get; } = Path.Combine(AppDataPath, "logs", "app-.log");

        /// <summary>
        /// Pulls <c>--config &lt;path&gt;</c> (or <c>--config=&lt;path&gt;</c>) out of the argument
        /// list, falling back to <see cref="ConfigPathEnvironmentVariable"/> and then to
        /// <see cref="DefaultConfigPath"/>. The remaining arguments are what the host builder
        /// should see: it only understands <c>--key=value</c> pairs and would otherwise bind
        /// the path as a stray configuration key.
        /// </summary>
        public static (string ConfigPath, string[] RemainingArgs) ResolveConfigPath(string[] args)
        {
            const string inlinePrefix = "--config=";

            string? path = null;
            var remaining = new List<string>(args.Length);

            for (var i = 0; i < args.Length; i++)
            {
                var arg = args[i];

                if (arg is "--config" or "-c")
                {
                    if (i + 1 >= args.Length)
                    {
                        throw new ArgumentException($"{arg} requires a path argument", nameof(args));
                    }

                    path = args[++i];
                }
                else if (arg.StartsWith(inlinePrefix, StringComparison.Ordinal))
                {
                    path = arg[inlinePrefix.Length..];
                }
                else
                {
                    remaining.Add(arg);
                }
            }

            path ??= Environment.GetEnvironmentVariable(ConfigPathEnvironmentVariable);

            return (
                string.IsNullOrWhiteSpace(path) ? DefaultConfigPath : Path.GetFullPath(path),
                [.. remaining]);
        }

        /// <summary>
        /// Writes a skeleton config on first run. Only ever touches
        /// <see cref="DefaultConfigPath"/>: a path supplied by a configuration manager
        /// belongs to whatever generated it, and a missing one there is an error worth
        /// failing on rather than papering over with defaults that can't reach a broker.
        /// </summary>
        public static void EnsureDefaultConfig(string configPath)
        {
            if (configPath != DefaultConfigPath || File.Exists(configPath))
            {
                return;
            }

            Directory.CreateDirectory(AppDataPath);

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

        /// <summary>
        /// Adds the config file as a source and registers every options type it binds,
        /// with validation that runs at startup rather than at first use.
        /// </summary>
        public static void AddAgentConfiguration(this IHostApplicationBuilder builder, string configPath)
        {
            var isUserManaged = configPath == DefaultConfigPath;

            // Watching a generated config is pointless — nothing rewrites it in place, and a
            // path under a read-only store would put a file watch on a directory with an
            // enormous number of entries.
            builder.Configuration.AddJsonFile(configPath, optional: false, reloadOnChange: isUserManaged);

            // Re-added last so environment wins over the file. That is what lets a secret
            // arrive as Mqtt__Password (systemd EnvironmentFile=) or Mqtt__PasswordFile
            // (systemd LoadCredential=) when the config file itself is world-readable.
            builder.Configuration.AddEnvironmentVariables();

            builder.Services.AddOptions<AgentConfiguration>()
                .Bind(builder.Configuration.GetSection("Agent"))
                .ValidateDataAnnotations()
                .ValidateOnStart();

            builder.Services.AddOptions<MqttConfiguration>()
                .Bind(builder.Configuration.GetSection("Mqtt"))
                .ValidateDataAnnotations()
                .ValidateOnStart();

            // Resolves Mqtt.PasswordFile into Mqtt.Password before the validators below run.
            builder.Services.AddSingleton<IPostConfigureOptions<MqttConfiguration>, MqttSecretFileLoader>();
            builder.Services.AddSingleton<IValidateOptions<MqttConfiguration>, MqttConfigurationValidator>();

            builder.Services.AddOptions<List<ProcessSwitchConfiguration>>()
                .Bind(builder.Configuration.GetSection("ProcessSwitches"))
                .ValidateDataAnnotations()
                .ValidateOnStart();

            builder.Services.AddSingleton<IValidateOptions<List<ProcessSwitchConfiguration>>, ProcessSwitchConfigurationValidator>();

            builder.Services.AddOptions<NameMappingConfiguration>()
                .Bind(builder.Configuration.GetSection("NameMappings"));
        }
    }
}
