from __future__ import annotations

import base64
import re
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
SOURCE_DIR = ROOT / "操作指导"
SOURCE_HTML = SOURCE_DIR / "有序连点器-操作指导PRD.html"
TARGET_HTML = SOURCE_DIR / "有序连点器-使用说明.html"


def inline_image(match: re.Match[str]) -> str:
    relative_path = match.group(1)
    image_path = SOURCE_DIR / relative_path
    encoded = base64.b64encode(image_path.read_bytes()).decode("ascii")
    return f'src="data:image/png;base64,{encoded}"'


def main() -> None:
    html = SOURCE_HTML.read_text(encoding="utf-8")

    replacements = {
        "有序连点器操作指导 PRD": "有序连点器使用指导",
        "零基础操作指导 PRD": "零基础功能使用指导",
        "操作指导 PRD · 极光科技深色模式": "功能使用指导 · 极光科技深色模式",
        "有序连点器-完整操作教程.mp4": "有序连点器-视频演示.mp4",
        "OrderedClicker.exe": "有序连点器.exe",
        "更新日期：2026-07-22": "更新日期：2026-07-23",
        "Ctrl+Alt+F8": "F6",
        "Ctrl+Alt+F9": "F7",
        "Ctrl+Alt+F10": "F8",
        "恢复默认快捷键": "简洁模式",
        "快捷键必须包含 Ctrl、Alt、Shift 或 Win，不能只使用单个字母或功能键。": "无修饰键仅允许 F6 到 F12；组合键可使用 Ctrl、Alt、Shift 或 Win。",
        "首次只在专用演示靶场测试，不操作支付或删除页面。": "首次只在安全空白测试页面操作，不操作支付或删除页面。",
        "首次测试打开本地演示靶场。": "首次测试先打开一个安全的空白测试页面。",
        "启动软件和演示靶场": "启动软件和测试页面",
        "连点器 EXE 和本指导目录中的演示靶场。": "连点器 EXE 和一个安全的空白测试页面。",
        "演示靶场三个按钮的中心。": "测试页面中三个安全按钮或空白区域的中心。",
        "演示靶场计数和连点器底部状态栏。": "测试页面反馈和连点器底部状态栏。",
        "优先在演示靶场测试": "优先在安全页面测试",
        "不会影响真实业务数据，结果可以直接核对。": "不会影响真实业务数据，便于确认点位和执行顺序。",
        "关闭不再需要的演示靶场和目标窗口。": "关闭不再需要的测试页面和目标窗口。",
        '<a class="quick-link" href="连点演示靶场.html">打开安全演示靶场</a>\n'
        '          <a class="quick-link" href="有序连点器-视频字幕.srt" download>'
        "下载视频字幕</a>": "",
        "在\n          <code>连点演示靶场.html</code> 中验证": "在安全的空白测试页面中验证",
        "先打开连点器，再打开“连点演示靶场.html”。": "先打开连点器，再打开一个安全的空白测试页面。",
    }
    for old, new in replacements.items():
        html = html.replace(old, new)

    cloud_section = """
      <section id="cloud-desktop">
        <div class="section-heading">
          <span class="section-number">10</span>
          <div>
            <h2>云桌面增强与复杂流程</h2>
            <p>用于浏览器内云桌面、页面加载时间不固定和步骤较多的操作。</p>
          </div>
        </div>
        <div class="panel">
          <h3>两点校准云桌面区域</h3>
          <ol>
            <li>勾选“云桌面增强”，点击“校准云桌面区域”。</li>
            <li>将鼠标移到远程画面的左上角，按 F6。</li>
            <li>将鼠标移到远程画面的右下角，再按 F6。</li>
            <li>校准完成后再采点。点位会保存为区域内相对坐标。</li>
          </ol>
          <p>浏览器窗口移动或改变大小后，只需重新校准区域，不必重新采集全部点位。</p>
        </div>
        <div class="panel">
          <h3>长流程不会提前显示完成</h3>
          <ul>
            <li>开始前核对总行数、启用行、禁用行、循环数、计划点次和计划点击数。</li>
            <li>只有实际完成点次和点击数与计划完全一致，状态栏才显示全部完成。</li>
            <li>停止或异常后可从精确断点继续，也可点击“重新开始”。</li>
            <li>执行日志保存在 %LocalAppData%\\OrderedClicker\\logs。</li>
          </ul>
        </div>
        <div class="panel">
          <h3>等待画面稳定</h3>
          <p>勾选“等待画面稳定”后，程序会在每个点完成后检查远程画面。超过设定时间仍在变化时自动暂停；确认页面可操作后按 F7 继续，或按 F8 停止。</p>
        </div>
        <div class="panel">
          <h3>保存、另存为和加载</h3>
          <p>“保存”覆盖当前方案；“另存为”选择新的 JSON 路径；“加载”恢复点位、顺序、时间参数和云桌面设置。</p>
        </div>
      </section>
"""
    html = html.replace(
        '      <section id="troubleshooting">',
        cloud_section + '\n      <section id="troubleshooting">',
        1,
    )

    html = html.replace(
        "grid-template-columns: repeat(3, 1fr);\n      gap: 10px;\n      margin-top: 14px;",
        "grid-template-columns: minmax(260px, 380px);\n"
        "      gap: 10px;\n"
        "      margin-top: 14px;",
    )
    html = re.sub(
        r'src="(assets/screenshots/[^"]+\.png)"',
        inline_image,
        html,
    )

    forbidden = (
        "PRD",
        "演示靶场",
        "连点演示靶场.html",
        "有序连点器-视频字幕.srt",
        "assets/screenshots/",
    )
    leftovers = [item for item in forbidden if item in html]
    if leftovers:
        raise RuntimeError(f"独立使用指导仍含非交付依赖: {', '.join(leftovers)}")

    TARGET_HTML.parent.mkdir(parents=True, exist_ok=True)
    TARGET_HTML.write_text(html, encoding="utf-8")
    print(f"Generated: {TARGET_HTML}")
    print(f"Size: {TARGET_HTML.stat().st_size} bytes")


if __name__ == "__main__":
    main()
