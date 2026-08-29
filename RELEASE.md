# 发布指南

本项目的 GitHub Actions 工作流名为 **Release MSIX**，文件是 `.github/workflows/release-msix.yml`。它会构建已签名的 MSIX 侧载包，并发布到 GitHub Release。

## 首次配置

开始前确认：

1. `.github/workflows/release-msix.yml` 已经提交到仓库的默认分支。
2. 仓库已启用 GitHub Actions。
3. PFX 证书包含私钥，且证书 Subject 必须严格为 `CN=SHM_white`。

在本机用 PowerShell 将 PFX 编码为 Base64。命令只输出编码结果，不会修改仓库，也不要把结果写入项目文件：

```powershell
[Convert]::ToBase64String([IO.File]::ReadAllBytes("C:\path\to\windows-signing-certificate.pfx"))
```

打开仓库的 **Settings > Secrets and variables > Actions**，点击 **New repository secret**，添加以下两个 Actions secrets：

| Name | Value |
| --- | --- |
| `WINDOWS_CERTIFICATE_BASE64` | 上一条命令输出的 PFX Base64 内容 |
| `WINDOWS_CERTIFICATE_PASSWORD` | PFX 密码 |

## 通过标签发布

标签必须严格使用 `vMAJOR.MINOR.PATCH` 格式，例如 `v1.2.3`。三个数字都必须是非负整数，不能使用多余的前导零，并且每个数字不能超过 `65535`。`v1.2.3` 会生成 MSIX 版本 `1.2.3.0`。

先运行 `git status` 检查工作区。如果存在未提交修改，只暂存本次版本应包含的文件并提交，不要使用 `git add .` 把无关文件带入发布版本。随后确认目标提交已推送：

```powershell
git status
git push origin HEAD
```

然后创建带注释的标签并推送。把示例版本替换为实际版本：

```powershell
git tag -a v1.2.3 -m "Release v1.2.3"
git push origin v1.2.3
```

推送标签后，`Release MSIX` 会自动运行。工作流会分别构建 `x86`、`x64` 和 `ARM64` 包，并创建或更新对应的 GitHub Release。

## 手动运行

也可以在 GitHub 仓库中打开 **Actions > Release MSIX > Run workflow**，在 `tag` 输入框中填写要发布的标签，例如 `v1.2.3`，然后运行工作流。

手动运行时，标签必须已经存在于远程仓库。也可以使用 GitHub CLI：

```powershell
gh workflow run release-msix.yml -f tag=v1.2.3
```

如果标签还没推送，先执行 `git push origin v1.2.3`。手动运行同样要求标签严格匹配 `vMAJOR.MINOR.PATCH`，并满足每个版本分量不超过 `65535`。

## 下载和安装

发布完成后，在对应 GitHub Release 的 **Assets** 中下载以下三个 ZIP 文件：

```text
GuideAssistant-v1.2.3-x86.zip
GuideAssistant-v1.2.3-x64.zip
GuideAssistant-v1.2.3-ARM64.zip
```

根据目标 Windows 设备的架构选择 ZIP。每个 ZIP 都包含完整的侧载目录和 `Add-AppDevPackage.ps1`。解压后按侧载目录中的脚本安装。工作流只发布已签名的包，不能依赖无签名包安装。

## 重跑和更新已有发布

标签对应的工作流失败时，可以在 **Actions > Release MSIX** 中打开失败的运行并点击 **Re-run jobs**，或者再次手动运行同一个远程标签。

如果对应 Release 已存在，工作流会复用该 Release，并用同名资产覆盖旧文件。重跑完成后，请重新下载需要的 ZIP。

## 常见故障

- **缺少 secrets**：确认仓库 Actions secrets 中存在 `WINDOWS_CERTIFICATE_BASE64` 和 `WINDOWS_CERTIFICATE_PASSWORD`，名称必须完全一致，值不能为空。
- **Subject 不匹配**：证书必须包含私钥，且 Subject 必须精确为 `CN=SHM_white`。不要只检查证书显示名称，也不要使用其他 Subject 的证书。
- **标签无效或不存在**：使用严格的 `vMAJOR.MINOR.PATCH`，例如 `v1.2.3`。检查每个分量不超过 `65535`，并确认标签已推送到远程仓库。手动运行尤其要求标签已经存在。
- **签名失败或证书过期**：确认 PFX 密码正确、PFX 可导入、证书仍在有效期内并包含私钥。工作流会验证 MSIX 签名，签名失败不会发布无签名包。
- **GITHUB_TOKEN 权限受限**：仓库或组织的 Actions 设置不能禁止工作流写入内容。发布作业需要 `contents: write`，如果仓库级设置限制了工作流权限，请允许 Actions 写入仓库内容后重跑。

## 安全提醒

永远不要提交 PFX 文件、PFX 密码或 PFX 的 Base64 内容。不要把它们写入源码、日志、Issue、Pull Request 或普通仓库变量，只应通过 GitHub Actions secrets 保存。
