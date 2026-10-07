# macOS 构建与验收

macOS 客户端复用 `OrderedClicker.Shared` 中从 Windows 源文件链接的执行核心、方案、草稿、断点和日志服务。Windows 工程继续使用原有 WinForms 客户端。Mac 界面为 Avalonia 11.3.7，使用 .NET 8 自包含发布以兼容 macOS 13；发布前持续更新 .NET 安全补丁，并在 .NET 8 支持期结束前升级运行时及最低系统版本。

## 构建

环境：macOS 13+、.NET 8 SDK、Xcode Command Line Tools、Python 3。

```bash
dotnet run --project tests/OrderedClicker.CrossPlatform.Tests -c Release
python3 scripts/build-macos-guide.py
bash scripts/build-macos.sh
python3 scripts/package-macos-delivery.py
```

默认产物：`releases/macos/ordered-clicker-macos-{arm64,x64}-v2.1.0.dmg`。默认使用 ad-hoc 本地签名，不能等同于 Developer ID 签名或 Apple 公证。打包脚本对正式公证模式实行缺失凭据即失败。

正式发布时，在本机钥匙串配置 Developer ID Application 签名身份和 notarytool profile，然后运行：

```bash
export ORDERED_CLICKER_MAC_SIGNING_IDENTITY='Developer ID Application: your identity'
export ORDERED_CLICKER_NOTARY_PROFILE='ordered-clicker-notary'
export ORDERED_CLICKER_RELEASE=1
python3 scripts/build-macos-guide.py --signed
bash scripts/build-macos.sh
```

凭据留在钥匙串中。不要提交证书、密码或私钥。脚本先签内部 Mach-O，再签应用包；分别公证并附加应用与 DMG 的公证票据。Hardened Runtime 仅用于 Developer ID 正式签名，本地 ad-hoc 构建不启用它，避免无 Team ID 的动态库被拒绝加载。JIT 仅使用 `com.apple.security.cs.allow-jit`；应用不采用 App Sandbox，不使用 Apple Events 控制其他应用。

`ORDERED_CLICKER_DOTNET` 可指定 SDK；`ORDERED_CLICKER_NUGET_SOURCE` 可指定本地包源；`ORDERED_CLICKER_OUTPUT` 指定输出目录；`ORDERED_CLICKER_VERSION` 指定版本。

## 平台约定

- 鼠标、屏幕采点、云桌面区域统一使用 Core Graphics 全局屏幕坐标（逻辑点），不把 Retina 像素直接当作鼠标坐标。
- 截屏使用 ScreenCaptureKit，对每个实际显示器分别采样、按全局区域组合；排除自身应用窗口和指针。无权限、无有效帧或采集流失败都不能被判定为稳定。
- Carbon 注册全局热键，避免为普通热键引入全键盘监听。停止不依赖主界面的消息处理：后台安全角直接取消执行，显示器变化、失去权限、休眠和会话状态变化会停止任务。
- 文件锁采用 Darwin flock。所有协议与 .oclick v4 兼容，但 Windows 的绝对点位必须重新采点；云桌面先校准后复用相对坐标。
- 后台进度合并为 100 ms 刷新。执行断点每 500 ms 限速持久化，停止时强制保存；不能保证异常断电情况下点击恰好一次。

## 验收

自动测试覆盖原有 19 组非 WinForms 功能和 Mac 坐标 / 安全角 / 文件锁约定，另有五步导航、保存、循环输入和保存冲突的界面行为测试。原有 Windows UI 和 Windows 单实例测试保留在 Windows CI。GitHub CI 对 Apple Silicon 和 Intel 运行器执行共享回归，双架构构建后由一个发布任务汇总附件，避免互相删除；默认建立草稿 Release。

发布前必须在对应芯片上检查：

1. 从新下载的 DMG 拖入 Applications 后启动，首次辅助功能与屏幕录制授权、撤回授权及重新启动。
2. 真实点击落点、全局快捷键冲突、暂停后鼠标移开再继续、停止与恢复、关闭窗口安全停止。
3. Retina / 外接非 Retina 混合缩放、负坐标屏幕、显示器热插拔；不能在显示器间空隙或安全角执行。
4. 云桌面区域重校准、画面变化 / 稳定 / 超时 / 采集失败、全屏窗口与 Spaces。
5. 草稿、保存冲突、异常退出后的断点确认、重复启动窗口激活、日志清理。
6. `codesign --verify --deep --strict`、`hdiutil verify` 和独立目录中的自包含启动；正式发布还需 `spctl` 与 `stapler validate`。

当前本地验证平台为 Intel macOS 13.7.8，未配置 Developer ID 身份。M 芯片构建与结构验证不能代替 M 芯片实机验收，真实系统授权后点击 / 截屏功能亦需按上述清单验收。
