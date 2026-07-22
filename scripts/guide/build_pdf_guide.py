from __future__ import annotations

from html import escape
from pathlib import Path

from reportlab.lib import colors
from reportlab.lib.enums import TA_CENTER, TA_LEFT
from reportlab.lib.pagesizes import A4
from reportlab.lib.styles import ParagraphStyle
from reportlab.lib.units import mm
from reportlab.pdfbase import pdfmetrics
from reportlab.pdfbase.ttfonts import TTFont
from reportlab.platypus import (
    BaseDocTemplate,
    Frame,
    Image,
    PageBreak,
    PageTemplate,
    Paragraph,
    Spacer,
    Table,
    TableStyle,
)


ROOT = Path(__file__).resolve().parents[2]
GUIDE_DIR = ROOT / "操作指导"
SCREENSHOT_DIR = GUIDE_DIR / "assets" / "screenshots"
OUTPUT_PDF = GUIDE_DIR / "有序连点器-使用说明.pdf"

PAGE_BG = colors.HexColor("#0f131a")
SURFACE = colors.HexColor("#171d27")
SURFACE_ALT = colors.HexColor("#202838")
TEXT = colors.HexColor("#eef4ff")
MUTED = colors.HexColor("#a9b6c9")
PRIMARY = colors.HexColor("#4d86ff")
CYAN = colors.HexColor("#19c7d9")
GREEN = colors.HexColor("#24d6a3")
AMBER = colors.HexColor("#f3b842")
RED = colors.HexColor("#ff5b6e")
BORDER = colors.HexColor("#344158")


def register_fonts() -> None:
    font_path = Path("C:/Windows/Fonts/simhei.ttf")
    if not font_path.is_file():
        raise FileNotFoundError(f"未找到中文字体: {font_path}")
    pdfmetrics.registerFont(TTFont("GuideCN", str(font_path)))


def styles() -> dict[str, ParagraphStyle]:
    return {
        "cover_title": ParagraphStyle(
            "CoverTitle",
            fontName="GuideCN",
            fontSize=28,
            leading=36,
            textColor=TEXT,
            alignment=TA_LEFT,
            spaceAfter=8,
        ),
        "cover_subtitle": ParagraphStyle(
            "CoverSubtitle",
            fontName="GuideCN",
            fontSize=12,
            leading=20,
            textColor=MUTED,
            alignment=TA_LEFT,
            spaceAfter=16,
        ),
        "section": ParagraphStyle(
            "Section",
            fontName="GuideCN",
            fontSize=20,
            leading=28,
            textColor=TEXT,
            spaceAfter=8,
        ),
        "section_intro": ParagraphStyle(
            "SectionIntro",
            fontName="GuideCN",
            fontSize=10,
            leading=17,
            textColor=MUTED,
            spaceAfter=14,
        ),
        "heading": ParagraphStyle(
            "Heading",
            fontName="GuideCN",
            fontSize=13,
            leading=20,
            textColor=CYAN,
            spaceBefore=8,
            spaceAfter=6,
        ),
        "body": ParagraphStyle(
            "Body",
            fontName="GuideCN",
            fontSize=9.5,
            leading=16,
            textColor=TEXT,
            spaceAfter=6,
        ),
        "small": ParagraphStyle(
            "Small",
            fontName="GuideCN",
            fontSize=8.2,
            leading=13,
            textColor=MUTED,
        ),
        "bullet": ParagraphStyle(
            "Bullet",
            fontName="GuideCN",
            fontSize=9.2,
            leading=15,
            textColor=TEXT,
            leftIndent=12,
            firstLineIndent=-8,
            bulletIndent=2,
            spaceAfter=4,
        ),
        "callout": ParagraphStyle(
            "Callout",
            fontName="GuideCN",
            fontSize=9.4,
            leading=16,
            textColor=TEXT,
            borderColor=BORDER,
            borderWidth=0.8,
            borderPadding=9,
            backColor=SURFACE_ALT,
            spaceBefore=8,
            spaceAfter=8,
        ),
        "center": ParagraphStyle(
            "Center",
            fontName="GuideCN",
            fontSize=10,
            leading=16,
            textColor=TEXT,
            alignment=TA_CENTER,
        ),
    }


def paragraph(text: str, style: ParagraphStyle) -> Paragraph:
    return Paragraph(escape(text).replace("\n", "<br/>"), style)


def rich(text: str, style: ParagraphStyle) -> Paragraph:
    return Paragraph(text, style)


def section_header(
    story: list,
    number: str,
    title: str,
    intro: str,
    style_map: dict[str, ParagraphStyle],
) -> None:
    story.append(
        rich(
            f'<font color="#19c7d9">{number}</font>&nbsp;&nbsp;{escape(title)}',
            style_map["section"],
        )
    )
    story.append(paragraph(intro, style_map["section_intro"]))


def heading(story: list, text: str, style_map: dict[str, ParagraphStyle]) -> None:
    story.append(paragraph(text, style_map["heading"]))


def bullets(
    story: list,
    items: list[str],
    style_map: dict[str, ParagraphStyle],
) -> None:
    for item in items:
        story.append(rich(f'<font color="#24d6a3">●</font> {escape(item)}', style_map["bullet"]))


def callout(
    story: list,
    label: str,
    text: str,
    style_map: dict[str, ParagraphStyle],
    color: str = "#f3b842",
) -> None:
    story.append(
        rich(
            f'<font color="{color}">{escape(label)}</font> {escape(text)}',
            style_map["callout"],
        )
    )


def guide_table(
    rows: list[list[str]],
    widths: list[float],
    style_map: dict[str, ParagraphStyle],
) -> Table:
    data = [
        [rich(f"<b>{escape(cell)}</b>", style_map["small"]) for cell in rows[0]]
    ]
    for row in rows[1:]:
        data.append([paragraph(cell, style_map["small"]) for cell in row])

    table = Table(data, colWidths=widths, repeatRows=1, hAlign="LEFT")
    table.setStyle(
        TableStyle(
            [
                ("BACKGROUND", (0, 0), (-1, 0), SURFACE_ALT),
                ("BACKGROUND", (0, 1), (-1, -1), SURFACE),
                ("TEXTCOLOR", (0, 0), (-1, -1), TEXT),
                ("GRID", (0, 0), (-1, -1), 0.5, BORDER),
                ("VALIGN", (0, 0), (-1, -1), "TOP"),
                ("LEFTPADDING", (0, 0), (-1, -1), 7),
                ("RIGHTPADDING", (0, 0), (-1, -1), 7),
                ("TOPPADDING", (0, 0), (-1, -1), 6),
                ("BOTTOMPADDING", (0, 0), (-1, -1), 6),
            ]
        )
    )
    return table


def screenshot(path: Path, width: float, max_height: float) -> Image:
    image = Image(str(path))
    ratio = min(width / image.imageWidth, max_height / image.imageHeight)
    image.drawWidth = image.imageWidth * ratio
    image.drawHeight = image.imageHeight * ratio
    image.hAlign = "CENTER"
    return image


def draw_page(canvas, doc) -> None:
    width, height = A4
    canvas.saveState()
    canvas.setFillColor(PAGE_BG)
    canvas.rect(0, 0, width, height, stroke=0, fill=1)
    canvas.setStrokeColor(BORDER)
    canvas.line(18 * mm, 15 * mm, width - 18 * mm, 15 * mm)
    canvas.setFont("GuideCN", 7.5)
    canvas.setFillColor(MUTED)
    canvas.drawString(18 * mm, 9.5 * mm, "有序连点器 1.1.0 · 零基础使用说明")
    canvas.drawRightString(width - 18 * mm, 9.5 * mm, f"第 {doc.page} 页")
    canvas.restoreState()


def build_story(style_map: dict[str, ParagraphStyle]) -> list:
    story: list = []

    story.append(Spacer(1, 13 * mm))
    story.append(rich('<font color="#19c7d9">ORDERED CLICKER</font>', style_map["heading"]))
    story.append(paragraph("有序连点器", style_map["cover_title"]))
    story.append(
        paragraph(
            "Windows 10/11 x64 免费开源工具 · 无授权码 · 无联网验证",
            style_map["cover_subtitle"],
        )
    )
    story.append(
        screenshot(
            SCREENSHOT_DIR / "主界面-极光科技.png",
            170 * mm,
            102 * mm,
        )
    )
    story.append(Spacer(1, 7 * mm))
    story.append(
        guide_table(
            [
                ["适用用户", "核心能力", "文档版本"],
                ["零基础普通用户", "多点有序点击、批量时间、单点例外、循环执行", "1.1 · 2026-07-22"],
            ],
            [42 * mm, 88 * mm, 42 * mm],
            style_map,
        )
    )
    callout(
        story,
        "安全提醒：",
        "自动点击会真实控制鼠标。首次使用只设置 1 轮，并在安全空白页面测试；任何时候按 F10 停止。",
        style_map,
        "#ff5b6e",
    )

    story.append(PageBreak())
    section_header(story, "00", "一分钟操作流程", "先掌握完整流程，再进入逐项说明。", style_map)
    story.append(
        guide_table(
            [
                ["步骤", "操作", "结果"],
                ["1", "打开采点模式，将鼠标移到目标位置后按 F8。", "按采集顺序新增点位。"],
                ["2", "在“点位时间”栏填写时间，并点击对应“应用全部”。", "所有点统一获得该列时间。"],
                ["3", "直接修改特殊点位所在行。", "该行成为例外，不影响其他点。"],
                ["4", "设置总循环次数和轮间等待。", "整组点位按顺序重复执行。"],
                ["5", "按 F9 开始；按 F9 暂停/继续；按 F10 停止。", "随时控制任务。"],
                ["6", "确认无误后保存方案。", "下次可直接加载复用。"],
            ],
            [16 * mm, 100 * mm, 56 * mm],
            style_map,
        )
    )
    heading(story, "三个必须记住的快捷键", style_map)
    story.append(
        guide_table(
            [
                ["快捷键", "作用", "什么时候用"],
                ["F8", "采集当前鼠标位置", "只在采点模式开启时"],
                ["F9", "开始、暂停、继续", "配置完成后或运行过程中"],
                ["F10", "立即停止", "发现误点风险或窗口变化时"],
            ],
            [26 * mm, 58 * mm, 88 * mm],
            style_map,
        )
    )

    story.append(PageBreak())
    section_header(story, "01", "产品概览", "一个方案就是一张可保存、可重复执行的点击清单。", style_map)
    bullets(
        story,
        [
            "可按顺序采集多个屏幕点位，并通过上移、下移调整执行顺序。",
            "每个点可以独立设置点击次数、点击间隔和点后等待。",
            "点击间隔和点后等待支持分别批量应用到全部点位。",
            "批量设置后仍可修改单行，形成只对该点生效的例外。",
            "新采集点自动继承当前全局点击间隔和点后等待。",
            "全部启用点执行完一遍算一轮，可设置总循环次数和轮间等待。",
            "支持五套主题、方案保存、DPI 缩放与多显示器环境检查。",
        ],
        style_map,
    )
    callout(story, "免费说明：", "公开版本不需要授权码，不要求首次联网，也没有设备数量限制。", style_map, "#24d6a3")

    story.append(PageBreak())
    section_header(story, "02", "使用前准备", "采点前先固定窗口和显示环境，减少坐标失效风险。", style_map)
    heading(story, "系统与权限", style_map)
    bullets(
        story,
        [
            "使用 Windows 10 或 Windows 11 x64。",
            "普通权限的连点器不能可靠控制以管理员身份运行的目标程序。",
            "如目标程序以管理员身份运行，请关闭连点器后也以管理员身份启动。",
            "暂时关闭可能占用 F8、F9、F10 的软件。",
        ],
        style_map,
    )
    heading(story, "显示与窗口", style_map)
    bullets(
        story,
        [
            "采点前固定目标窗口的位置、大小和页面内容。",
            "确认显示器分辨率、排列、主副屏关系和缩放比例不再变化。",
            "执行期间避免通知、弹窗或其他窗口遮挡目标位置。",
            "首次只在安全空白页面或可撤销场景测试。",
        ],
        style_map,
    )

    story.append(PageBreak())
    section_header(story, "03", "界面认识", "主界面按方案、时间、点位、操作和执行分区。", style_map)
    story.append(
        screenshot(
            SCREENSHOT_DIR / "主界面-极光科技.png",
            172 * mm,
            98 * mm,
        )
    )
    story.append(Spacer(1, 5 * mm))
    story.append(
        guide_table(
            [
                ["区域", "作用"],
                ["方案区", "设置方案名称、总循环次数和轮间等待，并保存或加载。"],
                ["点位时间", "设置全局点击间隔、点后等待，并通过“应用全部”批量更新。"],
                ["点位表", "查看启用状态、顺序、坐标、点击次数、时间和显示器。"],
                ["点位操作栏", "采点、上移、下移、删除、清空、主题设置和使用说明。"],
                ["执行区", "开始、暂停、继续和停止，并查看运行状态。"],
            ],
            [38 * mm, 134 * mm],
            style_map,
        )
    )

    story.append(PageBreak())
    section_header(story, "04", "快速入门：采集点位", "以下示例采集三个点，并按蓝、青、紫的顺序执行。", style_map)
    heading(story, "步骤 1：建立安全测试环境", style_map)
    bullets(
        story,
        [
            "打开连点器和一个安全的空白测试页面。",
            "将总循环次数暂时设置为 1。",
            "确认目标窗口不会自动移动或改变布局。",
        ],
        style_map,
    )
    heading(story, "步骤 2：进入采点模式", style_map)
    bullets(
        story,
        [
            "点击“采点模式 (F8)”，按钮会变成“结束采点”。",
            "把鼠标移到第一个目标位置中心，按一次 F8。",
            "依次移动到第二、第三个位置，每个位置按一次 F8。",
            "点位按采集顺序出现在表格中。",
        ],
        style_map,
    )
    heading(story, "步骤 3：结束并检查", style_map)
    bullets(
        story,
        [
            "点击“结束采点”，防止后续误按 F8 添加多余点位。",
            "检查表格是否有三行，以及坐标和显示器信息是否完整。",
            "顺序错误时，选中一行后使用“上移”或“下移”。",
        ],
        style_map,
    )

    story.append(PageBreak())
    section_header(story, "04", "快速入门：设置时间与执行", "先批量设置常用值，再为少数特殊点位单独修改。", style_map)
    heading(story, "步骤 4：批量设置两列时间", style_map)
    bullets(
        story,
        [
            "在全局“点击间隔(ms)”输入 500，点击旁边的“应用全部”。",
            "在全局“点后等待(ms)”输入 800，点击旁边的“应用全部”。",
            "此时全部点位的点击间隔为 500，点后等待为 800。",
        ],
        style_map,
    )
    heading(story, "步骤 5：设置单点例外", style_map)
    bullets(
        story,
        [
            "蓝色点：点击次数改为 2，时间保持 500/800。",
            "青色点：点击次数为 1，点后等待单独改为 600。",
            "紫色点：点击次数改为 3，点击间隔改为 300，点后等待改为 1000。",
            "单行修改不会反向改变全局输入框，也不会影响其他点。",
        ],
        style_map,
    )
    heading(story, "步骤 6：执行与核对", style_map)
    bullets(
        story,
        [
            "总循环次数先填 1，确认无误后可改为 2；轮间等待示例填 1500。",
            "按 F9 后有 3 秒倒计时，利用这段时间切换到目标窗口。",
            "运行中按 F9 暂停或继续，按 F10 随时停止。",
            "两轮结束后，三个点的总点击次数应分别为 4、2、6。",
        ],
        style_map,
    )

    story.append(PageBreak())
    section_header(story, "05", "参数详细解释", "时间单位统一为毫秒，1000 毫秒等于 1 秒。", style_map)
    story.append(
        guide_table(
            [
                ["参数", "作用", "建议"],
                ["点击次数", "当前点位每轮连续点击多少次。", "从 1 开始测试。"],
                ["点击间隔", "同一点位多次点击之间的等待。", "最小 10 ms，建议先用 500 ms。"],
                ["点后等待", "当前点完成后，到下一个点开始前的等待。", "可设 0，建议先用 500-1000 ms。"],
                ["总循环次数", "全部启用点执行完一遍算一轮。", "首次只设 1。"],
                ["轮间等待", "一轮结束到下一轮开始的等待。", "页面刷新慢时适当增加。"],
                ["启用", "取消勾选后跳过该点，但保留配置。", "用于临时测试部分流程。"],
            ],
            [34 * mm, 90 * mm, 48 * mm],
            style_map,
        )
    )
    callout(
        story,
        "区别：",
        "点后等待发生在相邻点位之间；轮间等待只发生在整轮之间，最后一轮结束后不再等待下一轮。",
        style_map,
    )

    story.append(PageBreak())
    section_header(story, "05", "全局时间与单点例外", "新版本的核心操作是“批量统一，再按行覆盖”。", style_map)
    heading(story, "应用全部", style_map)
    bullets(
        story,
        [
            "点击间隔和点后等待各有独立的“应用全部”按钮。",
            "点击哪个按钮，只批量修改对应的那一列。",
            "例如点击间隔设为 1000 并应用全部，不会覆盖每个点的点后等待。",
            "没有点位时也可以先设置全局值，随后采集的新点会继承这些值。",
        ],
        style_map,
    )
    heading(story, "单行覆盖", style_map)
    bullets(
        story,
        [
            "批量设置后，直接点击表格中的某个时间单元格输入例外值。",
            "例如全部点击间隔为 1000，再把第二行改为 200，只有第二行使用 200。",
            "单行覆盖不会修改全局默认值；之后采集的新点仍继承全局值。",
        ],
        style_map,
    )
    callout(
        story,
        "推荐顺序：",
        "先设置全局时间，再采点；采点完成后批量确认一次，最后只修改少量特殊点。",
        style_map,
        "#24d6a3",
    )

    story.append(PageBreak())
    section_header(story, "06", "开始、暂停与停止", "快捷键为全局热键，连点器不在前台时也可以使用。", style_map)
    story.append(
        guide_table(
            [
                ["状态", "操作", "程序行为"],
                ["空闲", "按 F9 或点击开始", "进入 3 秒倒计时，然后执行。"],
                ["运行中", "按 F9", "暂停当前倒计时、等待或点位流程。"],
                ["已暂停", "再次按 F9", "从暂停位置继续。"],
                ["任意执行状态", "按 F10", "终止任务并回到就绪。"],
            ],
            [34 * mm, 52 * mm, 86 * mm],
            style_map,
        )
    )
    callout(
        story,
        "立即停止：",
        "发现目标窗口移动、弹窗遮挡、页面内容变化或误点风险时，优先按 F10。",
        style_map,
        "#ff5b6e",
    )

    story.append(PageBreak())
    section_header(story, "07", "保存与加载方案", "方案文件保存点位、单点参数和全局时间默认值。", style_map)
    heading(story, "保存内容", style_map)
    bullets(
        story,
        [
            "方案名称、总循环次数和轮间等待。",
            "每个点的启用状态、顺序、坐标、点击次数、点击间隔和点后等待。",
            "全局点击间隔和全局点后等待默认值。",
            "采点时的显示器信息，用于开始前检测环境变化。",
        ],
        style_map,
    )
    heading(story, "加载后的检查", style_map)
    bullets(
        story,
        [
            "先确认点位数量、顺序、启用状态和时间参数。",
            "显示器环境没有变化时，仍建议先执行一轮测试。",
            "显示器排列、分辨率、缩放或目标窗口变化后，应重新采点。",
            "不要手工删除 JSON 字段或加入注释，以免文件损坏。",
        ],
        style_map,
    )

    story.append(PageBreak())
    section_header(story, "08", "主题与内置帮助", "主题只改变颜色，不改变点位和执行行为。", style_map)
    story.append(
        screenshot(
            SCREENSHOT_DIR / "主题设置.png",
            118 * mm,
            72 * mm,
        )
    )
    story.append(Spacer(1, 4 * mm))
    bullets(
        story,
        [
            "内置主题：极光科技、经典深色、海洋蓝、翡翠绿、明亮模式。",
            "点击主题卡片立即预览；点击保存后下次启动自动恢复。",
            "点击取消、关闭弹窗或按 Esc，会恢复打开设置前的主题。",
            "主界面的“? 使用说明”可随时打开内置帮助。",
        ],
        style_map,
    )
    story.append(Spacer(1, 4 * mm))
    story.append(
        screenshot(
            SCREENSHOT_DIR / "使用说明.png",
            112 * mm,
            76 * mm,
        )
    )

    story.append(PageBreak())
    section_header(story, "09", "DPI、屏幕缩放与多显示器", "程序使用物理屏幕坐标，并启用 Per-Monitor V2 DPI 感知。", style_map)
    heading(story, "支持的情况", style_map)
    bullets(
        story,
        [
            "支持 100%、125%、150% 等 Windows 屏幕缩放比例。",
            "支持副屏位于主屏左侧或上方，因此坐标可能为负数。",
            "记录采点时显示器的位置、分辨率和缩放信息。",
        ],
        style_map,
    )
    heading(story, "必须重新采点的变化", style_map)
    bullets(
        story,
        [
            "改变显示器分辨率、缩放比例、排列位置或主显示器。",
            "拔插显示器、切换远程桌面或改变目标程序窗口位置和大小。",
            "目标程序升级后按钮位置发生变化。",
        ],
        style_map,
    )
    callout(
        story,
        "为什么不自动换算：",
        "绝对坐标自动缩放可能静默点击错误位置。程序选择提示重新采点，优先保证可控性。",
        style_map,
    )

    story.append(PageBreak())
    section_header(story, "10", "常见问题", "出现问题时先停止任务，再按现象逐项排查。", style_map)
    story.append(
        guide_table(
            [
                ["现象", "处理方法"],
                ["F8/F9/F10 无反应", "检查是否被其他程序占用；按钮操作仍可使用。"],
                ["点击位置偏移", "检查窗口、分辨率、缩放和显示器排列，变化后重新采点。"],
                ["无法点击管理员程序", "让连点器与目标程序使用相同权限。"],
                ["时间输入后恢复", "点击间隔至少 10 ms；输入后点击其他单元格完成编辑。"],
                ["应用全部后特殊值消失", "这是正常批量覆盖；请在批量应用后再设置单行例外。"],
                ["新点时间不符合预期", "检查“点位时间”栏；新采集点继承当前全局值。"],
                ["加载方案失败", "选择软件保存的 JSON 文件，不要手工破坏格式。"],
                ["安全软件不响应", "不要绕过安全限制，改用目标程序允许的方式。"],
            ],
            [53 * mm, 119 * mm],
            style_map,
        )
    )

    story.append(PageBreak())
    section_header(story, "11", "安全建议", "自动点击只适合可逆、低风险、位置固定的重复操作。", style_map)
    callout(
        story,
        "禁止场景：",
        "不要用于支付、转账、删除数据、不可撤销提交、自动发布内容或其他需要人工确认的高风险操作。",
        style_map,
        "#ff5b6e",
    )
    bullets(
        story,
        [
            "正式使用前先执行一轮，核对坐标、顺序、次数和等待时间。",
            "执行期间不要主动移动鼠标，不要拖动目标窗口。",
            "始终确保 F10 可用，并定期观察目标页面状态。",
            "页面出现弹窗、加载异常或内容变化时立即停止。",
            "环境发生变化后重新采点，不继续依赖旧坐标。",
        ],
        style_map,
    )

    story.append(PageBreak())
    section_header(story, "12", "执行检查清单", "每次正式运行前后，用这一页快速核对。", style_map)
    heading(story, "执行前", style_map)
    bullets(
        story,
        [
            "目标窗口的位置、大小和内容没有变化。",
            "显示器分辨率、排列和缩放比例没有变化。",
            "点位启用状态和执行顺序正确。",
            "已先应用全局时间，再确认单行例外。",
            "点击次数、点击间隔、点后等待和循环次数已核对。",
            "首次或环境变化后只运行一轮。",
            "确认 F10 可以立即停止。",
        ],
        style_map,
    )
    heading(story, "执行后", style_map)
    bullets(
        story,
        [
            "状态栏显示任务完成或已经停止。",
            "目标结果与预期点击次数一致。",
            "没有多余点击、漏点或被弹窗遮挡。",
            "需要复用时保存当前方案。",
        ],
        style_map,
    )
    callout(
        story,
        "完成标准：",
        "示例执行两轮后，三个点总点击次数应为 4、2、6；结果一致说明顺序、次数和循环配置正确。",
        style_map,
        "#24d6a3",
    )

    story.append(PageBreak())
    section_header(story, "附录", "交付文件与版本信息", "本页用于确认你拿到的是完整公开版本。", style_map)
    story.append(
        guide_table(
            [
                ["文件", "用途"],
                ["ordered-clicker-setup-v1.1.0.exe", "Windows 安装版。"],
                ["ordered-clicker-portable-v1.1.0.zip", "免安装便携版。"],
                ["有序连点器-使用说明.pdf", "当前离线说明书。"],
                ["有序连点器-使用说明.html", "可搜索、可放大图片的网页说明。"],
                ["有序连点器-视频演示.mp4", "完整语音操作演示，视频内容沿用上一版。"],
                ["SHA256SUMS.txt", "用于核对文件是否完整。"],
            ],
            [76 * mm, 96 * mm],
            style_map,
        )
    )
    callout(
        story,
        "版本说明：",
        "1.1.0 新增点击间隔和点后等待的全局“应用全部”、单行覆盖及新采集点继承。",
        style_map,
        "#19c7d9",
    )

    return story


def main() -> None:
    register_fonts()
    style_map = styles()

    doc = BaseDocTemplate(
        str(OUTPUT_PDF),
        pagesize=A4,
        leftMargin=18 * mm,
        rightMargin=18 * mm,
        topMargin=17 * mm,
        bottomMargin=20 * mm,
        title="有序连点器使用说明",
        author="ljy-codes",
        subject="有序连点器 1.1.0 零基础操作指导",
    )
    frame = Frame(
        doc.leftMargin,
        doc.bottomMargin,
        doc.width,
        doc.height,
        id="content",
        leftPadding=0,
        rightPadding=0,
        topPadding=0,
        bottomPadding=0,
    )
    doc.addPageTemplates([PageTemplate(id="dark", frames=[frame], onPage=draw_page)])
    doc.build(build_story(style_map))
    print(f"Generated: {OUTPUT_PDF}")
    print(f"Size: {OUTPUT_PDF.stat().st_size} bytes")


if __name__ == "__main__":
    main()
