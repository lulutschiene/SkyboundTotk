"""Draws the Skybound app icon (a floating island with a waterfall under a paraglider) into skybound.ico, standard
library only: each size is filled at 4x by scanlines and averaged down, then stored as PNG frames in one .ico.
Usage: python make_icon.py [out.ico]"""
import math
import struct
import sys
import zlib
from pathlib import Path

SIZES = (16, 24, 32, 48, 64, 128, 256)
SS = 4   # samples per pixel along each axis


def quad(p0, p1, p2, n=16):
    return [((1 - t) ** 2 * p0[0] + 2 * (1 - t) * t * p1[0] + t * t * p2[0],
             (1 - t) ** 2 * p0[1] + 2 * (1 - t) * t * p1[1] + t * t * p2[1]) for t in (i / n for i in range(1, n + 1))]


def circle(cx, cy, r, n=48):
    return [(cx + r * math.cos(2 * math.pi * i / n), cy + r * math.sin(2 * math.pi * i / n)) for i in range(n)]


def rounded_rect(x, y, w, h, r, n=12):
    pts = []
    for cx, cy, a0 in ((x + w - r, y + r, -90), (x + w - r, y + h - r, 0), (x + r, y + h - r, 90), (x + r, y + r, 180)):
        pts += [(cx + r * math.cos(math.radians(a0 + 90 * i / n)), cy + r * math.sin(math.radians(a0 + 90 * i / n)))
                for i in range(n + 1)]
    return pts


def line(a, b, width):
    dx, dy = b[0] - a[0], b[1] - a[1]
    k = width / 2 / math.hypot(dx, dy)
    nx, ny = -dy * k, dx * k
    return [(a[0] + nx, a[1] + ny), (b[0] + nx, b[1] + ny), (b[0] - nx, b[1] - ny), (a[0] - nx, a[1] - ny)]


# The icon in a 160 x 160 design space, painted in order: (RGBA, polygon)
SKY, ROCK, GRASS, WATER, CANOPY, CORD = ((0x1F, 0x6F, 0x8B), (0x6B, 0x4A, 0x35), (0x5F, 0xA3, 0x5A),
                                         (0xCD, 0xEB, 0xF5), (0xE8, 0xB0, 0x4B), (0xF6, 0xE7, 0xC4))
SHAPES = [
    (SKY, rounded_rect(0, 0, 160, 160, 36)),
    (ROCK, [(32, 82)] + quad((32, 82), (80, 68), (128, 82)) + [(114, 94), (94, 126), (80, 144), (66, 126), (46, 94)]),
    (GRASS, [(32, 82)] + quad((32, 82), (80, 68), (128, 82)) + quad((128, 82), (122, 92), (114, 94))
     + quad((114, 94), (80, 84), (46, 94)) + quad((46, 94), (36, 92), (32, 82))),
    (WATER, [(108, 90), (114, 90), (114, 134), (108, 134)]),
    (CANOPY, [(46, 46)] + quad((46, 46), (80, 22), (114, 46)) + quad((114, 46), (80, 37), (46, 46))),
    (CORD, line((52, 45), (77, 67), 2.5)),
    (CORD, line((108, 45), (83, 67), 2.5)),
    (CORD, circle(80, 71, 6)),
]


def render(size):
    n = size * SS
    scale = n / 160
    label = bytearray(n * n)   # 0 = transparent, else 1 + shape index
    for idx, (_, poly) in enumerate(SHAPES):
        pts = [(x * scale, y * scale) for x, y in poly]
        edges = list(zip(pts, pts[1:] + pts[:1]))
        ys = [p[1] for p in pts]
        for row in range(max(0, int(min(ys))), min(n, int(max(ys)) + 1)):
            y = row + 0.5
            xs = sorted(x0 + (y - y0) * (x1 - x0) / (y1 - y0) for (x0, y0), (x1, y1) in edges if (y0 <= y) != (y1 <= y))
            for a, b in zip(xs[::2], xs[1::2]):
                lo, hi = max(0, int(a + 0.5)), min(n, int(b + 0.5))
                if hi > lo:
                    label[row * n + lo:row * n + hi] = bytes([idx + 1]) * (hi - lo)
    colours = [None] + [c for c, _ in SHAPES]
    out = bytearray(size * size * 4)
    for py in range(size):
        for px in range(size):
            r = g = b = a = 0
            for sy in range(SS):
                base = (py * SS + sy) * n + px * SS
                for v in label[base:base + SS]:
                    if v:
                        c = colours[v]
                        r += c[0]; g += c[1]; b += c[2]; a += 1
            o = (py * size + px) * 4
            if a:
                out[o:o + 4] = bytes((r // a, g // a, b // a, a * 255 // (SS * SS)))
    return bytes(out)


def png(size, rgba):
    def chunk(kind, data):
        return struct.pack(">I", len(data)) + kind + data + struct.pack(">I", zlib.crc32(kind + data) & 0xFFFFFFFF)
    rows = b"".join(b"\0" + rgba[y * size * 4:(y + 1) * size * 4] for y in range(size))
    return (b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", size, size, 8, 6, 0, 0, 0))
            + chunk(b"IDAT", zlib.compress(rows, 9)) + chunk(b"IEND", b""))


def main():
    out = Path(sys.argv[1]) if len(sys.argv) > 1 else Path(__file__).with_name("skybound.ico")
    frames = [(s, png(s, render(s))) for s in SIZES]
    head = struct.pack("<HHH", 0, 1, len(frames))
    offset = 6 + 16 * len(frames)
    entries, blobs = b"", b""
    for s, data in frames:
        entries += struct.pack("<BBBBHHII", s % 256, s % 256, 0, 0, 1, 32, len(data), offset + len(blobs))
        blobs += data
    out.write_bytes(head + entries + blobs)
    (out.with_suffix(".png")).write_bytes(frames[-1][1])
    print(f"wrote {out} ({len(frames)} sizes)")


if __name__ == "__main__":
    main()
