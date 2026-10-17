"""
Builds TaoSFV's icons from art/icon-source.png (run from the repository root: python tools/make-icon.py).

Outputs
  TaoSFV/TaoSFV.ico          16-256 px, for TaoSFV.exe, taosfv-cli.exe, file types and the installer
  art/taosfv-window.ico      16-64 px, embedded in MainForm.resx for the window and tray icon
  art/taosfv-about.png       96 px, the Preferences > About picture
  art/taosfv-256.png         README logo
  art/taosfv-1024.png        master

The artwork is cropped to a square around its frame and given rounded, transparent corners. At 16-32 px the
translucent glass check almost disappears, so those sizes get a brightened check; larger sizes are unchanged.
"""
import os
from PIL import Image, ImageChops, ImageDraw, ImageFilter

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SOURCE = os.path.join(ROOT, 'art', 'icon-source.png')
ICO_SIZES = [16, 20, 24, 32, 40, 48, 64, 96, 128, 256]
WINDOW_SIZES = [16, 20, 24, 32, 40, 48, 64]
SMALL = 32  # sizes up to this get the brightened check

# measured on the source: the frame spans x 222..946, y 91..805 (corner radius ~25); the check reaches x 1019, y 822
FRAME = (222, 91, 946, 805)
MARGIN = 74       # even margin around the frame: keeps it centred and the check inside the tile
FRAME_RADIUS = 25


def build_master():
    art = Image.open(SOURCE).convert('RGB')
    box = (FRAME[0] - MARGIN, FRAME[1] - MARGIN, FRAME[2] + MARGIN, FRAME[3] + MARGIN)
    tile = art.crop(box)
    side = max(tile.size)
    square = Image.new('RGB', (side, side), art.getpixel((40, 40)))
    square.paste(tile, ((side - tile.width) // 2, (side - tile.height) // 2))

    ss = 4  # supersampled mask for smooth corners
    mask = Image.new('L', (side * ss, side * ss), 0)
    ImageDraw.Draw(mask).rounded_rectangle((0, 0, side * ss - 1, side * ss - 1), radius=(FRAME_RADIUS + MARGIN) * ss, fill=255)
    mask = mask.resize((side, side), Image.Resampling.LANCZOS)

    master = square.convert('RGBA')
    master.putalpha(mask)
    return master.resize((1024, 1024), Image.Resampling.LANCZOS)


def brighten_check(master):
    rgb = master.convert('RGB')
    r, g, b = rgb.split()
    # the glass check is the only clearly blue-tinted part, in the bottom-right corner
    blue = ImageChops.subtract(b, r).point(lambda v: 0 if v < 8 else min(255, (v - 8) * 18))
    corner = Image.new('L', master.size, 0)
    ImageDraw.Draw(corner).polygon([(560, 1024), (1024, 1024), (1024, 520), (700, 700)], fill=255)
    check = ImageChops.multiply(blue, corner).filter(ImageFilter.MaxFilter(9)).filter(ImageFilter.GaussianBlur(3))
    glow = Image.new('RGB', master.size, (205, 232, 255))
    out = Image.composite(glow, rgb, check.point(lambda v: int(v * 0.85))).convert('RGBA')
    out.putalpha(master.getchannel('A'))
    return out


def render(master, small_master, size):
    src = small_master if size <= SMALL else master
    im = src.resize((size, size), Image.Resampling.LANCZOS)
    if size <= 48:
        # downscaling softens detail; a light unsharp mask keeps the disc and check readable
        rgb = im.convert('RGB').filter(ImageFilter.UnsharpMask(radius=0.6, percent=60, threshold=1))
        rgb.putalpha(im.getchannel('A'))
        im = rgb
    return im


def save_ico(path, images, sizes):
    largest = images[max(sizes)]
    largest.save(path, format='ICO', sizes=[(s, s) for s in sizes], append_images=[images[s] for s in sizes if s != max(sizes)])


def main():
    master = build_master()
    small_master = brighten_check(master)
    images = {s: render(master, small_master, s) for s in sorted(set(ICO_SIZES + WINDOW_SIZES))}

    art = os.path.join(ROOT, 'art')
    master.save(os.path.join(art, 'taosfv-1024.png'))
    images[256].save(os.path.join(art, 'taosfv-256.png'))
    render(master, small_master, 96).save(os.path.join(art, 'taosfv-about.png'))
    save_ico(os.path.join(ROOT, 'TaoSFV', 'TaoSFV.ico'), images, ICO_SIZES)
    save_ico(os.path.join(art, 'taosfv-window.ico'), images, WINDOW_SIZES)
    print('wrote TaoSFV/TaoSFV.ico and art/*')


if __name__ == '__main__':
    main()
