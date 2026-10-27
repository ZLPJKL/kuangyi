# 框译 KuangYi

[![Platform](https://img.shields.io/badge/platform-Windows%2010%20%7C%2011-0078D4?logo=windows)](https://www.microsoft.com/windows)
[![.NET](https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![Version](https://img.shields.io/badge/version-1.0.1-blue)](CHANGELOG.md)
[![License](https://img.shields.io/badge/license-MIT-green)](LICENSE)

> 按 `F2` 框选屏幕任意区域，本地 OCR 识别后直接翻译。离线可用，无广告、无账号、无付费墙。

## 简介

框译是面向 Windows 10/11 的桌面框选翻译工具，适合阅读外文资料、图片、PDF、游戏字幕和报错弹窗等不可选中文字的场景。

- **本地优先**：OCR 使用 Windows 自带识别引擎，离线翻译使用随包分发的 ONNX 模型。
- **五语互译**：中文、英语、日语、韩语、俄语。
- **轻量常驻**：托盘运行，不占任务栏；模型按需加载、用完释放。
- **可选在线**：DeepL 免费 API、MyMemory、LibreTranslate；失败自动回退离线。

## 功能

### 框选与 OCR

- 全局热键 `F2`，可在设置中录制其他组合键。
- 冻结屏幕、全屏遮罩拖选、实时尺寸提示；`Esc` 或右键取消。
- 按物理像素裁剪，多显示器坐标一致。
- 自动选择 OCR 引擎；深色背景自动增加反色识别。
- 结果显示识别语言、OCR 引擎和估算置信度。
- 手动切换源语言会重新对原图 OCR，而不是拿旧乱码重译。
- 缺少 Windows 语言包时明确提示安装路径。

### 翻译

- 在线翻译（开启时优先）→ 离线模型 → 内置词典，逐级降级并标注实际来源。
- 离线模型覆盖 8 个方向：中英直连、英俄/英日/英韩枢轴。
- 在线源：DeepL Free API（需 Key）、MyMemory（免 Key）、LibreTranslate（自建/私有实例）。
- 在线失败时显示原因，并提示已回退离线模型。
- 支持修改原文后重译、切换目标语言、以译文为源继续翻译。
- 长文本按段落切分，保留段落结构。

### 输入方式

- `F2` 屏幕框选。
- 剪贴板图片识别（`Ctrl+V`）。
- 选择本地图片文件识别（PNG/JPG/BMP/GIF/TIF）。

### 界面与隐私

- 主窗口、结果窗默认只在托盘显示，不占任务栏。
- 结果窗可拖动、置顶、最小化到托盘。
- 纯白 / 纯黑 / 跟随系统三种主题。
- 结果窗显示识别信息、翻译来源、耗时。
- 本地历史：最近记录 50 条或 30 天；收藏独立计数，上限 50 条。
- 历史支持搜索、收藏、删除、清空、导出 CSV。
- 在线翻译默认关闭；「仅本地模式」禁止一切网络请求。
- 日志不记录原文；DeepL / LibreTranslate Key 使用 Windows DPAPI 加密保存。

## 技术栈

| 组件 | 用途 |
| --- | --- |
| .NET 10 + WPF | 桌面界面、托盘、窗口与主题 |
| Windows.Media.Ocr | 本地 OCR |
| ONNX Runtime | 本地翻译模型推理 |
| SQLite | 本地历史记录 |
| Microsoft.Data.Sqlite | 数据访问 |

## 项目结构

```text
src/KuangYi.App/          WPF 界面、热键、屏幕捕获、OCR、托盘
src/KuangYi.Core/         设置、历史、语言检测、翻译链、离线模型
tests/KuangYi.Tests/      核心逻辑自测
tools/KuangYi.Probe/      OCR / 捕获 / 在线翻译探针
tools/KuangYi.OfflineProbe/  离线模型端到端探针
docs/                     使用说明、模型说明、教程
scripts/                  模型下载和构建脚本
```

## 环境要求

- Windows 10 1809+ 或 Windows 11，x64
- .NET 10 SDK（构建）
- .NET 10 Desktop Runtime（运行非 self-contained 版本）
- Windows OCR 语言包：设置 → 时间和语言 → 语言和区域 → 添加语言 → 可选功能中安装「光学字符识别」

日/韩/俄 OCR 需要对应系统语言包；应用会在设置页列出缺失项。

## 快速开始

### 构建

```powershell
git clone https://github.com/ZLPJKL/kuangyi.git
cd kuangyi

dotnet restore KuangYi.sln
dotnet build KuangYi.sln -c Release
```

### 运行

```powershell
dotnet run --project src/KuangYi.App
```

也可以直接运行构建产物：

```text
src\KuangYi.App\bin\Release\net10.0-windows10.0.19041.0\KuangYi.exe
```

### 测试

```powershell
dotnet run --project tests/KuangYi.Tests -c Release
dotnet run --project tools/KuangYi.OfflineProbe -c Release -- models
```

离线模型探针需要 `models` 目录；没有模型时只运行核心自测。

### 离线模型

模型不包含在源码仓库中，首次构建后需要单独准备：

```powershell
powershell -ExecutionPolicy Bypass -File scripts/download-models.ps1
```

模型来源、许可证和质量说明见 [docs/离线模型.md](docs/离线模型.md)。

## 配置与数据

数据目录：

```text
%LOCALAPPDATA%\KuangYi\
├─ settings.json        设置
├─ history.db           SQLite 历史
├─ thumbs\              历史缩略图
├─ models\              模型回退目录
└─ logs\                滚动日志
```

模型查找顺序：程序目录 `models` → `%LOCALAPPDATA%\KuangYi\models`。

在线翻译配置：

- DeepL：填写免费 API Key，通常以 `:fx` 结尾；
- MyMemory：无需 Key；
- LibreTranslate：填写实例地址，可选 Key。

设置页提供「测试连接」，保存前即可验证。

## 文档

- [启动说明](docs/启动说明.txt)
- [离线模型说明](docs/离线模型.md)
- [DeepL 注册及使用教程](docs/DeepL注册及使用教程.txt)
- [LibreTranslate 自建教程](docs/LibreTranslate自建教程.txt)
- [第三方组件与模型许可](THIRD-PARTY-NOTICES.md)
- [更新日志](CHANGELOG.md)

## 已知限制

- 日/韩/俄 OCR 依赖 Windows 系统语言包。
- 日文页面如果夹杂大量英文，自动判断可能需要手动选择源语言。
- 英→日使用多语模型，个别语气词和问候语质量一般。
- 离线模型约 1.5 GB，首次加载需要额外时间。
- 暂未提供安装器、代码签名和自动更新。
- 暂不支持竖排日文。

## 参与贡献

欢迎提交 Issue 和 Pull Request。

- 提交前请先搜索是否已有相同问题；
- 代码修改请附带可复现步骤或测试；
- 不要提交 `bin/`、`obj/`、`models/`、`*.zip` 或个人设置文件；
- 模型和第三方组件遵循各自许可证，见 [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)。

## 许可证

本项目代码采用 [MIT](LICENSE) 许可证。

离线模型和第三方组件遵循各自许可证；再分发时请保留相应署名和许可说明。

## 致谢

- [Helsinki-NLP / OPUS-MT](https://github.com/Helsinki-NLP)
- [facebook/m2m100](https://github.com/facebookresearch/fairseq/tree/main/examples/m2m100)
- [Xenova](https://huggingface.co/Xenova) 的 ONNX 模型转换
- [ONNX Runtime](https://github.com/microsoft/onnxruntime)
- [SQLite](https://sqlite.org/)
