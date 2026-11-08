"""KATALOG icon: a deep blue rounded square with three stacked discs (a catalogue of disks) and a white letter K.

Run: python3 tools/ikona/vytvor_ikonu.py  ->  src/Katalog.Okno/Zdroje/katalog.ico and katalog-256.png
Only Pillow and a free font (DejaVu Sans Bold) are used; the drawing is the project's own.
"""
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / "src" / "Katalog.Okno" / "Zdroje"
FONT = "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf"
S = 1024  # drawn large, then scaled down for every size


def draw() -> Image.Image:
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    # background: deep blue gradient in a rounded square
    for y in range(S):
        t = y / S
        c = (int(18 + 20 * t), int(52 + 30 * t), int(110 + 40 * t), 255)
        d.line([(0, y), (S, y)], fill=c)
    mask = Image.new("L", (S, S), 0)
    ImageDraw.Draw(mask).rounded_rectangle([0, 0, S - 1, S - 1], radius=200, fill=255)
    img.putalpha(mask)
    d = ImageDraw.Draw(img)
    # three stacked discs in the lower right
    for i, col in enumerate([(120, 190, 255, 255), (90, 160, 235, 255), (60, 130, 215, 255)]):
        top = 640 + i * 88
        d.rounded_rectangle([430, top, 900, top + 70], radius=35, fill=col)
        d.ellipse([820, top + 20, 850, top + 50], fill=(255, 255, 255, 230))
    # the letter K
    font = ImageFont.truetype(FONT, 500)
    d.text((130, 30), "K", font=font, fill=(255, 255, 255, 255))
    return img


def main() -> None:
    OUT.mkdir(parents=True, exist_ok=True)
    big = draw()
    sizes = [16, 24, 32, 48, 64, 128, 256]
    big.resize((256, 256), Image.LANCZOS).save(OUT / "katalog-256.png")
    big.resize((256, 256), Image.LANCZOS).save(OUT / "katalog.ico", sizes=[(s, s) for s in sizes])
    print("written", OUT / "katalog.ico")


if __name__ == "__main__":
    main()
