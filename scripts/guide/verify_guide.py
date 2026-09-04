from __future__ import annotations

import argparse
import sys
from pathlib import Path

from pypdf import PdfReader


ROOT = Path(__file__).resolve().parents[2]
GUIDE_DIR = ROOT / "操作指导"
HTML_PATH = GUIDE_DIR / "有序连点器-使用说明.html"
PDF_PATH = GUIDE_DIR / "有序连点器-使用说明.pdf"

REQUIRED_TERMS = (
    "2.0.0",
    "五步",
    ".oclick",
    "迁移旧方案",
    "活动草稿",
    "2 秒后记录",
    "F6",
    "F7",
    "F8",
    "安全角",
    "计划点击",
    "预计耗时",
    "可靠断点",
    "云桌面",
    "画面稳定",
    "有序连点器-免安装.exe",
    "SHA256SUMS.txt",
)

FORBIDDEN_TERMS = (
    "1.3.1",
    "导入为副本",
    "ordered-clicker-portable-v1.3.1.zip",
)


def require_file(path: Path, minimum_size: int) -> None:
    if not path.is_file():
        raise AssertionError(f"文件不存在: {path}")
    if path.stat().st_size < minimum_size:
        raise AssertionError(f"文件过小: {path} ({path.stat().st_size} bytes)")


def verify_text(name: str, text: str) -> None:
    missing = [term for term in REQUIRED_TERMS if term not in text]
    if missing:
        raise AssertionError(f"{name} 缺少关键内容: {', '.join(missing)}")
    stale = [term for term in FORBIDDEN_TERMS if term in text]
    if stale:
        raise AssertionError(f"{name} 含过时内容: {', '.join(stale)}")


def main() -> int:
    require_file(HTML_PATH, 100_000)
    require_file(PDF_PATH, 100_000)

    html = HTML_PATH.read_text(encoding="utf-8")
    verify_text("HTML", html)
    if "data:image/png;base64," not in html:
        raise AssertionError("HTML 未内嵌界面截图")
    if 'src="assets/' in html or 'href="assets/' in html:
        raise AssertionError("HTML 仍依赖外部 assets 目录")

    reader = PdfReader(str(PDF_PATH))
    if len(reader.pages) < 8:
        raise AssertionError(f"PDF 页数不足: {len(reader.pages)}")
    pdf_text = "\n".join(page.extract_text() or "" for page in reader.pages)
    verify_text("PDF", pdf_text)

    print("2.0.0 使用说明校验通过")
    print(f"- HTML: {HTML_PATH.stat().st_size} bytes，自包含截图")
    print(f"- PDF: {len(reader.pages)} 页，{PDF_PATH.stat().st_size} bytes")
    return 0


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument(
        "--skip-video",
        action="store_true",
        help="兼容旧命令；2.0.0 交付不包含视频。",
    )
    parser.parse_args()
    try:
        raise SystemExit(main())
    except AssertionError as error:
        print(f"校验失败: {error}", file=sys.stderr)
        raise SystemExit(1)
