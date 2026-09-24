from __future__ import annotations

from pathlib import Path

from reportlab.lib import colors
from reportlab.lib.enums import TA_CENTER
from reportlab.lib.pagesizes import A4
from reportlab.lib.styles import ParagraphStyle, getSampleStyleSheet
from reportlab.lib.units import mm
from reportlab.pdfbase import pdfmetrics
from reportlab.pdfbase.cidfonts import UnicodeCIDFont
from reportlab.pdfbase.ttfonts import TTFont
from reportlab.platypus import (
    Image,
    PageBreak,
    Paragraph,
    SimpleDocTemplate,
    Spacer,
    Table,
    TableStyle,
)


ROOT = Path(__file__).resolve().parents[2]
ARTIFACTS = ROOT / "artifacts"
OUTPUT = ROOT / "操作指导" / "有序连点器-使用说明.pdf"

BLUE = colors.HexColor("#2968D8")
CYAN = colors.HexColor("#087D8D")
GREEN = colors.HexColor("#087A5B")
AMBER = colors.HexColor("#A86800")
RED = colors.HexColor("#B42336")
TEXT = colors.HexColor("#182230")
MUTED = colors.HexColor("#58677A")
SURFACE = colors.HexColor("#F3F6FA")
LINE = colors.HexColor("#CCD5E0")
GUIDE_FONT_NAME = "GuideCN"


def register_font() -> None:
    global GUIDE_FONT_NAME
    font_path = Path("C:/Windows/Fonts/simhei.ttf")
    if font_path.is_file():
        pdfmetrics.registerFont(TTFont(GUIDE_FONT_NAME, str(font_path)))
        return

    GUIDE_FONT_NAME = "STSong-Light"
    pdfmetrics.registerFont(UnicodeCIDFont(GUIDE_FONT_NAME))


def styles() -> dict[str, ParagraphStyle]:
    base = getSampleStyleSheet()
    return {
        "title": ParagraphStyle(
            "TitleCN",
            parent=base["Title"],
            fontName=GUIDE_FONT_NAME,
            fontSize=28,
            leading=38,
            textColor=TEXT,
            spaceAfter=8,
        ),
        "subtitle": ParagraphStyle(
            "SubtitleCN",
            fontName=GUIDE_FONT_NAME,
            fontSize=11,
            leading=19,
            textColor=MUTED,
            spaceAfter=14,
        ),
        "section": ParagraphStyle(
            "SectionCN",
            fontName=GUIDE_FONT_NAME,
            fontSize=21,
            leading=29,
            textColor=TEXT,
            spaceAfter=10,
        ),
        "heading": ParagraphStyle(
            "HeadingCN",
            fontName=GUIDE_FONT_NAME,
            fontSize=13,
            leading=20,
            textColor=CYAN,
            spaceBefore=8,
            spaceAfter=5,
        ),
        "body": ParagraphStyle(
            "BodyCN",
            fontName=GUIDE_FONT_NAME,
            fontSize=9.5,
            leading=16,
            textColor=TEXT,
            spaceAfter=7,
        ),
        "bullet": ParagraphStyle(
            "BulletCN",
            fontName=GUIDE_FONT_NAME,
            fontSize=9.3,
            leading=15.5,
            textColor=TEXT,
            leftIndent=13,
            firstLineIndent=-9,
            spaceAfter=5,
        ),
        "small": ParagraphStyle(
            "SmallCN",
            fontName=GUIDE_FONT_NAME,
            fontSize=8,
            leading=13,
            textColor=MUTED,
        ),
        "center": ParagraphStyle(
            "CenterCN",
            fontName=GUIDE_FONT_NAME,
            fontSize=9.5,
            leading=16,
            textColor=TEXT,
            alignment=TA_CENTER,
        ),
    }


def p(text: str, style: ParagraphStyle) -> Paragraph:
    return Paragraph(text.replace("\n", "<br/>"), style)


def bullets(items: list[str], style: ParagraphStyle) -> list[Paragraph]:
    return [p(f"- {item}", style) for item in items]


def screenshot(name: str, width: float) -> Image:
    path = ARTIFACTS / name
    if not path.is_file():
        raise FileNotFoundError(f"缺少界面截图: {path}")
    image = Image(str(path))
    ratio = width / image.imageWidth
    image.drawWidth = width
    image.drawHeight = image.imageHeight * ratio
    image.hAlign = "CENTER"
    return image


def table(rows: list[list[str]], widths: list[float], style_map) -> Table:
    data = [
        [p(cell, style_map["small"] if row_index else style_map["center"])
         for cell in row]
        for row_index, row in enumerate(rows)
    ]
    result = Table(data, colWidths=widths, repeatRows=1, hAlign="LEFT")
    result.setStyle(
        TableStyle(
            [
                ("BACKGROUND", (0, 0), (-1, 0), SURFACE),
                ("TEXTCOLOR", (0, 0), (-1, 0), CYAN),
                ("GRID", (0, 0), (-1, -1), 0.6, LINE),
                ("VALIGN", (0, 0), (-1, -1), "TOP"),
                ("LEFTPADDING", (0, 0), (-1, -1), 7),
                ("RIGHTPADDING", (0, 0), (-1, -1), 7),
                ("TOPPADDING", (0, 0), (-1, -1), 7),
                ("BOTTOMPADDING", (0, 0), (-1, -1), 7),
            ]
        )
    )
    return result


def callout(text: str, style_map, color=AMBER) -> Table:
    result = Table([[p(text, style_map["body"])]], colWidths=[172 * mm])
    result.setStyle(
        TableStyle(
            [
                ("BACKGROUND", (0, 0), (-1, -1), SURFACE),
                ("BOX", (0, 0), (-1, -1), 0.8, LINE),
                ("LINEBEFORE", (0, 0), (0, -1), 4, color),
                ("LEFTPADDING", (0, 0), (-1, -1), 12),
                ("RIGHTPADDING", (0, 0), (-1, -1), 10),
                ("TOPPADDING", (0, 0), (-1, -1), 9),
                ("BOTTOMPADDING", (0, 0), (-1, -1), 9),
            ]
        )
    )
    return result


def page_header_footer(canvas, doc) -> None:
    canvas.saveState()
    width, height = A4
    canvas.setStrokeColor(LINE)
    canvas.line(18 * mm, height - 13 * mm, width - 18 * mm, height - 13 * mm)
    canvas.line(18 * mm, 14 * mm, width - 18 * mm, 14 * mm)
    canvas.setFont(GUIDE_FONT_NAME, 7.5)
    canvas.setFillColor(MUTED)
    canvas.drawString(18 * mm, height - 10 * mm, "有序连点器 2.1.0 使用说明")
    canvas.drawRightString(width - 18 * mm, 9 * mm, f"第 {doc.page} 页")
    canvas.restoreState()


def build_story(style_map) -> list:
    story: list = []

    story += [
        Spacer(1, 12 * mm),
        p("有序连点器 2.1.0", style_map["title"]),
        p(
            "Windows 10/11 x64 · 五步式工作台 · v4 .oclick 方案 · "
            "草稿恢复 · 可靠断点 · 双重停止保护",
            style_map["subtitle"],
        ),
        screenshot("ordered-clicker-ui-v2.0.0.png", 172 * mm),
        Spacer(1, 7 * mm),
        table(
            [
                ["适用用户", "核心能力", "文档日期"],
                ["普通桌面和云桌面用户", "有序点位、循环、稳定检测、暂停继续和日志", "2026-09-24"],
            ],
            [48 * mm, 82 * mm, 42 * mm],
            style_map,
        ),
        Spacer(1, 7 * mm),
        callout(
            "安全提醒：程序会真实移动并点击鼠标。首次使用只设置 1 轮，"
            "在没有支付、删除或提交风险的页面测试。任何时候可按 F8 或触发安全角停止。",
            style_map,
            RED,
        ),
        PageBreak(),
    ]

    story += [
        p("01 一分钟操作流程", style_map["section"]),
        table(
            [
                ["步骤", "操作", "完成标志"],
                ["1 模式", "普通桌面直接使用；云桌面先启用增强模式。", "坐标方式明确"],
                ["2 方案", "新建或打开 .oclick，设置循环和轮间等待。", "方案参数完整"],
                ["3 采点", "按 F6 或使用“2 秒后记录”，按顺序添加点位。", "点位顺序正确"],
                ["4 检查", "核对点击数、预计耗时、显示器和安全停止。", "计划已确认"],
                ["5 运行", "倒计时后开始；F7 暂停/继续，F8 停止。", "完成数等于计划数"],
            ],
            [25 * mm, 99 * mm, 48 * mm],
            style_map,
        ),
        Spacer(1, 10 * mm),
        p("默认快捷键", style_map["heading"]),
        table(
            [
                ["快捷键", "作用", "使用时机"],
                ["F6", "记录当前位置", "采点或云桌面校准"],
                ["F7", "开始、暂停、继续", "计划确认后或执行中"],
                ["F8", "停止", "发现误点风险或窗口变化"],
            ],
            [28 * mm, 55 * mm, 89 * mm],
            style_map,
        ),
        Spacer(1, 10 * mm),
        callout(
            "开始前必须同时具备停止快捷键和已启用的安全角。"
            "点位落入安全角时，程序会拒绝执行。",
            style_map,
        ),
        PageBreak(),
    ]

    story += [
        p("02 方案、迁移与草稿", style_map["section"]),
        p("v4 .oclick 方案", style_map["heading"]),
        *bullets(
            [
                "“保存”覆盖当前绑定文件；“另存为”创建新的方案身份。",
                "“打开方案”用于任意位置的 v4 .oclick 文件。",
                "默认方案目录只列出 .oclick 文件，避免把旧 JSON 当作当前方案直接编辑。",
            ],
            style_map["bullet"],
        ),
        p("旧 JSON 单向迁移", style_map["heading"]),
        *bullets(
            [
                "v1-v3 JSON 只能通过“迁移旧方案”读取。",
                "迁移结果是未保存的新方案；来源文件不会被覆盖。",
                "坐标或字段无法完整推断时会显示警告，保存前必须核对。",
            ],
            style_map["bullet"],
        ),
        p("切换和关闭保护", style_map["heading"]),
        *bullets(
            [
                "有未保存修改时切换方案，会出现“保存 / 不保存 / 取消”。",
                "程序自动保存活动草稿；异常退出后下次启动可以恢复。",
                "正常关闭且没有未保存修改时清理活动草稿。",
            ],
            style_map["bullet"],
        ),
        callout(
            "损坏的设置文件不会静默覆盖。程序会把原文件重命名为带时间戳的 "
            ".broken.json，再恢复默认设置。",
            style_map,
            BLUE,
        ),
        PageBreak(),
    ]

    story += [
        p("03 采点与步骤时间", style_map["section"]),
        *bullets(
            [
                "进入采点模式后，将鼠标移到目标位置按 F6。",
                "无法方便使用全局热键时，点击“2 秒后记录”，利用倒计时切回目标窗口。",
                "新点与已有点距离小于等于 5 像素时，程序会询问是否仍要添加。",
                "新增、删除和清空后可撤销，最多保留最近 10 次历史。",
                "使用上移、下移调整实际执行顺序；禁用点位保留在方案中但不执行。",
            ],
            style_map["bullet"],
        ),
        Spacer(1, 6 * mm),
        table(
            [
                ["参数", "含义", "常见误区"],
                ["点击次数", "当前点位连续点击多少次。", "不是整个方案的循环次数"],
                ["同点连击间隔", "同一位置多次点击之间的等待。", "只在点击次数大于 1 时发生"],
                ["步骤完成后等待", "当前点全部点击后，到下一点前的等待。", "不是每次连击后的等待"],
                ["轮间等待", "整轮结束后，到下一轮开始前的等待。", "最后一轮结束后不再等待"],
            ],
            [38 * mm, 75 * mm, 59 * mm],
            style_map,
        ),
        PageBreak(),
    ]

    story += [
        p("04 检查、运行与安全角", style_map["section"]),
        p("执行检查", style_map["heading"]),
        *bullets(
            [
                "核对总行数、启用行、禁用行、循环数和计划点击数。",
                "同时查看基础预计耗时和包含稳定检测超时的最大耗时。",
                "确认后如修改点位、循环或时间，旧计划和断点自动失效。",
            ],
            style_map["bullet"],
        ),
        p("双重停止保护", style_map["heading"]),
        *bullets(
            [
                "停止快捷键默认是 F8，可在设置中改为其他合法按键。",
                "安全角可选择左上、右上、左下或右下，并设置范围和连续停留时间。",
                "鼠标连续停留达到阈值后，倒计时、等待和点击都会走同一停止流程。",
                "任何启用点位落在安全角内时，执行检查失败。",
            ],
            style_map["bullet"],
        ),
        screenshot("ordered-clicker-settings-v2.0.0.png", 126 * mm),
        PageBreak(),
    ]

    story += [
        p("05 暂停、停止与可靠断点", style_map["section"]),
        *bullets(
            [
                "F7、主窗口和置顶状态小窗都可以暂停或继续。",
                "暂停会冻结点击间隔、步骤等待、轮间等待和画面稳定检测。",
                "停止后已完成的点击不会重复；未完成的等待和稳定检查会保守重做。",
                "只有完成点次和点击数都与计划一致时，状态才显示全部完成。",
                "运行页可以直接打开执行日志和诊断目录。",
            ],
            style_map["bullet"],
        ),
        Spacer(1, 8 * mm),
        table(
            [
                ["断点阶段", "继续执行行为"],
                ["移动前", "重新移动到当前点位"],
                ["点击后", "从下一次尚未完成的点击继续"],
                ["点击间隔中", "重新等待该间隔，不重复已完成点击"],
                ["步骤等待或稳定检查中", "重新执行该等待或检查"],
                ["轮间等待中", "重新等待后进入下一轮"],
            ],
            [62 * mm, 110 * mm],
            style_map,
        ),
        PageBreak(),
    ]

    story += [
        p("06 云桌面与画面稳定", style_map["section"]),
        p("校准相对坐标", style_map["heading"]),
        *bullets(
            [
                "启用云桌面增强后，先记录远程画面的左上角和右下角。",
                "校准完成后再采点，点位保存为区域内相对坐标。",
                "浏览器窗口移动或改变大小后重新校准；有效相对坐标保留，只补齐缺失值。",
            ],
            style_map["bullet"],
        ),
        p("等待画面稳定", style_map["heading"]),
        *bullets(
            [
                "程序使用低分辨率 RGB 采样比较画面变化，减少长流程 CPU 和内存占用。",
                "检测期间同样响应暂停和停止。",
                "超过超时时间仍不稳定时自动暂停；确认页面可操作后按 F7 继续。",
            ],
            style_map["bullet"],
        ),
        callout(
            "显示器分辨率、排列、主副屏关系或缩放比例变化后，"
            "普通坐标建议重新采点，云桌面坐标建议重新校准。",
            style_map,
            BLUE,
        ),
        PageBreak(),
    ]

    story += [
        p("07 数据目录与故障排查", style_map["section"]),
        table(
            [
                ["目录", "内容"],
                ["profiles", "本机 v4 .oclick 方案"],
                ["drafts", "活动草稿和恢复数据"],
                ["logs", "计划数、完成数、结果、耗时和断点"],
                ["diagnostics", "设置、方案和运行异常诊断"],
            ],
            [42 * mm, 130 * mm],
            style_map,
        ),
        Spacer(1, 7 * mm),
        *bullets(
            [
                "安装版根目录：%LocalAppData%\\OrderedClicker。",
                "免安装版根目录：程序旁的 OrderedClickerData。",
                "免安装目录不可写时会明确报错，不会静默改写到其他位置。",
                "快捷键冲突时关闭占用软件，或在设置中切换为组合快捷键。",
                "点击位置偏移时先检查 Windows 缩放、显示器排列和云桌面校准。",
            ],
            style_map["bullet"],
        ),
        screenshot("ordered-clicker-help-v2.0.0.png", 145 * mm),
        PageBreak(),
    ]

    story += [
        p("08 安装、免安装与文件校验", style_map["section"]),
        table(
            [
                ["交付文件", "用途"],
                ["ordered-clicker-setup-v2.1.0.exe", "当前用户安装版"],
                ["有序连点器-免安装.exe", "单文件免安装版"],
                ["有序连点器-使用说明.pdf", "离线 PDF 说明"],
                ["有序连点器-使用说明.html", "自包含 HTML 说明"],
                ["SHA256SUMS.txt", "前四个文件的 SHA-256 校验值"],
            ],
            [82 * mm, 90 * mm],
            style_map,
        ),
        Spacer(1, 9 * mm),
        p("正式发布检查", style_map["heading"]),
        *bullets(
            [
                "核对 SHA256SUMS.txt 中的文件名和哈希。",
                "正式 Release 构建必须签名应用和安装器。",
                "构建脚本会验证 Authenticode 状态和可信时间戳。",
                "最终产品目录只允许以上五个文件，避免把内部 ZIP 或旧版本混入交付。",
            ],
            style_map["bullet"],
        ),
        Spacer(1, 12 * mm),
        callout(
            "版本：2.1.0\n文档日期：2026-09-24\n"
            "本说明与五步工作台、v4 方案、安全角和五文件交付保持一致。",
            style_map,
            GREEN,
        ),
    ]
    return story


def main() -> None:
    register_font()
    style_map = styles()
    OUTPUT.parent.mkdir(parents=True, exist_ok=True)
    document = SimpleDocTemplate(
        str(OUTPUT),
        pagesize=A4,
        rightMargin=18 * mm,
        leftMargin=18 * mm,
        topMargin=18 * mm,
        bottomMargin=19 * mm,
        title="有序连点器 2.1.0 使用说明",
        author="Ordered Clicker",
        subject="有序连点器 2.1.0 操作指导",
    )
    document.build(
        build_story(style_map),
        onFirstPage=page_header_footer,
        onLaterPages=page_header_footer,
    )
    print(f"Generated: {OUTPUT}")
    print(f"Size: {OUTPUT.stat().st_size} bytes")


if __name__ == "__main__":
    main()
