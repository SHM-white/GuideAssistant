# GuideAssistant

Windows 游戏内攻略辅助工具：WinUI 3 + WebView2 嵌入攻略视频，WPF 透明悬浮层显示字幕和方向提示。

主要功能：Bilibili 视频、标签页/收藏夹、游戏识别、独立工具栏、置顶与窗口透明度、CC/DOM/语音识别字幕、方向提示。

- **同一个按键允许绑定多个操作**，这是刻意保留的特性。
- `.github/workflows/build.yml`：PR/master Windows x64 编译检查。
- `.github/workflows/release-msix.yml`：只有创建版本 tag 或手动指定已存在 tag 后才发布 MSIX。
- 发布和签名证书说明：[RELEASE.md](RELEASE.md)。
- 回归检查：[docs/REGRESSION.md](docs/REGRESSION.md)。
- 后续功能：[docs/ROADMAP.md](docs/ROADMAP.md)。

当前语音识别在网页音频无法捕获时可回退到系统 Loopback；此模式可能混入其他应用声音，不推荐涉及隐私的场景。不同游戏的全屏/反作弊设置也可能影响窗口置顶。
