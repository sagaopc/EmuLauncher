# Builds the self-contained single-file EmuLauncher.exe
# Usage:  powershell -ExecutionPolicy Bypass -File .\build.ps1

param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
Push-Location $PSScriptRoot

try {
    dotnet publish .\EmuLauncher.csproj -c $Configuration -r win-x64 --self-contained true
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed (exit code $LASTEXITCODE)."
    }

    $exe = Join-Path $PSScriptRoot "bin\$Configuration\net8.0-windows\win-x64\publish\EmuLauncher.exe"
    if (-not (Test-Path $exe)) {
        throw "Build finished but $exe was not found."
    }

    $size = [math]::Round((Get-Item $exe).Length / 1MB, 1)
    Write-Host ""
    Write-Host "Built: $exe ($size MB)" -ForegroundColor Green
}
finally {
    Pop-Location
}
