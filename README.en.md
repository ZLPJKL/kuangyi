# Framed Translation (框译)

**Language / 语言:** [简体中文](README.md) ｜ **English**

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

- `KuangYi-v1.2.6-win-x64.zip` — application (~89 MB)
- `KuangYi-models.zip` — offline models (~1.2 GB; stable filename, not tied to app version)
- `SHA256SUMS.txt`

Unzip both archives into the same folder and run `框译.exe`.

> The application can start without the model pack, but offline translation will be unavailable until the model pack is installed.

## Quick start

1. Unzip `KuangYi-v1.2.6-win-x64.zip` into a folder.
2. Unzip `KuangYi-models.zip` into the same folder.
3. Run `框译.exe`.
4. Press `F2`, drag over any text, and wait for the result.

You can also use **Clipboard OCR** for a copied image or text, drag an image into the main window, or use **Batch / PDF** for multiple files. Settings are available from the main window or the tray menu.

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

Models are not stored in the Git repository. Use the in-app model download manager, or download the model pack from the release page and extract it into the `models` folder.

## Glossary

The glossary is stored in `%LOCALAPPDATA%\KuangYi\glossary.txt`. Open **Settings → Glossary** to edit it, or import/export CSV.

```text
en>zh Account=账户
en>zh prompt=提示词
ja>zh 読書=阅读
API=API
```

A missing language direction applies to all pairs. Changes take effect on the next translation and invalidate the old translation cache.

## Data and privacy

Local data is stored in:

```text
%LOCALAPPDATA%\KuangYi\
├─ settings.json        settings and DPAPI-encrypted API keys
├─ history.db           SQLite history
├─ translation-cache.db translation cache
├─ glossary.txt         user glossary
├─ thumbnails\          history thumbnails
├─ models\              fallback model directory
└─ logs\                rolling logs
```

OCR and offline translation run locally. Online translation is disabled by default. API keys are encrypted with Windows DPAPI. No telemetry or account is used.

## Troubleshooting

- **Offline translation is unavailable**: install the model pack, or open **Settings → Recognition & Models → Download offline language pack**.
- **A language is not recognized**: install its Windows OCR language pack from Windows Settings → Time & language → Language & region.
- **SmartScreen warns about an unknown publisher**: the portable build is not code-signed; verify the file came from this repository's release page.
- **Online translation fails**: check the API key, quota, and network; frame translation automatically falls back to offline translation.
- Logs: `%LOCALAPPDATA%\KuangYi\logs\app.log`.

## Known limitations

- Japanese/Korean/Russian OCR depends on Windows language packs.
- Vertical Japanese uses rotated OCR attempts, not a dedicated vertical layout engine.
- Real-time translation captures the locked region periodically; avoid placing the result window over that region.
- The portable build has no installer, code signing, or auto-update yet.
- Batch PDF quality depends on the system PDF renderer and source image quality.

## Contributing

Issues and pull requests are welcome. See [CONTRIBUTING.md](CONTRIBUTING.md).

## License

Code is MIT licensed. Models and third-party components follow their own licenses; see [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
