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
          };

          config = lib.mkIf cfg.enable {
            # config.json is deliberately NOT managed here: it holds the MQTT
            # password, and anything this module wrote would land world-readable in
            # the nix store. It stays a user-managed file at
            # $XDG_DATA_HOME/HADesktopAgent/config.json.
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
                ExecStart = lib.getExe cfg.package;
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
