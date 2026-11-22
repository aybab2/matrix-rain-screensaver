# Selects MatrixRain.scr as your screensaver (same as right-click > Install). No admin / UAC needed.
# Optional: -TimeoutMinutes 10   sets how long the PC must be idle before it starts.
param([int]$TimeoutMinutes = 0)
$ErrorActionPreference = 'Stop'
$scr = Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) 'MatrixRain.scr'
if (-not (Test-Path $scr)) { throw "MatrixRain.scr not found - run build.ps1 first." }

Add-Type @'
using System; using System.Runtime.InteropServices;
public static class Spi {
  [DllImport("user32.dll")] public static extern bool SystemParametersInfo(uint a, uint u, IntPtr p, uint f);
  [DllImport("user32.dll")] public static extern bool SystemParametersInfo(uint a, uint u, ref uint p, uint f);
}
'@
$key = 'HKCU:\Control Panel\Desktop'
Set-ItemProperty $key -Name 'SCRNSAVE.EXE' -Value $scr
[Spi]::SystemParametersInfo(17, 1, [IntPtr]::Zero, 3) | Out-Null          # SPI_SETSCREENSAVEACTIVE = on
if ($TimeoutMinutes -gt 0) {
  [Spi]::SystemParametersInfo(15, [uint32]($TimeoutMinutes * 60), [IntPtr]::Zero, 3) | Out-Null  # SPI_SETSCREENSAVETIMEOUT
}
[uint32]$t = 0; [Spi]::SystemParametersInfo(14, 0, [ref]$t, 0) | Out-Null  # SPI_GETSCREENSAVETIMEOUT
Write-Host "Screensaver set to $scr  (starts after $([math]::Round($t/60,1)) minutes idle)"
