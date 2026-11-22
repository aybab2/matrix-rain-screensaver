# Turns the screensaver off (Screen Saver Settings will show "(None)").
Add-Type @'
using System; using System.Runtime.InteropServices;
public static class Spi { [DllImport("user32.dll")] public static extern bool SystemParametersInfo(uint a, uint u, IntPtr p, uint f); }
'@
Remove-ItemProperty 'HKCU:\Control Panel\Desktop' -Name 'SCRNSAVE.EXE' -ErrorAction SilentlyContinue
[Spi]::SystemParametersInfo(17, 0, [IntPtr]::Zero, 3) | Out-Null   # SPI_SETSCREENSAVEACTIVE = off
Write-Host "Screensaver turned off."
