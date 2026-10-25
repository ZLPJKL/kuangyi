# 第三方组件与模型许可

本项目代码采用 MIT 许可证，见 `LICENSE`。
以下第三方组件和模型分别遵循各自的许可证，不能混用。

## 运行库

- .NET 8 / WPF / Windows SDK projections：MIT（Microsoft）
- Microsoft.ML.OnnxRuntime：MIT
- Microsoft.Data.Sqlite / SQLitePCLRaw：MIT / Apache-2.0
- SQLite：Public Domain
- Windows.Media.Ocr：Windows 系统组件，不随本项目再分发

## 离线翻译模型

| 方向 | 来源 | 许可证 |
| --- | --- | --- |
| en-zh / zh-en | Xenova/opus-mt-en-zh、opus-mt-zh-en（上游 Helsinki-NLP） | CC-BY-4.0 |
| en-ru / ru-en | Xenova/opus-mt-en-ru、opus-mt-ru-en（上游 Helsinki-NLP） | CC-BY-4.0 |
| ja-en | Xenova/opus-mt-ja-en（上游 Helsinki-NLP） | CC-BY-4.0 |
| ko-en | Xenova/opus-mt-ko-en（上游 Helsinki-NLP） | CC-BY-4.0 |
| en-ja | Xenova/m2m100_418M（上游 facebook/m2m100_418M） | MIT |
| en-ko | Helsinki-NLP/opus-mt-tc-big-en-ko 的 int8 导出（R4kSo1997） | 沿用上游模型许可证 |

- 模型权重不包含在源码仓库中，由 `scripts/download-models.ps1` 下载，
  或作为 Release 的独立模型包分发。
- 再分发模型时请保留本说明、`docs/离线模型.md` 以及上游模型链接和许可证。
- 模型转换/量化由 Xenova、R4kSo1997 等第三方完成，本项目只做 ONNX 推理集成。

## 其他

- 应用图标由项目所有者提供/委托设计。
- DeepL、MyMemory、LibreTranslate 是在线服务，使用时遵循各自服务条款；
  本项目不包含它们的服务端代码或密钥。
