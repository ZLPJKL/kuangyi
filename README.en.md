# Framed Translation (框译)

A lightweight Windows 10/11 screen-capture translator. Press `F2`, drag a region, and get OCR plus translation without leaving the keyboard.

- **Local first**: Windows OCR + bundled ONNX translation models work offline.
- **Five languages**: Chinese, English, Japanese, Korean, Russian.
- **Tray resident**: no taskbar clutter; low idle memory.
- **Optional online**: DeepL, MyMemory, LibreTranslate; falls back to offline on failure.
- **No ads, no accounts, no telemetry.**

## Features

- Region capture with `F2`, clipboard image/text, image file, drag and drop.
- Auto OCR engine selection, dark-background inversion, contrast enhancement, blank-border cropping.
- Real-time region translation (refreshes a locked region every 2.5 s).
- Pin screenshot to the desktop; result window can stay on top.
- Translation cache with configurable size and app/model fingerprint.
- User glossary with CSV import/export.
- Batch translation for images and PDF pages, CSV export.
- First-run setup, model download manager, local data management.
- Fast / Balanced / High OCR modes.

## Download

Use the [latest release](https://github.com/ZLPJKL/kuangyi/releases/latest):

- `KuangYi-v1.2.3-win-x64.zip` — application (~89 MB)
- `KuangYi-v1.2.3-models.zip` — offline models (~1.2 GB)
- `SHA256SUMS.txt`

Unzip both archives into the same folder and run `框译.exe`.

> The application can start without the model pack, but offline translation will be unavailable until the model pack is installed.

## Requirements

- Windows 10 19041+ or Windows 11, x64.
- OCR language packs for Japanese / Korean / Russian (Windows Settings → Language).
- No .NET runtime installation required for the self-contained release.

## Build

```powershell
git clone https://github.com/ZLPJKL/kuangyi.git
cd kuangyi
dotnet restore KuangYi.sln
dotnet build KuangYi.sln -c Release
dotnet run --project tests/KuangYi.Tests -c Release
dotnet run --project src/KuangYi.App
```

Models are not stored in the Git repository. See the Chinese README for model preparation.

## Privacy

OCR and offline translation run locally. Online translation is disabled by default. API keys are encrypted with Windows DPAPI. See [the Chinese README](README.md) for data paths and limitations.

## License

Code is MIT licensed. Models and third-party components follow their own licenses; see [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
