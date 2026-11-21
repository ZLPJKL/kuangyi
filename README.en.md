# Framed Translation (框译)

**Language / 语言:** [简体中文](README.md) ｜ **English**

A lightweight Windows screen-capture translator. Press `F2`, drag a region, and get OCR plus translation instantly — offline capable, no ads, no account, no telemetry.

## Download

From the [latest release](https://github.com/ZLPJKL/kuangyi/releases/latest) **Assets**:

- Application: `KuangYi-*-win-x64.zip` (~89 MB)
- Offline models: `KuangYi-models.zip` (~1.2 GB, stable filename)
- Checksums: `SHA256SUMS.txt`

Unzip the application into any folder; unzip `KuangYi-models.zip` into its `bin` folder so the final path is `bin\models\...` next to `bin\KuangYi.exe`. The app starts without the model pack, but offline translation requires it.

## Features

- `F2` region capture with multi-monitor support and live size feedback.
- Windows OCR with dark-background inversion, contrast enhancement, auto-crop and three quality modes.
- Live region translation with a visible border around the captured area.
- Clipboard image/text, image files, and drag-and-drop input.
- Batch translation for images and PDF pages, with CSV export.
- Original/translation cards, language switching, copy, pin, always-on-top, font size and opacity.
- Translation cache, editable glossary with CSV import/export, history and favorites.
- First-run wizard, offline model downloader, local data cleanup and settings export/import.

## Quick start

1. Download and unzip the application into a folder.
2. Unzip `KuangYi-models.zip` into its `bin` folder, so `bin\models` sits next to `bin\KuangYi.exe`.
3. Run `框译.exe`.
4. Press `F2`, select a region, and wait for the result.

## Build

```powershell
git clone https://github.com/ZLPJKL/kuangyi.git
cd kuangyi
dotnet restore KuangYi.sln
dotnet build KuangYi.sln -c Release
dotnet run --project src/KuangYi.App
```

Run tests:

```powershell
dotnet run --project tests/KuangYi.Tests -c Release
```

Models are not stored in Git. Download `KuangYi-models.zip` or run:

```powershell
pwsh -File scripts/download-models.ps1
```

## Data and privacy

Data is stored in `%LOCALAPPDATA%\KuangYi\`:

```text
settings.json          settings
history.db             history
translation-cache.db   translation cache
glossary.txt           user glossary
models\                fallback model directory
logs\                  logs
```

OCR and offline translation run locally. Online translation is disabled by default, logs do not contain source text, and no telemetry is collected.

## Documentation

- [FAQ (Chinese)](docs/FAQ.md)
- [Startup guide (Chinese)](docs/启动说明.txt)
- [Glossary guide (Chinese)](docs/术语表说明.txt)
- [Offline models (Chinese)](docs/离线模型.md)
- [Third-party notices](THIRD-PARTY-NOTICES.md)
- [Changelog](CHANGELOG.md)

## Known limitations

- Windows 10 2004+ or Windows 11, x64.
- Japanese/Korean/Russian OCR depends on Windows language packs.
- Vertical Japanese uses rotated OCR, not a dedicated vertical layout engine.
- No installer, code signing, or auto-update yet.

## Contributing

Issues and pull requests are welcome. See [CONTRIBUTING.md](CONTRIBUTING.md).

## License

MIT for the code. Models and third-party components follow their own licenses; see [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
