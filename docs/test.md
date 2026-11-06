# 有序连点器测试与清理记录

## 2026-10-08 版本 2.1.0

### 基线

- 仓库：`ljy-codes/ordered-clicker`
- 分支：`main`
- 首轮验证基线：`38ec08fa53380389e672db6fa7a6c09c21fb7e93`
- 同步后复验基线：`c24a0f9`，包含远端 macOS 相关提交；本次仅新增测试与清理记录，未修改远端功能代码。
- 验证环境：Windows，.NET 10，PowerShell 7

### 本轮验证

| 检查项 | 命令 | 结果 |
| --- | --- | --- |
| 核心自动化测试 | `pwsh -NoProfile -File .\scripts\test.ps1 -Configuration Release` | 通过，21/21 |
| 安装器与发布契约 | `pwsh -NoProfile -File .\tests\Installer.Tests.ps1` | 全部通过 |
| 同步后 Windows 核心复验 | `pwsh -NoProfile -File .\scripts\test.ps1 -Configuration Release` | 通过，21/21 |
| 同步后安装器契约复验 | `pwsh -NoProfile -File .\tests\Installer.Tests.ps1` | 全部通过 |
| 跨平台测试 | `dotnet run --project .\tests\OrderedClicker.CrossPlatform.Tests -c Release` | 执行被用户中断，无完整结果；未认定通过 |
| Mac UI 测试与原生功能验收 | 未执行 | 不属于本次产物清理验收范围 |

本轮没有执行真实鼠标点击、外部桌面联调或代码签名验证。上述结果覆盖本地业务逻辑、配置迁移、执行可靠性、单实例、日志清理、界面烟雾测试及安装发布契约。

恢复任务时未发现仍在运行的 dotnet 或 OrderedClicker 进程。检查发现跨平台测试中的 `MacContracts` 会调用 `MacInstance`，后者直接使用 `libc` 和 Darwin 文件锁参数，不能将 Windows 上的执行视为完整 macOS 验收。Mac UI 项目使用 Avalonia Headless；本轮未运行，不能据此判断原生 UI 行为。

### 清理记录

以下目录仅包含测试、构建、文档验证或浏览器临时产物，均已被 `.gitignore` 排除，可由现有脚本重新生成：

- `.tmp`
- `src/OrderedClicker/bin`
- `src/OrderedClicker/obj`
- `tests/OrderedClicker.Tests/bin`
- `tests/OrderedClicker.Tests/obj`

首轮已清理：1,079 个文件，170,229,737 字节，约 162.34 MiB。

同步后复测再次生成上述目录，并额外生成以下可再生目录，本轮一并清理：

- `src/OrderedClicker.Shared/bin`
- `src/OrderedClicker.Shared/obj`
- `tests/OrderedClicker.CrossPlatform.Tests/bin`
- `tests/OrderedClicker.CrossPlatform.Tests/obj`

收尾状态：第二轮清理完成，共删除 9 个目录、107 个文件、4,420,981 字节。逐项确认目标目录已不存在；清理前检查了工作区路径边界、Git 跟踪状态及重解析点。以上两轮统计含复测重建的文件，不代表去重后的磁盘净释放量。

保留内容：

- `tests/OrderedClicker.Tests` 下的测试源码和测试项目文件
- 新同步的跨平台测试与 Mac UI 测试源码及项目文件
- `tests/Installer.Tests.ps1`
- `scripts/test.ps1`
- `artifacts` 下已纳入 Git 的界面验证截图
- `publish`、正式安装包、免安装版及 `交付产品`

### 兼容性与风险

- 本次不修改生产代码、测试逻辑、安装器逻辑或交付文件，不影响版本 2.1.0 的运行兼容性。
- 后续执行构建或测试会重新生成 `bin`、`obj` 和部分 `.tmp` 内容，这是正常行为。
- EXE 仍未配置 Authenticode 代码签名，Windows 可能提示未知发布者。

## 2026-11-06 版本 2.2.0

### 范围与环境

- 实施基线：`2203695`，Windows，.NET SDK 10.0.400，PowerShell 7。
- 本轮修复审查列出的 11 项问题，新增演练/单步、可选窗口绑定、自定义稳定检测区域。
- 方案格式继续使用 v4；新增字段可选，保留旧方案指纹兼容。
- 执行回归使用假鼠标、假平台和临时数据目录，没有向用户桌面发送真实点击。
- Mac 托管项目目标仍为 .NET 8，本机无 .NET 8 运行时，使用 .NET 10 的 `--roll-forward Major` 执行无头测试；不能等同于 .NET 8 或 macOS 原生验收。
- 本轮标识：`OC-220-20261106`；最终验收于 2026-11-06 12:08（Asia/Shanghai）继续。系统 SDK 为 10.0.400；打包脚本优先使用仓库本地 SDK 10.0.302，分别记录，不能混作同一环境。
- 调用链：Windows `MainForm` / Mac `MainWindow` → 方案验证与执行计划 → 共享执行引擎 → 平台输入、窗口和屏幕采样适配器；保存链为界面 → `ProfileService` / `DraftService` / `ExecutionCheckpointService` → 本机文件。没有 DB、Redis、MQ、账号租户或生产服务。

### 需求与历史回归对应

以下编号为本轮稳定回归标识；“托管通过”只说明隔离运行结果，不代替系统权限、真实点击或 macOS 原生验证。

| ID | 需求或历史问题 | 回归入口与断言 | 证据范围 |
| --- | --- | --- | --- |
| REG-220-01 | 重启恢复与方案断点匹配 | `UiSmokeTests`、`ExecutionPlanSafetyTests`：优先恢复快照，匹配方案保留断点，坏草稿不隔离有效旧断点 | Windows 隔离 UI / 共享逻辑 |
| REG-220-02 | 连击定位与暂停恢复 | `ClickExecutionEngineTests`、`ExecutionModeTests`：每次点击定位，移动偏离后暂停，恢复后重定位 | 假鼠标 |
| REG-220-03 | 撤销跨方案污染及批量时间撤销 | `UiSmokeTests`、`PointTimingServiceTests`：切换方案清空历史，批量修改可撤销 | Windows 隔离 UI |
| REG-220-04 | 显示环境变化与恢复准备时间 | `WindowsExecutionEnvironmentTests`、`UiSmokeTests`：显示检查、恢复倒计时可取消、会话变化停机 | 纯窗口快照 / UI；真实 RDP 断开未执行 |
| REG-220-05 | 正式方案保存与旧中间文件恢复 | `ProfileServiceTests`：正式路径可见、旧读句柄与备份、并发冲突、最新有效孤留副本、加载与哈希同源 | 临时目录；本机 NTFS |
| REG-220-06 | 设置暂时不可读及损坏隔离失败 | `DiagnosticAndSettingsTests`：启动使用默认值，原文件不移动、不被默认值覆盖 | 临时文件与受控占用 |
| REG-220-07 | 无法删除日志时清理越界 | `LogRetentionServiceTests`：有界遍历，锁定日志保留，其他旧日志按策略处理 | 临时目录 |
| REG-220-08 | Mac 坐标编辑及未校准草稿 | `MacUiRegressionTests`、`MacPlanValidationTests`：相对坐标更新，未完成草稿可恢复 | 假平台无头界面 |
| REG-220-09 | Mac 快捷键顺序与记录位置 | `MacUiRegressionTests`：按键顺序及按键时坐标保持 | 假平台无头界面 |
| REG-220-10 | Mac HUD 关闭影响下一次采点 | `MacUiRegressionTests`：旧窗口关闭不取消新采点状态 | 无头界面 |
| REG-220-11 | Release 重跑误删其他附件 | `Installer.Tests.ps1`：精确同名替换，分页、失败、空目录和其他平台附件保留 | 工作流脚本故障注入契约 |
| UX-220-01 | 演练与单步 | `ExecutionModeTests`、两端界面回归：演练零点击、不改正式断点；单步完成全点后暂停 | 假鼠标 / 无头界面 |
| UX-220-02 | 可选目标窗口绑定 | `WindowsExecutionEnvironmentTests`、`MacUiRegressionTests`：前台身份、遮挡、实际落点及平台不匹配均受控暂停 | 假窗口；原生窗口另验 |
| UX-220-03 | 自定义局部稳定检测区域 | `ProfileValidatorTests`、`ExecutionPlanServiceTests`、`ScreenStabilityDetectorTests`：区域边界校验、持久化、采样范围 | 共享逻辑 / 假采样器 |

### 覆盖维度审计

| 维度 | 适用性、执行层级与缺口 |
| --- | --- |
| 正常主链路、参数契约、数值文本时间 | 适用；方案加载保存、执行计数、时间及坐标边界、Unicode 文件名、缺省字段由现有及新增回归覆盖 |
| 身份与权限 | 应用账号与租户不适用；系统输入权限与窗口身份适用，窗口身份由快照覆盖，macOS 权限实机验收未执行 |
| 查询与分页 | 业务列表分页不适用；Release API 分页适用，由契约注入超过一页附件验证 |
| 存量脏数据、生命周期 | 适用；v4 方案、旧指纹、损坏草稿、设置、孤留副本，以及草稿/运行/暂停/停止/恢复/完成 |
| 事务失败原子性、并发幂等 | 适用；方案原子提交、外部写入冲突、同进程并发保存、交付回滚和附件替换失败；跨进程重命名的严格 CAS 不保证 |
| Redis、MQ、流程引擎 | 不适用；该桌面应用及本次链路没有这些组件 |
| 异步与任务 | 适用；暂停取消、恢复代次、按键队列、HUD 关闭与倒计时；执行使用假输入 |
| 跨项目与外部系统 | 共享引擎两端集成适用；Windows 与 Mac 托管分别构建回归；GitHub 真实发布在最终发布阶段核验 |
| 页面交互 | Windows 布局及隔离 UI、Mac Headless 适用且已执行；真实桌面操作及 Mac 原生窗口未执行 |
| 性能与容量 | 适用；日志有界遍历、保存测试含大量点位；未做压测，没有声明延时或吞吐 SLA |
| 日志可观测性 | 适用；诊断与设置错误、执行日志、锁定日志清理由隔离测试覆盖 |
| 发布与兼容 | 适用；2.2.0 元数据、v4/旧字段、安装器、五文件交付及哈希；签名与 Mac 原生产物未验收 |
| 数据收尾与回滚 | 适用；测试数据仅临时目录，保留源码、截图和报告；文档渲染临时图可重建，交付脚本自带备份回滚 |

### 已执行检查

| 检查项 | 命令 | 实际结果 |
| --- | --- | --- |
| Windows 最终集成 | `pwsh -NoProfile -File scripts/test.ps1 -Configuration Release -NoRestore` | 最终 23/23 测试组通过；最初 23/23，新增故障回归后 20/23；中间修复为 22/23，正式路径、原生缓冲区及长路径问题均修复后复测通过 |
| 安装器与发布契约 | `pwsh -NoProfile -File tests/Installer.Tests.ps1` | 全部通过；覆盖 2.2.0 元数据、同名附件替换、分页、上传失败、查询失败、空目录及其他平台附件保留 |
| Mac 托管构建 | `dotnet build tests/OrderedClicker.Mac.UiTests/OrderedClicker.Mac.UiTests.csproj -c Release --no-restore` | 通过，0 警告、0 错误 |
| Mac 无头界面 | `dotnet --roll-forward Major tests/OrderedClicker.Mac.UiTests/bin/Release/net8.0/OrderedClicker.Mac.UiTests.dll` | 通过：基础导航/保存/外部修改冲突及 9 项专项回归 |
| 共享测试构建及运行 | `dotnet build tests/OrderedClicker.CrossPlatform.Tests/OrderedClicker.CrossPlatform.Tests.csproj -c Release --no-restore`，然后 `dotnet --roll-forward Major .../OrderedClicker.CrossPlatform.Tests.dll` 显式传入除 `MacContracts` 外的全部组名 | 0 警告、0 错误；20/20 测试组通过。未运行的 `MacContracts` 单列为跳过 |
| Windows 截图与布局 | 测试程序 `--render-workspace ModePage/RunPage` | 通过边界及按钮文字断言；已检查两张 2.2.0 截图，禁用文字可读、按钮无溢出 |
| HTML/PDF 说明 | `scripts/guide/build_product_guide.py`、`build_pdf_guide.py`、`verify_guide.py --skip-video` | 校验通过，HTML 内嵌截图，PDF 共 10 页；逐页缩略图及新增功能页、更新截图页已渲染检查 |
| Mac HTML 说明 | `python scripts/build-macos-guide.py` | 已生成 2.2.0 说明 |

NuGet 直连最初因 TLS EOF 失败，使用已有本机代理重试恢复固定的 Avalonia 11.3.7 依赖成功；另一次命令行临时源恢复也成功，没有修改仓库包源或依赖版本。Mac 首次编译发现 `CapturedPoint` 缺少命名空间导入，补齐后编译通过。.NET 8 运行时下载失败，未将失败认定通过。

Windows 静态复审额外发现并修复：移动容差内的实际落点可能命中邻窗、坏草稿连带隔离有效旧断点、RDP 断开事件遗漏。对应回归已通过，复审确认原问题路径已覆盖。

最终保存故障定位及复测：

- `File.Replace` 的路径观察用例实际捕获正式路径短暂不存在，不能仅靠备份恢复认定原子可见。改为 Windows 原生单次重命名，保留旧文件的只读租约、同进程保存锁及哈希冲突校验。
- 首版原生缓冲区缺少 UTF-16 结束符，复现了 Win32 123、意外文件名 `atomic.oclickl`，以及保存后正式文件仍为旧版本。补齐结束符后，旧读句柄/备份/新版本断言通过。
- 新增长中文路径回归，先复现 Win32 206，再为源和目标均使用扩展路径。最终正式路径 32 次连续保存均读到新版本，无多余文件名，长中文路径和旧读句柄测试通过。
- 对原生接口错误 1/50/87 使用普通同目录替换回退，不吞掉权限或其他 IO 错误；本机 NTFS 已验收，真实 FAT/exFAT/U 盘/网络共享回退未执行。
- 保存前的哈希检查不能对任意外部进程的重命名提供严格 CAS；仍建议发生并发编辑时另存方案，不承诺跨进程事务。

本轮自动化统计按测试组汇总：Windows 23/23、共享托管 20/20、Mac 基础界面及 9 项专项回归通过；安装器契约通过。测试组并非端到端业务案例计数。整版验证范围仍不含下列原生及签名项目，不能写作所有平台全面验收通过。

### 未执行与限制

- macOS arm64/x64 原生编译、真实输入、辅助功能/屏幕录制权限、真实窗口命中与 Spaces、签名、公证及 DMG 验收未执行；不发布未经验证的新 Mac DMG。
- 跨平台测试的 `MacContracts` 包含 Darwin `libc` 单实例锁，Windows 无法运行完整组；不把跳过计为通过。
- Windows EXE 与安装器没有配置 Authenticode 代码签名。
- 发布附件先全部上传再逐个替换精确同名项；GitHub API 删除/改名阶段仍不具备整批事务，失败需重跑并核对。

### 清理

保留测试源码、测试入口、故障回归和新版截图。本轮已清理 PDF 检查临时图 `.tmp/guide-2.2.0`，以及主线程创建的两个 `.tmp/save-probe-<guid>` 保存诊断目录：合计 3 个目录、18 个文件、1,730,750 字节。删除前核对绝对路径边界、Git 跟踪状态与重解析点，删除后确认目标不存在。

构建产生的 `bin`、`obj`、本机工具与 `publish` 由 Git 忽略，不混入五文件交付目录；本轮未删除源码或用户方案，未停止其他任务进程。

### 本机交付验收

- 构建命令：`pwsh -NoProfile -File scripts/build-installer.ps1 -Version 2.2.0 -ProductDirectory "D:\专用工具\连点器\交付产品" -SkipTests`；此前完整测试与安装契约已经通过，跳过仅避免重复运行。
- 本机 SDK 10.0.302、自包含运行时 10.0.10，Inno Setup 7.0.2；安装版与免安装版均构建成功，EXE 版本 `2.2.0.0` / `2.2.0`。
- 2026-11-06 12:15 核验交付目录恰好五文件：`ordered-clicker-setup-v2.2.0.exe`、`有序连点器-免安装.exe`、HTML、PDF、`SHA256SUMS.txt`；四项文件哈希均与清单一致，2.1.0 安装版无残留。
- GitHub 提交、标签、CI 和公开 Release 附件校验在下一阶段执行；本机打包成功不代表远端发布成功。

### 远端发布与严格模式回归

- 功能提交 `ec3216b8134275e78bf28c9c7273f1c87d9f83cc` 和 `v2.2.0` 已推送。主分支 CI `42169024215` 通过；标签流程 `42169024265` 的 `verify` 通过，`release` 在打包时的安装器测试阶段失败，附件上传步骤未执行。
- 失败原因为测试脚本使用 `$macPlist.plist.dict` 读取 XML。plist 文档类型与根元素同名，PowerShell XML 属性适配在父脚本严格模式下触发 `dict` 不存在；已用 `Set-StrictMode -Version Latest; & ./tests/Installer.Tests.ps1` 本机复现，不属于应用执行失败。
- `tests/Installer.Tests.ps1` 改用 XPath `/plist/dict/*`，并固定启用严格模式，避免独立运行和打包内运行的验收条件不同。修复后完整打包入口已再次通过 Windows 23/23 与全部安装器契约。
- 发布采用本机完整验证成品补发；功能标签保持原指向。后续提交只包含安装器测试与验收记录，不修改标签对应的产品代码或版本。

### 最终发布验收

2026-11-06 13:44（Asia/Shanghai）完成本机交付与 GitHub 公开附件核对：

- 严格模式修复提交 `f1cd4f62574bb52b20a216f75593425fac3b0128` 的主分支 CI `42169531588` 为 `completed/success`。最终打包使用完整入口 `pwsh -NoProfile -File scripts/build-installer.ps1 -Version 2.2.0 -ProductDirectory "D:\专用工具\连点器\交付产品"`，没有 `-SkipTests`；Windows 23/23 和安装器契约均通过。
- GitHub Release ID `436136986`、标签 `v2.2.0` 已公开，匿名查询 `releases/latest` 返回本版本，`draft=false`、`prerelease=false`，正文与 `docs/release-notes-2.2.0.md` 一致。
- 六个附件为 `ordered-clicker-portable-v2.2.0.exe`、`ordered-clicker-portable-v2.2.0.zip`、`ordered-clicker-setup-v2.2.0.exe`、`ordered-clicker-user-guide-v2.2.0.html`、`ordered-clicker-user-guide-v2.2.0.pdf`、`SHA256SUMS.txt`。上传响应及最终 API 返回的状态、大小、SHA-256 digest 均与本机暂存一致。
- 从公开下载地址匿名下载全部六个附件，逐个 SHA-256 与上传源一致；下载清单中的五项文件哈希全部通过。公开 ZIP 仅包含 `有序连点器-免安装.exe`，内容哈希与交付目录的免安装版一致。
- 交付目录保留五文件布局；安装版、免安装版、HTML、PDF 均与对应公开附件一致。免安装版文件版本为 `2.2.0.0`，产品版本为 `2.2.0`。
- 本次手动上传命令曾因 `@(Invoke-RestMethod .../assets)` 产生嵌套数组而将六个附件误计为一个，停止在公开之前。已复现外层数量为 1、首项类型 `System.Object[]`、展开数量为 6；改用 Release 对象的 `assets` 属性逐项复核后公开，没有重传或删除附件。这是本轮一次性发布命令的问题，未修改产品或已验收的发布脚本。
- 远端标签仍指向功能提交 `ec3216b8134275e78bf28c9c7273f1c87d9f83cc`；`git diff --exit-code v2.2.0 HEAD -- src installer scripts .github README.md` 通过。标签 CI `42169024265` 保持失败记录，本次采用本机完整验证成品补发，没有将失败流程改写为成功，也没有移动标签。

发布后清理 `.tmp/release-v2.2.0`、`.tmp/release-v2.2.0-download-verify` 两个本轮目录和 `.tmp/release-220-ci-first-attempt.log`：合计 13 个文件、291,586,869 字节。删除前确认所有上传及下载进程已结束、目标位于本仓库 `.tmp` 内、不含 Git 跟踪文件或重解析点，删除后确认目标均不存在。保留交付目录、测试源码及本报告，不清理未知临时文件。
