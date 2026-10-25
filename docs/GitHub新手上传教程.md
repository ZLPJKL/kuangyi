# GitHub 新手上传教程（框译 v1.0）

这份教程从“什么都没有”开始，讲清楚两件事：

1. 怎么把源码上传到 GitHub；
2. 怎么把「程序本体」和「离线语言包」作为 Release 附件发布。

> 重要：不要把 `models\`、`程序\`、`bin\`、`obj\` 直接提交到普通 Git 仓库。
> GitHub 普通文件有 100 MB 限制，离线模型约 1.55 GB，必须走 Release 附件。

---

## 一、准备

### 1. 注册 GitHub

1. 打开 https://github.com
2. 点右上角 **Sign up**，用邮箱注册；
3. 到邮箱里点验证链接；
4. 登录后记住你的用户名，例如 `yourname`。

### 2. 安装 Git

1. 打开 https://git-scm.com/download/win
2. 下载 64-bit Git for Windows；
3. 安装时一路点 Next（默认选项即可）；
4. 安装完成后打开 PowerShell，输入：

```powershell
git --version
```

能看到类似 `git version 2.51...` 就说明装好了。

### 3. 准备发布包

本教程使用这两个文件（已经生成在 `框译\发布\`）：

```text
框译-v1.0-程序本体-win-x64.zip
框译-v1.0-离线语言包.zip
```

- 程序本体：约 100–200 MB，自包含 .NET 运行时，不含模型；
- 离线语言包：约 1.1 GB，只有 `models\` 和模型许可说明。

不要把它们提交进 Git 仓库，后面在 Release 页面上传。

---

## 二、在 GitHub 创建空仓库

1. 登录 GitHub；
2. 右上角 **+** → **New repository**；
3. 填写：
   - **Repository name**：`kuangyi`
   - **Description**：`Windows 框选翻译工具`
   - 选择 **Public**（公开）或 Private（私有）
   - **不要**勾选 “Add a README file”
   - **不要**勾选 “Add .gitignore”
   - **不要**选择 License
4. 点 **Create repository**。

创建后会看到仓库地址，例如：

```text
https://github.com/yourname/kuangyi.git
```

把 `yourname` 换成你的 GitHub 用户名。

---

## 三、把源码上传到 GitHub

### 1. 打开 PowerShell，进入源码目录

```powershell
cd C:\Users\zlp\Desktop\框译\源码
```

### 2. 第一次使用 Git 时配置身份

```powershell
git config --global user.name "你的名字"
git config --global user.email "你的GitHub邮箱"
```

这两行只需要执行一次。

### 3. 初始化本地仓库

```powershell
git init
git status
```

`git status` 里应该只看到源码、文档、脚本；**不能看到 models、程序、bin、obj**。
如果看到了，先不要继续，检查 `.gitignore` 是否包含：

```text
models/
bin/
obj/
artifacts/
程序/
```

### 4. 提交源码

```powershell
git add .
git commit -m "Initial commit: KuangYi v1.0"
```

### 5. 关联 GitHub 仓库

```powershell
git branch -M main
git remote add origin https://github.com/yourname/kuangyi.git
```

把 `yourname` 换成你的用户名。

如果提示 `remote origin already exists`，改用：

```powershell
git remote set-url origin https://github.com/yourname/kuangyi.git
```

### 6. 推送

```powershell
git push -u origin main
```

第一次推送会弹出登录窗口：

- 推荐点 **Sign in with your browser**，在浏览器里授权；
- 或者使用 GitHub 的 **Personal Access Token** 作为密码；
- **不要**输入 GitHub 账号密码，GitHub 已经不支持密码推送。

看到类似：

```text
branch 'main' set up to track 'origin/main'.
```

就成功了。刷新 GitHub 仓库页面，应该能看到源码和 README。

---

## 四、发布「程序本体」和「离线语言包」

普通 Git 仓库只放源码；编译好的程序和模型放 **Releases**。

### 1. 进入 Release 页面

在 GitHub 仓库页面：

1. 点右侧 **Releases**；
2. 点 **Create a new release**；
3. **Choose a tag** 输入：

```text
v1.0.0
```

4. 点 **Create new tag: v1.0.0 on publish**。

### 2. 填写标题和说明

- **Release title**：`框译 v1.0.0`
- **Describe this release**：把 `CHANGELOG.md` 的内容复制进去即可。

### 3. 上传两个 ZIP

在 **Attach binaries by dropping them here or selecting them** 区域：

1. 选择 `框译-v1.0-程序本体-win-x64.zip`；
2. 再选择 `框译-v1.0-离线语言包.zip`；
3. 等待上传完成（模型包约 1.1 GB，可能需要几分钟）；
4. 点 **Publish release**。

上传后，别人在 Release 页面能看到两个下载链接。

> GitHub 单个 Release 附件上限约 2 GB。当前模型包约 1.1 GB，可以正常上传。
> 如果以后模型包超过 2 GB，需要拆分或改用网盘/Hugging Face。

### 4. 不要做的事

- 不要把 ZIP 拖进普通源码目录再 `git add`；
- 不要用 Git LFS 管理 1.55 GB 模型（免费额度和带宽都不合适）；
- 不要把 `%LOCALAPPDATA%\KuangYi\settings.json` 上传（里面有 DeepL Key）；
- 不要把 `history.db`、日志、截图历史上传。

---

## 五、别人下载后怎么用

把两个 ZIP 都下载，解压到**同一个文件夹**：

```text
框译\
├─ KuangYi.exe
├─ 很多 .dll / .json
├─ models\
│  ├─ en-zh\
│  ├─ zh-en\
│  └─ ...
├─ 免责声明.txt
├─ 启动说明.txt
└─ ...
```

最终必须保证：

```text
KuangYi.exe 和 models 在同一层目录
```

然后双击 `KuangYi.exe`。不需要安装 .NET。

如果只解压了程序本体，没有 `models`，程序能打开，但离线翻译不可用；
把离线语言包里的 `models` 文件夹放到 `KuangYi.exe` 旁边即可。

---

## 六、GitHub Desktop（不想敲命令的替代方案）

1. 安装 GitHub Desktop：https://desktop.github.com/
2. 登录 GitHub 账号；
3. **File → Add local repository**；
4. 选择 `C:\Users\zlp\Desktop\框译\源码`；
5. 如果提示不是 Git 仓库，点 **create a repository**；
6. 在左下角填写提交信息，点 **Commit to main**；
7. 点 **Publish repository**；
8. 取消勾选 “Keep this code private”（如果要公开）；
9. 发布成功后，再到 GitHub 网页的 **Releases** 上传两个 ZIP。

GitHub Desktop 只负责源码，ZIP 仍然在网页 Release 里上传。

---

## 七、常见错误

### 1. `git push` 提示 authentication failed

- 确认邮箱已验证；
- 使用浏览器登录或 Personal Access Token；
- 不要用账号密码；
- Windows 凭据管理器里删除旧的 GitHub 凭据后重试。

### 2. `Updates were rejected because the remote contains work...`

仓库创建时勾选了 README，先合并：

```powershell
git pull --rebase origin main
git push -u origin main
```

### 3. `File ... is 100.00 MB; this exceeds GitHub's file size limit`

说明把 `models` 或 `程序` 提交进去了。

```powershell
git rm -r --cached models 程序 bin obj artifacts
git commit -m "Remove build outputs and models"
git push
```

如果已经提交到历史里，最省事的办法是删掉 GitHub 仓库重新建一个，
并确认 `.gitignore` 生效后再提交。

### 4. Release 上传失败

- 检查单个文件是否超过 2 GB；
- 检查网络是否稳定；
- 模型包可以拆成 `models-part1.zip`、`models-part2.zip`，或放网盘。

### 5. 中文文件名在 ZIP 里显示乱码

Windows 自带解压对 UTF-8 中文名支持不一致。建议用 7-Zip 解压，
或者把 ZIP 内文件名改成英文；当前包在 Windows 资源管理器中通常可正常显示。

---

## 八、推荐的上传顺序

```text
1. 创建空仓库
2. 本地 git init / add / commit
3. push 源码
4. 网页创建 Release v1.0.0
5. 上传 程序本体.zip
6. 上传 离线语言包.zip
7. Publish release
8. 下载测试：两个 ZIP 解压到同一目录，确认 KuangYi.exe + models
```

源码仓库很小（约 1 MB），模型和程序都走 Release，这是最适合本项目的发布方式。
