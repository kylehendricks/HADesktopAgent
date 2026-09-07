{
  description = "HADesktopAgent — Home Assistant desktop agent (Linux)";

  inputs = {
    nixpkgs.url = "nixpkgs/nixos-26.05";
  };

  outputs = { self, nixpkgs, ... }:
    let
      systems = [ "x86_64-linux" "aarch64-linux" ];
      forAllSystems = f:
        nixpkgs.lib.genAttrs systems (system: f nixpkgs.legacyPackages.${system});
    in
    {
      # Runtime tools the Linux agent shells out to. Kept as a separate list so the
      # eventual package derivation can reuse it as runtimeInputs.
      runtimeDeps = forAllSystems (pkgs: [
        pkgs.pulseaudio            # pactl — speaks the PulseAudio protocol to pipewire-pulse
        pkgs.kdePackages.libkscreen # kscreen-doctor
        pkgs.edid-decode
      ]);

      devShells = forAllSystems (pkgs: {
        default = pkgs.mkShell {
          packages = [ pkgs.dotnet-sdk_10 ] ++ self.runtimeDeps.${pkgs.system};

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
