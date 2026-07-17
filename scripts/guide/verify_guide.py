from __future__ import annotations

import re
import subprocess
import sys
from pathlib import Path
from urllib.parse import unquote

from pypdf import PdfReader


ROOT = Path(__file__).resolve().parents[2]
GUIDE_DIR = ROOT / "操作指导"
ASSETS_DIR = GUIDE_DIR / "assets"


def fail(message: str) -> None:
    raise AssertionError(message)


def require_file(path: Path, minimum_size: int = 1) -> None:
    if not path.is_file():
        fail(f"缺少文件: {path}")
    if path.stat().st_size < minimum_size:
        fail(f"文件过小或为空: {path}")


def verify_local_links(html_path: Path) -> None:
    html = html_path.read_text(encoding="utf-8")
    for raw in re.findall(r"""(?:src|href)=["']([^"']+)["']""", html):
        if raw.startswith(("#", "http://", "https://", "mailto:", "javascript:")):
            continue
        clean = unquote(raw.split("#", 1)[0].split("?", 1)[0])
        if not clean:
            continue
        target = (html_path.parent / clean).resolve()
        if not target.exists():
            fail(f"HTML 本地资源不存在: {raw} -> {target}")


def srt_seconds(value: str) -> float:
    hours, minutes, rest = value.split(":")
    seconds, millis = rest.split(",")
    return (
        int(hours) * 3600
        + int(minutes) * 60
        + int(seconds)
        + int(millis) / 1000
    )


def verify_srt(path: Path) -> tuple[int, float]:
    text = path.read_text(encoding="utf-8-sig")
    matches = re.findall(
        r"(?m)^(\d{2}:\d{2}:\d{2},\d{3}) --> "
        r"(\d{2}:\d{2}:\d{2},\d{3})$",
        text,
    )
    if not matches:
        fail("字幕文件中没有有效时间轴")

    previous_end = 0.0
    for start_text, end_text in matches:
        start = srt_seconds(start_text)
        end = srt_seconds(end_text)
        if start < previous_end - 0.02:
            fail(f"字幕时间轴重叠或倒退: {start_text}")
        if end <= start:
            fail(f"字幕结束时间无效: {start_text} -> {end_text}")
        previous_end = end
    return len(matches), previous_end


def find_ffmpeg() -> Path:
    candidates = list(
        (ROOT / ".tools" / "python" / "imageio_ffmpeg" / "binaries").glob(
            "ffmpeg-*.exe"
        )
    )
    if not candidates:
        fail("未找到项目本地 FFmpeg")
    return candidates[0]


def verify_video(path: Path) -> tuple[float, str, float]:
    ffmpeg = find_ffmpeg()
    result = subprocess.run(
        [str(ffmpeg), "-hide_banner", "-i", str(path)],
        capture_output=True,
        text=True,
        encoding="utf-8",
        errors="replace",
        check=False,
    )
    metadata = result.stderr

    duration_match = re.search(r"Duration: (\d+):(\d+):(\d+\.\d+)", metadata)
    if not duration_match:
        fail("无法读取视频时长")
    hours, minutes, seconds = duration_match.groups()
    duration = int(hours) * 3600 + int(minutes) * 60 + float(seconds)

    if not 599.0 <= duration <= 601.0:
        fail(f"视频时长不是约 10 分钟: {duration:.2f}s")
    if not re.search(r"Video: h264.*1920x1080", metadata):
        fail("视频不是 H.264 1920x1080")
    if not re.search(r"Audio: aac", metadata):
        fail("视频缺少 AAC 音轨")

    decode = subprocess.run(
        [
            str(ffmpeg),
            "-hide_banner",
            "-loglevel",
            "error",
            "-ss",
            "599",
            "-i",
            str(path),
            "-t",
            "0.5",
            "-f",
            "null",
            "NUL",
        ],
        capture_output=True,
        text=True,
        encoding="utf-8",
        errors="replace",
        check=False,
    )
    if decode.returncode != 0:
        fail(f"视频结尾解码失败: {decode.stderr.strip()}")

    volume = subprocess.run(
        [
            str(ffmpeg),
            "-hide_banner",
            "-nostats",
            "-i",
            str(path),
            "-vn",
            "-af",
            "volumedetect",
            "-f",
            "null",
            "NUL",
        ],
        capture_output=True,
        text=True,
        encoding="utf-8",
        errors="replace",
        check=False,
    )
    volume_match = re.search(r"mean_volume: (-?\d+(?:\.\d+)?) dB", volume.stderr)
    if not volume_match:
        fail("无法读取视频音轨响度")
    mean_volume = float(volume_match.group(1))
    if mean_volume < -35.0:
        fail(f"视频旁白音量过低: {mean_volume:.1f} dB")

    return duration, "H.264 1920x1080 + AAC", mean_volume


def main() -> int:
    required = {
        "PRD HTML": (GUIDE_DIR / "有序连点器-操作指导PRD.html", 30_000),
        "PRD PDF": (GUIDE_DIR / "有序连点器-操作指导PRD.pdf", 100_000),
        "完整视频": (GUIDE_DIR / "有序连点器-完整操作教程.mp4", 5_000_000),
        "字幕": (GUIDE_DIR / "有序连点器-视频字幕.srt", 5_000),
        "旁白稿": (GUIDE_DIR / "有序连点器-旁白稿.md", 3_000),
        "分镜表": (GUIDE_DIR / "有序连点器-视频分镜表.md", 3_000),
        "演示靶场": (GUIDE_DIR / "连点演示靶场.html", 5_000),
        "视频封面": (ASSETS_DIR / "视频封面.png", 20_000),
    }
    for _, (path, size) in required.items():
        require_file(path, size)

    prd_html = required["PRD HTML"][0]
    prd_text = prd_html.read_text(encoding="utf-8")
    prd_terms = [
        "采点模式",
        "点击次数",
        "点击间隔",
        "点后等待",
        "总循环次数",
        "F8",
        "F9",
        "F10",
        "重新采点",
        "屏幕缩放",
        "多显示器",
    ]
    missing_terms = [term for term in prd_terms if term not in prd_text]
    if missing_terms:
        fail(f"PRD 缺少关键内容: {', '.join(missing_terms)}")
    verify_local_links(prd_html)
    verify_local_links(required["演示靶场"][0])

    pdf_reader = PdfReader(str(required["PRD PDF"][0]))
    if len(pdf_reader.pages) < 15:
        fail(f"PDF 页数不足: {len(pdf_reader.pages)}")
    pdf_text = "\n".join(page.extract_text() or "" for page in pdf_reader.pages)
    for term in ("采点模式", "点击次数", "F8", "F9", "F10", "重新采点"):
        if term not in pdf_text:
            fail(f"PDF 缺少关键内容: {term}")

    subtitle_count, subtitle_end = verify_srt(required["字幕"][0])
    if subtitle_count < 80:
        fail(f"字幕条目过少: {subtitle_count}")
    if subtitle_end > 600.1:
        fail(f"字幕超过视频总时长: {subtitle_end:.3f}s")

    for folder, pattern, expected in (
        (ASSETS_DIR / "audio", "chapter-*.mp3", 14),
        (ASSETS_DIR / "audio", "chapter-*.wav", 14),
        (ASSETS_DIR / "clips", "chapter-*.mp4", 14),
        (ASSETS_DIR / "scenes", "scene-*.png", 14),
    ):
        actual = len(list(folder.glob(pattern)))
        if actual != expected:
            fail(f"{folder.name}/{pattern} 数量错误: {actual}, 预期 {expected}")

    duration, media, mean_volume = verify_video(required["完整视频"][0])

    print("操作指导交付校验通过")
    print(f"- 必需交付文件: {len(required)} 项")
    print(f"- PDF: {len(pdf_reader.pages)} 页")
    print(f"- 字幕: {subtitle_count} 条，结束于 {subtitle_end:.3f}s")
    print(f"- 视频: {duration:.2f}s，{media}，平均响度 {mean_volume:.1f} dB")
    print("- 章节素材: 14 张场景图、14 段语音、14 个视频片段")
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except AssertionError as error:
        print(f"校验失败: {error}", file=sys.stderr)
        raise SystemExit(1)
