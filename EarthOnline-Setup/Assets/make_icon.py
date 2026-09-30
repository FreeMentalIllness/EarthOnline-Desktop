# -*- coding: utf-8 -*-
"""地球Online 应用图标生成器 v2：metaball 柔和大陆 + 更大球体。"""
from PIL import Image, ImageDraw, ImageFilter
import math, os

SS = 4
S = 1024 * SS
OUT = os.path.dirname(os.path.abspath(__file__))

CREAM_TOP = (253, 250, 245)
CREAM_BOT = (243, 234, 221)
OCEAN_HI = (110, 192, 198)
OCEAN_LO = (52, 122, 146)
LAND = (168, 208, 142)
LAND_DEEP = (128, 178, 118)
AMBER = (212, 163, 115)
AMBER_DARK = (176, 128, 78)
SHADOW = (150, 125, 95)


def rounded_rect_mask(size, radius):
    m = Image.new("L", (size, size), 0)
    ImageDraw.Draw(m).rounded_rectangle([0, 0, size - 1, size - 1], radius=radius, fill=255)
    return m


def build():
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))

    # ---- 1. 圆角底板 ----
    grad = Image.new("RGBA", (S, S))
    px = grad.load()
    for y in range(S):
        t = y / (S - 1)
        c = tuple(int(CREAM_TOP[i] + (CREAM_BOT[i] - CREAM_TOP[i]) * t) for i in range(3)) + (255,)
        for x in range(S):
            px[x, y] = c
    mask = rounded_rect_mask(S, int(S * 0.225))
    img.paste(grad, (0, 0), mask)

    cx, cy = S * 0.5, S * 0.50
    R = S * 0.335                      # 球更大

    # ---- 2. 投影 ----
    shadow = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    ImageDraw.Draw(shadow).ellipse(
        [cx - R * 0.95, cy + R * 0.80, cx + R * 0.95, cy + R * 1.10],
        fill=SHADOW + (80,))
    shadow = shadow.filter(ImageFilter.GaussianBlur(S * 0.040))
    img.alpha_composite(Image.composite(shadow, Image.new("RGBA", (S, S), (0, 0, 0, 0)), mask))

    # ---- 3. 球体（径向光照海洋） ----
    sphere = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    sp = sphere.load()
    hl_x, hl_y = cx - R * 0.40, cy - R * 0.44
    Rint = int(R)
    for yy in range(-Rint - 2, Rint + 3):
        for xx in range(-Rint - 2, Rint + 3):
            d = math.hypot(xx, yy)
            if d > R:
                continue
            dh = min(1.0, math.hypot(xx - (hl_x - cx), yy - (hl_y - cy)) / (R * 1.75))
            edge = (d / R) ** 2.4
            t = min(1.0, dh * 0.72 + edge * 0.50)
            x = int(cx + xx)
            y = int(cy + yy)
            sp[x, y] = tuple(int(OCEAN_HI[i] + (OCEAN_LO[i] - OCEAN_HI[i]) * t) for i in range(3)) + (255,)
    sphere_mask = Image.new("L", (S, S), 0)
    ImageDraw.Draw(sphere_mask).ellipse([cx - R, cy - R, cx + R, cy + R], fill=255)

    # ---- 4. metaball 大陆（叠加圆 + 膨胀模糊 + 阈值化 → 有机圆润形状） ----
    land_seeds = Image.new("L", (S, S), 0)
    lsd = ImageDraw.Draw(land_seeds)

    def blob(cx0, cy0, rw, rh, circles):
        for (a, rr, cr) in circles:
            ang = math.radians(a)
            px0 = cx0 + math.cos(ang) * rw * rr
            py0 = cy0 + math.sin(ang) * rh * rr
            cr_abs = cr * R
            lsd.ellipse([px0 - cr_abs, py0 - cr_abs, px0 + cr_abs, py0 + cr_abs], fill=255)

    # 北美
    blob(cx - R * 0.48, cy - R * 0.36, R * 0.36, R * 0.28,
         [(0, 0, 0.16), (65, 0.6, 0.13), (130, 0.9, 0.14), (200, 0.6, 0.11), (290, 0.65, 0.12)])
    # 南美
    blob(cx - R * 0.20, cy + R * 0.34, R * 0.20, R * 0.30,
         [(0, 0, 0.13), (75, 0.6, 0.10), (160, 0.9, 0.11), (250, 0.55, 0.09)])
    # 欧非
    blob(cx + R * 0.36, cy + R * 0.02, R * 0.30, R * 0.38,
         [(0, 0, 0.17), (60, 0.55, 0.13), (120, 0.85, 0.14), (185, 0.6, 0.12), (250, 0.85, 0.13), (320, 0.6, 0.11)])
    # 亚洲
    blob(cx + R * 0.50, cy - R * 0.50, R * 0.26, R * 0.20,
         [(0, 0, 0.14), (70, 0.65, 0.12), (150, 0.9, 0.13), (250, 0.6, 0.11)])
    # 绿岛点缀
    lsd.ellipse([cx + R * 0.05, cy - R * 0.78, cx + R * 0.22, cy - R * 0.62], fill=255)

    # metaball：模糊后阈值化
    land_seeds = land_seeds.filter(ImageFilter.GaussianBlur(S * 0.012))
    land_bin = land_seeds.point(lambda v: 255 if v >= 140 else 0)
    land_bin = land_bin.filter(ImageFilter.GaussianBlur(S * 0.002))

    land_layer = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    land_color = Image.new("RGBA", (S, S), LAND + (255,))
    land_layer.paste(land_color, (0, 0), land_bin)

    # 大陆暗部（右下偏移内影）：暗部 = 大陆 ∩ 偏移大陆之外
    from PIL import ImageChops
    offset = ImageChops.offset(land_bin, int(S * 0.010), int(S * 0.012))
    deep_only = ImageChops.multiply(ImageChops.subtract(offset, land_bin), land_bin)
    deep_final = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    deep_final.paste(Image.new("RGBA", (S, S), LAND_DEEP + (200,)), (0, 0), deep_only)
    land_layer.alpha_composite(deep_final)

    # 大陆裁剪进球体
    sphere.alpha_composite(Image.composite(land_layer, Image.new("RGBA", (S, S), (0, 0, 0, 0)), sphere_mask))

    # ---- 5. 球体高光 / rim light ----
    gloss = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    ImageDraw.Draw(gloss).ellipse([cx - R * 0.75, cy - R * 0.85, cx + R * 0.08, cy - R * 0.10],
                                  fill=(255, 255, 255, 85))
    gloss = gloss.filter(ImageFilter.GaussianBlur(S * 0.032))
    sphere.alpha_composite(Image.composite(gloss, Image.new("RGBA", (S, S), (0, 0, 0, 0)), sphere_mask))

    rim = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    ImageDraw.Draw(rim).arc([cx - R + 2, cy - R + 2, cx + R - 2, cy + R - 2],
                            start=195, end=300, fill=(255, 255, 255, 140), width=int(S * 0.008))
    rim = rim.filter(ImageFilter.GaussianBlur(S * 0.004))
    sphere.alpha_composite(rim)

    img.alpha_composite(sphere)

    # ---- 6. 琥珀轨道环 ----
    ring_w = int(S * 0.022)
    ring_a = -16
    ring_rx, ring_ry = R * 1.40, R * 0.52

    def ring_point(t):
        ang = math.radians(t)
        x0 = math.cos(ang) * ring_rx
        y0 = math.sin(ang) * ring_ry
        xr = x0 * math.cos(math.radians(ring_a)) - y0 * math.sin(math.radians(ring_a))
        yr = x0 * math.sin(math.radians(ring_a)) + y0 * math.cos(math.radians(ring_a))
        return cx + xr, cy + yr

    steps = 720
    pts = [ring_point(i * 360 / steps) for i in range(steps + 1)]
    ring_full = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    rld = ImageDraw.Draw(ring_full)
    # 后段（在球后面）：左上弧段
    back_pts = [p for p in pts if p[1] < cy - R * 0.02 and p[0] < cx]
    front_pts = [p for p in pts if not (p[1] < cy - R * 0.02 and p[0] < cx)]
    rld.line(back_pts, fill=AMBER_DARK + (255,), width=ring_w, joint="curve")
    img.alpha_composite(ring_full)

    img.alpha_composite(sphere)  # 球遮后段

    ring_front = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    rfld = ImageDraw.Draw(ring_front)
    rfld.line(front_pts, fill=AMBER + (255,), width=ring_w, joint="curve")
    front_dark = [(x + ring_w * 0.22, y + ring_w * 0.26) for x, y in front_pts]
    rfld.line(front_dark, fill=AMBER_DARK + (120,), width=int(ring_w * 0.5), joint="curve")
    img.alpha_composite(ring_front)

    # 卫星点缀
    sx, sy = ring_point(196)
    sr = int(S * 0.017)
    ImageDraw.Draw(img).ellipse([sx - sr, sy - sr, sx + sr, sy + sr], fill=AMBER + (255,))

    # ---- 7. 输出 ----
    final = img.resize((1024, 1024), Image.LANCZOS)
    final.save(os.path.join(OUT, "app_logo_new.png"))
    ico_sizes = [16, 24, 32, 48, 64, 128, 256]
    final.save(os.path.join(OUT, "app_logo_new.ico"), format="ICO", sizes=[(s, s) for s in ico_sizes])
    print("icon v2 generated")


if __name__ == "__main__":
    build()
