#!/usr/bin/env python3
"""Generates assets/app.ico: three accent dots in a triangle on the family's dark
brown, one dot per tool. Same pure-stdlib pipeline as the sibling repos.

Usage: python3 tools/make-icons.py
"""

import os
import struct
import zlib

SIZES = (16, 32, 48, 256)
SS = 4

BG = (0x12, 0x10, 0x0E)
ACCENT = (0xE0, 0x91, 0x3F)
TEXT = (0xF2, 0xEC, 0xE2)


def _circle(cx, cy, r):
    return lambda x, y: (x - cx) ** 2 + (y - cy) ** 2 <= r * r


def _rounded(x0, y0, x1, y1, r):
    def inside(x, y):
        if not (x0 <= x <= x1 and y0 <= y <= y1):
            return False
        cx = min(max(x, x0 + r), x1 - r)
        cy = min(max(y, y0 + r), y1 - r)
        return (x - cx) ** 2 + (y - cy) ** 2 <= r * r
    return inside


def shapes(size):
    # Dot radius floors at device pixels so 16px stays three dots, not a smear.
    r = max(0.13, 2.2 / size)
    layers = [(_rounded(0.02, 0.02, 0.98, 0.98, 0.20), BG)]
    layers.append((_circle(0.50, 0.30, r), ACCENT))
    layers.append((_circle(0.32, 0.66, r), ACCENT))
    layers.append((_circle(0.68, 0.66, r), TEXT))
    return layers


def render(size, layers):
    rows = []
    for py in range(size):
        row = bytearray()
        for px in range(size):
            acc = [0.0, 0.0, 0.0, 0.0]
            for sy in range(SS):
                y = (py + (sy + 0.5) / SS) / size
                for sx in range(SS):
                    x = (px + (sx + 0.5) / SS) / size
                    hit = None
                    for shape, colour in layers:
                        if shape(x, y):
                            hit = colour
                    if hit is not None:
                        acc[0] += hit[0]
                        acc[1] += hit[1]
                        acc[2] += hit[2]
                        acc[3] += 255.0
            n = SS * SS
            a = acc[3] / n
            if a <= 0.0:
                row += b"\x00\x00\x00\x00"
                continue
            covered = acc[3] / 255.0
            row += bytes((
                int(round(acc[0] / covered)),
                int(round(acc[1] / covered)),
                int(round(acc[2] / covered)),
                int(round(a)),
            ))
        rows.append(bytes(row))
    return rows


def png(size, rows):
    raw = b"".join(b"\x00" + r for r in rows)

    def chunk(tag, data):
        body = tag + data
        return struct.pack(">I", len(data)) + body + struct.pack(">I", zlib.crc32(body))

    return (
        b"\x89PNG\r\n\x1a\n"
        + chunk(b"IHDR", struct.pack(">IIBBBBB", size, size, 8, 6, 0, 0, 0))
        + chunk(b"IDAT", zlib.compress(raw, 9))
        + chunk(b"IEND", b"")
    )


def ico(images):
    count = len(images)
    header = struct.pack("<HHH", 0, 1, count)
    offset = 6 + 16 * count
    entries, blobs = b"", b""
    for size, data in images:
        dim = 0 if size >= 256 else size
        entries += struct.pack("<BBBBHHII", dim, dim, 0, 0, 1, 32, len(data), offset)
        blobs += data
        offset += len(data)
    return header + entries + blobs


def main():
    os.makedirs("assets", exist_ok=True)
    images = [(s, png(s, render(s, shapes(s)))) for s in SIZES]
    with open("assets/app.ico", "wb") as fh:
        fh.write(ico(images))
    print("wrote assets/app.ico (%d bytes)" % os.path.getsize("assets/app.ico"))


if __name__ == "__main__":
    main()
