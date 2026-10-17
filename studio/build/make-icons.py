"""Draws Studio's app icons into studio/public: the side rail's mark, the accent
ring with a dot at its centre, on Dark's ground. Run by hand when the mark
changes; the PNGs are committed, so a build never needs Python.

The brand book says Studio has no logo yet, so the icon is the mark the rail
already shows rather than something drawn for the occasion.
"""
from pathlib import Path
from PIL import Image, ImageDraw, ImageFilter

GROUND = (3, 7, 12, 255)
ACCENT = (111, 227, 255, 255)
HERE = Path(__file__).resolve().parent.parent / "public"


def mark(size: int, safe: float) -> Image.Image:
    """The mark at a size, inside a safe zone (a fraction of the edge) for maskable icons."""
    scale = 4
    big = size * scale
    image = Image.new("RGBA", (big, big), GROUND)
    glow = Image.new("RGBA", (big, big), (0, 0, 0, 0))
    centre = big / 2
    radius = big * safe * 0.36
    ring = max(2, round(big * 0.028))
    dot = radius * 0.46

    draw = ImageDraw.Draw(glow)
    draw.ellipse((centre - radius, centre - radius, centre + radius, centre + radius), outline=ACCENT, width=ring * 3)
    glow = glow.filter(ImageFilter.GaussianBlur(big * 0.03))
    image.alpha_composite(Image.blend(Image.new("RGBA", (big, big), (0, 0, 0, 0)), glow, 0.55))

    draw = ImageDraw.Draw(image)
    draw.ellipse((centre - radius, centre - radius, centre + radius, centre + radius), outline=ACCENT, width=ring)
    draw.ellipse((centre - dot, centre - dot, centre + dot, centre + dot), fill=ACCENT)

    return image.resize((size, size), Image.LANCZOS)


for size in (192, 512):
    mark(size, 1.0).save(HERE / f"icon-{size}.png", optimize=True)
    mark(size, 0.8).save(HERE / f"icon-maskable-{size}.png", optimize=True)

print("drew", sorted(p.name for p in HERE.glob("icon-*.png")))
