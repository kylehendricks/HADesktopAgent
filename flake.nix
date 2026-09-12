{
  description = "HADesktopAgent — Home Assistant desktop agent (Linux)";

  inputs = {
    nixpkgs.url = "nixpkgs/nixos-26.05";
  };

  outputs = { self, nixpkgs, ... }:
    let
      systems = [ "x86_64-linux" "aarch64-linux" ];
      forAllSystems = f:
        nixpkgs.lib.genAttrs systems (system: f {
          inherit system;
          pkgs = nixpkgs.legacyPackages.${system};
        });
    in
    {
      # Runtime tools the Linux agent shells out to. Shared by the dev shell and by
      # the package's PATH wrapper, so the two can't drift.
      runtimeDeps = forAllSystems ({ pkgs, ... }: [
        pkgs.pulseaudio             # pactl — speaks the PulseAudio protocol to pipewire-pulse
        pkgs.kdePackages.libkscreen # kscreen-doctor
        pkgs.edid-decode
      ]);

      packages = forAllSystems ({ pkgs, system }: rec {
        default = hadesktopagent;

        hadesktopagent = pkgs.buildDotnetModule {
          pname = "hadesktopagent";
          version = "0.1.0";

          src = pkgs.lib.cleanSourceWith {
            src = ./.;
            filter = path: type:
              let base = baseNameOf path; in
              !(type == "directory" && (base == "bin" || base == "obj"));
          };

          # Only the Linux project. Pointing at the solution would drag in
          # HADesktopAgent.Windows, which targets net10.0-windows.
          projectFile = "HADesktopAgent.Linux/HADesktopAgent.Linux.csproj";
          nugetDeps = ./nuget-deps.json;

          dotnet-sdk = pkgs.dotnet-sdk_10;
          dotnet-runtime = pkgs.dotnet-runtime_10;

          executables = [ "HADesktopAgent.Linux" ];

          # buildDotnetModule's own runtimeDeps is LD_LIBRARY_PATH; these tools are
          # executables the agent spawns, so they belong on PATH.
          makeWrapperArgs = [
            "--prefix"
            "PATH"
            ":"
            (pkgs.lib.makeBinPath self.runtimeDeps.${system})
          ];

          meta = {
            description = "Exposes desktop controls to Home Assistant over MQTT";
            mainProgram = "HADesktopAgent.Linux";
            platforms = pkgs.lib.platforms.linux;
          };
        };
      });

      homeManagerModules.default = { config, lib, pkgs, ... }:
        let
          cfg = config.services.hadesktopagent;
          jsonFormat = pkgs.formats.json { };

          # Options left unset arrive as null. Serializing those would hand the agent an
          # explicit JSON null where it wants a string, overriding its own default with
          # nothing, so they are dropped on the way out instead.
          stripNulls = value:
            if lib.isList value then map stripNulls value
            else if lib.isAttrs value && !lib.isDerivation value then
              lib.mapAttrs (_: stripNulls) (lib.filterAttrs (_: v: v != null) value)
            else value;

          generatedConfig =
            jsonFormat.generate "hadesktopagent-config.json" (stripNulls cfg.settings);

          # Sub-sections are submodules rather than plain attrsets so typos fail at build
          # time, but each carries the JSON freeform type as well: a config key the agent
          # grows before this module does still passes straight through.
          section = options: lib.types.submodule {
            freeformType = jsonFormat.type;
            inherit options;
          };

          processSwitchType = lib.types.submodule {
            freeformType = jsonFormat.type;
            options = {
              Name = lib.mkOption {
                type = lib.types.str;
                description = "Entity ID suffix, e.g. `steam` becomes `switch.<device_id>_steam`.";
              };

              PrettyName = lib.mkOption {
                type = lib.types.str;
                description = "Name shown in Home Assistant.";
              };

              Icon = lib.mkOption {
                type = lib.types.str;
                default = "mdi:application";
                description = "Material Design icon for the switch.";
              };

              ApplicationPath = lib.mkOption {
                type = lib.types.str;
                example = lib.literalExpression "lib.getExe pkgs.steam";
                description = ''
                  Executable to launch. Point this at a store path so the switch cannot
                  break when the program is upgraded out from under it.
                '';
              };

              StartArgument = lib.mkOption {
                type = lib.types.nullOr lib.types.str;
                default = null;
                description = "Arguments passed when the switch is turned on.";
              };

              StopArgument = lib.mkOption {
                type = lib.types.nullOr lib.types.str;
                default = null;
                description = ''
                  Arguments for a graceful stop. Leave null to kill the process instead.
                '';
              };
            };
          };
        in
        {
          options.services.hadesktopagent = {
            enable = lib.mkEnableOption "the Home Assistant desktop agent";

            package = lib.mkOption {
              type = lib.types.package;
              default = self.packages.${pkgs.stdenv.hostPlatform.system}.default;
              defaultText = lib.literalExpression "hadesktopagent";
              description = "The agent package to run.";
            };

            configFile = lib.mkOption {
              type = lib.types.nullOr lib.types.path;
              default = generatedConfig;
              defaultText = lib.literalExpression "a config.json generated from `settings`";
              description = ''
                config.json to run the agent against, passed as `--config`.

                Set to null to manage the file by hand instead: the agent then falls back
                to `$XDG_DATA_HOME/HADesktopAgent/config.json` and creates a skeleton there
                on first run. {option}`settings` is ignored in that case.
              '';
            };

            settings = lib.mkOption {
              default = { };
              description = ''
                Contents of the agent's config.json. Keys match the file exactly.

                This lands in the world-readable Nix store, so the broker password does not
                belong here — see {option}`settings.Mqtt.PasswordFile`.
              '';
              example = lib.literalExpression ''
                {
                  Agent = {
                    DeviceId = "living_room_pc";
                    DeviceName = "Living Room PC";
                  };
                  Mqtt = {
                    Host = "homeassistant.lan";
                    Username = "desktop-agent";
                    PasswordFile = "/run/user/1000/secrets/mqtt-password";
                  };
                  ProcessSwitches = [{
                    Name = "steam";
                    PrettyName = "Steam Big Picture";
                    Icon = "mdi:steam";
                    ApplicationPath = lib.getExe pkgs.steam;
                    StartArgument = "-gamepadui";
                    StopArgument = "-shutdown";
                  }];
                  NameMappings.Monitors."SAM-7796-HNTXA00720" = "Living Room TV";
                }
              '';
              type = section {
                Agent = lib.mkOption {
                  default = { };
                  description = "Identity the agent registers under in Home Assistant.";
                  type = section {
                    DeviceId = lib.mkOption {
                      type = lib.types.str;
                      default = "ha_agent";
                      description = ''
                        Unique identifier used in MQTT topics and entity IDs. Changing it
                        orphans the entities registered under the previous value.
                      '';
                    };

                    DeviceName = lib.mkOption {
                      type = lib.types.str;
                      default = "HA Agent";
                      description = "Friendly name in Home Assistant's device registry.";
                    };
                  };
                };

                Mqtt = lib.mkOption {
                  default = { };
                  description = "Broker connection.";
                  type = section {
                    Host = lib.mkOption {
                      type = lib.types.str;
                      default = "127.0.0.1";
                      description = "Broker hostname or IP.";
                    };

                    Username = lib.mkOption {
                      type = lib.types.str;
                      default = "";
                      description = ''
                        Broker username. Leave empty for an unauthenticated broker; the agent
                        requires a username and a password together or neither.
                      '';
                    };

                    Password = lib.mkOption {
                      type = lib.types.nullOr lib.types.str;
                      default = null;
                      description = ''
                        Broker password, in the clear. Prefer {option}`PasswordFile`: anything
                        set here is copied into the world-readable Nix store, where every user
                        on the machine can read it. Setting it emits a warning.
                      '';
                    };

                    PasswordFile = lib.mkOption {
                      type = lib.types.nullOr lib.types.path;
                      default = null;
                      example = "/run/user/1000/secrets/mqtt-password";
                      description = ''
                        Path to a file holding the broker password, read by the agent at
                        startup. Only the path is stored in config.json, so the secret itself
                        never reaches the Nix store — point this at sops-nix, agenix, or any
                        file only your user can read. A single trailing newline is stripped.
                      '';
                    };

                    DiscoveryPrefix = lib.mkOption {
                      type = lib.types.str;
                      default = "homeassistant";
                      description = "Must match Home Assistant's MQTT discovery prefix.";
                    };

                    StatusTopic = lib.mkOption {
                      type = lib.types.str;
                      default = "ha_desktop_agent/status";
                      description = "Topic carrying the agent's online/offline birth-and-will status.";
                    };
                  };
                };

                ProcessSwitches = lib.mkOption {
                  type = lib.types.listOf processSwitchType;
                  default = [ ];
                  description = "Applications exposed to Home Assistant as switches.";
                };

                NameMappings = lib.mkOption {
                  default = { };
                  description = ''
                    Friendly names for discovered hardware. Unmapped devices keep the name
                    the OS reports.
                  '';
                  type = section {
                    Monitors = lib.mkOption {
                      type = lib.types.attrsOf lib.types.str;
                      default = { };
                      example = { "SAM-7796-HNTXA00720" = "Living Room TV"; };
                      description = ''
                        Map of monitor identifier to display name. Keys are an EDID identifier
                        (`MFG-PRODUCT-SERIAL`, or `MFG-PRODUCT` for every monitor of a model)
                        or the product name. The agent logs the identifier of each monitor it
                        discovers at startup.
                      '';
                    };

                    AudioDevices = lib.mkOption {
                      type = lib.types.attrsOf lib.types.str;
                      default = { };
                      example = { "Built-in Audio Digital Stereo (HDMI)" = "TV Speakers"; };
                      description = ''
                        Map of audio device name to display name. Keys are the PulseAudio sink
                        description on Linux.
                      '';
                    };
                  };
                };
              };
            };
          };

          config = lib.mkIf cfg.enable {
            assertions = [
              {
                assertion = cfg.settings.Mqtt.PasswordFile == null
                  || !lib.hasPrefix builtins.storeDir (toString cfg.settings.Mqtt.PasswordFile);
                message = ''
                  services.hadesktopagent.settings.Mqtt.PasswordFile points into the Nix
                  store, which is world-readable — the secret would be exposed to every user
                  on the machine. Give it a runtime path instead (a sops-nix or agenix
                  secret, or a file you place yourself with mode 0600).
                '';
              }
            ];

            warnings = lib.optional (cfg.settings.Mqtt.Password != null) ''
              services.hadesktopagent.settings.Mqtt.Password writes the broker password into
              the world-readable Nix store. Use settings.Mqtt.PasswordFile instead.
            '';

            systemd.user.services.hadesktopagent = {
              Unit = {
                Description = "Home Assistant Desktop Agent";
                PartOf = [ "graphical-session.target" ];
                After = [ "plasma-core.target" "graphical-session.target" ];
              };

              Service = {
                # AddSystemd() in Program.cs sends the readiness ping, so systemd can wait
                # for the agent to actually be up rather than just forked.
                Type = "notify";
                ExecStart = lib.escapeShellArgs ([ (lib.getExe cfg.package) ]
                  ++ lib.optionals (cfg.configFile != null) [ "--config" "${cfg.configFile}" ]);
                Restart = "on-failure";
                RestartSec = 5;
                # Only the agent is killed on stop/restart: applications started by
                # ProcessSwitches are children, and control-group would take Steam
                # down with the service.
                KillMode = "process";
              };

              Install.WantedBy = [ "graphical-session.target" ];
            };
          };
        };

      devShells = forAllSystems ({ pkgs, system }: {
        default = pkgs.mkShell {
          packages = [ pkgs.dotnet-sdk_10 ] ++ self.runtimeDeps.${system};

          env = {
            DOTNET_ROOT = "${pkgs.dotnet-sdk_10}/share/dotnet";
            DOTNET_CLI_TELEMETRY_OPTOUT = "1";
            DOTNET_NOLOGO = "1";
          };

          shellHook = ''
            echo "HADesktopAgent dev shell"
            echo "  dotnet         $(dotnet --version 2>/dev/null || echo MISSING)"
            echo "  pactl          $(command -v pactl || echo MISSING)"
            echo "  kscreen-doctor $(command -v kscreen-doctor || echo MISSING)"
            echo "  edid-decode    $(command -v edid-decode || echo MISSING)"
            echo
            echo "Run the agent with:  dotnet run --project HADesktopAgent.Linux"
          '';
        };
      });
    };
}
