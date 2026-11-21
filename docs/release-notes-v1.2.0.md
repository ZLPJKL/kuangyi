# 发布说明模板

## 框译 Framed Translation v1.2.0

Windows 10/11 屏幕框选 OCR 翻译工具，支持离线模型、可选在线翻译、术语表和批量 PDF。

### 本次更新

- 首次启动向导
- 实时区域翻译
- 贴图窗口
- 剪贴板文字、拖拽图片
- 批量图片 / PDF 翻译
- 离线语言包下载
- OCR 自动裁边、二值化、竖排尝试
- 数据清理、设置导入导出
- 结果窗字号/透明度

### 下载

- `KuangYi-v1.2.0-win-x64.zip` — 程序本体（约 89 MB）
- `KuangYi-v1.2.0-models.zip` — 离线模型（约 1.2 GB）
- `SHA256SUMS.txt`

### 安装

1. 解压程序本体到任意文件夹。
2. 解压语言包到同一个文件夹。
3. 双击 `框译.exe`。

### 校验

```powershell
Get-FileHash .\KuangYi-v1.2.0-win-x64.zip -Algorithm SHA256
Get-FileHash .\KuangYi-v1.2.0-models.zip -Algorithm SHA256
```

### 系统要求

Windows 10 19041+ / Windows 11，x64；日/韩/俄 OCR 需要 Windows 识别语言包。

### 已知限制

- 未提供竖排日文专用排版引擎。
- 批量 PDF 使用系统 PDF 渲染，扫描件质量依赖原图。
- 实时翻译会周期性截取固定区域，结果窗可能与选区重叠。
