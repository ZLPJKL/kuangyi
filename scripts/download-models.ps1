param(
    [string]$ModelsRoot,
    [switch]$Force
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
if (-not $ModelsRoot) {
    $ModelsRoot = Join-Path $repoRoot "models"
}

# Each entry: target directory, HuggingFace repo, remote folder for the ONNX files,
# metadata files to fetch from the repo root, and the extra SentencePiece files (if any).
$definitions = @(
    @{ dir = "en-zh"; repo = "Xenova/opus-mt-en-zh"; onnx = "onnx/"; files = @("config.json", "tokenizer.json", "generation_config.json") },
    @{ dir = "zh-en"; repo = "Xenova/opus-mt-zh-en"; onnx = "onnx/"; files = @("config.json", "tokenizer.json", "generation_config.json") },
    @{ dir = "en-ru"; repo = "Xenova/opus-mt-en-ru"; onnx = "onnx/"; files = @("config.json", "tokenizer.json", "generation_config.json") },
    @{ dir = "ru-en"; repo = "Xenova/opus-mt-ru-en"; onnx = "onnx/"; files = @("config.json", "tokenizer.json", "generation_config.json") },
    @{ dir = "en-ja"; repo = "Xenova/m2m100_418M"; onnx = "onnx/"; files = @("config.json", "tokenizer.json", "generation_config.json") },
    @{ dir = "ja-en"; repo = "Xenova/opus-mt-ja-en"; onnx = "onnx/"; files = @("config.json", "tokenizer.json", "generation_config.json") },
    @{ dir = "en-ko"; repo = "R4kSo1997/opus-mt-en-ko-onnx-int8"; onnx = ""; files = @("config.json", "generation_config.json", "source.spm", "target.spm") },
    @{ dir = "ko-en"; repo = "Xenova/opus-mt-ko-en"; onnx = "onnx/"; files = @("config.json", "tokenizer.json", "generation_config.json") }
)

New-Item -ItemType Directory -Force -Path $ModelsRoot | Out-Null

foreach ($definition in $definitions) {
    $target = Join-Path $ModelsRoot $definition.dir
    New-Item -ItemType Directory -Force -Path $target | Out-Null
    $base = "https://huggingface.co/$($definition.repo)/resolve/main"

    foreach ($file in $definition.files) {
        $destination = Join-Path $target $file
        if ((Test-Path $destination) -and ((Get-Item $destination).Length -gt 0) -and (-not $Force)) {
            continue
        }

        & curl.exe -L --retry 3 --silent --show-error -o $destination "$base/$file"
        if ($LASTEXITCODE -ne 0) {
            throw "download failed: $($definition.repo)/$file"
        }
    }

    foreach ($onnxFile in @("encoder_model_quantized.onnx", "decoder_model_quantized.onnx")) {
        $destination = Join-Path $target $onnxFile
        if ((Test-Path $destination) -and ((Get-Item $destination).Length -gt 1MB) -and (-not $Force)) {
            continue
        }

        & curl.exe -L --retry 3 --silent --show-error -o $destination "$base/$($definition.onnx)$onnxFile"
        if ($LASTEXITCODE -ne 0) {
            throw "download failed: $($definition.repo)/$onnxFile"
        }
    }

    $size = (Get-ChildItem $target -File | Measure-Object Length -Sum).Sum
    Write-Host ("{0}: {1:N1} MB" -f $definition.dir, ($size / 1MB))
}

Write-Host ("models ready at " + $ModelsRoot)
