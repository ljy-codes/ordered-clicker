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
