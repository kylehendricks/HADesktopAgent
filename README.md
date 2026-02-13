# HADesktopAgent

A cross-platform desktop agent that integrates with [Home Assistant](https://www.home-assistant.io/) via MQTT. Exposes display, audio, process, and power controls as Home Assistant entities, enabling remote management of Windows and Linux desktops.

## Features

- **Display Switches** — Individual on/off switches for each connected monitor. Monitors are discovered dynamically — hot-plugging a display creates a new switch in Home Assistant automatically.
- **Display Configuration** — An MQTT command topic that accepts a JSON list of monitor names to enable, disabling everything else in a single atomic operation. Useful for switching display presets without toggling monitors one at a time.
- **Audio Device Select** — A select entity listing all available audio output devices. Changing the selection switches the system default audio output.
- **Process Switches** — User-configurable switches that launch and stop applications. Define them in `config.json` with a path, optional arguments, and an icon.
- **Sleep Button** — A button entity that triggers system sleep/suspend.
- **Name Mappings** — Custom display names for monitors and audio devices in Home Assistant. Monitors can be mapped by a stable EDID identifier (manufacturer + product code + serial) derived from the monitor's firmware, so names survive driver updates and port changes.

## Prerequisites

### Windows
- .NET 9.0 SDK or later

### Linux
- .NET 10.0 SDK or later
- `pactl` (PulseAudio utilities) — for audio device control
- `kscreen-doctor` (KDE Plasma) — for display management
- `edid-decode` — for reading monitor EDID data (name and identifier)
- systemd with logind — for sleep and power state
- D-Bus

## Configuration

On first run the agent creates a default config file:

- **Linux**: `~/.local/share/HADesktopAgent/config.json`
- **Windows**: `%LOCALAPPDATA%\HADesktopAgent\config.json`

```json
{
  "Agent": {
    "DeviceId": "ha_agent",
    "DeviceName": "HA Agent"
  },
  "Mqtt": {
    "Host": "127.0.0.1",
    "Username": "",
    "Password": "",
    "DiscoveryPrefix": "homeassistant"
  },
  "ProcessSwitches": [
    {
      "Name": "vlc",
      "PrettyName": "VLC Media Player",
      "ApplicationPath": "/usr/bin/vlc",
      "Icon": "mdi:play"
    }
  ],
  "NameMappings": {
    "Monitors": {
      "SAM-7796-HNTXA00720": "Gaming Monitor",
      "GSM-83CD": "Living Room TV"
    },
    "AudioDevices": {
      "Built-in Audio Digital Stereo (HDMI)": "TV Speakers"
    }
  }
}
```

| Section | Field | Description |
|---------|-------|-------------|
| `Agent` | `DeviceId` | Unique identifier used in MQTT topics and entity IDs |
| `Agent` | `DeviceName` | Friendly name shown in Home Assistant's device registry |
| `Mqtt` | `Host` | MQTT broker hostname or IP |
| `Mqtt` | `Username` / `Password` | Broker credentials (leave empty if unauthenticated) |
| `Mqtt` | `DiscoveryPrefix` | MQTT discovery prefix (default: `homeassistant` — must match Home Assistant's setting) |
| `ProcessSwitches` | `Name` | Entity ID suffix (e.g. `vlc` becomes `switch.ha_agent_vlc`) |
| `ProcessSwitches` | `PrettyName` | Display name in Home Assistant |
| `ProcessSwitches` | `ApplicationPath` | Full path to executable |
| `ProcessSwitches` | `Icon` | [Material Design icon](https://pictogrammers.com/library/mdi/) (default: `mdi:application`) |
| `ProcessSwitches` | `StartArgument` | Optional CLI args passed on launch |
| `ProcessSwitches` | `StopArgument` | Optional CLI args for graceful stop (omit to kill process) |
| `NameMappings` | `Monitors` | Map of identifier → custom name for monitors (see [Name Mappings](#name-mappings)) |
| `NameMappings` | `AudioDevices` | Map of device name → custom name for audio devices (see [Name Mappings](#name-mappings)) |

## Name Mappings

The `NameMappings` config section lets you assign custom names to monitors and audio devices in Home Assistant. Monitors can be mapped by a stable EDID identifier (derived from the monitor's firmware) or by display name. Audio devices are mapped by their device name as reported by the OS. On startup, the agent logs each monitor's identifier to help you find the right key to use — check the log files for lines like `Discovered monitor: 'Odyssey G91SD' (EDID: SAM-7796-HNTXA00720)`.

## Installation

### Linux

From the `HADesktopAgent.Linux/` directory:

```bash
./install.sh
```

This publishes a self-contained build to `~/.local/share/HADesktopAgent/`, installs a systemd user service, and enables it to start on login.

Useful commands after installation:

```bash
systemctl --user status hadesktopagent.service
systemctl --user restart hadesktopagent.service
journalctl --user -u hadesktopagent -f
```

To uninstall:

```bash
./uninstall.sh
```

### Windows

From the `HADesktopAgent.Windows/` directory:

```powershell
.\install-tray-app.ps1
```

This publishes to `%LOCALAPPDATA%\HADesktopAgent\` and creates a startup shortcut so the agent launches on login. It runs as a system tray application.

To uninstall:

```powershell
.\uninstall-tray-app.ps1
```

## Building from Source

```bash
# Linux
dotnet build HADesktopAgent.Linux/

# Windows
dotnet build HADesktopAgent.Windows/
```

## Logs

Logs are written with Serilog to rolling daily files (7-day retention):

- **Linux**: `~/.local/share/HADesktopAgent/logs/`
- **Windows**: `%LOCALAPPDATA%\HADesktopAgent\logs/`

## Home Assistant MQTT Setup

The agent uses [MQTT Discovery](https://www.home-assistant.io/integrations/mqtt/#mqtt-discovery) — entities appear automatically once the agent connects to the same broker as Home Assistant. Ensure:

1. The [MQTT integration](https://www.home-assistant.io/integrations/mqtt/) is configured in Home Assistant
2. The agent's `Mqtt.Host` points to the same broker
3. The `Mqtt.DiscoveryPrefix` matches Home Assistant's discovery prefix (both default to `homeassistant`)

## Display Configuration

In addition to the per-monitor switches, the agent listens on `ha_desktop_agent/{device_id}/display_config/command` for an atomic display configuration payload. Publish a JSON array of monitor names to enable — all unlisted monitors are disabled in one shot. If name mappings are configured, use the mapped names in the payload.

```yaml
# Example: HA script to switch to a single gaming monitor
# (uses the mapped name from NameMappings config)
script:
  gaming_mode:
    sequence:
      - service: mqtt.publish
        data:
          topic: ha_desktop_agent/my_desktop/display_config/command
          payload: '["Gaming Monitor"]'
```
