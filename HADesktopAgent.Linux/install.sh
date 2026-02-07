#!/usr/bin/env bash
# Install HADesktopAgent.Linux as a systemd user service
# Run this script from the HADesktopAgent.Linux/ directory

set -euo pipefail

INSTALL_DIR="$HOME/.local/share/HADesktopAgent"
SERVICE_DIR="$HOME/.config/systemd/user"
SERVICE_NAME="hadesktopagent.service"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

echo "=== Installing Home Assistant Desktop Agent ==="
echo ""

# Build and publish
echo "Publishing application..."
dotnet publish "$SCRIPT_DIR" -c Release -r linux-x64 --self-contained -o "$INSTALL_DIR"

if [ $? -ne 0 ]; then
    echo "ERROR: Build failed"
    exit 1
fi

echo "Application published to: $INSTALL_DIR"

# Install systemd service
echo ""
echo "Installing systemd user service..."
mkdir -p "$SERVICE_DIR"
cp "$SCRIPT_DIR/$SERVICE_NAME" "$SERVICE_DIR/$SERVICE_NAME"

systemctl --user daemon-reload
systemctl --user enable "$SERVICE_NAME"

echo "Service installed and enabled"

# Start or restart the service
echo ""
if systemctl --user is-active --quiet "$SERVICE_NAME" 2>/dev/null; then
    read -rp "Service is running. Restart to apply updates? (y/N) " response
    if [[ "$response" =~ ^[Yy]$ ]]; then
        echo "Restarting service..."
        systemctl --user restart "$SERVICE_NAME"
        echo "Service restarted!"
    fi
else
    read -rp "Do you want to start the service now? (y/N) " response
    if [[ "$response" =~ ^[Yy]$ ]]; then
        echo "Starting service..."
        systemctl --user start "$SERVICE_NAME"
        echo "Service started!"
    fi
fi

echo ""
echo "========================================"
echo "Installation complete!"
echo "========================================"
echo ""
echo "The service will:"
echo "- Start automatically when you log in"
echo "- Restart automatically on failure"
echo "- Run as a systemd user service"
echo ""
echo "Useful commands:"
echo "  systemctl --user status $SERVICE_NAME"
echo "  systemctl --user restart $SERVICE_NAME"
echo "  journalctl --user -u $SERVICE_NAME -f"
echo ""
echo "Installation location: $INSTALL_DIR"
echo "Service file: $SERVICE_DIR/$SERVICE_NAME"
