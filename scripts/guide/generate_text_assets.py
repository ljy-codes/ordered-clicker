from __future__ import annotations

import json
import re
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
CONTENT_PATH = ROOT / "scripts" / "guide" / "video_content.json"
OUTPUT_DIR = ROOT / "操作指导"

VISUAL_DESCRIPTIONS = {
    "cover": "视频封面、软件主界面和三点靶场组合画面；依次显示 F8、F9、F10 键帽。",
    "overview": "主界面截图居中，五项能力以蓝、青、琥珀、紫、红色标签依次出现。",
    "main": "极光科技主界面截图；依次高亮顶部方案区、点位表、操作栏、执行区和状态栏。",
    "target": "绘制深色演示靶场，蓝、青、紫三个按钮保持固定位置，底部计数均为零。",
    "capture": "主界面截图与靶场并排；鼠标光圈依次移动到三个按钮，右下角显示 F8 键帽。",
    "order": "放大点位表格，展示三行顺序；上移、下移、启用、删除按钮依次高亮。",
    "parameters": "放大表格参数列，分三次显示蓝色 2/500/800、青色 1/0/600、紫色 3/300/1000。",
    "loops": "放大顶部总循环次数和轮间等待，显示 2 轮与 1500 毫秒的解释卡片。",
    "run": "三秒倒计时后展示靶场计数按蓝、青、紫顺序增长，最终停在 4、2、6。",
    "pause-stop": "显示 F9 蓝色键帽、暂停状态条，再显示 F10 红色键帽和任务停止状态。",
    "profiles": "主界面保存和加载按钮高亮，旁边展示 JSON 方案文件和重新加载流程。",
    "theme-help": "主题设置和使用说明截图左右排列，五套主题色条依次出现。",
    "display": "主界面、双显示器示意和 100%/125%/150% 缩放标签组合，显示重新采点警告。",
    "summary": "六项执行前检查清单，底部固定显示 F10 紧急停止提示。",
}


def timestamp(seconds: float, separator: str = ",") -> str:
    total_ms = max(0, round(seconds * 1000))
    hours, remainder = divmod(total_ms, 3_600_000)
    minutes, remainder = divmod(remainder, 60_000)
    secs, milliseconds = divmod(remainder, 1000)
    return f"{hours:02}:{minutes:02}:{secs:02}{separator}{milliseconds:03}"


def markdown_timestamp(seconds: float) -> str:
    minutes, secs = divmod(round(seconds), 60)
    return f"{minutes:02}:{secs:02}"


def split_caption_units(text: str) -> list[str]:
    sentences = [
        part.strip()
        for part in re.split(r"(?<=[。！？；])", text)
        if part.strip()
    ]
    units: list[str] = []
    for sentence in sentences:
        if len(sentence) <= 34:
            units.append(sentence)
            continue

        clauses = [
            part.strip()
            for part in re.split(r"(?<=[，、：])", sentence)
            if part.strip()
        ]
        current = ""
        for clause in clauses:
            if current and len(current) + len(clause) > 34:
                units.append(current)
                current = clause
            else:
                current += clause
        if current:
            units.append(current)
    return units


def wrap_caption(text: str, width: int = 18) -> str:
    if len(text) <= width:
        return text
    split_at = min(width, len(text))
    for index in range(split_at, max(8, split_at - 6), -1):
        if text[index - 1] in "，。；：、":
            split_at = index
            break
    return text[:split_at] + "\n" + text[split_at:]


def main() -> None:
    scenes = json.loads(CONTENT_PATH.read_text(encoding="utf-8"))
    OUTPUT_DIR.mkdir(parents=True, exist_ok=True)

    narration_lines = [
        "# 有序连点器视频旁白稿",
        "",
        "- 旁白：标准普通话女声",
        "- 目标时长：10 分钟",
        "- 发音约定：F8 读作“F 八”，F9 读作“F 九”，F10 读作“F 十”",
        "- 技术词：DPI 逐字母朗读，JSON 逐字母朗读，ms 统一说“毫秒”",
        "",
    ]
    storyboard_lines = [
        "# 有序连点器视频分镜表",
        "",
        "| 章节 | 时间 | 时长 | 画面与动作 | 旁白重点 |",
        "| --- | --- | ---: | --- | --- |",
    ]
    srt_blocks: list[str] = []

    scene_start = 0.0
    caption_index = 1
    for scene in scenes:
        scene_end = scene_start + float(scene["duration"])
        narration_lines.extend(
            [
                f"## {scene['id']:02}. {scene['title']}",
                "",
                scene["narration"],
                "",
            ]
        )
        storyboard_lines.append(
            "| "
            f"{scene['id']:02}. {scene['title']} | "
            f"{markdown_timestamp(scene_start)}–{markdown_timestamp(scene_end)} | "
            f"{scene['duration']} 秒 | "
            f"{VISUAL_DESCRIPTIONS[scene['visual']]} | "
            f"{scene['narration'][:52]}…… |"
        )

        units = split_caption_units(scene["narration"])
        usable_duration = max(1.0, scene["duration"] - 1.0)
        total_weight = sum(max(6, len(unit)) for unit in units)
        cursor = scene_start + 0.35
        for unit_number, unit in enumerate(units):
            is_last = unit_number == len(units) - 1
            share = usable_duration * max(6, len(unit)) / total_weight
            end = min(scene_end - (0.2 if is_last else 0.05), cursor + share)
            srt_blocks.extend(
                [
                    str(caption_index),
                    f"{timestamp(cursor)} --> {timestamp(end)}",
                    wrap_caption(unit),
                    "",
                ]
            )
            caption_index += 1
            cursor = end + 0.05

        scene_start = scene_end

    storyboard_lines.extend(
        [
            "",
            "## 统一画面规则",
            "",
            "- 分辨率：1920 × 1080，30 FPS。",
            "- 主界面：极光科技深色主题。",
            "- 鼠标：青色光圈；点击时显示扩散动画。",
            "- 快捷键：右下角显示 F8、F9、F10 键帽。",
            "- 字幕：底部安全区域，最多两行。",
            "- 演示结果：蓝色 4 次、青色 2 次、紫色 6 次，总计 12 次。",
            "- 安全原则：只操作本地演示靶场，不出现真实业务和私人信息。",
        ]
    )

    (OUTPUT_DIR / "有序连点器-旁白稿.md").write_text(
        "\n".join(narration_lines), encoding="utf-8"
    )
    (OUTPUT_DIR / "有序连点器-视频分镜表.md").write_text(
        "\n".join(storyboard_lines), encoding="utf-8"
    )
    (OUTPUT_DIR / "有序连点器-视频字幕.srt").write_text(
        "\n".join(srt_blocks), encoding="utf-8-sig"
    )

    print(f"Generated narration, storyboard and {caption_index - 1} subtitles.")
    print(f"Timeline: {scene_start:.1f} seconds.")


if __name__ == "__main__":
    main()
