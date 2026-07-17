from __future__ import annotations

import json
import os
import subprocess
import sys
from pathlib import Path

from PIL import Image, ImageDraw, ImageEnhance, ImageFilter, ImageFont, ImageOps

import imageio_ffmpeg


ROOT = Path(__file__).resolve().parents[2]
CONTENT_PATH = ROOT / "scripts" / "guide" / "video_content.json"
GUIDE_DIR = ROOT / "操作指导"
ASSETS_DIR = GUIDE_DIR / "assets"
SCREENSHOT_DIR = ASSETS_DIR / "screenshots"
SCENE_DIR = ASSETS_DIR / "scenes"
AUDIO_DIR = ASSETS_DIR / "audio"
CLIP_DIR = ASSETS_DIR / "clips"
SUBTITLE_PATH = GUIDE_DIR / "有序连点器-视频字幕.srt"
OUTPUT_PATH = GUIDE_DIR / "有序连点器-完整操作教程.mp4"
BASE_VIDEO_PATH = ASSETS_DIR / "有序连点器-无字幕母版.mp4"
CONCAT_PATH = ASSETS_DIR / "concat.txt"
WIDTH = 1920
HEIGHT = 1080
FPS = 30

COLORS = {
    "bg": "#11141A",
    "surface": "#191D26",
    "surface2": "#1F2530",
    "surface3": "#252C38",
    "border": "#3A4352",
    "text": "#EDF1F8",
    "muted": "#9EA9BA",
    "blue": "#4782FF",
    "blue_soft": "#1E315C",
    "cyan": "#37BEE8",
    "cyan_soft": "#173644",
    "green": "#23CBA7",
    "green_soft": "#153A33",
    "amber": "#F6B73C",
    "amber_soft": "#3D3118",
    "red": "#F95C6C",
    "red_soft": "#412028",
    "purple": "#9968FF",
    "purple_soft": "#30234E",
}


def font_path(*names: str) -> str:
    fonts = Path(os.environ.get("WINDIR", r"C:\Windows")) / "Fonts"
    for name in names:
        candidate = fonts / name
        if candidate.exists():
            return os.fspath(candidate)
    raise FileNotFoundError(f"找不到字体：{', '.join(names)}")


FONT_REGULAR = font_path("msyh.ttc", "msyh.ttf", "segoeui.ttf")
FONT_BOLD = font_path("msyhbd.ttc", "msyhbd.ttf", "seguisb.ttf")


def fnt(size: int, bold: bool = False) -> ImageFont.FreeTypeFont:
    return ImageFont.truetype(FONT_BOLD if bold else FONT_REGULAR, size=size)


def rounded(draw: ImageDraw.ImageDraw, box, fill, outline=None, width=1, radius=8):
    draw.rounded_rectangle(box, radius=radius, fill=fill, outline=outline, width=width)


def draw_wrapped(
    draw: ImageDraw.ImageDraw,
    text: str,
    box: tuple[int, int, int, int],
    font: ImageFont.FreeTypeFont,
    fill: str,
    line_spacing: int = 10,
    max_lines: int | None = None,
) -> int:
    x1, y1, x2, _ = box
    lines: list[str] = []
    current = ""
    for char in text:
        candidate = current + char
        if draw.textlength(candidate, font=font) <= x2 - x1:
            current = candidate
        else:
            if current:
                lines.append(current)
            current = char
    if current:
        lines.append(current)
    if max_lines is not None:
        lines = lines[:max_lines]
    y = y1
    line_height = font.size + line_spacing
    for line in lines:
        draw.text((x1, y), line, font=font, fill=fill)
        y += line_height
    return y


def paste_contained(
    canvas: Image.Image,
    image: Image.Image,
    box: tuple[int, int, int, int],
    border: str = COLORS["border"],
    padding: int = 8,
) -> None:
    x1, y1, x2, y2 = box
    draw = ImageDraw.Draw(canvas)
    rounded(draw, box, COLORS["surface2"], border, width=2, radius=6)
    target_size = (x2 - x1 - padding * 2, y2 - y1 - padding * 2)
    fitted = ImageOps.contain(image.convert("RGB"), target_size, Image.Resampling.LANCZOS)
    left = x1 + padding + (target_size[0] - fitted.width) // 2
    top = y1 + padding + (target_size[1] - fitted.height) // 2
    canvas.paste(fitted, (left, top))


def draw_keycap(
    draw: ImageDraw.ImageDraw,
    position: tuple[int, int],
    text: str,
    accent: str,
    width: int = 126,
) -> None:
    x, y = position
    rounded(draw, (x, y, x + width, y + 64), COLORS["surface3"], accent, width=3, radius=6)
    label_font = fnt(28, True)
    bbox = draw.textbbox((0, 0), text, font=label_font)
    draw.text(
        (x + (width - (bbox[2] - bbox[0])) / 2, y + 13),
        text,
        font=label_font,
        fill=COLORS["text"],
    )


def draw_title(canvas: Image.Image, scene: dict) -> ImageDraw.ImageDraw:
    draw = ImageDraw.Draw(canvas)
    draw.rectangle((0, 0, WIDTH, 92), fill="#141820")
    draw.line((0, 91, WIDTH, 91), fill=COLORS["border"], width=2)
    draw.text((62, 23), f"{scene['id']:02}", font=fnt(30, True), fill=COLORS["cyan"])
    draw.text((124, 22), scene["title"], font=fnt(32, True), fill=COLORS["text"])
    draw.text(
        (WIDTH - 420, 31),
        "有序连点器 · 零基础操作教程",
        font=fnt(18),
        fill=COLORS["muted"],
    )
    draw.rectangle((0, HEIGHT - 118, WIDTH, HEIGHT), fill="#12161D")
    draw.line((0, HEIGHT - 118, WIDTH, HEIGHT - 118), fill=COLORS["border"], width=2)
    draw.text(
        (62, HEIGHT - 84),
        f"章节 {scene['id']:02} / 14",
        font=fnt(18, True),
        fill=COLORS["cyan"],
    )
    draw.text(
        (62, HEIGHT - 52),
        "画面内字幕与普通话女声同步，完整字幕另附 SRT 文件",
        font=fnt(16),
        fill=COLORS["muted"],
    )
    return draw


def draw_target_board(
    canvas: Image.Image,
    box: tuple[int, int, int, int],
    counts=(0, 0, 0),
    sequence="尚未记录点击",
) -> None:
    x1, y1, x2, y2 = box
    draw = ImageDraw.Draw(canvas)
    rounded(draw, box, "#141820", COLORS["border"], width=2, radius=6)
    draw.text((x1 + 30, y1 + 24), "有序连点器演示靶场", font=fnt(27, True), fill=COLORS["text"])
    draw.text(
        (x1 + 30, y1 + 66),
        "本地安全测试页面 · 三个固定点击目标",
        font=fnt(16),
        fill=COLORS["muted"],
    )
    labels = (
        ("蓝色按钮", counts[0], COLORS["blue"], "点位 1 · 每轮 2 次"),
        ("青色按钮", counts[1], COLORS["green"], "点位 2 · 每轮 1 次"),
        ("紫色按钮", counts[2], COLORS["purple"], "点位 3 · 每轮 3 次"),
    )
    button_width = 300
    gap = 70
    total_width = button_width * 3 + gap * 2
    start_x = x1 + (x2 - x1 - total_width) // 2
    button_y = y1 + 145
    for index, (label, count, accent, hint) in enumerate(labels):
        bx = start_x + index * (button_width + gap)
        rounded(
            draw,
            (bx, button_y, bx + button_width, button_y + 210),
            COLORS["surface2"],
            accent,
            width=3,
            radius=8,
        )
        draw.text((bx + 28, button_y + 26), label, font=fnt(24, True), fill=COLORS["text"])
        count_text = str(count)
        count_box = draw.textbbox((0, 0), count_text, font=fnt(64, True))
        draw.text(
            (bx + (button_width - count_box[2]) / 2, button_y + 72),
            count_text,
            font=fnt(64, True),
            fill=accent,
        )
        draw.text((bx + 28, button_y + 174), hint, font=fnt(15), fill=COLORS["muted"])
    footer_y = y2 - 112
    rounded(
        draw,
        (x1 + 28, footer_y, x2 - 28, y2 - 28),
        COLORS["surface"],
        COLORS["border"],
        radius=5,
    )
    draw.text(
        (x1 + 52, footer_y + 22),
        f"总点击次数：{sum(counts)}",
        font=fnt(22, True),
        fill=COLORS["cyan"],
    )
    draw.text(
        (x1 + 330, footer_y + 23),
        sequence,
        font=fnt(18),
        fill=COLORS["muted"],
    )


def annotation_card(
    draw: ImageDraw.ImageDraw,
    box: tuple[int, int, int, int],
    title: str,
    body: str,
    accent: str,
) -> None:
    rounded(draw, box, COLORS["surface"], COLORS["border"], radius=6)
    x1, y1, x2, y2 = box
    draw.rectangle((x1, y1, x1 + 8, y2), fill=accent)
    draw.text((x1 + 26, y1 + 18), title, font=fnt(21, True), fill=COLORS["text"])
    draw_wrapped(
        draw,
        body,
        (x1 + 26, y1 + 58, x2 - 20, y2 - 12),
        fnt(16),
        COLORS["muted"],
        line_spacing=8,
        max_lines=3,
    )


def render_scene(scene: dict, images: dict[str, Image.Image]) -> Image.Image:
    canvas = Image.new("RGB", (WIDTH, HEIGHT), COLORS["bg"])
    draw = draw_title(canvas, scene)
    content_box = (54, 118, WIDTH - 54, HEIGHT - 142)
    visual = scene["visual"]

    if visual == "cover":
        background = ImageOps.fit(images["main"], (WIDTH, HEIGHT), Image.Resampling.LANCZOS)
        background = ImageEnhance.Brightness(background).enhance(0.34).filter(
            ImageFilter.GaussianBlur(1.4)
        )
        canvas.paste(background, (0, 0))
        draw = ImageDraw.Draw(canvas)
        draw.rectangle((0, 0, WIDTH, HEIGHT), fill=(17, 20, 26, 150))
        draw.text((110, 205), "有序连点器", font=fnt(68, True), fill=COLORS["text"])
        draw.text((112, 310), "零基础完整操作教程", font=fnt(40, True), fill=COLORS["cyan"])
        draw_wrapped(
            draw,
            "从 F8 采点，到多点顺序、独立参数、循环、暂停停止和方案保存。",
            (114, 390, 1040, 510),
            fnt(26),
            "#C8D2E1",
            line_spacing=14,
        )
        draw_keycap(draw, (118, 620), "F8 采点", COLORS["green"], 180)
        draw_keycap(draw, (320, 620), "F9 开始", COLORS["blue"], 190)
        draw_keycap(draw, (532, 620), "F10 停止", COLORS["red"], 200)
        draw.text((118, 750), "普通话女声 · 1920 × 1080 · 深色模式", font=fnt(20), fill=COLORS["muted"])
        return canvas

    if visual == "overview":
        paste_contained(canvas, images["main"], (54, 130, 1180, 900))
        cards = (
            ("有序点位", "多个点按表格顺序执行", COLORS["blue"]),
            ("独立参数", "每点次数和等待单独设置", COLORS["green"]),
            ("循环执行", "整组点位重复多轮", COLORS["amber"]),
            ("方案复用", "保存和加载 JSON 方案", COLORS["purple"]),
            ("随时停止", "F10 中断倒计时与点击", COLORS["red"]),
        )
        for index, card in enumerate(cards):
            top = 138 + index * 148
            annotation_card(draw, (1230, top, 1858, top + 126), *card)

    elif visual == "main":
        paste_contained(canvas, images["main"], (74, 132, 1846, 896))
        markers = (
            (230, 182, "1", "方案与循环"),
            (570, 390, "2", "有序点位表"),
            (860, 738, "3", "点位操作栏"),
            (315, 825, "4", "开始与停止"),
            (1485, 884, "5", "状态与快捷键"),
        )
        for x, y, number, label in markers:
            draw.ellipse((x, y, x + 48, y + 48), fill=COLORS["blue"], outline="white", width=2)
            draw.text((x + 15, y + 7), number, font=fnt(23, True), fill="white")
            rounded(draw, (x + 56, y + 4, x + 228, y + 44), COLORS["blue_soft"], COLORS["blue"], radius=4)
            draw.text((x + 70, y + 10), label, font=fnt(16, True), fill="#D7E2FF")

    elif visual == "target":
        draw_target_board(canvas, (90, 132, 1830, 892))
        draw.text(
            (1250, 154),
            "打开后固定窗口位置",
            font=fnt(21, True),
            fill=COLORS["amber"],
        )

    elif visual == "capture":
        paste_contained(canvas, images["main"], (54, 132, 972, 892))
        draw_target_board(canvas, (1004, 132, 1866, 892))
        draw_keycap(draw, (1620, 770), "F8", COLORS["green"], 150)
        for number, x, accent in (
            ("1", 1098, COLORS["blue"]),
            ("2", 1385, COLORS["green"]),
            ("3", 1668, COLORS["purple"]),
        ):
            draw.ellipse((x, 430, x + 58, 488), outline=accent, width=6)
            draw.text((x + 20, 438), number, font=fnt(22, True), fill=accent)
        draw.text((1040, 835), "蓝 → 青 → 紫，采点顺序就是执行顺序", font=fnt(20, True), fill=COLORS["text"])

    elif visual == "order":
        paste_contained(canvas, images["main"], (54, 132, 1220, 892))
        annotation_card(draw, (1260, 160, 1858, 302), "上移 / 下移", "改变选中点位的执行顺序，顺序列自动更新。", COLORS["blue"])
        annotation_card(draw, (1260, 326, 1858, 468), "启用", "取消勾选后保留点位，但运行时跳过。", COLORS["green"])
        annotation_card(draw, (1260, 492, 1858, 634), "删除", "只移除当前选中的点位。", COLORS["red"])
        annotation_card(draw, (1260, 658, 1858, 800), "清空", "删除全部点位，执行前会再次确认。", COLORS["amber"])

    elif visual == "parameters":
        paste_contained(canvas, images["main"], (54, 132, 1080, 892))
        rows = (
            ("蓝色按钮", "2", "500", "800", COLORS["blue"]),
            ("青色按钮", "1", "0", "600", COLORS["green"]),
            ("紫色按钮", "3", "300", "1000", COLORS["purple"]),
        )
        x1, y1, x2, y2 = 1120, 170, 1858, 702
        rounded(draw, (x1, y1, x2, y2), COLORS["surface"], COLORS["border"], width=2, radius=6)
        draw.text((x1 + 28, y1 + 24), "每个点独立设置", font=fnt(26, True), fill=COLORS["text"])
        headers = ("点位", "次数", "间隔", "点后等待")
        columns = (x1 + 28, x1 + 310, x1 + 430, x1 + 558)
        for header, x in zip(headers, columns):
            draw.text((x, y1 + 88), header, font=fnt(17, True), fill=COLORS["muted"])
        for index, row in enumerate(rows):
            ry = y1 + 140 + index * 106
            draw.line((x1 + 24, ry - 12, x2 - 24, ry - 12), fill=COLORS["border"], width=1)
            draw.ellipse((x1 + 30, ry + 7, x1 + 48, ry + 25), fill=row[4])
            draw.text((x1 + 60, ry), row[0], font=fnt(18, True), fill=COLORS["text"])
            for value, x in zip(row[1:4], columns[1:]):
                draw.text((x, ry), value, font=fnt(21, True), fill=row[4])
        annotation_card(draw, (1120, 730, 1858, 872), "时间单位", "1000 毫秒等于 1 秒。等待值不允许是负数。", COLORS["amber"])

    elif visual == "loops":
        paste_contained(canvas, images["main"], (54, 132, 1250, 892))
        annotation_card(draw, (1290, 190, 1858, 358), "总循环次数：2", "蓝、青、紫全部执行完算一轮，整组操作重复两遍。", COLORS["blue"])
        annotation_card(draw, (1290, 402, 1858, 570), "轮间等待：1500 ms", "第一轮全部完成后等待 1.5 秒，再开始第二轮。", COLORS["amber"])
        annotation_card(draw, (1290, 614, 1858, 782), "首次验证：1 轮", "先确认坐标和参数，再提高循环次数。", COLORS["green"])

    elif visual == "run":
        draw_target_board(
            canvas,
            (90, 132, 1830, 892),
            counts=(4, 2, 6),
            sequence="最近点击顺序：蓝 → 蓝 → 青 → 紫 → 紫 → 紫 → 蓝 → 蓝 → 青 → 紫 → 紫 → 紫",
        )
        rounded(draw, (690, 146, 1230, 214), COLORS["green_soft"], COLORS["green"], width=2, radius=5)
        draw.text((756, 162), "执行完成：2 / 2 轮", font=fnt(26, True), fill="#B8F0E3")
        draw_keycap(draw, (1610, 788), "F9", COLORS["blue"], 150)

    elif visual == "pause-stop":
        paste_contained(canvas, images["main"], (54, 132, 1080, 892))
        annotation_card(draw, (1130, 180, 1858, 370), "F9 暂停 / 继续", "运行中按一次暂停，再按一次从原位置继续。", COLORS["blue"])
        annotation_card(draw, (1130, 430, 1858, 620), "F10 立即停止", "倒计时、等待和点击过程中都可以请求停止。", COLORS["red"])
        draw_keycap(draw, (1255, 698), "F9", COLORS["blue"], 210)
        draw_keycap(draw, (1510, 698), "F10", COLORS["red"], 220)

    elif visual == "profiles":
        paste_contained(canvas, images["main"], (54, 132, 1160, 892))
        annotation_card(draw, (1200, 160, 1858, 326), "1. 保存", "输入“三点演示方案”，点击保存写入 JSON 文件。", COLORS["blue"])
        annotation_card(draw, (1200, 360, 1858, 526), "2. 加载", "选择之前保存的文件，恢复点位和全部参数。", COLORS["cyan"])
        annotation_card(draw, (1200, 560, 1858, 726), "3. 重新验证", "显示环境变化后先执行一轮，必要时重新采点。", COLORS["amber"])
        draw.text((1260, 785), "{ 方案名称 · 点位 · 参数 · 显示器 }", font=fnt(19, True), fill=COLORS["purple"])

    elif visual == "theme-help":
        paste_contained(canvas, images["settings"], (54, 132, 938, 892))
        paste_contained(canvas, images["help"], (982, 132, 1866, 892))
        rounded(draw, (664, 780, 874, 842), COLORS["purple_soft"], COLORS["purple"], width=2, radius=5)
        draw.text((716, 796), "主题设置", font=fnt(20, True), fill="#E0D4FF")
        rounded(draw, (1592, 780, 1812, 842), COLORS["cyan_soft"], COLORS["cyan"], width=2, radius=5)
        draw.text((1640, 796), "使用说明", font=fnt(20, True), fill="#C7EDF8")

    elif visual == "display":
        paste_contained(canvas, images["main"], (54, 132, 1010, 892))
        x1, y1, x2, y2 = 1050, 160, 1858, 830
        rounded(draw, (x1, y1, x2, y2), COLORS["surface"], COLORS["border"], width=2, radius=6)
        draw.text((x1 + 30, y1 + 28), "显示环境变化", font=fnt(28, True), fill=COLORS["text"])
        for index, scale in enumerate(("100%", "125%", "150%")):
            bx = x1 + 38 + index * 240
            rounded(draw, (bx, y1 + 104, bx + 190, y1 + 190), COLORS["surface2"], COLORS["blue"], radius=5)
            draw.text((bx + 51, y1 + 127), scale, font=fnt(27, True), fill="#C9D9FF")
        monitor_y = y1 + 250
        rounded(draw, (x1 + 70, monitor_y, x1 + 360, monitor_y + 188), COLORS["surface2"], COLORS["cyan"], width=3, radius=5)
        rounded(draw, (x1 + 410, monitor_y + 50, x1 + 730, monitor_y + 246), COLORS["surface2"], COLORS["purple"], width=3, radius=5)
        draw.text((x1 + 150, monitor_y + 72), "主显示器", font=fnt(23, True), fill=COLORS["cyan"])
        draw.text((x1 + 500, monitor_y + 124), "副显示器", font=fnt(23, True), fill=COLORS["purple"])
        annotation_card(draw, (x1 + 38, y1 + 530, x2 - 38, y1 + 640), "环境改变后重新采点", "分辨率、缩放、屏幕排列或目标窗口位置变化都会影响绝对坐标。", COLORS["amber"])

    elif visual == "summary":
        draw.text((92, 150), "执行前六项检查", font=fnt(34, True), fill=COLORS["text"])
        checks = (
            ("1", "固定目标窗口", "采点后不要再移动或缩放目标窗口。", COLORS["blue"]),
            ("2", "按 F8 依次采点", "采点顺序就是执行顺序。", COLORS["green"]),
            ("3", "核对单点参数", "次数、间隔和点后等待都使用整数。", COLORS["amber"]),
            ("4", "首次只运行一轮", "验证无误后再提高循环次数。", COLORS["purple"]),
            ("5", "F9 开始或暂停", "利用三秒倒计时切换到目标窗口。", COLORS["cyan"]),
            ("6", "F10 随时停止", "发现异常不要等待当前一轮结束。", COLORS["red"]),
        )
        for index, (number, title, body, accent) in enumerate(checks):
            column = index % 2
            row = index // 2
            x = 92 + column * 870
            y = 224 + row * 194
            rounded(draw, (x, y, x + 808, y + 158), COLORS["surface"], COLORS["border"], radius=6)
            draw.ellipse((x + 26, y + 36, x + 94, y + 104), fill=accent)
            draw.text((x + 49, y + 48), number, font=fnt(27, True), fill="white")
            draw.text((x + 122, y + 28), title, font=fnt(24, True), fill=COLORS["text"])
            draw_wrapped(draw, body, (x + 122, y + 72, x + 770, y + 138), fnt(17), COLORS["muted"])

    return canvas


def run(command: list[str], cwd: Path | None = None) -> None:
    print("RUN", " ".join(command[:8]), "...")
    subprocess.run(command, cwd=os.fspath(cwd) if cwd else None, check=True)


def main() -> None:
    scenes = json.loads(CONTENT_PATH.read_text(encoding="utf-8"))
    SCENE_DIR.mkdir(parents=True, exist_ok=True)
    CLIP_DIR.mkdir(parents=True, exist_ok=True)
    ffmpeg = imageio_ffmpeg.get_ffmpeg_exe()
    images = {
        "main": Image.open(SCREENSHOT_DIR / "主界面-极光科技.png").convert("RGB"),
        "settings": Image.open(SCREENSHOT_DIR / "主题设置.png").convert("RGB"),
        "help": Image.open(SCREENSHOT_DIR / "使用说明.png").convert("RGB"),
    }

    clip_paths: list[Path] = []
    for scene in scenes:
        chapter = int(scene["id"])
        duration = float(scene["duration"])
        scene_path = SCENE_DIR / f"scene-{chapter:02}.png"
        clip_path = CLIP_DIR / f"chapter-{chapter:02}.mp4"
        audio_path = AUDIO_DIR / f"chapter-{chapter:02}.wav"

        render_scene(scene, images).save(scene_path, "PNG", optimize=True)
        fade_out_start = max(0.0, duration - 0.35)
        video_filter = (
            f"fade=t=in:st=0:d=0.35,"
            f"fade=t=out:st={fade_out_start:.2f}:d=0.35,"
            "format=yuv420p"
        )
        audio_filter = (
            f"loudnorm=I=-16:LRA=7:TP=-1.0,"
            f"apad=pad_dur={duration:.2f}"
        )
        run(
            [
                ffmpeg,
                "-hide_banner",
                "-loglevel",
                "error",
                "-y",
                "-loop",
                "1",
                "-framerate",
                str(FPS),
                "-i",
                os.fspath(scene_path),
                "-i",
                os.fspath(audio_path),
                "-t",
                f"{duration:.3f}",
                "-vf",
                video_filter,
                "-af",
                audio_filter,
                "-c:v",
                "libx264",
                "-preset",
                "veryfast",
                "-crf",
                "21",
                "-r",
                str(FPS),
                "-c:a",
                "aac",
                "-b:a",
                "160k",
                "-ar",
                "48000",
                "-ac",
                "1",
                "-movflags",
                "+faststart",
                os.fspath(clip_path),
            ]
        )
        clip_paths.append(clip_path)
        print(f"Built chapter {chapter:02}: {duration:.0f}s")

    CONCAT_PATH.write_text(
        "\n".join(f"file '{path.as_posix()}'" for path in clip_paths) + "\n",
        encoding="utf-8",
    )
    run(
        [
            ffmpeg,
            "-hide_banner",
            "-loglevel",
            "error",
            "-y",
            "-f",
            "concat",
            "-safe",
            "0",
            "-i",
            os.fspath(CONCAT_PATH),
            "-c",
            "copy",
            "-movflags",
            "+faststart",
            os.fspath(BASE_VIDEO_PATH),
        ]
    )

    subtitle_filter = (
        "subtitles='有序连点器-视频字幕.srt':"
        "force_style='FontName=Microsoft YaHei UI,FontSize=22,"
        "PrimaryColour=&H00FFFFFF,OutlineColour=&H0011141A,"
        "BackColour=&H8011141A,BorderStyle=3,Outline=1,Shadow=0,"
        "MarginV=38,Alignment=2'"
    )
    run(
        [
            ffmpeg,
            "-hide_banner",
            "-loglevel",
            "error",
            "-y",
            "-i",
            os.fspath(BASE_VIDEO_PATH),
            "-vf",
            subtitle_filter,
            "-c:v",
            "libx264",
            "-preset",
            "veryfast",
            "-crf",
            "20",
            "-c:a",
            "copy",
            "-movflags",
            "+faststart",
            os.fspath(OUTPUT_PATH),
        ],
        cwd=GUIDE_DIR,
    )
    print(f"Video created: {OUTPUT_PATH}")


if __name__ == "__main__":
    try:
        main()
    except subprocess.CalledProcessError as exception:
        print(f"FFmpeg failed with exit code {exception.returncode}", file=sys.stderr)
        raise
