# 参与贡献

感谢你愿意改进框译。

## 提交 Issue

- 先搜索是否已有相同问题。
- Bug 请附上框译版本、Windows 版本、复现步骤和日志：
  `%LOCALAPPDATA%\KuangYi\logs\app.log`
- **不要粘贴 API Key、截图中的敏感文字或个人信息。**

## 提交代码

1. Fork 并创建分支。
2. 保持现有代码风格，不提交 `bin/`、`obj/`、`models/`、`*.zip` 或个人设置。
3. 修改核心逻辑时请同步补充 `tests/KuangYi.Tests`。
4. 提交前运行：

```powershell
dotnet build KuangYi.sln -c Release
dotnet run --project tests/KuangYi.Tests -c Release
```

5. 在 Pull Request 中说明改动、验证方式和已知限制。

## 模型与许可证

模型和第三方组件遵循各自许可证，见 [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)。
