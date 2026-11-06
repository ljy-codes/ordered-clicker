# macOS 构建与验收

macOS 客户端复用 `OrderedClicker.Shared` 中从 Windows 源文件链接的执行核心、方案、草稿、断点和日志服务。Windows 工程继续使用原有 WinForms 客户端。Mac 界面为 Avalonia 11.3.7，使用 .NET 8 自包含发布以兼容 macOS 13；发布前持续更新 .NET 安全补丁，并在 .NET 8 支持期结束前升级运行时及最低系统版本。

## 构建

环境：macOS 13+、.NET 8 SDK、Xcode Command Line Tools、Python 3。

```bash
dotnet run --project tests/OrderedClicker.CrossPlatform.Tests -c Release
python3 scripts/build-macos-guide.py
ORDERED_CLICKER_VERSION=2.2.0 bash scripts/build-macos.sh
ORDERED_CLICKER_VERSION=2.2.0 bash scripts/verify-macos-packages.sh
python3 scripts/package-macos-delivery.py --version 2.2.0
```

默认产物：`releases/macos/ordered-clicker-macos-{arm64,x64}-v2.2.0.dmg`。默认使用 ad-hoc 本地签名，不能等同于 Developer ID 签名或 Apple 公证。打包脚本对正式公证模式实行缺失凭据即失败。

正式发布时，在本机钥匙串配置 Developer ID Application 签名身份和 notarytool profile，然后运行：

```bash
export ORDERED_CLICKER_MAC_SIGNING_IDENTITY='Developer ID Application: your identity'
export ORDERED_CLICKER_NOTARY_PROFILE='ordered-clicker-notary'
export ORDERED_CLICKER_RELEASE=1
export ORDERED_CLICKER_VERSION=2.2.0
python3 scripts/build-macos-guide.py --signed
bash scripts/build-macos.sh
bash scripts/verify-macos-packages.sh
python3 scripts/package-macos-delivery.py --version 2.2.0
```

凭据留在钥匙串中。不要提交证书、密码或私钥。脚本先签内部 Mach-O，再签应用包；分别公证并附加应用与 DMG 的公证票据。Hardened Runtime 仅用于 Developer ID 正式签名，本地 ad-hoc 构建不启用它，避免无 Team ID 的动态库被拒绝加载。JIT 仅使用 `com.apple.security.cs.allow-jit`；应用不采用 App Sandbox，不使用 Apple Events 控制其他应用。

`ORDERED_CLICKER_DOTNET` 可指定 SDK；`ORDERED_CLICKER_NUGET_SOURCE` 可指定本地包源；`ORDERED_CLICKER_OUTPUT` 指定输出目录；`ORDERED_CLICKER_VERSION` 指定版本。

## 平台约定

- 鼠标、屏幕采点、云桌面区域统一使用 Core Graphics 全局屏幕坐标（逻辑点），不把 Retina 像素直接当作鼠标坐标。
- 截屏使用 ScreenCaptureKit，对每个实际显示器分别采样、按全局区域组合；排除自身应用窗口和指针。无权限、无有效帧或采集流失败都不能被判定为稳定。
- Carbon 注册全局热键，避免为普通热键引入全键盘监听。停止不依赖主界面的消息处理：后台安全角直接取消执行，显示器变化、失去权限、休眠和会话状态变化会停止任务。
- 文件锁采用 Darwin flock。所有协议与 .oclick v4 兼容，但 Windows 的绝对点位必须重新采点；云桌面先校准后复用相对坐标。
- 2.2.0 保持 v4，兼容 2.0.0 / 2.1.0 的 v4 方案。窗口绑定与检测区域为可选字段；旧方案默认不绑定窗口，检测范围仍为云桌面区域或虚拟桌面。绑定按平台保存，跨平台后需重新绑定。
- 演练只移动、不点击（包括停止清理时不发送鼠标释放），实际点击数为 0，不改变正式执行断点。单步完成当前点的全部连击、点击间隔、点后等待和稳定检查后，在下一点前暂停。
- 可选窗口绑定要求启动或继续时切到目标窗口并保持可见；窗口不匹配或落点被遮挡时暂停。排除遮挡、切回窗口并确认页面后再继续；不支持后台点击或控件识别。
- 自定义检测区域仅改变画面采样范围，不改变点击坐标。选择区域时避开动画、视频和时钟，窗口移动或显示器变化后重新检查；持续超时后确认页面，再调整容差、稳定时长或超时。需要屏幕录制权限。
- 后台进度合并为 100 ms 刷新。执行断点每 500 ms 限速持久化，停止时强制保存；不能保证异常断电情况下点击恰好一次。

## 验收

共享回归及 Mac 界面测试应在打包前运行，Windows UI 和单实例测试保留在 Windows CI。仓库当前发布工作流仍负责 Windows 构建；上传全部成功后，仅替换它将上传的精确同名附件，保留另行上传的 Mac DMG 和其他附件。Mac 校验清单使用独立名称（例如 `SHA256SUMS-macos.txt`），避免与 Windows 的 `SHA256SUMS.txt` 重名。上传失败保留原附件；替换阶段发生 API 错误时流程失败，需重跑并核对完整性。此处不代表已执行 2.2.0 双架构原生验收。

发布前必须在对应芯片上检查：

1. 从新下载的 DMG 拖入 Applications 后启动，首次辅助功能与屏幕录制授权、撤回授权及重新启动。
2. 真实点击落点、全局快捷键冲突、暂停后鼠标移开再继续、停止与恢复、关闭窗口安全停止。
3. Retina / 外接非 Retina 混合缩放、负坐标屏幕、显示器热插拔；不能在显示器间空隙或安全角执行。
4. 云桌面区域重校准、画面变化 / 稳定 / 超时 / 采集失败、全屏窗口与 Spaces。
5. 草稿、保存冲突、异常退出后的断点确认、重复启动窗口激活、日志清理。
6. `codesign --verify --deep --strict`、`hdiutil verify` 和独立目录中的自包含启动；正式发布还需 `spctl` 与 `stapler validate`。
7. 先产生一个正式断点，再演练、暂停、停止，确认无真实点击且原断点未改变；单步设置多次连击和点后等待，确认完整点完成后才暂停。
8. 绑定窗口后在启动 / 继续时切回目标窗口，验证错误窗口、遮挡和 Spaces 切换会暂停；不绑定时仍能执行旧方案。
9. 自定义检测区域排除动画后能稳定，区域内变化会等待、超时会暂停；未设置区域的旧 v4 方案沿用原范围。撤销屏幕录制权限不得误判稳定。

此前本地验证平台为 Intel macOS 13.7.8，未配置 Developer ID 身份，不能视为 2.2.0 已验收。M 芯片构建与结构验证不能代替 M 芯片实机验收，真实系统授权后点击 / 截屏及窗口保护亦需按上述清单验收。
