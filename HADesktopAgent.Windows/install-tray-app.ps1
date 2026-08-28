# Install HA Desktop Agent as a tray application that starts with Windows
# No administrator rights required!

$appName = "HA Desktop Agent"
$publishPath = "$env:LOCALAPPDATA\HADesktopAgent"
$exeName = "HADesktopAgent.Windows.exe"
$exePath = "$publishPath\$exeName"
$startupPath = "$env:APPDATA\Microsoft\Windows\Start Menu\Programs\Startup"
$shortcutPath = "$startupPath\$appName.lnk"

# Legacy install (pre-rename from "HA Windows Agent")
$legacyShortcutPath = "$startupPath\HA Windows Agent.lnk"
$legacyPublishPath = "$env:LOCALAPPDATA\HAWindowsAgent"
$legacyExeName = "HAWindowsAgent"

Write-Host "=== Installing HA Desktop Agent as Tray Application ===" -ForegroundColor Cyan
Write-Host ""

# Stop any running instances so publish can overwrite locked files
$wasRunning = $false
foreach ($processName in @([System.IO.Path]::GetFileNameWithoutExtension($exeName), $legacyExeName)) {
    $running = Get-Process -Name $processName -ErrorAction SilentlyContinue
    if ($running) {
        Write-Host "Stopping running instance ($processName)..." -ForegroundColor Yellow
        $running | Stop-Process -Force
        $running | Wait-Process -Timeout 10 -ErrorAction SilentlyContinue
        $wasRunning = $true
    }
}

# Clean up legacy install so it doesn't also launch at login
if (Test-Path $legacyShortcutPath) {
    Write-Host "Removing legacy startup shortcut: $legacyShortcutPath" -ForegroundColor Yellow
    Remove-Item $legacyShortcutPath -Force
}
if (Test-Path $legacyPublishPath) {
    Write-Host "Removing legacy install folder: $legacyPublishPath" -ForegroundColor Yellow
    Remove-Item $legacyPublishPath -Recurse -Force
}

# Build and publish (anchored to this script's project, works from any directory)
Write-Host "Publishing application..." -ForegroundColor Cyan
dotnet publish "$PSScriptRoot" -c Release -o $publishPath

if ($LASTEXITCODE -ne 0) {
    Write-Error "Build failed"
    exit 1
}

if (-not (Test-Path $exePath)) {
    Write-Error "Publish succeeded but $exePath was not found"
    exit 1
}

Write-Host "Application published to: $publishPath" -ForegroundColor Green

# Warn if config.json is missing (required before first run)
$configPath = "$publishPath\config.json"
$hasConfig = Test-Path $configPath
if (-not $hasConfig) {
    Write-Host ""
    Write-Host "WARNING: No config.json found at $configPath" -ForegroundColor Yellow
    Write-Host "The agent will not work until you create one. See the README for the format." -ForegroundColor Yellow
}

# Create startup shortcut
Write-Host "`nCreating startup shortcut..." -ForegroundColor Cyan

$WshShell = New-Object -ComObject WScript.Shell
$Shortcut = $WshShell.CreateShortcut($shortcutPath)
$Shortcut.TargetPath = $exePath
$Shortcut.WorkingDirectory = $publishPath
$Shortcut.Description = "Home Assistant Desktop Agent"
$Shortcut.Save()

Write-Host "Startup shortcut created: $shortcutPath" -ForegroundColor Green

# Start (or restart) the app
Write-Host ""
if ($wasRunning) {
    Write-Host "Restarting application..." -ForegroundColor Cyan
    Start-Process $exePath -WorkingDirectory $publishPath
    Write-Host "Application restarted! Look for the icon in your system tray." -ForegroundColor Green
}
elseif ($hasConfig) {
    $response = Read-Host "Do you want to start the application now? (Y/N)"
    if ($response -eq 'Y' -or $response -eq 'y') {
        Write-Host "Starting application..." -ForegroundColor Cyan
        Start-Process $exePath -WorkingDirectory $publishPath
        Write-Host "Application started! Look for the icon in your system tray." -ForegroundColor Green
    }
}
else {
    Write-Host "Not starting the application - create $configPath first, then run $exeName." -ForegroundColor Yellow
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Green
Write-Host "Installation complete!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
Write-Host ""
Write-Host "The application will:" -ForegroundColor Cyan
Write-Host "- Start automatically when you log in" -ForegroundColor White
Write-Host "- Run in the system tray (bottom-right of taskbar)" -ForegroundColor White
Write-Host "- Double-click the tray icon for info" -ForegroundColor White
Write-Host "- Right-click the tray icon to exit" -ForegroundColor White
Write-Host ""
Write-Host "Installation location: $publishPath" -ForegroundColor Cyan
Write-Host "Startup shortcut: $shortcutPath" -ForegroundColor Cyan
