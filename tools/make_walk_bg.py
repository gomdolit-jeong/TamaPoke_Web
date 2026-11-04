# 산책 화면의 풀밭을 만듭니다. 이어 붙여도 경계가 보이지 않는 도트 타일입니다.
#   wwwroot/ui/bg_walk.png         움직이지 않는 땅 (잔디 얼룩, 꽃, 조약돌)
#   wwwroot/ui/bg_walk_sway_N.png  그 위에 겹치는 풀잎 4장. 차례로 보여 주면 바람에 흔들립니다.
# 실행: python tools/make_walk_bg.py
import math
import random
import struct
import zlib
from pathlib import Path

GROUND_SIZE = 160
SWAY_SIZE = 224  # 땅과 크기를 다르게 해서 같은 무늬가 반복되어 보이는 것을 줄입니다.
SWAY_FRAMES = 4
TUFT_COUNT = 150
OUTPUT_DIR = Path(__file__).parent.parent / "TamaPoke.Web" / "wwwroot" / "ui"

BASE = (126, 200, 80)
LIGHT = (140, 212, 92)
DARK = (112, 186, 72)
BLADE = (84, 156, 58)
BLADE_MID = (98, 172, 64)
BLADE_LIGHT = (168, 228, 110)
PETALS = [(255, 255, 255), (255, 226, 92), (255, 168, 196)]
FLOWER_CENTER = (246, 170, 50)
PEBBLE = (170, 176, 150)
PEBBLE_SHADE = (138, 148, 118)
CLEAR = (0, 0, 0, 0)


def chunk(kind, data):
    return struct.pack(">I", len(data)) + kind + data + struct.pack(">I", zlib.crc32(kind + data))


def save_png(name, pixels, color_type):
    raw = b"".join(b"\x00" + bytes(value for pixel in row for value in pixel) for row in pixels)
    path = OUTPUT_DIR / name
    path.write_bytes(
        b"\x89PNG\r\n\x1a\n"
        + chunk(b"IHDR", struct.pack(">IIBBBBB", len(pixels[0]), len(pixels), 8, color_type, 0, 0, 0))
        + chunk(b"IDAT", zlib.compress(raw, 9))
        + chunk(b"IEND", b"")
    )
    print(path.name, path.stat().st_size, "bytes")


def make_ground():
    pixels = [[BASE] * GROUND_SIZE for _ in range(GROUND_SIZE)]

    def put(x, y, color):
        pixels[y % GROUND_SIZE][x % GROUND_SIZE] = color

    # 밝고 어두운 잔디 얼룩 (울퉁불퉁한 타원)
    def patch(cx, cy, rx, ry, color):
        for dy in range(-ry, ry + 1):
            for dx in range(-rx, rx + 1):
                distance = (dx / rx) ** 2 + (dy / ry) ** 2
                if distance + random.uniform(-0.12, 0.12) < 1:
                    put(cx + dx, cy + dy, color)

    for _ in range(9):
        patch(random.randrange(GROUND_SIZE), random.randrange(GROUND_SIZE), random.randint(10, 22), random.randint(6, 12), LIGHT)
    for _ in range(7):
        patch(random.randrange(GROUND_SIZE), random.randrange(GROUND_SIZE), random.randint(8, 18), random.randint(5, 10), DARK)

    # 작은 점 무늬
    for _ in range(140):
        put(random.randrange(GROUND_SIZE), random.randrange(GROUND_SIZE), random.choice([LIGHT, DARK]))

    # 꽃
    for _ in range(12):
        x, y = random.randrange(GROUND_SIZE), random.randrange(GROUND_SIZE)
        petal = random.choice(PETALS)
        for dx, dy in [(-1, 0), (1, 0), (0, -1), (0, 1)]:
            put(x + dx, y + dy, petal)
        put(x, y, FLOWER_CENTER)
        put(x, y + 2, BLADE)

    # 조약돌
    for _ in range(4):
        x, y = random.randrange(GROUND_SIZE), random.randrange(GROUND_SIZE)
        for dx in range(3):
            put(x + dx, y, PEBBLE)
            put(x + dx, y + 1, PEBBLE_SHADE)

    save_png("bg_walk.png", pixels, 2)


# 풀잎 한 포기: 뿌리는 그대로이고 위로 갈수록 바람 쪽으로 기웁니다.
def draw_tuft(pixels, x, y, height, spread, color, lean):
    def put(px, py, c):
        pixels[py % SWAY_SIZE][px % SWAY_SIZE] = c + (255,)

    for blade in range(-spread, spread + 1):
        blade_height = height - abs(blade)
        for step in range(blade_height):
            bend = round(lean * (step / max(blade_height - 1, 1)) ** 2)
            put(x + blade + bend, y - step, color if step > 0 else BLADE)


def make_sway_frames():
    tufts = []
    for _ in range(TUFT_COUNT):
        tufts.append((
            random.randrange(SWAY_SIZE),
            random.randrange(SWAY_SIZE),
            random.choice([3, 3, 4, 4, 5, 6]),
            random.choice([0, 1, 1]),
            random.choice([BLADE, BLADE, BLADE_MID, BLADE_MID, BLADE_LIGHT]),
            random.uniform(1.2, 2.2),
        ))
    tufts.sort(key=lambda t: t[1])  # 앞(아래)에 있는 풀이 뒤의 풀을 가립니다.

    for frame in range(SWAY_FRAMES):
        pixels = [[CLEAR] * SWAY_SIZE for _ in range(SWAY_SIZE)]
        for x, y, height, spread, color, strength in tufts:
            # 위치마다 박자를 다르게 해서 바람이 풀밭을 훑고 지나가는 물결을 만듭니다. (정수 배라 타일 경계가 이어집니다.)
            phase = 2 * math.pi * (frame / SWAY_FRAMES - 2 * x / SWAY_SIZE - y / SWAY_SIZE)
            draw_tuft(pixels, x, y, height, spread, color, strength * math.sin(phase))
        save_png(f"bg_walk_sway_{frame}.png", pixels, 6)


random.seed(7)
make_ground()
make_sway_frames()
