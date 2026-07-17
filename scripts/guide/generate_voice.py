from __future__ import annotations

import argparse
import asyncio
import json
import os
import subprocess
import wave
from pathlib import Path

import edge_tts
import imageio_ffmpeg


ROOT = Path(__file__).resolve().parents[2]
CONTENT_PATH = ROOT / "scripts" / "guide" / "video_content.json"
AUDIO_DIR = ROOT / "操作指导" / "assets" / "audio"


def wav_duration(path: Path) -> float:
    with wave.open(os.fspath(path), "rb") as stream:
        return stream.getnframes() / stream.getframerate()


async def select_voice() -> str:
    voices = await edge_tts.list_voices()
    candidates = [
        voice
        for voice in voices
        if voice.get("Locale") == "zh-CN" and voice.get("Gender") == "Female"
    ]
    if not candidates:
        raise RuntimeError("在线语音服务没有返回可用的 zh-CN 普通话女声。")

    preferred_fragments = ("Xiaoxiao", "Xiaoyi", "Xiaohan", "Xiaomeng")
    for fragment in preferred_fragments:
        match = next(
            (
                voice
                for voice in candidates
                if fragment.casefold() in voice["ShortName"].casefold()
            ),
            None,
        )
        if match is not None:
            return match["ShortName"]
    return candidates[0]["ShortName"]


async def synthesize_scene(
    text: str,
    voice: str,
    rate: str,
    mp3_path: Path,
    wav_path: Path,
    ffmpeg: str,
) -> float:
    communicator = edge_tts.Communicate(
        text=text,
        voice=voice,
        rate=rate,
        volume="+0%",
        pitch="+0Hz",
    )
    await communicator.save(os.fspath(mp3_path))

    subprocess.run(
        [
            ffmpeg,
            "-hide_banner",
            "-loglevel",
            "error",
            "-y",
            "-i",
            os.fspath(mp3_path),
            "-ar",
            "48000",
            "-ac",
            "1",
            "-c:a",
            "pcm_s16le",
            os.fspath(wav_path),
        ],
        check=True,
    )
    return wav_duration(wav_path)


async def main_async(rate: str) -> None:
    scenes = json.loads(CONTENT_PATH.read_text(encoding="utf-8"))
    AUDIO_DIR.mkdir(parents=True, exist_ok=True)
    voice = await select_voice()
    ffmpeg = imageio_ffmpeg.get_ffmpeg_exe()
    total_duration = 0.0

    print(f"Voice: {voice}")
    print(f"Rate: {rate}")
    print(f"FFmpeg: {ffmpeg}")

    for scene in scenes:
        chapter = int(scene["id"])
        mp3_path = AUDIO_DIR / f"chapter-{chapter:02}.mp3"
        wav_path = AUDIO_DIR / f"chapter-{chapter:02}.wav"
        duration = await synthesize_scene(
            scene["narration"],
            voice,
            rate,
            mp3_path,
            wav_path,
            ffmpeg,
        )
        total_duration += duration
        target = float(scene["duration"])
        status = "OK" if duration <= target - 0.6 else "LONG"
        print(
            f"chapter-{chapter:02}: {duration:.2f}s / "
            f"target {target:.0f}s [{status}]"
        )

    print(f"Total voice duration: {total_duration:.2f}s")
    print(f"Timeline duration: {sum(float(scene['duration']) for scene in scenes):.2f}s")


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--rate", default="-8%")
    args = parser.parse_args()
    asyncio.run(main_async(args.rate))


if __name__ == "__main__":
    main()
