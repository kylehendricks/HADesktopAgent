# Install HA Windows Agent as a tray application that starts with Windows
# No administrator rights required!

$appName = "HA Windows Agent"
$publishPath = "$env:LOCALAPPDATA\HAWindowsAgent"
$exePath = "$publishPath\HAWindowsAgent.exe"
$startupPath = "$env:APPDATA\Microsoft\Windows\Start Menu\Programs\Startup"
$shortcutPath = "$startupPath\$appName.lnk"

Write-Host "=== Installing HA Windows Agent as Tray Application ===" -ForegroundColor Cyan
Write-Host ""

# Build and publish
Write-Host "Publishing application..." -ForegroundColor Cyan
dotnet publish -c Release -o $publishPath

if ($LASTEXITCODE -ne 0) {
    Write-Error "Build failed"
    exit 1
}

Write-Host "Application published to: $publishPath" -ForegroundColor Green

# Create startup shortcut
Write-Host "`nCreating startup shortcut..." -ForegroundColor Cyan

$WshShell = New-Object -ComObject WScript.Shell
$Shortcut = $WshShell.CreateShortcut($shortcutPath)
$Shortcut.TargetPath = $exePath
$Shortcut.WorkingDirectory = $publishPath
$Shortcut.Description = "Home Assistant Windows Agent"
$Shortcut.Save()

Write-Host "Startup shortcut created: $shortcutPath" -ForegroundColor Green

# Ask if user wants to start now
Write-Host ""
$response = Read-Host "Do you want to start the application now? (Y/N)"

if ($response -eq 'Y' -or $response -eq 'y') {
    Write-Host "Starting application..." -ForegroundColor Cyan
    Start-Process $exePath -WorkingDirectory $publishPath
    Write-Host "Application started! Look for the icon in your system tray." -ForegroundColor Green
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
