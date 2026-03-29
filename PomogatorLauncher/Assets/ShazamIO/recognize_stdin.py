# PomogatorLauncher — только ShazamIO: https://github.com/shazamio/ShazamIO
# WAV целиком со stdin → shazam.recognize(data=bytes)
# pip install shazamio
#
# Важно: на Windows print() кодирует в cp1251/OEM — C# читает stdout как UTF-8.
# Пишем JSON только в sys.stdout.buffer в UTF-8.
import asyncio
import json
import sys


def out_json(obj: dict) -> None:
    line = json.dumps(obj, ensure_ascii=False) + "\n"
    sys.stdout.buffer.write(line.encode("utf-8"))
    sys.stdout.buffer.flush()


async def main() -> None:
    data = sys.stdin.buffer.read()
    if len(data) < 256:
        out_json({"ok": False, "error": "Мало аудиоданных со stdin"})
        sys.exit(2)

    try:
        from shazamio import Shazam
    except ImportError:
        out_json(
            {
                "ok": False,
                "error": "Установите: pip install shazamio (https://github.com/shazamio/ShazamIO)",
            }
        )
        sys.exit(3)

    try:
        shazam = Shazam(language="ru-RU", endpoint_country="RU", segment_duration_seconds=10)
        out = await shazam.recognize(data=data)
    except Exception as e:
        out_json({"ok": False, "error": str(e)})
        sys.exit(1)

    matches = out.get("matches") or []
    if not matches:
        out_json({"ok": False, "error": "no_match"})
        sys.exit(0)

    track = out.get("track") or {}
    title = (track.get("title") or "").strip()
    subtitle = (track.get("subtitle") or "").strip()

    cover = None
    share = track.get("share")
    if isinstance(share, dict) and share.get("image"):
        cover = share["image"]

    images = track.get("images")
    if isinstance(images, dict):
        cover = images.get("coverarthq") or images.get("background") or cover

    if not title and not subtitle:
        out_json({"ok": False, "error": "no_match"})
        sys.exit(0)

    out_json(
        {
            "ok": True,
            "title": title or "Неизвестный трек",
            "artist": subtitle or "Неизвестный исполнитель",
            "coverUrl": cover,
        }
    )


if __name__ == "__main__":
    asyncio.run(main())
