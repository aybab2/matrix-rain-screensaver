# Builds MatrixRain.scr (the screensaver) and MatrixRain.exe (same program, used by the Ctrl+M hotkey)
# from src\MatrixRain.cs using the C# compiler that ships with Windows.
# Usage:  powershell -ExecutionPolicy Bypass -File build.ps1
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$csc  = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
$exe  = Join-Path $here 'MatrixRain.exe'
$scr  = Join-Path $here 'MatrixRain.scr'

# The hotkey listener keeps MatrixRain.exe open; stop it while rebuilding and restart it afterwards.
$listener = @(Get-CimInstance Win32_Process -Filter "Name='MatrixRain.exe'" | Where-Object { $_.CommandLine -like '*/hotkey*' })
foreach ($p in $listener) { Stop-Process -Id $p.ProcessId -Force }

$tmp = Join-Path $here 'MatrixRain.build.exe'
& $csc /nologo /optimize+ /target:winexe /platform:anycpu "/out:$tmp" `
    /reference:System.Windows.Forms.dll /reference:System.Drawing.dll `
    (Join-Path $here 'src\MatrixRain.cs')
if ($LASTEXITCODE -ne 0) { throw "Compile failed ($LASTEXITCODE)" }

Copy-Item -Force $tmp $scr
Move-Item -Force $tmp $exe
Write-Host "Built $scr and $exe"

if ($listener.Count -gt 0) { Start-Process $exe -ArgumentList '/hotkey'; Write-Host 'Restarted the Ctrl+M hotkey listener.' }
