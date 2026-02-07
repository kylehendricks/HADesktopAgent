# HADesktopAgent

A cross-platform desktop agent that integrates with [Home Assistant](https://www.home-assistant.io/) via MQTT. Exposes display, audio, process, and power controls as Home Assistant entities, enabling remote management of Windows and Linux desktops.

## Features

### Display Switches
Individual on/off switches for each connected monitor. Monitors are discovered dynamically — hot-plugging a display creates a new switch in Home Assistant automatically. A safety check prevents disabling the last active monitor.

- **Windows**: Uses the DesktopManager library (enable only — Windows doesn't expose a clean API for disabling individual monitors)
- **Linux**: Uses `kscreen-doctor` for full enable/disable support

### Audio Device Select
A select entity listing all available audio output devices. Changing the selection switches the system default audio output.

- **Windows**: Windows Core Audio COM API
- **Linux**: PulseAudio via `pactl`, with real-time device monitoring via `pactl subscribe`

### Process Switches
User-configurable switches that launch and stop applications. Define them in `config.json` with a path, optional arguments, and an icon. Turning a switch on starts the process; turning it off sends a graceful stop (or kills it if no stop argument is configured).

### Sleep Button
A button entity that triggers system sleep/suspend.

- **Windows**: Windows Power API
- **Linux**: systemd logind via D-Bus

### Power State Awareness
The agent detects system suspend/resume events to cleanly disconnect and reconnect MQTT, preventing stale availability states in Home Assistant.

## Architecture

```
HADesktopAgent.sln
├── HADesktopAgent.Core/       # Shared library — MQTT, entities, interfaces
├── HADesktopAgent.Windows/    # Windows Forms tray application (net9.0-windows)
└── HADesktopAgent.Linux/      # Console application / systemd service (net10.0)
```

The core library defines platform-agnostic interfaces (`IMonitorSwitcher`, `IAudioDeviceManager`, `ISleepControl`, `IPowerState`) and handles all MQTT communication and Home Assistant discovery. Each platform variant provides concrete implementations using native APIs or system tools.

## Prerequisites

### Windows
- .NET 9.0 SDK or later

### Linux
- .NET 10.0 SDK or later
- `pactl` (PulseAudio utilities) — for audio device control
- `kscreen-doctor` (KDE Plasma) — for display management
- systemd with logind — for sleep and power state
- D-Bus

## Configuration

On first run the agent creates a default config file:

- **Linux**: `~/.local/share/HADesktopAgent/config.json`
- **Windows**: `%LOCALAPPDATA%\HAWindowsAgent\config.json`

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
  ]
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

This publishes to `%LOCALAPPDATA%\HAWindowsAgent\` and creates a startup shortcut so the agent launches on login. It runs as a system tray application.

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
- **Windows**: `%LOCALAPPDATA%\HAWindowsAgent\logs\`

## Home Assistant MQTT Setup

The agent uses [MQTT Discovery](https://www.home-assistant.io/integrations/mqtt/#mqtt-discovery) — entities appear automatically once the agent connects to the same broker as Home Assistant. Ensure:

1. The [MQTT integration](https://www.home-assistant.io/integrations/mqtt/) is configured in Home Assistant
2. The agent's `Mqtt.Host` points to the same broker
3. The `Mqtt.DiscoveryPrefix` matches Home Assistant's discovery prefix (both default to `homeassistant`)
