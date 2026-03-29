# PomogatorLauncher — только ShazamIO: https://github.com/shazamio/ShazamIO
# WAV целиком со stdin → shazam.recognize(data=bytes)
# pip install shazamio
import asyncio
import json
import sys


async def main() -> None:
    data = sys.stdin.buffer.read()
    if len(data) < 256:
        print(json.dumps({"ok": False, "error": "Мало аудиоданных со stdin"}, ensure_ascii=False))
        sys.exit(2)

    try:
        from shazamio import Shazam
    except ImportError:
        print(
            json.dumps(
                {
                    "ok": False,
                    "error": "Установите: pip install shazamio (https://github.com/shazamio/ShazamIO)",
                },
                ensure_ascii=False,
            )
        )
        sys.exit(3)

    try:
        shazam = Shazam(language="ru-RU", endpoint_country="RU", segment_duration_seconds=10)
        out = await shazam.recognize(data=data)
    except Exception as e:
        print(json.dumps({"ok": False, "error": str(e)}, ensure_ascii=False))
        sys.exit(1)

    matches = out.get("matches") or []
    if not matches:
        print(
            json.dumps(
                {"ok": False, "error": "no_match"},
                ensure_ascii=False,
            )
        )
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
        print(json.dumps({"ok": False, "error": "no_match"}, ensure_ascii=False))
        sys.exit(0)

    print(
        json.dumps(
            {
                "ok": True,
                "title": title or "Неизвестный трек",
                "artist": subtitle or "Неизвестный исполнитель",
                "coverUrl": cover,
            },
            ensure_ascii=False,
        )
    )


if __name__ == "__main__":
    asyncio.run(main())
