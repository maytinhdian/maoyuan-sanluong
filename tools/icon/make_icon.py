"""Vẽ icon app (màn hình TV có biểu đồ cột) ra src/DisplayBoard.App/Assets/app.ico.

Chạy: python3 tools/icon/make_icon.py   (cần Pillow)
"""
from pathlib import Path
from PIL import Image, ImageDraw

S = 1024  # vẽ lớn rồi thu nhỏ cho mượt
OUT = Path(__file__).resolve().parents[2] / "src" / "DisplayBoard.App" / "Assets"


def draw() -> Image.Image:
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    # Nền bo góc xanh navy (màu nền bảng TV).
    d.rounded_rectangle((0, 0, S - 1, S - 1), radius=200, fill="#0B1E3F")
    # Khung TV.
    d.rounded_rectangle((110, 170, S - 110, 740), radius=60, fill="#1A3A6B", outline="#4FA3FF", width=36)
    # Chân TV.
    d.rectangle((452, 740, 572, 820), fill="#4FA3FF")
    d.rounded_rectangle((330, 810, 694, 870), radius=30, fill="#4FA3FF")
    # 3 cột sản lượng: đỏ / vàng / xanh (màu trạng thái trong app), tăng dần.
    base = 650
    bars = [(215, 440, "#EF4444"), (430, 340, "#F5B301"), (645, 240, "#22C55E")]
    for x, top, color in bars:
        d.rounded_rectangle((x, top, x + 165, base), radius=24, fill=color)
    return img


def main() -> None:
    OUT.mkdir(parents=True, exist_ok=True)
    img = draw()
    img.save(OUT / "app.ico", sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)])
    print("Đã tạo", OUT / "app.ico")


if __name__ == "__main__":
    main()
