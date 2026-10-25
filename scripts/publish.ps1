param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [switch]$SelfContained,
    [switch]$SkipModels,
    [string]$Output = "artifacts/publish/win-x64"
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
Push-Location $root

try {
    $candidates = @()
    $dotnetCommand = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($null -ne $dotnetCommand) {
        $candidates += $dotnetCommand.Source
    }

    $candidates += (Join-Path $env:LOCALAPPDATA "dotnet-sdk10\dotnet.exe")
    $candidates += (Join-Path $env:LOCALAPPDATA "dotnet-sdk8\dotnet.exe")
    $candidates += "C:\Program Files\dotnet\dotnet.exe"

    $dotnet = $null
    foreach ($candidate in ($candidates | Select-Object -Unique)) {
        if (-not (Test-Path $candidate)) {
            continue
        }

        $sdks = & $candidate --list-sdks 2>$null
        if ($LASTEXITCODE -eq 0 -and $sdks) {
            $dotnet = $candidate
            break
        }
    }

    if (-not $dotnet) {
        throw "No .NET SDK was found. Install the .NET 10 SDK first."
    }

    $publishArgs = @(
        "publish", "src/KuangYi.App/KuangYi.App.csproj",
        "-c", $Configuration,
        "-r", $Runtime,
        "-o", $Output,
        "-p:PublishSingleFile=false"
    )

    if ($SelfContained) {
        $publishArgs += @("--self-contained", "true")
    }
    else {
        $publishArgs += @("--self-contained", "false")
    }

    Write-Host ("dotnet " + ($publishArgs -join " "))
    & $dotnet @publishArgs

    $models = Join-Path $root "models"
    if (-not (Test-Path $models)) {
        $sibling = Join-Path (Split-Path $root -Parent) "程序\models"
        if (Test-Path $sibling) {
            $models = $sibling
        }
    }

    if (-not $SkipModels -and (Test-Path $models)) {
        $target = Join-Path $root $Output
        $targetModels = Join-Path $target "models"
        if (Test-Path $targetModels) {
            Remove-Item -Recurse -Force $targetModels
        }

        Copy-Item -Recurse $models $targetModels
        $size = (Get-ChildItem $targetModels -Recurse -File | Measure-Object Length -Sum).Sum
        Write-Host ("Models copied: {0:N1} MB" -f ($size / 1MB))
    }

    Write-Host ("Output: " + $Output)
}
finally {
    Pop-Location
}
