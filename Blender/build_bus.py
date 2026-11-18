"""Ônibus urbano REALISTA para o TheDelivery (Blender, background) — geometria por script + materiais
procedurais com desgaste assados em PBR (ver realistic_kit.py).
Frente no -X do Blender (-> yaw -90 no prefab), Z para cima, lado DIREITO (portas) no +Y.
Uso: blender --background --python build_bus.py -- <saida.fbx> <pasta_texturas>
"""
import bpy, sys, math, os
sys.path.append(os.path.dirname(os.path.abspath(__file__)))
import realistic_kit as K

argv = sys.argv[sys.argv.index("--") + 1:]
OUT_FBX, TEX_DIR = argv[0], argv[1]
K.reset_scene()

L, W, H = 11.5, 2.5, 3.0
X0, X1 = -L / 2, L / 2
HW = W / 2
CLEAR, SKIRT_TOP = 0.35, 1.10
WR = 0.5
FRONT_AXLE, REAR_AXLE = X0 + 2.6, X1 - 3.0
ARCH = 0.72
E = 0.012

# --- Materiais (cor base sRGB-ish, rugosidade, metálico, desgaste) ---------------------------
white = K.material("PaintWhite", (0.80, 0.80, 0.76), 0.46, dirt=0.35, ground_dirt=0.5, wear=0.3, color_var=0.03,
                   wear_color=(0.33, 0.34, 0.35), seams=("X", 1.92), streaks=0.15)
blue = K.material("PaintBlue", (0.05, 0.11, 0.26), 0.45, dirt=0.4, ground_dirt=0.7, wear=0.4, color_var=0.03,
                  wear_color=(0.38, 0.38, 0.40), rust=0.15, seams=("X", 1.92))
red = K.material("PaintRed", (0.50, 0.055, 0.04), 0.40, dirt=0.4, ground_dirt=0.5, wear=0.3)
roof = K.material("RoofGrey", (0.56, 0.56, 0.54), 0.62, dirt=0.9, streaks=0.5, bump=0.08, bump_scale=30)
glass = K.material("Glass", (0.015, 0.018, 0.022), 0.22, dirt=0.15, color_var=0.02, rough_var=0.10,
                   streaks=0.2, dirt_color=(0.20, 0.19, 0.17))
gasket = K.material("Gasket", (0.02, 0.02, 0.02), 0.75, dirt=0.3)
rubber = K.material("Rubber", (0.03, 0.03, 0.03), 0.85, bump=0.25, bump_scale=60, dirt=0.4, ground_dirt=0.8,
                    dirt_color=(0.13, 0.11, 0.09))
tire = K.material("Tire", (0.028, 0.028, 0.028), 0.9, bump=0.7, bump_scale=140, dirt=0.4,
                  dirt_color=(0.14, 0.12, 0.10))
metal = K.material("Metal", (0.60, 0.60, 0.60), 0.30, 1.0, dirt=0.5, ground_dirt=0.5, rust=0.2, bump=0.04)
frame = K.material("FrameDark", (0.07, 0.07, 0.075), 0.5, 0.3, dirt=0.35, wear=0.2, wear_color=(0.3, 0.3, 0.3))
well = K.material("Well", (0.012, 0.012, 0.012), 0.95)
head = K.material("HeadLamp", (0.90, 0.90, 0.85), 0.08, emission=(1.0, 0.95, 0.85), emission_strength=1.0)
tail = K.material("TailLamp", (0.45, 0.02, 0.02), 0.12, emission=(0.6, 0.02, 0.02), emission_strength=1.0)
amber = K.material("Amber", (0.80, 0.42, 0.05), 0.15, emission=(0.45, 0.22, 0.0), emission_strength=1.0)
signbg = K.material("SignBG", (0.015, 0.015, 0.015), 0.3)
led = K.material("SignLED", (1.0, 0.55, 0.05), 0.3, emission=(1.0, 0.50, 0.05), emission_strength=1.0)
plate = K.material("Plate", (0.82, 0.82, 0.78), 0.45, dirt=0.6, wear=0.2)
ink = K.material("Ink", (0.04, 0.04, 0.045), 0.5)

def rot_box(cx, cy, cz, sx, sy, sz, mat, rz=0.0, ry=0.0):
    ob = K.box(cx - sx / 2, cx + sx / 2, cy - sy / 2, cy + sy / 2, cz - sz / 2, cz + sz / 2, mat)
    ob.rotation_euler = (0.0, ry, rz)
    bpy.ops.object.select_all(action='DESELECT'); ob.select_set(True); bpy.context.view_layer.objects.active = ob
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=False)
    return ob

# --- Carroceria -------------------------------------------------------------------------------
K.box(X0, X1, -HW, HW, SKIRT_TOP, H - 0.10, white, bevel=0.14, segments=3)
K.box(X0 + 0.22, X1 - 0.22, -HW + 0.10, HW - 0.10, H - 0.16, H - 0.01, roof, bevel=0.06)
segs = [(X0, FRONT_AXLE - ARCH), (FRONT_AXLE + ARCH, REAR_AXLE - ARCH), (REAR_AXLE + ARCH, X1)]
for a, b in segs:
    K.box(a, b, -HW, HW, CLEAR, SKIRT_TOP, blue, bevel=0.06)
for ax in (FRONT_AXLE, REAR_AXLE):
    K.box(ax - ARCH, ax + ARCH, -HW, HW, 2 * WR + 0.08, SKIRT_TOP, blue, bevel=0.03)
    K.box(ax - ARCH, ax + ARCH, -HW + 0.42, HW - 0.42, CLEAR - 0.05, 2 * WR + 0.08, well)
    for s in (-1, 1):  # borracha do para-lama
        K.box(ax - ARCH - 0.03, ax + ARCH + 0.03, s * HW - 0.02, s * HW + 0.02, 2 * WR + 0.06, 2 * WR + 0.12, rubber)
K.box(X0 + 0.02, X1 - 0.02, -HW - E, HW + E, SKIRT_TOP, SKIRT_TOP + 0.10, red)
# Frisos laterais (borracha de proteção) — no lado direito, interrompidos nos vãos das portas
# e nos dois lados interrompidos nas caixas de roda (não passam na frente do pneu)
def strip_segments(gaps):
    segs, x = [], X0 + 0.3
    for a, b in sorted(gaps):
        if a > x: segs.append((x, a))
        x = max(x, b)
    if x < X1 - 0.3: segs.append((x, X1 - 0.3))
    return segs
arches = [(FRONT_AXLE - ARCH - 0.05, FRONT_AXLE + ARCH + 0.05), (REAR_AXLE - ARCH - 0.05, REAR_AXLE + ARCH + 0.05)]
for a, b in strip_segments(arches):
    K.box(a, b, -HW - 0.02, -HW, 0.70, 0.76, rubber)
for a, b in strip_segments(arches + [(X0 + 0.25, X0 + 1.45), (-0.30, 1.00)]):
    K.box(a, b, HW, HW + 0.02, 0.70, 0.76, rubber)

# --- Janelas laterais com borracha ----------------------------------------------------------
WIN_Z0, WIN_Z1 = 1.45, 2.60
DOOR_F = (X0 + 0.30, X0 + 1.40)
DOOR_M = (-0.25, 0.95)

def pane(x0, x1, side, z0=WIN_Z0, z1=WIN_Z1):
    yb0, yb1 = (side * HW, side * HW + side * E) if side > 0 else (side * HW - E, side * HW)
    K.box(x0 - 0.04, x1 + 0.04, min(yb0, yb1), max(yb0, yb1), z0 - 0.04, z1 + 0.04, gasket)
    yg0, yg1 = (side * HW + E, side * HW + 2 * E) if side > 0 else (side * HW - 2 * E, side * HW - E)
    K.box(x0, x1, min(yg0, yg1), max(yg0, yg1), z0, z1, glass)

def panes(side, x_from, x_to, skip=()):
    x = x_from
    while x < x_to - 0.3:
        x2 = min(x + 1.25, x_to)
        if not any(a - 0.05 < (x + x2) / 2 < b + 0.05 for a, b in skip):
            pane(x + 0.07, x2 - 0.07, side)
        x = x2

panes(-1, X0 + 0.55, X1 - 0.45)
panes(1, DOOR_F[1] + 0.10, X1 - 0.45, skip=(DOOR_M,))
# Janela do motorista (esquerda, menor) já coberta pela fileira; acrescenta defletor de chuva
K.box(X0 + 0.55, X1 - 0.45, -HW - 0.05, -HW, WIN_Z1 + 0.08, WIN_Z1 + 0.11, frame)
K.box(DOOR_F[1] + 0.10, X1 - 0.45, HW, HW + 0.05, WIN_Z1 + 0.08, WIN_Z1 + 0.11, frame)

# --- Portas (direita): moldura, folhas de vidro com borracha, puxadores --------------------
for a, b in (DOOR_F, DOOR_M):
    K.box(a, b, HW, HW + E, CLEAR + 0.05, WIN_Z1 + 0.06, frame)
    mid = (a + b) / 2
    for c, d in ((a + 0.06, mid - 0.03), (mid + 0.03, b - 0.06)):
        K.box(c, d, HW + E, HW + 2 * E, CLEAR + 0.08, WIN_Z1 + 0.02, gasket)
        K.box(c + 0.04, d - 0.04, HW + 2 * E, HW + 3 * E, 0.78, WIN_Z1 - 0.02, glass)
        K.box(c + 0.04, d - 0.04, HW + 2 * E, HW + 3 * E, CLEAR + 0.12, 0.72, glass)
        K.box(c + 0.02, d - 0.02, HW + 2 * E, HW + 3.5 * E, 1.05, 1.09, metal)  # barra horizontal
    K.box(a - 0.03, b + 0.03, HW, HW + 0.03, WIN_Z1 + 0.06, WIN_Z1 + 0.14, frame)  # caixa do mecanismo

# --- Frente -----------------------------------------------------------------------------------
FX = X0 - E
K.box(FX - E, X0, -HW + 0.10, HW - 0.10, 1.16, 2.59, gasket)
K.box(FX - 2 * E, FX - E, -HW + 0.14, -0.035, 1.20, 2.55, glass)
K.box(FX - 2 * E, FX - E, 0.035, HW - 0.14, 1.20, 2.55, glass)
K.box(FX - 2 * E, FX - E, -0.035, 0.035, 1.20, 2.55, frame)  # coluna central
# Limpadores
for s in (-1, 1):
    rot_box(FX - 2.6 * E, s * 0.55, 1.42, 0.02, 0.95, 0.025, ink, rz=0.0, ry=0.0)
    rot_box(FX - 2.6 * E, s * 0.10, 1.30, 0.03, 0.05, 0.10, ink)
# Letreiro: caixa escura + texto LED
K.box(FX - E, X0, -HW + 0.15, HW - 0.15, 2.60, 2.92, frame)
K.box(FX - 2 * E, FX - E, -1.0, 1.0, 2.64, 2.88, signbg)
K.text("413  CENTRO", FX - 2 * E - 0.002, 0.0, 2.76, 0.17, led, rot=(math.pi / 2, 0, -math.pi / 2), extrude=0.002)
# Painel inferior: grade, faróis (com aro), piscas, para-choque, placa
for i in range(4):
    z = 0.92 + i * 0.05
    K.box(FX - E, X0, -0.55, 0.55, z, z + 0.025, ink)
for s in (-1, 1):
    K.box(FX - E, X0, s * 0.85 - 0.20, s * 0.85 + 0.20, 0.62, 0.88, frame)
    K.box(FX - 2 * E, FX - E, s * 0.85 - 0.16, s * 0.85 + 0.16, 0.66, 0.84, head)
    K.box(FX - 2 * E, FX - E, s * 1.09 - 0.07, s * 1.09 + 0.07, 0.66, 0.84, amber)
K.box(X0 - 0.12, X0 + 0.15, -HW + 0.02, HW - 0.02, CLEAR - 0.03, 0.58, rubber, bevel=0.04)
K.box(X0 - 0.13, X0 - 0.12, -0.27, 0.27, 0.37, 0.53, plate)
K.text("BUS-0413", X0 - 0.135, 0.0, 0.45, 0.085, ink, rot=(math.pi / 2, 0, -math.pi / 2), extrude=0.002)
# Retrovisores
for s in (-1, 1):
    K.box(X0 - 0.25, X0 + 0.05, min(s * HW, s * (HW + 0.25)) - 0.02, max(s * HW, s * (HW + 0.25)) + 0.02, 2.36, 2.40, frame)
    K.box(X0 - 0.32, X0 - 0.20, s * (HW + 0.20) - 0.11, s * (HW + 0.20) + 0.11, 1.92, 2.40, frame, bevel=0.02)
    K.box(X0 - 0.325, X0 - 0.32, s * (HW + 0.20) - 0.09, s * (HW + 0.20) + 0.09, 1.95, 2.37, glass)

# --- Traseira ---------------------------------------------------------------------------------
RX = X1 + E
K.box(X1, RX, -HW + 0.22, HW - 0.22, 1.71, 2.59, gasket)
K.box(RX, RX + E, -HW + 0.26, HW - 0.26, 1.75, 2.55, glass)
for i in range(6):  # grade do motor
    z = 0.62 + i * 0.07
    K.box(X1, RX + E, -0.75, 0.75, z, z + 0.035, ink)
for s in (-1, 1):
    K.box(X1, RX + E, s * 0.98 - 0.18, s * 0.98 + 0.18, 0.66, 1.04, frame)
    K.box(RX + E, RX + 2 * E, s * 0.98 - 0.15, s * 0.98 + 0.15, 0.70, 0.92, tail)
    K.box(RX + E, RX + 2 * E, s * 0.98 - 0.15, s * 0.98 + 0.15, 0.93, 1.01, amber)
K.box(X1 - 0.15, X1 + 0.12, -HW + 0.02, HW - 0.02, CLEAR - 0.03, 0.58, rubber, bevel=0.04)
K.box(X1 + 0.12, X1 + 0.13, -0.27, 0.27, 0.37, 0.53, plate)
K.text("BUS-0413", X1 + 0.135, 0.0, 0.45, 0.085, ink, rot=(math.pi / 2, 0, math.pi / 2), extrude=0.002)

# --- Teto: ar-condicionado e escotilha --------------------------------------------------------
K.box(X1 - 4.3, X1 - 1.7, -0.80, 0.80, H - 0.03, H + 0.24, roof, bevel=0.07)
for i in range(5):
    x = X1 - 4.1 + i * 0.5
    K.box(x, x + 0.3, -0.81, 0.81, H + 0.05, H + 0.18, ink)
K.box(X0 + 2.6, X0 + 3.4, -0.40, 0.40, H - 0.03, H + 0.06, frame, bevel=0.02)

# --- Número da linha nas laterais -------------------------------------------------------------
K.text("413", X0 + 1.05, -HW - E - 0.002, 1.28, 0.16, ink, rot=(math.pi / 2, 0, 0), extrude=0.002)
K.text("413", X1 - 1.0, HW + E + 0.002, 1.28, 0.16, ink, rot=(math.pi / 2, 0, math.pi), extrude=0.002)

body = K.join(list(K.PARTS), "Body")
K.PARTS.clear()

# --- Rodas: pneu (relevo), aro, cubo e porcas; pivô no centro ---------------------------------
wheels = []
for ax, axn in ((FRONT_AXLE, "F"), (REAR_AXLE, "R")):
    for s, sn in ((-1, "L"), (1, "R")):
        width = 0.30 if axn == "F" else 0.42
        y = s * (HW - 0.06 - width / 2)
        parts = [K.cyl(ax, y, WR, WR, width, tire, axis='Y', verts=28, bevel=0.04),
                 K.cyl(ax, y + s * 0.005, WR, WR * 0.60, width + 0.01, metal, axis='Y', verts=20),
                 K.cyl(ax, y + s * 0.010, WR, WR * 0.24, width + 0.03, frame, axis='Y', verts=12)]
        for k in range(8):
            a = k * math.pi / 4
            parts.append(K.cyl(ax + math.cos(a) * WR * 0.36, y + s * 0.02, WR + math.sin(a) * WR * 0.36,
                               0.028, width + 0.03, metal, axis='Y', verts=6))
        w = K.join(parts, f"Wheel_{axn}{sn}")
        K.PARTS.clear()
        bpy.ops.object.origin_set(type='ORIGIN_GEOMETRY', center='BOUNDS')
        wheels.append(w)

for o in [body] + wheels:
    for p in o.data.polygons:
        p.use_smooth = False
objs = [body] + wheels
K.unwrap(objs, margin=0.004)
K.bake_all(objs, TEX_DIR, 2048)
K.collapse_materials(objs)
for w in wheels:
    w.parent = body
K.export_fbx(OUT_FBX)
print("RESULT bus tris=%d" % sum(len(p.vertices) - 2 for o in objs for p in o.data.polygons))
print("OK", OUT_FBX)
