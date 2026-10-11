# 发布指南

本项目的 GitHub Actions 工作流名为 **Release MSIX**，文件是 `.github/workflows/release-msix.yml`。它会构建已签名的 MSIX 侧载包，并发布到 GitHub Release。

## 没有 PFX 证书时：一次性生成

如果旧版本已经用某张证书发布过，**应优先继续使用同一张 PFX**。更换签名证书可能使已安装用户无法直接升级。

在 Windows PowerShell 中，从项目根目录运行：

```powershell
.\scripts\New-WindowsSigningCertificate.ps1
```

脚本在用户主目录的 `GuideAssistant-signing` 文件夹生成两个文件：

- `GuideAssistant-signing.pfx`：包含私钥，用于发布签名，不可公开。
- `GuideAssistant-signing.cer`：公开证书，可以提供给安装者以建立本机信任。

将脚本提示的 PFX Base64 和密码配置为下面的两个 GitHub Actions Secrets。脚本不会自行上传它们。

**注意：自签名证书不具备公开 CA 的默认信任。** 安装者在确认软件来源后，需要把公开的 `.cer` 安装到 Windows 的 `TrustedPeople` 证书存储区（通常使用管理员 PowerShell）：

```powershell
Import-Certificate -FilePath '.\GuideAssistant-signing.cer' -CertStoreLocation Cert:\LocalMachine\TrustedPeople
```

信任发布者意味着信任该证书签名的软件。仅安装可信来源的证书。如需免去用户手动建立信任，请采用合适的受信任代码签名方案。

## 日常编译检查

`.github/workflows/build.yml` 会在向 `master` 提交、打开/更新 PR 或手动触发时执行 Windows x64 无签名编译检查。此任务**不依赖签名 Secrets，也不会发布安装包**。

正式 Release **不会因为合并 PR、推送普通分支或运行普通构建检查而触发**。仅推送符合格式的版本标签自动触发，或通过 Actions 手动指定已有的标签。预检首先确认 Secrets、标签存在、标签提交已合并进 master，并检查项目和 MSIX 版本一致；不通过则不会发布。

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

发版前先将 `GuideAssistant/GuideAssistant.csproj` 的 `<Version>` 和 `GuideAssistant/Package.appxmanifest` 的 `Identity.Version` 分别设成对应版本（例如 `1.3.0`、`1.3.0.0`），合并到 master，再从 master 提交打标签。不能直接在未合并的 PR 分支上创建正式发布标签。

然后创建带注释的标签并推送。把示例版本替换为实际版本：

```powershell
git tag -a v1.2.3 -m "Release v1.2.3"
git push origin v1.2.3
```

推送标签后，`Release MSIX` 会自动运行。构建时会同步设置 MSIX 包版本与程序集版本，“关于”页面会优先读取实际安装包版本，非打包版本则显示程序集版本。工作流会分别构建 `x86`、`x64` 和 `ARM64` 包，并创建或更新对应的 GitHub Release。

## 手动运行

也可以在 GitHub 仓库中打开 **Actions > Release MSIX > Run workflow**，在 `tag` 输入框中填写要发布的标签，例如 `v1.2.3`，然后运行工作流。

手动运行时，标签必须已经存在于远程仓库。也可以使用 GitHub CLI：

```powershell
gh workflow run release-msix.yml -f tag=v1.2.3
```

在 Actions 网页点击 **Run workflow** 时，先在分支下拉框选 **master**，再在 `tag` 输入框填写已存在的版本标签。Run workflow **不会自动创建标签**。如果标签还没推送，先执行 `git push origin v1.2.3`。手动运行同样要求标签严格匹配 `vMAJOR.MINOR.PATCH`，并满足每个版本分量不超过 `65535`。

## 下载和安装

发布完成后，在对应 GitHub Release 的 **Assets** 中下载以下三个 ZIP 文件：

```text
GuideAssistant-v1.2.3-x86.zip
GuideAssistant-v1.2.3-x64.zip
GuideAssistant-v1.2.3-ARM64.zip
```

Release 另附 `SHA256SUMS.txt` 记录三个 ZIP 的 SHA256。可以在 PowerShell 运行 `Get-FileHash -Algorithm SHA256 .\GuideAssistant-v1.3.0-x64.zip` 核对。

根据目标 Windows 设备的架构选择 ZIP。每个 ZIP 都包含完整的侧载目录和 `Add-AppDevPackage.ps1`。解压后按侧载目录中的脚本安装。工作流只发布已签名的包，不能依赖无签名包安装。

## 重跑和更新已有发布

标签对应的工作流失败时，若已经修复了 workflow YAML，**不要直接对旧运行点击 Re-run jobs**：旧运行可能仍采用旧版本工作流，导致同样的错误。应先把修复 PR 合并到 master，再打开 **Actions > Release MSIX > Run workflow**，分支选 **master**，`tag` 填现有标签（例如 `v1.3.0`），启动**全新**运行。无需删除或重建原有 tag；工作流会从该 tag 检出应用源代码，但执行最新 master 上的工作流步骤。

如果只是间歇性的网络或 runner 故障、工作流定义没有变，才适合直接对原运行使用 **Re-run jobs**。

如果对应 Release 已存在，工作流会复用该 Release，并用同名资产覆盖旧文件。重跑完成后，请重新下载需要的 ZIP。

## 常见故障

- **缺少 secrets**：确认仓库 Actions secrets 中存在 `WINDOWS_CERTIFICATE_BASE64` 和 `WINDOWS_CERTIFICATE_PASSWORD`，名称必须完全一致，值不能为空。
- **Subject 不匹配**：证书必须包含私钥，且 Subject 必须精确为 `CN=SHM_white`。不要只检查证书显示名称，也不要使用其他 Subject 的证书。
- **标签无效或不存在**：使用严格的 `vMAJOR.MINOR.PATCH`，例如 `v1.2.3`。检查每个分量不超过 `65535`，并确认标签已推送到远程仓库。手动运行尤其要求标签已经存在。
- **签名失败或证书过期**：确认 PFX 密码正确、PFX 可导入、证书仍在有效期内并包含私钥。工作流会验证 MSIX 签名，签名失败不会发布无签名包。
- **GITHUB_TOKEN 权限受限**：仓库或组织的 Actions 设置不能禁止工作流写入内容。发布作业需要 `contents: write`，如果仓库级设置限制了工作流权限，请允许 Actions 写入仓库内容后重跑。

## 安全提醒

永远不要提交 PFX 文件、PFX 密码或 PFX 的 Base64 内容。不要把它们写入源码、日志、Issue、Pull Request 或普通仓库变量，只应通过 GitHub Actions secrets 保存。
