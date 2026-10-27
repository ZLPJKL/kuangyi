# 框译 KuangYi 1.0.1

Windows 10/11 桌面框选翻译工具。按 `F2` 框选屏幕任意区域，本地 OCR 识别后，用随包分发的离线模型直接翻译。无广告、无账号、无付费墙，断网可用。

版本 **1.0.1** · 更新记录见 [CHANGELOG.md](CHANGELOG.md) · 许可证 [MIT](LICENSE)。

技术栈：.NET 10 + WPF + Windows.Media.Ocr + ONNX Runtime + SQLite。

## 当前已实现

- **全局热键**：默认 `F2`，可在设置中录制新组合键；冲突检测、暂停/恢复、注册失败提示。
- **框选捕获**：冻结当前屏幕、全屏遮罩拖选、实时尺寸提示、Esc/右键取消；以物理像素裁剪，多显示器坐标一致。
- **本地 OCR**：调用 Windows 自带识别引擎，不联网、不限次数；自动检测已安装语言包，缺失时引导到系统设置。
- **OCR 结果信息**：识别完成后显示识别语言、OCR 引擎和估算置信度，支持手动切换源语言重新识别。
- **剪贴板 / 图片识别**：主窗口可直接识别剪贴板图片（Ctrl+V）或选择本地图片文件。
- **离线翻译**：随包 8 个方向模型（中英高质量直连 + 英俄/英日/英韩枢轴），ONNX Runtime CPU 推理，断网可用、不限次数。
- **翻译链**：在线翻译（开启时优先）→ 离线模型 → 内置词典，逐级降级并标注实际来源。
- **在线翻译**：DeepL 免费 API（需 Key）、MyMemory（免 Key）、LibreTranslate（自建/私有实例）。
- **在线状态**：在线失败时显示具体原因，并提示已回退离线模型。
- **测试连接**：设置页可直接验证 DeepL / LibreTranslate / MyMemory，无需先保存。
- **语言检测**：按 Unicode 脚本统计判定中/英/日/韩/俄，纯汉字低置信回退中文，混排按主脚本判定。
- **二次翻译**：改动原文后重译、切换目标语言重译、以译文为源继续翻译。
- **三主题**：纯白 `#FFFFFF` / 纯黑 `#000000` / 跟随系统，资源字典即时切换。
- **本地历史**：最近记录 50 条或 30 天；收藏独立计数、上限 50 条、不过期；搜索/收藏/删除/清空/导出 CSV。
- **隐私**：在线翻译默认关闭；「仅本地模式」禁止一切网络请求；历史只存本机；日志不记录原文；DeepL / LibreTranslate Key 使用 Windows DPAPI 加密保存。
- **托盘常驻**：单实例、开机自启（当前用户）、暂停热键、快速入口。

## 离线模型

模型目录随发布目录一起分发，共约 **1.5 GB**：

```text
models/
  en-zh/  zh-en/     OPUS-MT 中英直连（int8，高质量方向）
  en-ru/  ru-en/     英俄枢轴
  en-ja/              M2M100-418M 英日（int8，多语模型，质量中等）
  ja-en/             英日反向 OPUS-MT
  en-ko/             OPUS-MT tc-big 英韩（SentencePiece，int8）
  ko-en/             韩英 OPUS-MT
```

- 五语任意两语：直连优先；缺少直连时经英语枢轴（如日→中 = 日→英→中）。
- 推理在 CPU 上完成，不联网；短句耗时约 **0.7–3 秒**，模型首次加载额外 1–3 秒。
- 模型不常驻：多语模型用完立即卸载，小模型空闲 90 秒后卸载，回到低内存状态。
- 许可证与来源见 [docs/离线模型.md](docs/离线模型.md) 和 [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)。

重新下载模型（需要联网，约 1.5 GB）：

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\download-models.ps1
```

## 尚未包含

1. **词典数据包需要自备。** 仓库不附带 ECDICT/CC-CEDICT/JMdict 等数据，只实现了读取格式（见下）。
2. **Tesseract 备选 OCR 引擎、安装包封装、代码签名、自动更新**未实现。
3. 多显示器混合 DPI 下，遮罩的视觉对齐可能有偏差；最终裁剪使用的是物理坐标，选区内容仍然准确。
4. 英→日使用多语 M2M100 模型，常见问候语质量尚可但不如专用模型稳定（例如把「Good morning」译成「こんばんは」），长句建议开启在线翻译。

## 环境要求

- Windows 10 1809+ 或 Windows 11，x64
- 运行：.NET 10 Desktop Runtime（或使用 self-contained 发布）
- 构建：.NET 10 SDK
- OCR 语言包：Windows 设置 → 时间和语言 → 语言和区域 → 添加语言 → 可选功能中安装「光学字符识别」

本机验证时 Windows 的 OCR 语言包不一定齐全，日/韩/俄需要在系统中补装语言包，应用会在设置页列出缺失项。

## 构建与运行

```powershell
dotnet build .\KuangYi.sln -c Debug
dotnet run --project .\src\KuangYi.App
```

或直接运行：

```text
src\KuangYi.App\bin\Debug\net10.0-windows10.0.19041.0\KuangYi.exe
```

发布（自动把 `models` 复制到输出目录）：

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\publish.ps1
```

自包含发布：

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\publish.ps1 -SelfContained
```

发布目录 `artifacts/publish/win-x64` 即绿色版：`KuangYi.exe` + 运行时依赖 + `models`，整体约 1.6–1.8 GB。

## 验证

```powershell
# 核心逻辑自测（语言检测 / 设置 / 历史双池 / 翻译链 / 分块）39 项
dotnet run --project .\tests\KuangYi.Tests

# 离线模型端到端探针（8 个直连方向 + 2 个枢轴方向）
dotnet run --project .\tools\KuangYi.OfflineProbe -- .\models

# 环境探针（Windows OCR 语言、屏幕捕获、MyMemory 连通性）
dotnet run --project .\tools\KuangYi.Probe
```

本机结果：自测 39/39 通过；离线探针 10/10 通过；OCR 语言 `en-US, zh-Hans-CN`；屏幕捕获 `2560x1600`。

离线探针示例输出：

```text
[ok] en->zh 1.4s  大家好,这是一次离线翻译测试
[ok] zh->en 1.5s  Hello world. This is offline translation.
[ok] en->ru 1.0s  Доброе утро.
[ok] ru->en 1.1s  Good morning.
[ok] ja->en 1.0s  Good morning. Thank you.
[ok] en->ko 0.8s  좋은 아침입니다. 감사합니다.
[ok] ja->zh 1.3s  早上好,你好吗?（经英语枢轴）
```

## 数据目录

```text
%LOCALAPPDATA%\KuangYi\
├─ settings.json        设置
├─ history.db           SQLite 历史（WAL）
├─ thumbs\              历史缩略图
├─ dict\dict.db         词典数据包（可选）
├─ models\              模型目录（当程序目录没有 models 时的回退位置）
└─ logs\app.log         滚动日志，不含原文
```

模型查找顺序：程序目录 `models` → `%LOCALAPPDATA%\KuangYi\models`。绿色版与安装版都把模型放在程序目录。

## 词典数据包格式

将 SQLite 文件放到 `dict\dict.db`，表结构：

```sql
CREATE TABLE dict (
    source_lang TEXT,
    target_lang TEXT,
    term        TEXT,
    translation TEXT,
    pos         TEXT,
    phonetic    TEXT
);
CREATE INDEX dict_lookup ON dict(source_lang, target_lang, term);
```

语言代码固定为 `zh` / `en` / `ja` / `ko` / `ru`。词典只命中单词与短语（≤64 字符），长句会落到离线模型。

## 在线翻译

在「设置 → 翻译」中开启。开启在线翻译时，在线源会优先尝试；失败或额度用完会自动回退离线模型。

- **DeepL**：需要免费 API Key（每月约 50 万字符）；密钥通常以 `:fx` 结尾。
- **MyMemory**：无需密钥，公共免费接口，有匿名额度限制；请求按 450 字节分块。
- **LibreTranslate**：填写自建实例地址 `https://host:port`，可选 API Key。

开启后仅发送 OCR 识别出的文本，不上传截图；「仅本地模式」优先级最高，会屏蔽所有在线源。
在线关闭时不会被调用。

## 已知限制

- 英→日为多语模型，问候语与语气词质量一般；中英/中俄/中韩直连质量明显更好。
- 推理是 CPU 贪心/束搜索实现，未使用 KV Cache；长文本会按 180 字切块，速度随之线性增加。
- 模型体积较大（约 1.5 GB），首次启动不加载，首次翻译时按需加载。
- 日/韩/俄 OCR 依赖系统语言包，未安装时识别结果会混入英文或乱码。
- 尚未提供安装器、签名与自动更新。

## 目录结构

```text
src/KuangYi.Core/        领域模型、设置、历史、语言检测、翻译链、离线推理（无 UI 依赖）
src/KuangYi.App/         WPF 界面、热键、捕获、OCR、托盘
tests/KuangYi.Tests/     控制台自测（39 项）
tools/KuangYi.OfflineProbe/  离线模型端到端探针
tools/KuangYi.Probe/     OCR / 捕获 / 在线翻译探针
scripts/publish.ps1      发布（含模型复制）
scripts/download-models.ps1  单独下载模型
docs/离线模型.md          模型来源、许可证、质量说明
THIRD-PARTY-NOTICES.md   第三方组件与模型许可
```

## 后续优先级

1. 英→日更换为专用 Ja 模型（当前 M2M100 为折中），并评估 NLLB-200 蒸馏版。
2. KV Cache 推理，降低长句延迟与内存占用。
3. 打包 ECDICT / CC-CEDICT / JMdict / KEngDic / OpenRussian 词库并做词形处理。
4. Tesseract 语言数据兜底。
5. 安装器、签名、增量更新。
