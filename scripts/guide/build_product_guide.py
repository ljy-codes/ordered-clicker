from __future__ import annotations

import base64
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
ARTIFACTS = ROOT / "artifacts"
OUTPUT = ROOT / "操作指导" / "有序连点器-使用说明.html"


def image_data(name: str) -> str:
    path = ARTIFACTS / name
    if not path.is_file():
        raise FileNotFoundError(f"缺少界面截图: {path}")
    return base64.b64encode(path.read_bytes()).decode("ascii")


def main() -> None:
    main_image = image_data("ordered-clicker-run-v2.2.0.png")
    mode_image = image_data("ordered-clicker-mode-v2.2.0.png")
    settings_image = image_data("ordered-clicker-settings-v2.0.0.png")
    help_image = image_data("ordered-clicker-help-v2.0.0.png")
    html = f"""<!doctype html>
<html lang="zh-CN">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>有序连点器 2.2.0 使用说明</title>
  <style>
    :root {{
      color-scheme: dark;
      --bg: #0f131a;
      --surface: #171d27;
      --surface-2: #202838;
      --text: #eef4ff;
      --muted: #a9b6c9;
      --line: #344158;
      --blue: #4d86ff;
      --cyan: #19c7d9;
      --green: #24d6a3;
      --amber: #f3b842;
      --red: #ff5b6e;
    }}
    * {{ box-sizing: border-box; }}
    body {{
      margin: 0;
      background: var(--bg);
      color: var(--text);
      font: 16px/1.75 "Microsoft YaHei", "Segoe UI", sans-serif;
    }}
    header, main, footer {{ width: min(1120px, calc(100% - 32px)); margin: 0 auto; }}
    header {{ padding: 48px 0 24px; }}
    h1 {{ margin: 0; font-size: clamp(30px, 5vw, 52px); letter-spacing: 0; }}
    h2 {{ margin: 0 0 12px; font-size: 25px; letter-spacing: 0; }}
    h3 {{ margin: 18px 0 8px; font-size: 18px; letter-spacing: 0; color: var(--cyan); }}
    p, ul, ol {{ margin: 8px 0 0; }}
    code {{ color: var(--green); }}
    .subtitle {{ color: var(--muted); max-width: 850px; }}
    .meta {{ display: flex; gap: 18px; flex-wrap: wrap; margin-top: 18px; color: var(--muted); }}
    .hero-image, .screen {{
      width: 100%;
      display: block;
      border: 1px solid var(--line);
      margin-top: 24px;
    }}
    section {{ padding: 34px 0; border-top: 1px solid var(--line); }}
    .steps {{ display: grid; grid-template-columns: repeat(5, minmax(0, 1fr)); gap: 10px; margin-top: 18px; }}
    .step {{ background: var(--surface); border: 1px solid var(--line); padding: 14px; min-height: 128px; }}
    .step strong {{ display: block; color: var(--cyan); font-size: 18px; }}
    .grid {{ display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 16px; margin-top: 18px; }}
    .panel {{ background: var(--surface); border: 1px solid var(--line); padding: 18px; }}
    .warning {{ border-left: 4px solid var(--amber); background: var(--surface-2); padding: 14px 16px; margin-top: 18px; }}
    .danger {{ border-left-color: var(--red); }}
    table {{ width: 100%; border-collapse: collapse; margin-top: 14px; }}
    th, td {{ border: 1px solid var(--line); padding: 10px 12px; text-align: left; vertical-align: top; }}
    th {{ background: var(--surface-2); color: var(--cyan); }}
    footer {{ padding: 24px 0 48px; color: var(--muted); border-top: 1px solid var(--line); }}
    @media (max-width: 820px) {{
      .steps, .grid {{ grid-template-columns: 1fr; }}
      header, main, footer {{ width: min(100% - 22px, 1120px); }}
    }}
    @media print {{
      body {{ background: white; color: #111; }}
      .panel, .step, .warning, th {{ background: #f4f6f8; }}
      section, table, th, td, .hero-image, .screen {{ border-color: #bbb; }}
      code, h3, .step strong, th {{ color: #075b68; }}
    }}
  </style>
</head>
<body>
  <header>
    <h1>有序连点器 2.2.0</h1>
    <p class="subtitle">面向普通用户和云桌面长流程的五步式操作说明。新版本使用 v4 <code>.oclick</code> 方案、活动草稿、可靠断点和双重停止保护。</p>
    <div class="meta"><span>适用：Windows 10/11 x64</span><span>更新：2026-11-06</span><span>默认快捷键：F6 / F7 / F8</span></div>
    <img class="hero-image" alt="有序连点器 2.2.0 运行页" src="data:image/png;base64,{main_image}">
  </header>
  <main>
    <section>
      <h2>一分钟操作流程</h2>
      <div class="steps">
        <div class="step"><strong>1 模式</strong>普通桌面直接使用；云桌面先选择增强模式。</div>
        <div class="step"><strong>2 方案</strong>新建或打开 <code>.oclick</code>，设置循环和时间。</div>
        <div class="step"><strong>3 采点</strong>按 F6 或使用“2 秒后记录”，按顺序添加点位。</div>
        <div class="step"><strong>4 检查</strong>核对点击数、预计耗时、显示器和安全停止。</div>
        <div class="step"><strong>5 运行</strong>确认后倒计时开始，F7 暂停/继续，F8 停止。</div>
      </div>
      <div class="warning danger"><strong>首次使用：</strong>先演练核对落点，再只设置 1 轮，在无支付、删除或提交风险的页面试运行。正式执行和单步会真实点击鼠标。</div>
    </section>

    <section>
      <h2>方案与草稿</h2>
      <div class="grid">
        <div class="panel">
          <h3>v4 方案</h3>
          <ul>
            <li>新方案扩展名为 <code>.oclick</code>。</li>
            <li>2.2.0 保持 v4，兼容 2.0.0 / 2.1.0 的 v4 方案；新增设置缺省时沿用旧行为。</li>
            <li>“保存”覆盖当前绑定文件；“另存为”创建新的方案身份。</li>
            <li>“打开方案”用于任意位置的 v4 文件。</li>
          </ul>
        </div>
        <div class="panel">
          <h3>旧方案迁移</h3>
          <ul>
            <li>v1-v3 JSON 只能通过“迁移旧方案”读取。</li>
            <li>迁移结果是未保存的新方案，来源文件不修改。</li>
            <li>迁移警告需要逐项核对后再保存。</li>
          </ul>
        </div>
        <div class="panel">
          <h3>切换保护</h3>
          <p>当前方案有修改时，切换本机方案会显示“保存 / 不保存 / 取消”，避免误覆盖或丢失。</p>
        </div>
        <div class="panel">
          <h3>活动草稿</h3>
          <p>程序自动保存活动草稿。异常退出后下次启动可恢复；正常关闭且没有未保存修改时自动清理。</p>
        </div>
      </div>
    </section>

    <section>
      <h2>采点与时间</h2>
      <ol>
        <li>进入“采点”页并开启采点模式。</li>
        <li>将鼠标移到目标位置按 F6；无法使用全局热键时，点击“2 秒后记录”。</li>
        <li>附近 5 像素已有点位时，确认是否仍要添加。</li>
        <li>使用上移、下移调整顺序；删除、清空和新增可撤销，保留最近 10 次。</li>
      </ol>
      <table>
        <tr><th>参数</th><th>含义</th></tr>
        <tr><td>点击次数</td><td>当前点位连续点击多少次。</td></tr>
        <tr><td>同点连击间隔</td><td>同一位置多次点击之间的等待时间。</td></tr>
        <tr><td>步骤完成后等待</td><td>当前点位全部点击完成后，到下一点位前的等待。</td></tr>
        <tr><td>轮间等待</td><td>一整轮结束后，到下一轮开始前的等待。</td></tr>
      </table>
    </section>

    <section>
      <h2>执行检查与安全停止</h2>
      <div class="grid">
        <div class="panel"><h3>确认计划</h3><p>检查总行数、启用行、禁用行、循环数、计划点击数、基础耗时和最大耗时。确认后的配置发生变化时，旧计划自动失效。</p></div>
        <div class="panel"><h3>双重停止</h3><p>停止快捷键和安全角同时作为保护。安全角可选择四个屏幕角、范围和连续停留时间。</p></div>
        <div class="panel"><h3>重叠检查</h3><p>任何启用点位落在安全角内都会阻止执行，必须移动点位或更换安全角。</p></div>
        <div class="panel"><h3>置顶状态</h3><p>运行时状态小窗保持置顶，显示当前阶段，并提供暂停/继续和停止按钮。</p></div>
      </div>
      <img class="screen" alt="设置和安全角（历史界面示例）" src="data:image/png;base64,{settings_image}">
      <p class="subtitle">此设置页截图为历史界面示例，实际操作以当前界面为准。</p>
    </section>

    <section>
      <h2>演练、单步与窗口绑定</h2>
      <h3>演练：只移动，不点击</h3>
      <p>按启用点位顺序移动鼠标，实际点击数为 0，不改变正式执行断点。先核对落点是否落在正确控件上，再切换为正式执行。鼠标移动仍可能触发悬停效果；F8 和安全角仍可停止。</p>
      <h3>单步：完整一个点后暂停</h3>
      <p>单步执行当前点的全部连击、点击间隔、点后等待和已启用的稳定检查，然后在下一点前暂停。它不是只点击一次；开始前确认当前点的点击次数和页面状态，继续下一步会产生真实点击。</p>
      <h3>可选窗口绑定</h3>
      <ol>
        <li>在当前平台为方案绑定目标窗口；不绑定时仍按屏幕坐标执行。</li>
        <li>启动或继续时，需切到目标窗口，保持窗口可见且布局与采点时一致。</li>
        <li>窗口不匹配或落点被遮挡时暂停。移开遮挡、切回目标窗口，确认页面后再继续。</li>
      </ol>
      <p>绑定不会自动识别按钮，也不向后台窗口发送点击。跨平台打开方案后需重新绑定；旧版程序不能提供 2.2.0 的窗口保护。</p>
    </section>

    <section>
      <h2>云桌面与画面稳定</h2>
      <ol>
        <li>启用云桌面增强，点击校准区域。</li>
        <li>依次记录远程画面的左上角和右下角。</li>
        <li>校准完成后再采点，点位会保存相对坐标。</li>
        <li>窗口移动或大小变化后重新校准；有效相对坐标会保留，只补齐缺失值。</li>
      </ol>
      <p>开启画面稳定检测后，程序使用低分辨率 RGB 采样判断变化。超过超时时间仍不稳定会暂停，确认页面可操作后按 F7 继续。</p>
      <h3>自定义检测区域</h3>
      <p>框选需要等待稳定的页面内容，避开视频、动画和时钟。未设置时沿用云桌面校准区域或虚拟桌面范围，兼容旧方案。检测区域仅用于画面采样，不改变点击坐标。窗口移动或显示器变化后重新检查区域；持续超时时，确认页面可操作后再调整变化容差、稳定时长或超时。</p>
      <img class="screen" alt="有序连点器 2.2.0 模式页：窗口绑定与检测区域" src="data:image/png;base64,{mode_image}">
    </section>

    <section>
      <h2>暂停、停止与断点</h2>
      <ul>
        <li>F7 在运行和暂停之间切换，等待和稳定检测同样可暂停。</li>
        <li>F8、主窗口停止按钮、安全角或置顶小窗停止按钮都走同一停止流程。</li>
        <li>已完成的点击不会因继续执行而重复；未完成的等待或稳定检查会保守重做。</li>
        <li>执行配置变化后，旧断点失效并要求重新确认计划。</li>
      </ul>
    </section>

    <section>
      <h2>数据目录与诊断</h2>
      <table>
        <tr><th>目录</th><th>内容</th></tr>
        <tr><td><code>profiles</code></td><td>本机 v4 方案。</td></tr>
        <tr><td><code>drafts</code></td><td>活动草稿和恢复数据。</td></tr>
        <tr><td><code>logs</code></td><td>每次执行的计划数、完成数、结果和断点。</td></tr>
        <tr><td><code>diagnostics</code></td><td>设置、方案和运行异常信息。</td></tr>
      </table>
      <p>安装版根目录是 <code>%LocalAppData%\\OrderedClicker</code>。免安装版根目录是程序旁的 <code>OrderedClickerData</code>；目录不可写时会明确报错，不会偷偷回退。</p>
      <img class="screen" alt="程序内使用说明（历史界面示例）" src="data:image/png;base64,{help_image}">
      <p class="subtitle">此帮助页截图为历史界面示例，2.2.0 功能以本说明和当前界面为准。</p>
    </section>

    <section>
      <h2>交付文件与校验</h2>
      <pre><code>ordered-clicker-setup-v2.2.0.exe
有序连点器-免安装.exe
有序连点器-使用说明.pdf
有序连点器-使用说明.html
SHA256SUMS.txt</code></pre>
      <p>使用 <code>SHA256SUMS.txt</code> 核对安装包、免安装程序和两份说明文件。正式 Release 构建还会验证应用和安装器的 Authenticode 签名及时间戳。</p>
    </section>
  </main>
  <footer>有序连点器 2.2.0 · 使用说明 · 2026-11-06</footer>
</body>
</html>
"""
    OUTPUT.parent.mkdir(parents=True, exist_ok=True)
    OUTPUT.write_text(html, encoding="utf-8")
    print(f"Generated: {OUTPUT}")
    print(f"Size: {OUTPUT.stat().st_size} bytes")


if __name__ == "__main__":
    main()
