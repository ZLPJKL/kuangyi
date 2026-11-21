# 框译 Framed Translation

**语言 / Language：** **简体中文** ｜ [English](README.en.md)

> 按 `F2` 框选屏幕任意区域，本地 OCR 识别后直接翻译。离线可用，无广告、无账号、无遥测。

**English:** A lightweight Windows screen OCR and offline translation tool. Press `F2`, select a region, and translate instantly. [English README](README.en.md)

## 下载

从 [最新 Release](https://github.com/ZLPJKL/kuangyi/releases/latest) 的 **Assets** 下载：

- 程序本体：`KuangYi-*-win-x64.zip`（约 89 MB）
- 离线语言包：`KuangYi-models.zip`（约 1.2 GB，固定文件名）
- 校验文件：`SHA256SUMS.txt`

解压程序本体到任意文件夹；把 `KuangYi-models.zip` 解压到该文件夹下的 `bin` 内，确保最终存在 `bin\models\...` 且与 `bin\KuangYi.exe` 同级。没有语言包也能启动，但离线翻译不可用。

## 特点

- **本地优先**：Windows OCR + 本地 ONNX 翻译，断网可用。
- **五语互译**：中文、英语、日语、韩语、俄语。
- **轻量常驻**：托盘运行，不占任务栏；模型按需加载。
- **可选在线**：DeepL、MyMemory、LibreTranslate，失败自动回退离线。
- **隐私友好**：无广告、无账号、无遥测；API Key 使用 Windows DPAPI 加密。

## 功能

- `F2` 全屏框选，支持多显示器、实时尺寸提示、`Esc` / 右键取消。
- OCR 自动选择引擎，支持深色反色、低对比度增强、自动裁边和三档质量。
- 实时区域翻译：独立框选区域，定时刷新，区域外显示蓝色边框。
- 输入方式：框选、剪贴板图片/文字、本地图片、拖拽图片。
- 批量翻译：多张图片和 PDF 逐页识别，可导出 CSV。
- 结果窗：原文/译文卡片、语言切换、复制、贴图、置顶、字号和透明度设置。
- 翻译缓存、可编辑术语表、CSV 导入导出、历史记录和收藏。
- 首次启动向导、离线模型下载、数据清理和设置导入导出。

## 快速开始

### 运行

下载并解压程序本体和语言包到同一目录，双击 `框译.exe`。

### 构建

```powershell
git clone https://github.com/ZLPJKL/kuangyi.git
cd kuangyi
dotnet restore KuangYi.sln
dotnet build KuangYi.sln -c Release
dotnet run --project src/KuangYi.App
```

### 测试

```powershell
dotnet run --project tests/KuangYi.Tests -c Release
```

### 离线模型

模型不包含在 Git 仓库中。发布版可直接下载 `KuangYi-models.zip`；源码构建可运行：

```powershell
pwsh -File scripts/download-models.ps1
```

模型说明、来源和许可证见 [docs/离线模型.md](docs/离线模型.md)。

## 数据与隐私

数据目录：`%LOCALAPPDATA%\KuangYi\`

```text
settings.json         设置
history.db            历史记录
translation-cache.db  翻译缓存
glossary.txt          术语表
models\               模型回退目录
logs\                 日志
```

OCR 和离线翻译在本机完成。在线翻译默认关闭；日志不记录原文；不收集遥测。

## 文档

- [常见问题](docs/FAQ.md)
- [启动说明](docs/启动说明.txt)
- [术语表说明](docs/术语表说明.txt)
- [离线模型说明](docs/离线模型.md)
- [DeepL 教程](docs/DeepL注册及使用教程.txt)
- [LibreTranslate 教程](docs/LibreTranslate自建教程.txt)
- [更新日志](CHANGELOG.md)
- [第三方组件与模型许可](THIRD-PARTY-NOTICES.md)

## 已知限制

- 需要 Windows 10 2004+ 或 Windows 11，x64。
- 日/韩/俄 OCR 依赖 Windows 系统语言包。
- 竖排日文使用旋转识别，不是专用排版引擎。
- 暂未提供安装器、代码签名和自动更新。

## 参与贡献

欢迎提交 Issue 和 Pull Request，提交前请先看 [CONTRIBUTING.md](CONTRIBUTING.md)。

## 许可证

代码采用 [MIT](LICENSE)。模型和第三方组件遵循各自许可证，再分发时请保留 [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)。

## 致谢

- [Helsinki-NLP / OPUS-MT](https://github.com/Helsinki-NLP)
- [facebook/m2m100](https://github.com/facebookresearch/fairseq/tree/main/examples/m2m100)
- [Xenova](https://huggingface.co/Xenova)
- [ONNX Runtime](https://github.com/microsoft/onnxruntime)
- [SQLite](https://sqlite.org/)
