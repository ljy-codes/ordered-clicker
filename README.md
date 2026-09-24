# 有序连点器 2.1.0

Windows 10/11 x64 桌面连点器。按列表顺序执行点位，支持单点连击、步骤等待、循环、暂停继续、停止、云桌面相对坐标、画面稳定检测、持久断点恢复和执行日志。2.1.0 增加单实例唤醒、后台安全角监控、平滑进度刷新、退出前安全停止，以及日志自动清理。

## 使用方式

推荐安装版日常使用：

- `ordered-clicker-setup-v2.1.0.exe`
- `有序连点器-免安装.exe`

安装版把方案、草稿和日志写入 `%LocalAppData%\OrderedClicker`。免安装版把数据写入程序旁的 `OrderedClickerData`，因此程序所在目录必须可写。发行类型在编译时确定，重命名免安装 EXE 不会改变数据目录。

主界面按五步操作：

1. **模式**：选择本机或云桌面坐标方式。
2. **方案**：新建、打开或保存 `.oclick` 方案。
3. **采点**：进入采点模式，使用快捷键或“2 秒后记录”添加点位。
4. **检查**：确认点位、循环、预计耗时、画面稳定检测和安全停止方式。
5. **运行**：倒计时后开始，可暂停、继续、停止或从可靠断点继续。

默认全局快捷键为：

- `F6`：记录当前位置
- `F7`：开始、暂停或继续
- `F8`：停止

设置中可改为组合快捷键。执行前还必须启用安全角停止；鼠标在所选屏幕角连续停留设定时间后会停止任务。任何点位与安全角重叠时，检查不会通过。

## 方案与恢复

2.1.0 使用 `.oclick` 方案格式 v4，与 2.0.0 方案保持兼容。旧版 1.x JSON 文件不能直接编辑，只能通过“迁移旧方案”单向转换；原文件不会被覆盖。

“保存”只覆盖当前已绑定方案，“另存为”创建新的方案身份。如果方案被其他窗口或程序修改，普通保存会停止并要求另存，避免静默覆盖。程序会自动保存活动草稿；异常退出后下次启动可恢复，正常关闭且没有未保存修改时会清理草稿。损坏草稿会被隔离为 `.broken.json`，不会在每次启动时重复报错。

## 云桌面

启用云桌面模式后先校准远程桌面可视区域，再采集点位。重新校准时保留已有有效相对坐标，只补齐缺失值。运行前仍会检查显示器变化、坐标范围和安全角冲突。

## 数据与日志

应用数据目录包括：

- `profiles`：本机 `.oclick` 方案
- `drafts`：活动草稿
- `logs`：每次执行结果
- `diagnostics`：设置、方案和运行异常诊断

损坏的设置文件会重命名为带时间戳的 `.broken.json`，随后恢复默认设置，不会静默覆盖原文件。

## 构建

依赖：

- Windows 10/11 x64
- PowerShell 7
- .NET 10 SDK
- Inno Setup 7（构建安装版）
- Windows SDK `signtool.exe` 和代码签名证书（正式 Release）

运行测试：

```powershell
pwsh -File .\scripts\test.ps1 -Configuration Release
.\tests\Installer.Tests.ps1
```

构建未签名开发交付：

```powershell
.\scripts\build-installer.ps1 -Version 2.1.0
```

构建正式签名交付：

```powershell
$env:ORDERED_CLICKER_SIGNING_PASSWORD = "<证书密码>"
.\scripts\build-installer.ps1 `
  -Version 2.1.0 `
  -Release `
  -SigningCertificatePath "D:\certs\ordered-clicker.pfx"
```

`-Release` 会把 PFX 临时导入当前用户证书库，按证书指纹签名安装版应用、免安装版应用和安装器，验证 Authenticode 状态及可信时间戳后移除本次导入的证书；证书密码不会进入 `signtool` 命令行。

## 交付文件

最终产品目录只包含安装版 EXE、免安装 EXE、PDF 使用说明、HTML 使用说明和 SHA256SUMS.txt：

```text
ordered-clicker-setup-v2.1.0.exe
有序连点器-免安装.exe
有序连点器-使用说明.pdf
有序连点器-使用说明.html
SHA256SUMS.txt
```

内部 `publish\packages` 仍会生成 ZIP 便携包，但不放入最终产品目录。
