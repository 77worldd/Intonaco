#!/usr/bin/env python3
"""
Generate Intonaco's application icon from the design in
src/Intonaco/Assets/intonaco-icon.svg.

The icon is drawn at 4x supersampling with Pillow (rounded-square
blue gradient, plaster texture lines, white play triangle), then
saved as a multi-resolution .ico (16/20/24/32/48/64/128/256) plus
a 256 px PNG used by the README/docs.

Usage:
    pip install pillow
    python tools/generate-icon.py
"""

from pathlib import Path

from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parent.parent
ASSETS = ROOT / "src" / "Intonaco" / "Assets"

SIZE = 256          # SVG viewBox size
SS = 4              # supersampling factor
TOP = (0x00, 0x78, 0xD4)     # #0078D4
BOTTOM = (0x00, 0x5A, 0x9E)  # #005A9E


def render() -> Image.Image:
    s = SIZE * SS
    img = Image.new("RGBA", (s, s), (0, 0, 0, 0))

    # Diagonal gradient (top-left -> bottom-right), as in the SVG.
    grad = Image.new("RGBA", (s, s))
    px = grad.load()
    for y in range(s):
        for x in range(0, s, 8):  # fill in 8px strips; visually identical
            t = (x + y) / (2 * s)
            color = tuple(round(TOP[i] + (BOTTOM[i] - TOP[i]) * t) for i in range(3)) + (255,)
            for dx in range(8):
                if x + dx < s:
                    px[x + dx, y] = color

    # Rounded-square mask: inset 16, corner radius 48 (SVG coordinates).
    mask = Image.new("L", (s, s), 0)
    md = ImageDraw.Draw(mask)
    md.rounded_rectangle(
        [16 * SS, 16 * SS, (SIZE - 16) * SS, (SIZE - 16) * SS],
        radius=48 * SS,
        fill=255,
    )
    img.paste(grad, (0, 0), mask)

    d = ImageDraw.Draw(img, "RGBA")

    # Plaster texture lines (rgba(255,255,255,0.15), width 3, round caps).
    line_color = (255, 255, 255, 38)
    half = 1.5 * SS
    for yy in (80, 120, 160):
        y = yy * SS
        d.line([(56 * SS, y), (200 * SS, y)], fill=line_color, width=3 * SS)
        for cx in (56 * SS, 200 * SS):
            d.ellipse([cx - half, y - half, cx + half, y + half], fill=line_color)

    # Play triangle (white, 95% opacity).
    d.polygon(
        [(108 * SS, 76 * SS), (108 * SS, 180 * SS), (188 * SS, 128 * SS)],
        fill=(255, 255, 255, 242),
    )

    return img.resize((SIZE, SIZE), Image.LANCZOS)


def main() -> None:
    icon = render()
    ASSETS.mkdir(parents=True, exist_ok=True)

    icon.save(ASSETS / "intonaco.png")
    icon.save(
        ASSETS / "intonaco.ico",
        format="ICO",
        sizes=[(16, 16), (20, 20), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)],
    )
    print(f"Wrote {ASSETS / 'intonaco.ico'}")
    print(f"Wrote {ASSETS / 'intonaco.png'}")


if __name__ == "__main__":
    main()
