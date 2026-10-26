#!/usr/bin/env python3
"""
Generate Intonaco's Windows and in-app brand assets from the two supplied
logo masters in docs/branding/.

Outputs:
  * src/Intonaco/Assets/intonaco.png  - transparent cat/image mark (dark variant)
  * src/Intonaco/Assets/intonaco-mark-light.png - mark extracted from the primary logo
  * src/Intonaco/Assets/intonaco-mark-dark.png  - transparent dark-surface mark
  * src/Intonaco/Assets/intonaco.ico  - 16/20/24/32/48/64/128/256 px
  * docs/branding/intonaco-logo-light.png - full navy lockup
  * docs/branding/intonaco-logo-dark.png  - full lockup with a light wordmark

The taskbar / EXE uses only the mark, never the horizontal wordmark: at
16-32 px the text would be unreadable and Windows app icons should read as
a symbol.

Usage:
    pip install pillow
    python tools/generate-icon.py
"""

from collections import deque
from pathlib import Path

from PIL import Image, ImageFilter

ROOT = Path(__file__).resolve().parent.parent
SOURCE = ROOT / "docs" / "branding" / "intonaco-logo-transparent.webp"
PRIMARY_SOURCE = ROOT / "docs" / "branding" / "intonaco-logo-primary.webp"
BRANDING = ROOT / "docs" / "branding"
ASSETS = ROOT / "src" / "Intonaco" / "Assets"

ALPHA_THRESHOLD = 8
ICON_MASTER_SIZE = 512
ICON_CONTENT_SIZE = 440  # Leaves a quiet 36 px margin on every side.
DARK_WORDMARK = (244, 247, 250, 255)
ICO_SIZES = [(16, 16), (20, 20), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)]


def alpha_bbox(image: Image.Image) -> tuple[int, int, int, int]:
    """Return the bounding box of pixels that are meaningfully visible."""
    alpha = image.getchannel("A").point(lambda value: 255 if value > ALPHA_THRESHOLD else 0)
    bbox = alpha.getbbox()
    if bbox is None:
        raise ValueError("The logo master contains no visible pixels.")
    return bbox


def alpha_column_ranges(image: Image.Image) -> list[tuple[int, int]]:
    """Find contiguous X ranges that contain at least one visible pixel."""
    alpha = image.getchannel("A")
    pixels = alpha.load()
    ranges: list[tuple[int, int]] = []
    start: int | None = None

    for x in range(image.width):
        visible = any(pixels[x, y] > ALPHA_THRESHOLD for y in range(image.height))
        if visible and start is None:
            start = x
        elif not visible and start is not None:
            ranges.append((start, x))
            start = None

    if start is not None:
        ranges.append((start, image.width))

    if len(ranges) < 2:
        raise ValueError("Could not separate the logo mark from the wordmark.")
    return ranges


def _contiguous_ranges(values: list[bool]) -> list[tuple[int, int]]:
    ranges: list[tuple[int, int]] = []
    start: int | None = None
    for index, visible in enumerate(values):
        if visible and start is None:
            start = index
        elif not visible and start is not None:
            ranges.append((start, index))
            start = None
    if start is not None:
        ranges.append((start, len(values)))
    return ranges


def extract_primary_mark() -> Image.Image:
    """Extract the mark from the supplied primary logo on its cream field.

    The primary source is an opaque brand sheet that also contains a text
    label above the lockup. The largest non-background row band is the
    actual lockup. A border flood-fill removes only exterior cream, which
    preserves the cream sun inside the mark.
    """
    source = Image.open(PRIMARY_SOURCE).convert("RGBA")
    background = source.getpixel((5, 5))[:3]

    def distance(pixel: tuple[int, int, int, int]) -> int:
        return max(abs(pixel[channel] - background[channel]) for channel in range(3))

    visible = [
        [distance(source.getpixel((x, y))) > 28 for x in range(source.width)]
        for y in range(source.height)
    ]
    row_ranges = _contiguous_ranges([any(row) for row in visible])
    logo_top, logo_bottom = max(row_ranges, key=lambda bounds: bounds[1] - bounds[0])
    column_ranges = _contiguous_ranges([
        any(visible[y][x] for y in range(logo_top, logo_bottom))
        for x in range(source.width)
    ])
    mark_left, mark_right = column_ranges[0]

    pad = 10
    crop_box = (
        max(0, mark_left - pad),
        max(0, logo_top - pad),
        min(source.width, mark_right + pad),
        min(source.height, logo_bottom + pad),
    )
    cropped = source.crop(crop_box)

    near_background = [
        [distance(cropped.getpixel((x, y))) <= 42 for x in range(cropped.width)]
        for y in range(cropped.height)
    ]
    exterior: set[tuple[int, int]] = set()
    queue: deque[tuple[int, int]] = deque()

    def enqueue(x: int, y: int) -> None:
        point = (x, y)
        if point not in exterior and near_background[y][x]:
            exterior.add(point)
            queue.append(point)

    for x in range(cropped.width):
        enqueue(x, 0)
        enqueue(x, cropped.height - 1)
    for y in range(cropped.height):
        enqueue(0, y)
        enqueue(cropped.width - 1, y)

    while queue:
        x, y = queue.popleft()
        for nx, ny in ((x - 1, y), (x + 1, y), (x, y - 1), (x, y + 1)):
            if 0 <= nx < cropped.width and 0 <= ny < cropped.height:
                enqueue(nx, ny)

    alpha = Image.new("L", cropped.size, 255)
    alpha_pixels = alpha.load()
    for x, y in exterior:
        alpha_pixels[x, y] = 0

    # Remove the one-pixel cream fringe created by the source's opaque
    # anti-aliased edge, then keep only the visible mark.
    cropped.putalpha(alpha.filter(ImageFilter.MinFilter(3)))
    return cropped.crop(alpha_bbox(cropped))


def fit_centered(mark: Image.Image, canvas_size: int, content_size: int) -> Image.Image:
    fitted = mark.copy()
    fitted.thumbnail((content_size, content_size), Image.Resampling.LANCZOS)

    canvas = Image.new("RGBA", (canvas_size, canvas_size), (0, 0, 0, 0))
    x = (canvas_size - fitted.width) // 2
    y = (canvas_size - fitted.height) // 2
    canvas.alpha_composite(fitted, (x, y))
    return canvas


def make_light_lockup(
    lockup: Image.Image,
    primary_mark: Image.Image,
    mark_left: int,
    mark_right: int,
    wordmark_left: int,
) -> Image.Image:
    """Pair the primary mark with the transparent master's crisp wordmark."""
    result = Image.new("RGBA", lockup.size, (0, 0, 0, 0))
    mark = primary_mark.copy()
    mark.thumbnail((mark_right - mark_left, lockup.height), Image.Resampling.LANCZOS)
    x = mark_left + ((mark_right - mark_left) - mark.width) // 2
    y = (lockup.height - mark.height) // 2
    result.alpha_composite(mark, (x, y))
    result.alpha_composite(
        lockup.crop((wordmark_left, 0, lockup.width, lockup.height)),
        (wordmark_left, 0),
    )
    return result


def make_dark_lockup(lockup: Image.Image, wordmark_left: int) -> Image.Image:
    """Recolor only the wordmark; preserve the mark and all alpha edges."""
    result = lockup.copy()
    wordmark = lockup.crop((wordmark_left, 0, lockup.width, lockup.height))
    recolored = Image.new("RGBA", wordmark.size, DARK_WORDMARK)
    recolored.putalpha(wordmark.getchannel("A"))
    result.alpha_composite(recolored, (wordmark_left, 0))
    return result


def main() -> None:
    source = Image.open(SOURCE).convert("RGBA")
    lockup = source.crop(alpha_bbox(source))

    ranges = alpha_column_ranges(lockup)
    mark_left, mark_right = ranges[0]
    wordmark_left = ranges[1][0]

    mark_region = lockup.crop((mark_left, 0, mark_right, lockup.height))
    mark = mark_region.crop(alpha_bbox(mark_region))
    icon_master = fit_centered(mark, ICON_MASTER_SIZE, ICON_CONTENT_SIZE)
    primary_mark = extract_primary_mark()
    light_mark_master = fit_centered(
        primary_mark, ICON_MASTER_SIZE, ICON_CONTENT_SIZE)

    ASSETS.mkdir(parents=True, exist_ok=True)
    BRANDING.mkdir(parents=True, exist_ok=True)

    icon_png = icon_master.resize((256, 256), Image.Resampling.LANCZOS)
    icon_png.save(ASSETS / "intonaco.png")
    icon_png.save(ASSETS / "intonaco-mark-dark.png")
    light_mark_master.resize((256, 256), Image.Resampling.LANCZOS).save(
        ASSETS / "intonaco-mark-light.png")
    icon_master.save(ASSETS / "intonaco.ico", format="ICO", sizes=ICO_SIZES)

    make_light_lockup(
        lockup, primary_mark, mark_left, mark_right, wordmark_left
    ).save(BRANDING / "intonaco-logo-light.png")
    make_dark_lockup(lockup, wordmark_left).save(BRANDING / "intonaco-logo-dark.png")

    print(f"Wrote {ASSETS / 'intonaco.png'}")
    print(f"Wrote {ASSETS / 'intonaco-mark-light.png'}")
    print(f"Wrote {ASSETS / 'intonaco-mark-dark.png'}")
    print(f"Wrote {ASSETS / 'intonaco.ico'}")
    print(f"Wrote {BRANDING / 'intonaco-logo-light.png'}")
    print(f"Wrote {BRANDING / 'intonaco-logo-dark.png'}")


if __name__ == "__main__":
    main()
