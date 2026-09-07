using HADesktopAgent.Core.Audio;
using HADesktopAgent.Core.Display;
using HADesktopAgent.Core.Entity;
using HADesktopAgent.Core.Mqtt;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace HADesktopAgent.Core.Dev
{
    /// <summary>
    /// Interactive REPL over the entity graph, for running the agent with no broker.
    /// </summary>
    public sealed class DevConsole
    {
        private readonly ConsoleHaHost _host;
        private readonly IServiceProvider _services;

        public DevConsole(ConsoleHaHost host, IServiceProvider services)
        {
            _host = host;
            _services = services;
        }

        public async Task RunAsync(CancellationToken ct)
        {
            PrintBanner();
            List();

            _host.BeginLiveReporting();

            while (!ct.IsCancellationRequested)
            {
                Console.Write("> ");
                var line = await Console.In.ReadLineAsync(ct);

                // stdin closed (piped input, or Ctrl-D): stop rather than spin.
                if (line == null)
                {
                    return;
                }

                line = line.Trim();
                if (line.Length == 0)
                {
                    continue;
                }

                var parts = line.Split(' ', 2, StringSplitOptions.TrimEntries);
                var command = parts[0].ToLowerInvariant();
                var rest = parts.Length > 1 ? parts[1] : "";

                try
                {
                    switch (command)
                    {
                        case "help" or "?":
                            PrintBanner();
                            break;
                        case "list" or "ls":
                            List();
                            break;
                        case "apis":
                            Apis();
                            break;
                        case "monitors" or "mon":
                            Monitors();
                            break;
                        case "audio":
                            Audio();
                            break;
                        case "config":
                            Config(rest);
                            break;
                        case "cmd":
                            Command(rest);
                            break;
                        case "quit" or "exit" or "q":
                            return;
                        default:
                            Error($"unknown command '{command}' — try 'help'");
                            break;
                    }
                }
                catch (Exception ex)
                {
                    // A broken entity must not kill the REPL; that's half of what it's for.
                    Error($"{ex.GetType().Name}: {ex.Message}");
                }
            }
        }

        private static void PrintBanner()
        {
            Console.WriteLine();
            WriteColored(ConsoleColor.White, "HADesktopAgent dev console — no MQTT, entities are live\n");
            Console.WriteLine("  list                  entities, types and current state");
            Console.WriteLine("  apis                  non-entity command APIs (e.g. display_config)");
            Console.WriteLine("  monitors              detected monitors, EDID ids and active set");
            Console.WriteLine("  audio                 detected audio sinks");
            Console.WriteLine("  config <entity>       the Home Assistant discovery JSON that would be published");
            Console.WriteLine("  cmd <target> <payload>  send a command to an entity or api");
            Console.WriteLine("  quit");
            Console.WriteLine();
            Console.WriteLine("  State changes print as they happen. Logs go to stderr — redirect with 2>/dev/null.");
            Console.WriteLine();
        }

        private void List()
        {
            var entities = _host.Entities;
            if (entities.Count == 0)
            {
                Console.WriteLine("(no entities registered)");
                return;
            }

            Console.WriteLine();
            Console.WriteLine($"{"TYPE",-8} {"NAME",-34} STATE");
            foreach (var entity in entities.OrderBy(e => e.EntityType).ThenBy(e => e.Name))
            {
                var state = entity is IHaStatefulEntity s ? s.State ?? "<null>" : "-";
                var writable = entity is IHaCommandableEntity ? "" : "   (read-only)";
                Console.WriteLine($"{entity.EntityType,-8} {entity.Name,-34} {state}{writable}");

                // Options go on their own line; a select with long device names would
                // otherwise blow the table apart.
                if (entity is IHaSelectableEntity sel)
                {
                    foreach (var option in sel.Options)
                    {
                        Console.WriteLine($"{"",-8} {"",-34}   - {option}");
                    }
                }
            }
            Console.WriteLine();
        }

        private void Apis()
        {
            var apis = _host.Apis;
            if (apis.Count == 0)
            {
                Console.WriteLine("(no apis registered)");
                return;
            }

            Console.WriteLine();
            foreach (var api in apis)
            {
                Console.WriteLine($"{api.Name}   <- {_host.AppPrefix}/{_host.DeviceId}/{api.Name}/command");
            }
            Console.WriteLine();
        }

        private void Monitors()
        {
            var watcher = _services.GetRequiredService<IDisplayWatcher>();
            var active = watcher.ActiveMonitors;

            Console.WriteLine();
            Console.WriteLine($"{"ACTIVE",-8} {"NAME",-34} EDID");
            foreach (var name in watcher.AvailableMonitors)
            {
                var edid = watcher.MonitorDetails.TryGetValue(name, out var info)
                    ? info.EdidIdentifier ?? "<unavailable>"
                    : "<no details>";
                Console.WriteLine($"{(active.Contains(name) ? "  *" : "   "),-8} {name,-34} {edid}");
            }
            Console.WriteLine();
        }

        private void Audio()
        {
            var audio = _services.GetRequiredService<IAudioManager>();

            Console.WriteLine();
            Console.WriteLine($"{"ACTIVE",-8} {"FRIENDLY NAME",-44} ID");
            foreach (var device in audio.GetAudioDevices())
            {
                Console.WriteLine($"{(device.IsActive ? "  *" : "   "),-8} {device.UserFriendlyName,-44} {device.Id}");
            }
            Console.WriteLine();
        }

        private void Config(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                Error("usage: config <entity>");
                return;
            }

            if (!TryResolveEntity(name, out var entity))
            {
                return;
            }

            var config = entity.GetConfig(_host.AppPrefix, _host.StatusTopic, _host.DeviceName, _host.DeviceId);

            Console.WriteLine();
            Console.WriteLine($"topic: {entity.GetConfigTopic(_host.HaPrefix, _host.DeviceId)}");
            Console.WriteLine(Reindent(config.ToJson()));
            Console.WriteLine();
        }

        private void Command(string rest)
        {
            var parts = rest.Split(' ', 2, StringSplitOptions.TrimEntries);
            if (parts.Length < 2 || parts[0].Length == 0)
            {
                Error("usage: cmd <target> <payload>");
                return;
            }

            var target = parts[0];
            var payload = parts[1];

            // APIs and entities share a namespace here; APIs are checked first since
            // there are few of them and their names are distinctive.
            var api = _host.Apis.FirstOrDefault(a => a.Name.Equals(target, StringComparison.OrdinalIgnoreCase));
            if (api != null)
            {
                WriteColored(ConsoleColor.Blue, $"-> api {api.Name}: {payload}\n");
                api.HandleCommand(payload);
                return;
            }

            if (!TryResolveEntity(target, out var entity))
            {
                return;
            }

            if (entity is not IHaCommandableEntity commandable)
            {
                Error($"'{entity.Name}' is not commandable");
                return;
            }

            if (entity is IHaSelectableEntity selectable && !selectable.Options.Contains(payload))
            {
                // Not fatal — send it anyway so you can watch how the entity rejects it.
                Warn($"'{payload}' is not one of [{string.Join(", ", selectable.Options)}]");
            }

            WriteColored(ConsoleColor.Blue, $"-> {entity.Name}: {payload}\n");
            commandable.HandleCommand(payload);
        }

        /// <summary>
        /// Resolves by exact name first, then by unique case-insensitive prefix so you
        /// don't have to type out full monitor names.
        /// </summary>
        private bool TryResolveEntity(string name, out IHaEntity entity)
        {
            var entities = _host.Entities;

            var exact = entities.FirstOrDefault(e => e.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (exact != null)
            {
                entity = exact;
                return true;
            }

            var matches = entities
                .Where(e => e.Name.Contains(name, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (matches.Count == 1)
            {
                entity = matches[0];
                return true;
            }

            entity = null!;
            if (matches.Count == 0)
            {
                Error($"no entity matching '{name}' — try 'list'");
            }
            else
            {
                Error($"'{name}' is ambiguous: {string.Join(", ", matches.Select(m => m.Name))}");
            }

            return false;
        }

        private static string Reindent(string json)
        {
            try
            {
                var node = JsonNode.Parse(json);
                return node?.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) ?? json;
            }
            catch (JsonException)
            {
                return json;
            }
        }

        private static void WriteColored(ConsoleColor color, string text)
        {
            var previous = Console.ForegroundColor;
            Console.ForegroundColor = color;
            Console.Write(text);
            Console.ForegroundColor = previous;
        }

        private static void Error(string message) => WriteColored(ConsoleColor.Red, $"{message}\n");

        private static void Warn(string message) => WriteColored(ConsoleColor.Yellow, $"{message}\n");
    }
}
