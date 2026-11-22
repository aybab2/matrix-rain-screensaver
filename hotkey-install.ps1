# Makes Ctrl+M start the Matrix rain at any time. A tiny listener (MatrixRain.exe /hotkey) starts
# now and at every sign-in. No admin / UAC needed.
$ErrorActionPreference = 'Stop'
$exe = Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) 'MatrixRain.exe'
if (-not (Test-Path $exe)) { throw "MatrixRain.exe not found - run build.ps1 first." }

Set-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'MatrixRainHotkey' -Value "`"$exe`" /hotkey"

$running = Get-CimInstance Win32_Process -Filter "Name='MatrixRain.exe'" | Where-Object { $_.CommandLine -like '*/hotkey*' }
if (-not $running) {
  $p = Start-Process $exe -ArgumentList '/hotkey' -PassThru
  Start-Sleep -Milliseconds 800
  $p.Refresh()
  if ($p.HasExited -and $p.ExitCode -eq 2) { throw 'Ctrl+M is already used by another program - hotkey not set.' }
}
Write-Host 'Ctrl+M now starts the Matrix rain (also after restarts).'
