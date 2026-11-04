# 산책 화면(2.5D 풍경)에 쓰는 도트 그림을 코드로 그립니다. 외부 라이브러리가 필요 없습니다.
#   wwwroot/walk/*.png  먼 산, 언덕, 절벽, 구름(가로로 이어짐)과 나무, 꽃 같은 소품
# 실행: python tools/make_walk_scene.py
import math
import random
import struct
import zlib
from pathlib import Path

OUTPUT_DIR = Path(__file__).parent.parent / "TamaPoke.Web" / "wwwroot" / "walk"
OUTLINE = (44, 58, 40, 255)


class Canvas:
    def __init__(self, width, height):
        self.width, self.height = width, height
        self.pixels = [[(0, 0, 0, 0)] * width for _ in range(height)]

    def put(self, x, y, color, wrap_x=False):
        if wrap_x:
            x %= self.width
        if 0 <= x < self.width and 0 <= y < self.height:
            self.pixels[y][x] = tuple(color) + (255,) if len(color) == 3 else tuple(color)

    def get(self, x, y):
        if 0 <= x < self.width and 0 <= y < self.height:
            return self.pixels[y][x]
        return (0, 0, 0, 0)

    def line(self, x0, y0, x1, y1, color):
        steps = max(abs(x1 - x0), abs(y1 - y0), 1)
        for i in range(int(steps) + 1):
            self.put(round(x0 + (x1 - x0) * i / steps), round(y0 + (y1 - y0) * i / steps), color)

    def outline(self, color=OUTLINE):
        solid = {(x, y) for y in range(self.height) for x in range(self.width) if self.pixels[y][x][3] > 0}
        for x, y in solid:
            for nx, ny in ((x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1)):
                if (nx, ny) not in solid:
                    self.put(nx, ny, color)

    def save(self, name):
        def chunk(kind, data):
            return struct.pack(">I", len(data)) + kind + data + struct.pack(">I", zlib.crc32(kind + data))

        raw = b"".join(b"\x00" + bytes(v for p in row for v in p) for row in self.pixels)
        path = OUTPUT_DIR / f"{name}.png"
        path.write_bytes(
            b"\x89PNG\r\n\x1a\n"
            + chunk(b"IHDR", struct.pack(">IIBBBBB", self.width, self.height, 8, 6, 0, 0, 0))
            + chunk(b"IDAT", zlib.compress(raw, 9))
            + chunk(b"IEND", b"")
        )
        print(f"{name}.png {self.width}x{self.height} {path.stat().st_size} bytes")


# 왼쪽 위에서 빛이 오는 공 하나. 뒤에서 앞으로 겹쳐 그리면 잎사귀 뭉치가 됩니다.
def ball(canvas, cx, cy, radius, palette, grain=0.16, wrap_x=False):
    r = int(radius) + 1
    for dy in range(-r, r + 1):
        for dx in range(-r, r + 1):
            if dx * dx + dy * dy + random.uniform(-radius, radius) * 0.6 > radius * radius:
                continue
            light = (-dx * 0.55 - dy * 0.85) / max(radius, 1) + random.uniform(-grain, grain)
            index = 0 if light > 0.45 else 1 if light > -0.05 else 2 if light > -0.55 else 3
            canvas.put(int(cx + dx), int(cy + dy), palette[index], wrap_x)


# 잎 뭉치 하나: 왼쪽 위가 밝고 아래쪽에 어두운 초승달이 생깁니다. 위쪽 뭉치를 나중에 그려 겹칩니다.
def puff(canvas, cx, cy, radius, palette, bias=0.0):
    phase_a, phase_b = random.uniform(0, math.tau), random.uniform(0, math.tau)
    r = int(radius * 1.2) + 1
    for dy in range(-r, r + 1):
        for dx in range(-r, r + 1):
            angle = math.atan2(dy, dx)
            edge = radius * (1 + 0.09 * math.sin(5 * angle + phase_a) + 0.05 * math.sin(11 * angle + phase_b))
            if dx * dx + dy * dy > edge * edge:
                continue
            light = (-dx * 0.5 - dy * 0.86) / radius + bias
            # 경계에서 체크무늬로 섞어 도트 그림답게 부드럽게 넘어갑니다.
            light += 0.05 if (int(cx + dx) + int(cy + dy)) % 2 == 0 else -0.05
            index = 0 if light > 0.56 else 1 if light > 0.14 else 2 if light > -0.3 else 3 if light > -0.68 else 4
            canvas.put(int(cx + dx), int(cy + dy), palette[index])


def canopy(canvas, cx, cy, half_width, half_height, palette, puff_radius):
    # 가운데를 먼저 어둡게 채워 잎 사이가 뚫려 보이지 않게 합니다.
    for dy in range(-half_height, half_height + 1):
        for dx in range(-half_width, half_width + 1):
            if (dx / half_width) ** 2 + (dy / half_height) ** 2 <= 0.8:
                canvas.put(cx + dx, cy + dy, palette[4])

    rows = max(round(half_height * 2 / (puff_radius * 1.05)), 3)
    for row in range(rows):  # 아래 줄부터 위로
        t = row / (rows - 1)
        y = cy + half_height * (0.72 - 1.44 * t)
        span = half_width * math.sqrt(max(1 - ((y - cy) / half_height) ** 2, 0.04)) * 0.86
        count = max(round(span * 2 / (puff_radius * 1.25)), 1) + (1 if 0 < row < rows - 1 else 0)
        positions = [cx + (span * (2 * (i + 0.5) / count - 1)) for i in range(count)]
        for x in sorted(positions, key=lambda value: -value):  # 오른쪽부터 → 왼쪽(밝은 쪽)이 앞으로
            px = x + random.uniform(-2.5, 2.5)
            py = y + random.uniform(-3, 3)
            bias = -(px - cx) / half_width * 0.22 - (py - cy) / half_height * 0.3
            puff(canvas, px, py, puff_radius * random.uniform(0.85, 1.12), palette, bias)

    # 잎사귀 느낌: 밝은 곳엔 짧은 반짝임, 어두운 곳엔 작은 틈
    for _ in range(half_width * half_height // 16):
        x, y = cx + random.randint(-half_width, half_width), cy + random.randint(-half_height, half_height)
        color = canvas.get(x, y)
        if color[3] == 0:
            continue
        if color[:3] == palette[1]:
            canvas.put(x, y, palette[0])
            canvas.put(x + 1, y, palette[0])
        elif color[:3] == palette[2]:
            canvas.put(x, y, palette[3])
            canvas.put(x + 1, y + 1, palette[1])
        elif color[:3] == palette[3]:
            canvas.put(x, y, palette[4])


def tree_trunk(canvas, cx, top, bottom, half):
    tones = [(186, 136, 88), (150, 104, 66), (116, 78, 52), (84, 56, 42)]
    height = bottom - top
    bark = {cx + i: random.randint(0, 5) for i in range(-half - 4, half + 5)}
    for y in range(top, bottom + 1):
        t = (y - top) / height
        sway = round(math.sin(t * math.pi) * 1.4)
        flare = int(max(t - 0.78, 0) * 18)  # 밑동이 벌어집니다.
        width = half - (1 if t < 0.25 else 0)
        for x in range(-width - flare, width + flare + 1):
            position = (x + width + flare) / max(2 * (width + flare), 1)
            index = 0 if position < 0.25 else 1 if position < 0.55 else 2 if position < 0.82 else 3
            # 잎 그늘이 지는 위쪽은 어둡게, 나무껍질 무늬를 세로로 넣습니다.
            if t < 0.52:
                index = min(index + 1, 3)
            elif (y + bark.get(cx + x, 0)) % 6 == 0 and index < 3:
                index += 1
            canvas.put(cx + x + sway, y, tones[index])
    # 뿌리
    for side in (-1, 1):
        for i in range(half + 6):
            canvas.put(cx + side * (half + i // 1), bottom - max(i // 3, 0) + 0, tones[2 if side < 0 else 3])


def branch(canvas, x0, y0, x1, y1):
    for offset, tone in ((0, (130, 90, 58)), (1, (98, 66, 46))):
        canvas.line(x0, y0 + offset, x1, y1 + offset, tone)
        canvas.line(x0 + 1, y0 + offset, x1 + 1, y1 + offset, tone)


# 땅에 드리운 그림자. 그림의 맨 아래에 반투명으로 깔아 나무가 땅에 붙어 보입니다.
def ground_shadow(canvas, cx, cy, rx, ry):
    for dy in range(-ry, ry + 1):
        for dx in range(-rx, rx + 1):
            if (dx / rx) ** 2 + (dy / ry) ** 2 <= 1 and canvas.get(cx + dx, cy + dy)[3] == 0:
                canvas.put(cx + dx, cy + dy, (24, 66, 36, 70))


def tree(name, palette, width, height, trunk_half=4, puff_radius=11, litter=None):
    canvas = Canvas(width, height)
    cx = width // 2
    base = height - 7
    canopy_h = int(height * 0.3)
    canopy_w = width // 2 - puff_radius
    canopy_y = int(canopy_h * 0.72 + puff_radius * 1.3) + 5
    tree_trunk(canvas, cx, canopy_y, base, trunk_half)
    fork = canopy_y + canopy_h + 10
    branch(canvas, cx - 1, fork, cx - canopy_w // 2, canopy_y + canopy_h // 2)
    branch(canvas, cx + 1, fork + 4, cx + canopy_w // 2, canopy_y + canopy_h // 2 + 2)
    canopy(canvas, cx, canopy_y, canopy_w, canopy_h, palette, puff_radius)
    canvas.outline(tuple(int(c * 0.62) for c in palette[4]) + (255,))
    ground_shadow(canvas, cx, base + 1, width // 3 + 2, 4)
    # 떨어진 꽃잎이나 낙엽
    if litter:
        for _ in range(16):
            x, y = cx + random.randint(-width // 3, width // 3), base + random.randint(-3, 5)
            if canvas.get(x, y)[3] < 255:
                canvas.put(x, y, random.choice(litter))
    canvas.save(name)


def pine(name):
    width, height = 56, 100
    canvas = Canvas(width, height)
    palette = [(150, 206, 118), (96, 170, 100), (62, 136, 92), (42, 106, 84), (30, 80, 72)]
    cx, base = width // 2, height - 6
    tree_trunk(canvas, cx, base - 18, base, 3)
    tiers = 6
    for tier in reversed(range(tiers)):  # 아래 층부터 그려 위층이 아래층을 덮습니다.
        top = 2 + tier * 12
        tier_height = 24 if tier else 20
        half = 9 + tier * 3.4
        for y in range(tier_height + 1):
            v = y / tier_height
            spread = half * v ** 0.9
            for x in range(-int(spread) - 1, int(spread) + 2):
                # 가지 끝이 처지는 들쭉날쭉한 치맛단
                hem = tier_height - 2.6 * abs(math.sin(x * math.pi / 5.5)) - (abs(x) / max(half, 1)) * -2
                if y > hem or abs(x) > spread:
                    continue
                value = -x / max(spread, 1) * 0.72 + 0.28 - v * 0.75
                if (x + y * (1 if x < 0 else -1)) % 5 == 0:
                    value -= 0.2
                value += 0.04 if (x + y) % 2 == 0 else -0.04
                index = 0 if value > 0.62 else 1 if value > 0.2 else 2 if value > -0.2 else 3 if value > -0.62 else 4
                canvas.put(cx + x, top + y, palette[index])
    canvas.outline((22, 60, 56, 255))
    ground_shadow(canvas, cx, base + 1, 19, 4)
    canvas.save(name)


def bush(name, palette):
    canvas = Canvas(40, 26)
    for x, y, r in [(10, 15, 7), (29, 15, 7), (20, 16, 8), (14, 11, 7), (25, 11, 7), (20, 8, 6)]:
        ball(canvas, x, y, r, palette)
    canvas.outline()
    canvas.save(name)


STONE = [(232, 230, 214), (202, 202, 186), (168, 170, 158), (132, 136, 130), (98, 104, 106)]
MOSS = [(150, 204, 96), (112, 172, 84)]


def rock(name, width, height, lobes, moss=0, crack=False):
    canvas = Canvas(width, height)
    base = height - 4
    for x, y, radius in lobes:
        puff(canvas, x, y, radius, STONE, -(y - base / 2) / base * 0.25)
    for y in range(base, height):  # 바닥은 평평하게
        for x in range(width):
            canvas.put(x, y, (0, 0, 0, 0))
    if crack:
        x, y = width * 0.55, base * 0.35
        for _ in range(int(base * 0.5)):
            if canvas.get(int(x), int(y))[3]:
                canvas.put(int(x), int(y), STONE[4])
            x += random.choice([-1, 0, 1])
            y += 1
    # 위쪽에 낀 이끼
    for _ in range(moss):
        cx, cy = random.randint(4, width - 4), random.randint(2, base // 2)
        for dy in range(-2, 3):
            for dx in range(-3, 4):
                if dx * dx + dy * dy * 2 <= 8 and canvas.get(cx + dx, cy + dy)[3]:
                    canvas.put(cx + dx, cy + dy, MOSS[0] if dy < 1 else MOSS[1])
    canvas.outline((58, 66, 68, 255))
    ground_shadow(canvas, width // 2, base, width // 2 - 1, 3)
    canvas.save(name)


def mushroom(name):
    canvas = Canvas(20, 20)
    for y in range(11, 19):
        for x in range(7, 13):
            canvas.put(x, y, (246, 236, 214) if x < 11 else (214, 198, 170))
    palette = [(248, 104, 88), (224, 64, 60), (188, 44, 52), (150, 32, 48)]
    for dy in range(-8, 1):
        for dx in range(-9, 10):
            if dx * dx / 81 + dy * dy / 64 <= 1:
                light = (-dx * 0.5 - dy * 0.8) / 8
                index = 0 if light > 0.5 else 1 if light > 0 else 2 if light > -0.4 else 3
                canvas.put(10 + dx, 11 + dy, palette[index])
    for sx, sy in [(6, 7), (11, 5), (14, 9), (8, 10)]:
        for dx, dy in [(0, 0), (1, 0), (0, 1), (1, 1)]:
            canvas.put(sx + dx, sy + dy, (255, 250, 236))
    canvas.outline()
    canvas.save(name)


def flowers(name, light, dark, center, count=11):
    width, height = 48, 28
    canvas = Canvas(width, height)
    leaves = [(120, 190, 76), (92, 164, 64), (68, 136, 58)]
    for _ in range(46):  # 아래에 무성한 잎
        x = random.randint(4, width - 5)
        y = random.randint(height - 11, height - 4)
        canvas.line(x, y + 2, x + random.randint(-2, 2), y - random.randint(1, 4), random.choice(leaves))
    blooms = []
    for i in range(count):
        x = 5 + i * (width - 10) // max(count - 1, 1) + random.randint(-1, 1)
        y = random.randint(5, height - 11)
        blooms.append((y, x))
    for y, x in sorted(blooms):
        canvas.line(x, y, x + random.randint(-1, 1), height - 5, leaves[2])
        big = random.random() < 0.55
        if big:  # 꽃잎 다섯 장 느낌의 둥근 꽃
            for dy in range(-2, 3):
                for dx in range(-2, 3):
                    if abs(dx) + abs(dy) <= 3 and not (abs(dx) == 2 and abs(dy) == 2):
                        canvas.put(x + dx, y + dy, dark if dy > 0 or dx > 0 else light)
        else:
            for dx, dy in [(-1, 0), (1, 0), (0, -1), (0, 1)]:
                canvas.put(x + dx, y + dy, light)
        canvas.put(x, y, center)
    canvas.outline((54, 104, 54, 255))
    ground_shadow(canvas, width // 2, height - 4, width // 2 - 2, 3)
    canvas.save(name)


def tulips(name, light, mid, dark):
    width, height = 30, 30
    canvas = Canvas(width, height)
    stem = [(92, 164, 64), (68, 136, 58)]
    for x, top in [(7, 10), (15, 5), (23, 11)]:
        for y in range(top + 5, height - 4):
            canvas.put(x, y, stem[0])
            canvas.put(x + 1, y, stem[1])
        for side in (-1, 1):
            for i in range(6):
                canvas.put(x + side * (i // 2 + 1), height - 5 - i, stem[0 if side < 0 else 1])
        for dy in range(0, 7):
            half = 3 if dy < 5 else 2 if dy == 5 else 1
            for dx in range(-half, half + 2):
                tone = light if dx <= -1 else mid if dx <= 1 else dark
                canvas.put(x + dx, top + dy, tone)
        for dx in (-3, 0, 3):  # 꽃잎 끝
            canvas.put(x + dx + (1 if dx == 3 else 0), top - 1, mid)
    canvas.outline((54, 96, 54, 255))
    ground_shadow(canvas, width // 2, height - 4, 12, 3)
    canvas.save(name)


def sunflower(name):
    canvas = Canvas(30, 46)
    stem = (72, 142, 58)
    for y in range(16, 45):
        canvas.put(15, y, stem)
        canvas.put(16, y, (52, 112, 54))
    for side, y in [(-1, 28), (1, 34)]:
        for i in range(10):
            lift = int(3 * math.sin(i / 3))
            for t in range(-(2 if 2 < i < 8 else 1), 2):
                canvas.put(15 + side * i, y - lift + t, (98, 172, 64) if t < 0 else stem)
    for angle in range(0, 360, 30):
        rad = math.radians(angle)
        for r in range(5, 11):
            x, y = 15 + math.cos(rad) * r, 13 + math.sin(rad) * r
            color = (255, 214, 72) if r < 9 else (244, 176, 44)
            canvas.put(round(x), round(y), color)
            canvas.put(round(x + math.sin(rad)), round(y + math.cos(rad)), (250, 190, 56))
    for dy in range(-5, 6):
        for dx in range(-5, 6):
            if dx * dx + dy * dy <= 26:
                canvas.put(15 + dx, 13 + dy, (118, 72, 40) if (dx + dy) % 3 else (84, 50, 32))
    canvas.outline()
    canvas.save(name)


def fern(name, tones=((112, 190, 84), (72, 150, 72), (48, 118, 66))):
    canvas = Canvas(34, 28)
    for frond in range(-3, 4):
        lean = frond * 3.6
        height = 24 - abs(frond) * 2
        for i in range(height):
            t = i / height
            x = 17 + lean * t * t * 1.5 + frond * 0.5
            y = 26 - i + (t * t * abs(frond) * 2.4)
            width = int(3 * math.sin(t * math.pi)) + 1
            for w in range(-width, width + 1):
                color = tones[0] if w < 0 else tones[1] if w == 0 else tones[2]
                canvas.put(round(x + w), round(y), color)
    canvas.outline((36, 72, 48, 255))
    canvas.save(name)


def grass_clump(name):
    canvas = Canvas(26, 22)
    tones = [(138, 206, 92), (98, 172, 64), (72, 142, 58)]
    for blade in range(12):
        x = 4 + blade * 18 // 11
        lean = (blade - 5.5) * 0.9
        height = random.randint(11, 20)
        tone = random.choice(tones)
        for i in range(height):
            t = i / height
            canvas.put(round(x + lean * t * t), 20 - i, tone)
            if t < 0.6:
                canvas.put(round(x + lean * t * t) + 1, 20 - i, tones[2])
    canvas.outline((46, 88, 46, 255))
    canvas.save(name)


# 여기부터는 가로로 이어지는 배경 띠입니다. 모두 sin의 정수 주기라 끝과 처음이 맞습니다.
def wave(x, width, *terms):
    return sum(amp * math.sin(math.tau * cycles * x / width + phase) for amp, cycles, phase in terms)


def mountains():
    width, height = 480, 90
    canvas = Canvas(width, height)
    light, mid, shade, snow = (150, 186, 214), (126, 164, 202), (104, 142, 184), (236, 244, 250)
    peaks = [(40, 62), (120, 42), (205, 70), (290, 48), (370, 74), (445, 40)]

    def ridge(x):
        best = height
        for px, h in peaks:
            d = min(abs(x - px), width - abs(x - px))
            best = min(best, height - h + d * 0.85)
        return best + wave(x, width, (2, 13, 0.4), (1.2, 29, 1.2))

    for x in range(width):
        top = ridge(x)
        slope = ridge(x + 2) - ridge(x - 2)
        for y in range(max(int(top), 0), height):
            color = light if slope > 0.5 else shade if slope < -0.5 else mid
            if y < top + 6 + wave(x, width, (2, 31, 0)) and top < height - 54:
                color = snow
            elif y > height - 22 and (x + y) % 2 == 0:
                color = shade
            canvas.put(x, y, color)
    canvas.save("mountains")


def hills():
    width, height = 480, 70
    canvas = Canvas(width, height)
    palette = [(120, 186, 104), (94, 164, 94), (74, 142, 88), (58, 120, 82)]

    def top(x):
        return 30 + wave(x, width, (11, 2, 0.8), (6, 3, 2.1), (2, 11, 0.2))

    for x in range(width):
        t = top(x)
        for y in range(int(t), height):
            depth = (y - t) / 40 + random.uniform(-0.08, 0.08)
            canvas.put(x, y, palette[min(int(depth * 4), 3)])
    # 멀리 보이는 나무 숲
    for _ in range(46):
        x = random.randrange(width)
        y = int(top(x)) + random.randint(-1, 6)
        ball(canvas, x, y, random.uniform(3.2, 6), [(96, 164, 92), (72, 142, 86), (56, 120, 80), (44, 100, 74)], 0.15, wrap_x=True)
    canvas.save("hills")


def cliff():
    width, height = 480, 64
    canvas = Canvas(width, height)
    stones = [(216, 192, 150), (200, 174, 132), (182, 154, 114), (160, 132, 96)]
    mortar = (128, 102, 76)
    grass = [(150, 216, 98), (126, 200, 80), (100, 176, 70)]
    ground = (126, 200, 80)

    def top(x):
        return 13 + wave(x, width, (6, 2, 0.5), (3, 5, 1.9), (1.2, 17, 0.7))

    def bottom(x):
        return 52 + wave(x, width, (4, 3, 2.4), (2, 7, 0.3), (1, 23, 1.1))

    row_height = 6
    brick_width = 40
    for x in range(width):
        t, b = top(x), bottom(x)
        for y in range(int(t), height):
            if y >= b:
                canvas.put(x, y, ground)
                continue
            # 절벽의 굴곡을 따라 벽돌 줄이 휘어집니다.
            row_y = y - t * 0.55
            row = int(row_y // row_height)
            brick = int((x + row * 17 + 480) // brick_width)
            random.seed(row * 1009 + brick % (width // brick_width))
            tone = random.choice([0, 1, 1, 2, 2, 3])
            random.seed(x * 7919 + y * 104729)
            in_row = row_y % row_height
            # 세로 틈은 줄의 일부에만 짧게 넣어 벽돌처럼 보이지 않게 합니다.
            at_edge = in_row < 1 or ((x + row * 8 + 480) % brick_width == 0 and in_row < 3)
            if at_edge:
                color = mortar
            else:
                color = stones[min(tone + (1 if in_row > row_height - 2 else 0), 3)]
            # 위쪽은 해가 비쳐 밝고, 밑동은 어둡습니다.
            if y > b - 6 and (x + y) % 2 == 0:
                color = stones[3] if not at_edge else mortar
            canvas.put(x, y, color)
        # 위쪽 잔디 턱 (들쭉날쭉 늘어짐)
        lip = 4 + (x * 13 % 7 == 0) * 3 + (x * 5 % 11 == 0) * 2
        for i in range(int(lip)):
            canvas.put(x, int(t) + i, grass[0] if i == 0 else grass[1] if i < lip - 1 else grass[2])
        # 땅과 만나는 곳의 풀
        grow = 2 + (x * 3 % 5 == 0) * 2 + (x * 7 % 13 == 0) * 2
        for i in range(int(grow)):
            canvas.put(x, int(b) - i, ground if i < grow - 1 else grass[2])
    random.seed(11)
    canvas.save("cliff")


def clouds():
    width, height = 640, 90
    canvas = Canvas(width, height)
    palette = [(255, 255, 255), (246, 251, 255), (226, 240, 250), (204, 226, 242)]
    for cx, cy, scale in [(70, 30, 1.0), (250, 52, 0.7), (400, 24, 1.2), (570, 58, 0.8)]:
        for dx, r in [(-26, 8), (-14, 12), (0, 15), (15, 12), (28, 8)]:
            ball(canvas, cx + dx * scale, cy, r * scale, palette, 0.08)
        for x in range(int(cx - 38 * scale), int(cx + 38 * scale)):  # 바닥은 납작하게
            for y in range(int(cy + 7 * scale), height):
                canvas.put(x, y, (0, 0, 0, 0))
    canvas.save("clouds")


OUTPUT_DIR.mkdir(exist_ok=True)
random.seed(11)
GREEN = [(150, 214, 104), (104, 184, 86), (70, 150, 76), (48, 118, 70)]
DEEP = [(120, 190, 92), (82, 160, 80), (56, 128, 74), (40, 98, 66)]
LEAF_GREEN = [(196, 234, 112), (138, 210, 90), (90, 174, 84), (60, 138, 84), (42, 104, 80)]
LEAF_DEEP = [(150, 212, 100), (98, 178, 88), (64, 144, 86), (44, 112, 82), (30, 84, 76)]
LEAF_BLOSSOM = [(255, 238, 242), (255, 198, 218), (242, 148, 188), (208, 108, 162), (150, 76, 132)]
LEAF_AUTUMN = [(255, 228, 122), (250, 186, 78), (230, 138, 60), (192, 94, 54), (136, 64, 52)]
tree("tree_green", LEAF_GREEN, 84, 96, puff_radius=13)
tree("tree_deep", LEAF_DEEP, 92, 108, trunk_half=5, puff_radius=15)
tree("tree_blossom", LEAF_BLOSSOM, 84, 92, puff_radius=12, litter=[(255, 214, 228), (255, 238, 242), (242, 148, 188)])
tree("tree_autumn", LEAF_AUTUMN, 76, 86, puff_radius=12, litter=[(250, 186, 78), (230, 138, 60), (255, 228, 122)])
pine("pine")
bush("bush", GREEN)
bush("bush_berry", DEEP)
mushroom("mushroom")
rock("rock", 40, 30, [(13, 17, 9), (27, 18, 10), (19, 11, 9)], moss=2)
rock("rock_big", 64, 46, [(16, 28, 13), (46, 29, 14), (32, 25, 16), (26, 15, 12), (40, 15, 10)], moss=4, crack=True)
rock("rock_small", 30, 18, [(8, 10, 5), (20, 10, 6), (14, 7, 5)])
flowers("flowers_white", (255, 255, 255), (222, 228, 236), (246, 196, 60))
flowers("flowers_yellow", (255, 232, 104), (246, 196, 62), (214, 126, 40))
flowers("flowers_pink", (255, 190, 214), (244, 136, 178), (252, 230, 130))
flowers("flowers_blue", (170, 204, 255), (118, 156, 238), (255, 236, 150))
tulips("tulip_red", (255, 112, 98), (232, 70, 72), (186, 44, 62))
tulips("tulip_yellow", (255, 234, 118), (250, 200, 70), (222, 150, 50))
sunflower("sunflower")
fern("fern")
grass_clump("grass")
mountains()
hills()
cliff()
clouds()
