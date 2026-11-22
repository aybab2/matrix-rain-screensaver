# Builds MatrixRain.scr from src\MatrixRain.cs using the C# compiler that ships with Windows.
# Usage:  powershell -ExecutionPolicy Bypass -File build.ps1
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$csc  = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
$exe  = Join-Path $here 'MatrixRain.exe'
$scr  = Join-Path $here 'MatrixRain.scr'

& $csc /nologo /optimize+ /target:winexe /platform:anycpu "/out:$exe" `
    /reference:System.Windows.Forms.dll /reference:System.Drawing.dll `
    (Join-Path $here 'src\MatrixRain.cs')
if ($LASTEXITCODE -ne 0) { throw "Compile failed ($LASTEXITCODE)" }

Move-Item -Force $exe $scr
Write-Host "Built $scr"
