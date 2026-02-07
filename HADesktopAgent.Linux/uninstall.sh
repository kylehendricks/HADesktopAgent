#!/usr/bin/env bash
# Uninstall HADesktopAgent.Linux systemd user service

set -euo pipefail

INSTALL_DIR="$HOME/.local/share/HADesktopAgent"
SERVICE_DIR="$HOME/.config/systemd/user"
SERVICE_NAME="hadesktopagent.service"

echo "=== Uninstalling Home Assistant Desktop Agent ==="
echo ""

# Stop the service
echo "Stopping service..."
if systemctl --user is-active --quiet "$SERVICE_NAME" 2>/dev/null; then
    systemctl --user stop "$SERVICE_NAME"
    echo "Service stopped"
else
    echo "Service is not running"
fi

# Disable the service
echo "Disabling service..."
if systemctl --user is-enabled --quiet "$SERVICE_NAME" 2>/dev/null; then
    systemctl --user disable "$SERVICE_NAME"
    echo "Service disabled"
else
    echo "Service is not enabled"
fi

# Remove service file
echo ""
echo "Removing service file..."
if [ -f "$SERVICE_DIR/$SERVICE_NAME" ]; then
    rm "$SERVICE_DIR/$SERVICE_NAME"
    echo "Service file removed"
else
    echo "Service file not found"
fi

systemctl --user daemon-reload

# Ask if user wants to remove application files
echo ""
read -rp "Do you want to remove all application files from $INSTALL_DIR? (y/N) " response

if [[ "$response" =~ ^[Yy]$ ]]; then
    echo "Removing application files..."
    if [ -d "$INSTALL_DIR" ]; then
        rm -rf "$INSTALL_DIR"
        echo "Application files removed"
    else
        echo "Application directory not found"
    fi
fi

echo ""
echo "========================================"
echo "Uninstallation complete!"
echo "========================================"
