# Removes the Ctrl+M hotkey (stops the listener and removes it from sign-in).
Remove-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'MatrixRainHotkey' -ErrorAction SilentlyContinue
Get-CimInstance Win32_Process -Filter "Name='MatrixRain.exe'" | Where-Object { $_.CommandLine -like '*/hotkey*' } |
  ForEach-Object { Stop-Process -Id $_.ProcessId -Force }
Write-Host 'Ctrl+M hotkey removed.'
