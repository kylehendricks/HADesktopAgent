# Uninstall HA Windows Agent tray application
# No administrator rights required!

$appName = "HA Windows Agent"
$publishPath = "$env:LOCALAPPDATA\HAWindowsAgent"
$startupPath = "$env:APPDATA\Microsoft\Windows\Start Menu\Programs\Startup"
$shortcutPath = "$startupPath\$appName.lnk"

Write-Host "=== Uninstalling HA Windows Agent ===" -ForegroundColor Cyan
Write-Host ""

# Kill any running instances
Write-Host "Stopping application..." -ForegroundColor Yellow
$processes = Get-Process -Name "HAWindowsAgent" -ErrorAction SilentlyContinue

if ($processes) {
    foreach ($process in $processes) {
        $process.Kill()
        $process.WaitForExit()
    }
    Write-Host "Application stopped" -ForegroundColor Green
} else {
    Write-Host "Application is not running" -ForegroundColor Gray
}

# Remove startup shortcut
Write-Host "`nRemoving startup shortcut..." -ForegroundColor Yellow
if (Test-Path $shortcutPath) {
    Remove-Item $shortcutPath -Force
    Write-Host "Startup shortcut removed" -ForegroundColor Green
} else {
    Write-Host "Startup shortcut not found" -ForegroundColor Gray
}

# Ask if user wants to remove installation files
Write-Host ""
$response = Read-Host "Do you want to remove all application files from $publishPath ? (Y/N)"

if ($response -eq 'Y' -or $response -eq 'y') {
    Write-Host "Removing application files..." -ForegroundColor Yellow
    if (Test-Path $publishPath) {
        Remove-Item $publishPath -Recurse -Force
        Write-Host "Application files removed" -ForegroundColor Green
    }
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Green
Write-Host "Uninstallation complete!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
