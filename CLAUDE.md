# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What This Is

HADesktopAgent is a cross-platform .NET background service that exposes desktop controls (monitors, audio devices, applications, sleep) to Home Assistant via MQTT Discovery. It's designed for couch gaming setups where you want to switch displays, audio outputs, or launch apps from an HA dashboard.

## Build Commands

```bash
# Build platform-specific projects
dotnet build HADesktopAgent.Windows/
dotnet build HADesktopAgent.Linux/

# Publish Windows (output: %LOCALAPPDATA%\HADesktopAgent\)
dotnet publish HADesktopAgent.Windows/ -c Release

# Publish Linux self-contained (output: ~/.local/share/HADesktopAgent/)
dotnet publish HADesktopAgent.Linux/ -c Release -r linux-x64 --self-contained
```

There are no automated tests. Validation happens at startup via `IValidateOptions<T>` implementations.

## Install / Run

```bash
# Windows — installs tray app and creates Startup shortcut
HADesktopAgent.Windows/install-tray-app.ps1

# Linux — installs and enables a systemd user service
bash HADesktopAgent.Linux/install.sh

# Linux service management
systemctl --user status hadesktopagent.service
systemctl --user restart hadesktopagent.service
journalctl --user -u hadesktopagent -f
```

## Architecture

### Project Layout

- **`HADesktopAgent.Core/`** — Platform-agnostic logic: MQTT client, HA entity abstractions, display/audio/process/sleep interfaces
- **`HADesktopAgent.Windows/`** — Windows implementations + WinForms tray app host
- **`HADesktopAgent.Linux/`** — Linux implementations + Worker Service host

### MQTT / Home Assistant Integration

`MqttManager` owns the raw MQTT connection (reconnect loop, online/offline status, suspend/resume awareness). `MqttHaManager` sits on top and handles Home Assistant MQTT Discovery: it registers entities, publishes config/state payloads to discovery topics, and routes incoming command messages to the right entity.

Topic conventions:
- Discovery config: `homeassistant/{type}/{device_id}_{entity_id}/config`
- State: `ha_desktop_agent/{device_id}/{entity_name}/state`
- Command: `ha_desktop_agent/{device_id}/{entity_name}/command`

### Entity Model

Entities implement a layered interface hierarchy in `HADesktopAgent.Core/Entity/`:

- `IHaEntity` — base (Name, Icon, EntityType, config topic)
- `IHaStatefulEntity` — fires `StateUpdated` event; `MqttHaManager` subscribes and publishes state
- `IHaCommandableEntity` — handles `HandleCommand(string payload)`; `MqttHaManager` routes inbound MQTT to these
- `IHaSelectableEntity` — extends commandable with an options list (used by audio select)

To add a new feature, implement the appropriate interfaces, register the entity with `MqttHaManager.RegisterEntity()`, and wire up the platform-specific dependency in `Program.cs`.

### Platform Abstraction Pattern

Core defines interfaces (e.g., `IMonitorSwitcher`, `IAudioDeviceManager`, `ISleepControl`). Each platform project provides concrete implementations:

| Feature | Windows | Linux |
|---|---|---|
| Display control | `DesktopManager` NuGet | `kscreen-doctor` CLI |
| Display hot-plug | `DesktopManager.MonitorWatcher` polling | Polls `kscreen-doctor --outputs` every 5s |
| Monitor identity | EDID via DesktopManager | DRM EDID parsed from `/sys/class/drm/` |
| Audio | WASAPI Core Audio (COM) | `pactl` CLI |
| Sleep | `PowerControl` COM | systemd logind D-Bus (`Tmds.DBus`) |

### Display Name Mapping

Monitors are identified by EDID (stable across port/driver changes). `NameMappingConfiguration` maps EDID identifiers or display names to user-friendly names. `MonitorSwitchManager` (Core) applies these mappings when creating switch entities.

### `DisplayConfigurationApi`

A special non-standard entity that accepts a JSON array payload on `display_config/command` to atomically switch an entire display preset in one MQTT message. This is the primary mechanism for couch gaming mode switches.

## Configuration

`config.json` is created by the user before first run. Key sections:

```json
{
  "Agent": { "DeviceId": "...", "DeviceName": "..." },
  "Mqtt": { "Host": "...", "Username": "...", "Password": "...", "DiscoveryPrefix": "homeassistant", "StatusTopic": "..." },
  "ProcessSwitches": [
    { "Name": "Steam", "Icon": "mdi:steam", "ExecutablePath": "...", "StartArguments": "...", "StopArguments": "..." }
  ],
  "NameMappings": { "Monitors": { "EDID_ID": "Living Room TV" }, "AudioDevices": { "device_id": "TV Speakers" } }
}
```

Config location: `%LOCALAPPDATA%\HADesktopAgent\config.json` (Windows) / `~/.local/share/HADesktopAgent/config.json` (Linux).

## Logging

Serilog rolling file logs (daily, 7-day retention):
- Windows: `%LOCALAPPDATA%\HADesktopAgent\logs\app-{date}.log`
- Linux: `~/.local/share/HADesktopAgent/logs/app-{date}.log`
