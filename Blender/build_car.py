"""Carros de passeio REALISTAS para o tráfego da Road (Blender, background) — substituem os do Meshy.

Geometria por script a partir de MEDIDAS REAIS (anos 80/90, frota de rua brasileira):
  - carroceria = perfil lateral extrudado na largura, com caixas de roda recortadas (boolean, só
    nos 32 cm de fora — o assoalho continua inteiro) e cantos arredondados (bevel por ângulo);
  - cabine = prisma do perfil das janelas com "tumblehome" (afina para cima), colunas A/B/C feitas
    pelo inset das janelas; as janelas viram FUROS com rebaixo e o vidro é um objeto separado
    ("Glass", OPACO e escuro no Unity — ninguém dirige os carros do tráfego);
  - detalhes: frisos (interrompidos nas caixas de roda), vincos das portas, maçanetas, retrovisores,
    grade, faróis com aro, piscas, para-choques, placas legíveis, lanternas, escapamento, antena,
    limpadores, tampa do tanque, emblema;
  - materiais procedurais com desgaste (realistic_kit) assados em PBR 2048.
Frente no -X do Blender (-> yaw -90 no prefab), Z para cima, lado direito no +Y.
Rodas "Wheel_FL/FR/RL/RR" com pivô no centro.

Uso: blender --background --python build_car.py -- <hatch|wagon|sedan|pickup> <saida.fbx> <pasta_texturas>
"""
import bpy, bmesh, sys, math, os
from mathutils import Vector
sys.path.append(os.path.dirname(os.path.abspath(__file__)))
import realistic_kit as K

argv = sys.argv[sys.argv.index("--") + 1:]
KIND, OUT_FBX, TEX_DIR = argv[0], argv[1], argv[2]
K.reset_scene()

# ------------------------------------------------------------------------------------ medidas
def hatch():
    x0, x1 = -1.85, 1.85
    return dict(
        L=3.70, hw=0.80, WR=0.29, tire=0.17, axles=(x0 + 0.70, x1 - 0.62),
        body=[(x0 + 0.04, 0.22), (x0, 0.30), (x0, 0.64), (x0 + 0.06, 0.72), (x0 + 0.60, 0.80),
              (x0 + 1.02, 0.84), (x1 - 0.06, 0.88), (x1, 0.82), (x1, 0.30), (x1 - 0.04, 0.22)],
        cab_bottom=[(x0 + 1.02, 0.835), (0.30, 0.852), (x1 - 0.06, 0.872)],
        cab_top=[(x0 + 1.72, 1.38), (0.45, 1.40), (x1 - 0.22, 1.36)],
        tumble=0.20, doors=[(x0 + 1.06, 0.36)], strip_z=0.50,
        paint=dict(base=(0.72, 0.46, 0.03), rough=0.36, wear=0.25, rust=0.05, dirt=0.35),
        bumper="black", lamp=("rect", 0.55, 0.62, 0.17, 0.06), grille=0.36,
        tail=("h", 0.60, 0.67, 0.15, 0.09), plate="GHF-2731", badge="GL 1.6",
        seats=(-0.10, 0.85), mirror_x=x0 + 1.12)

def wagon():
    x0, x1 = -2.225, 2.225
    return dict(
        L=4.45, hw=0.84, WR=0.30, tire=0.18, axles=(x0 + 0.86, x0 + 0.86 + 2.55),
        body=[(x0 + 0.05, 0.24), (x0, 0.32), (x0, 0.66), (x0 + 0.08, 0.74), (x0 + 0.80, 0.82),
              (x0 + 1.25, 0.86), (x1 - 0.05, 0.90), (x1, 0.86), (x1, 0.32), (x1 - 0.05, 0.24)],
        cab_bottom=[(x0 + 1.25, 0.855), (0.05, 0.872), (1.20, 0.884), (x1 - 0.05, 0.895)],
        cab_top=[(x0 + 1.95, 1.40), (0.10, 1.42), (1.25, 1.42), (x1 - 0.12, 1.41)],
        tumble=0.18, doors=[(x0 + 1.29, 0.08), (0.08, 1.15)], strip_z=0.52,
        paint=dict(base=(0.025, 0.11, 0.065), rough=0.42, wear=0.3, rust=0.12, dirt=0.4),
        bumper="chrome", lamp=("rect", 0.58, 0.63, 0.18, 0.07), grille=0.38,
        tail=("v", 0.70, 0.68, 0.07, 0.18), plate="LMP-4410", badge="CARAVAN",
        seats=(-0.45, 0.45), mirror_x=x0 + 1.35)

def sedan():
    x0, x1 = -2.25, 2.25
    return dict(
        L=4.50, hw=0.85, WR=0.30, tire=0.18, axles=(x0 + 0.88, x0 + 0.88 + 2.58),
        body=[(x0 + 0.05, 0.24), (x0, 0.32), (x0, 0.64), (x0 + 0.08, 0.72), (x0 + 0.85, 0.80),
              (x0 + 1.30, 0.84), (x1 - 0.85, 0.88), (x1 - 0.10, 0.90), (x1, 0.84), (x1, 0.32), (x1 - 0.05, 0.24)],
        cab_bottom=[(x0 + 1.30, 0.835), (0.10, 0.862), (x1 - 0.85, 0.875)],
        cab_top=[(x0 + 1.95, 1.37), (0.20, 1.38), (x1 - 1.35, 1.36)],
        tumble=0.20, doors=[(x0 + 1.34, 0.12), (0.12, 1.30)], strip_z=0.50,
        paint=dict(base=(0.045, 0.052, 0.065), rough=0.30, wear=0.15, rust=0.03, dirt=0.30),
        bumper="black", lamp=("rect", 0.56, 0.62, 0.19, 0.065), grille=0.36,
        tail=("h", 0.55, 0.70, 0.25, 0.08), plate="BDX-6625", badge="GLS 2.0",
        seats=(-0.40, 0.55), mirror_x=x0 + 1.40)

def pickup():
    x0, x1 = -2.20, 2.20
    return dict(
        L=4.40, hw=0.83, WR=0.33, tire=0.20, axles=(x0 + 0.88, x1 - 0.85),
        body=[(x0 + 0.05, 0.36), (x0, 0.42), (x0, 0.86), (x0 + 0.10, 0.96), (x0 + 0.70, 1.00),
              (x0 + 1.15, 1.02), (x1, 1.02), (x1, 0.42), (x1 - 0.05, 0.36)],
        cab_bottom=[(x0 + 1.15, 1.015), (x0 + 2.45, 1.015)],
        cab_top=[(x0 + 1.70, 1.60), (x0 + 2.40, 1.60)],
        tumble=0.16, doors=[(x0 + 1.19, x0 + 2.40)], strip_z=0.66,
        paint=dict(base=(0.28, 0.45, 0.60), rough=0.55, wear=0.55, rust=0.35, dirt=0.5),
        bumper="chrome", lamp=("round", 0.58, 0.72, 0.085, 0.085), grille=0.40,
        tail=("v", 0.72, 0.74, 0.07, 0.11), plate="KTR-0958", badge="D-20",
        seats=(x0 + 2.00, None), mirror_x=x0 + 1.25, bed=(x0 + 2.55, x1 - 0.07, 0.66))

S = {"hatch": hatch, "wagon": wagon, "sedan": sedan, "pickup": pickup}[KIND]()
L, HW, WR = S["L"], S["hw"], S["WR"]
X0, X1 = -L / 2, L / 2
XF, XR = S["axles"]
BELT = S["cab_bottom"][0][1]
E = 0.004

# ------------------------------------------------------------------------------------ materiais
p = S["paint"]
paint = K.material("Paint", p["base"], p["rough"], dirt=p["dirt"], ground_dirt=0.55, wear=p["wear"],
                   wear_color=(0.42, 0.42, 0.40), rust=p["rust"], color_var=0.035, rough_var=0.10, streaks=0.08)
chrome = K.material("Chrome", (0.80, 0.80, 0.80), 0.16, 1.0, dirt=0.35, rust=0.08, rough_var=0.08)
plastic = K.material("BlackPlastic", (0.035, 0.035, 0.037), 0.62, dirt=0.3, ground_dirt=0.4, bump=0.05, bump_scale=80)
rubber = K.material("Rubber", (0.025, 0.025, 0.025), 0.82, dirt=0.3, bump=0.1, bump_scale=90)
ink = K.material("Gap", (0.012, 0.012, 0.012), 0.8)
well = K.material("Well", (0.018, 0.017, 0.016), 0.92, dirt=0.6, dirt_color=(0.09, 0.08, 0.06))
tire = K.material("Tire", (0.03, 0.03, 0.03), 0.9, bump=0.6, bump_scale=160, dirt=0.4, ground_dirt=0.6,
                  dirt_color=(0.14, 0.12, 0.10))
steel = K.material("Steel", (0.42, 0.43, 0.44), 0.38, 1.0, dirt=0.55, rust=0.25)
fabric = K.material("Fabric", (0.07, 0.065, 0.06), 0.95, bump=0.3, bump_scale=220, color_var=0.08)
lining = K.material("Headliner", (0.30, 0.29, 0.27), 0.95, bump=0.1, bump_scale=200, dirt=0.3)
dash = K.material("Dash", (0.03, 0.03, 0.032), 0.7, bump=0.05)
head = K.material("HeadLamp", (0.88, 0.88, 0.84), 0.06, emission=(1.0, 0.95, 0.85), emission_strength=1.0)
tail = K.material("TailLamp", (0.42, 0.02, 0.02), 0.12, emission=(0.55, 0.02, 0.02), emission_strength=1.0)
amber = K.material("Amber", (0.80, 0.40, 0.04), 0.14, emission=(0.40, 0.20, 0.0), emission_strength=1.0)
reverse = K.material("Reverse", (0.85, 0.85, 0.82), 0.12)
plate = K.material("Plate", (0.70, 0.71, 0.70), 0.45, 0.2, dirt=0.6, wear=0.25)
mirror = K.material("Mirror", (0.85, 0.85, 0.85), 0.05, 1.0, dirt=0.2)

def rot_box(cx, cy, cz, sx, sy, sz, mat, rz=0.0, ry=0.0, rx=0.0):
    ob = K.box(cx - sx / 2, cx + sx / 2, cy - sy / 2, cy + sy / 2, cz - sz / 2, cz + sz / 2, mat)
    ob.rotation_euler = (rx, ry, rz)
    bpy.ops.object.select_all(action='DESELECT'); ob.select_set(True); bpy.context.view_layer.objects.active = ob
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=False)
    return ob

def new_obj(bm, name, mat_list):
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me); bm.free()
    ob = bpy.data.objects.new(name, me)
    bpy.context.collection.objects.link(ob)
    for m in mat_list: ob.data.materials.append(m)
    return ob

def activate(ob):
    bpy.ops.object.select_all(action='DESELECT'); ob.select_set(True); bpy.context.view_layer.objects.active = ob

def apply_mods(ob):
    activate(ob)
    for m in list(ob.modifiers):
        bpy.ops.object.modifier_apply(modifier=m.name)

def smooth(ob, deg=40):
    activate(ob)
    bpy.ops.object.shade_smooth_by_angle(angle=math.radians(deg))

def cutter_cyl(x, y, z, r, depth):
    bpy.ops.mesh.primitive_cylinder_add(vertices=48, radius=r, depth=depth, location=(x, y, z), rotation=(math.pi / 2, 0, 0))
    c = bpy.context.active_object
    c.data.materials.append(well)
    return c

def cutter_box(x0, x1, y0, y1, z0, z1):
    bpy.ops.mesh.primitive_cube_add(size=1, location=((x0 + x1) / 2, (y0 + y1) / 2, (z0 + z1) / 2))
    c = bpy.context.active_object
    c.scale = (x1 - x0, y1 - y0, z1 - z0)
    bpy.ops.object.transform_apply(scale=True)
    c.data.materials.append(well)
    return c

# ------------------------------------------------------------------------------------ carroceria
bm = bmesh.new()
A = [bm.verts.new((x, -HW, z)) for x, z in S["body"]]
B = [bm.verts.new((x, HW, z)) for x, z in S["body"]]
bm.faces.new(A); bm.faces.new(B[::-1])
n = len(A)
for i in range(n):
    j = (i + 1) % n
    bm.faces.new([A[i], A[j], B[j], B[i]])
bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
body = new_obj(bm, "Shell", [paint])

cutters = []
ARCH_R = WR + 0.055
for ax in (XF, XR):
    for s in (-1, 1):
        cutters.append(cutter_cyl(ax, s * (HW - 0.06), WR, ARCH_R, 0.52))
if "bed" in S:
    bx0, bx1, bz = S["bed"]
    cutters.append(cutter_box(bx0, bx1, -HW + 0.06, HW - 0.06, bz, 1.4))
for c in cutters:
    mod = body.modifiers.new("cut", 'BOOLEAN')
    mod.operation = 'DIFFERENCE'; mod.solver = 'EXACT'; mod.object = c
    try: mod.material_mode = 'TRANSFER'
    except Exception: pass
bev = body.modifiers.new("bevel", 'BEVEL')
bev.width = 0.045; bev.segments = 3; bev.limit_method = 'ANGLE'; bev.angle_limit = math.radians(30)
apply_mods(body)
for c in cutters: bpy.data.objects.remove(c, do_unlink=True)
smooth(body)
K.PARTS.append(body)

# ------------------------------------------------------------------------------------ cabine
bot, top = S["cab_bottom"], S["cab_top"]
WB = HW - 0.045
def half_w(z): return WB - max(0.0, z - BELT) * S["tumble"]
bm = bmesh.new()
VL, VR = {}, {}
for key, (x, z) in [(("b", i), bot[i]) for i in range(len(bot))] + [(("t", i), top[i]) for i in range(len(top))]:
    w = half_w(z)
    VL[key] = bm.verts.new((x, -w, z)); VR[key] = bm.verts.new((x, w, z))
m = len(bot)
wins = []
for V in (VL, VR):
    for i in range(m - 1):
        wins.append(bm.faces.new([V[("b", i)], V[("t", i)], V[("t", i + 1)], V[("b", i + 1)]]))
ws = bm.faces.new([VL[("b", 0)], VR[("b", 0)], VR[("t", 0)], VL[("t", 0)]])
roof = [bm.faces.new([VL[("t", i)], VR[("t", i)], VR[("t", i + 1)], VL[("t", i + 1)]]) for i in range(m - 1)]
bl = bm.faces.new([VL[("t", m - 1)], VR[("t", m - 1)], VR[("b", m - 1)], VL[("b", m - 1)]])
wins += [ws, bl]
bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
bm.normal_update()
if ws.normal.x > 0:  # para-brisa tem de olhar para a frente (-X)
    bmesh.ops.reverse_faces(bm, faces=bm.faces); bm.normal_update()
bmesh.ops.inset_individual(bm, faces=wins, thickness=0.065, depth=0.0, use_even_offset=True)
bm.normal_update()
glass_polys = []
for f in wins:
    nrm = f.normal.copy()
    for v in f.verts:
        v.co -= nrm * 0.022  # rebaixo do vidro na moldura
    glass_polys.append([v.co.copy() for v in f.verts])
bmesh.ops.delete(bm, geom=wins, context='FACES_ONLY')
cab = new_obj(bm, "Cabin", [paint])
bev = cab.modifiers.new("bevel", 'BEVEL')
bev.width = 0.028; bev.segments = 2; bev.limit_method = 'ANGLE'; bev.angle_limit = math.radians(25)
apply_mods(cab)
smooth(cab)
# Forro interno (a casca vista por dentro, pelas janelas — sem ele o teto some com o culling)
bm = bmesh.new(); bm.from_mesh(cab.data)
bmesh.ops.reverse_faces(bm, faces=bm.faces)
bm.normal_update()
for v in bm.verts:
    v.co += v.normal * 0.012
liner = new_obj(bm, "Liner", [lining])
K.PARTS += [cab, liner]

# Vidro (objeto separado, fica fora do bake)
bm = bmesh.new()
for poly in glass_polys:
    bm.faces.new([bm.verts.new(c) for c in poly])
glass = new_obj(bm, "Glass", [bpy.data.materials.new("CarGlass")])

# ------------------------------------------------------------------------------------ interior
sf, sr = S["seats"]
WI = half_w(BELT) - 0.06
HCAB = top[0][1] - BELT
def seat(x, y, width):
    K.box(x - 0.24, x + 0.24, y - width / 2, y + width / 2, BELT - 0.04, BELT + 0.04, fabric, bevel=0.03)
    rot_box(x + 0.27, y, BELT + 0.17, 0.09, width, 0.30, fabric, ry=math.radians(-12))
    K.box(x + 0.29, x + 0.37, y - 0.12, y + 0.12, BELT + 0.31, BELT + HCAB - 0.12, fabric, bevel=0.02)
for y in (-0.36, 0.36):
    seat(sf, y, 0.46)
if sr is not None:
    seat(sr, 0.0, 2 * WI - 0.10)
dx = bot[0][0]
RUN = (top[0][0] - dx) / (top[0][1] - bot[0][1])  # avanço do para-brisa por metro de altura
DX0 = dx + 0.17 * RUN + 0.05
K.box(DX0, DX0 + 0.32, -WI, WI, BELT - 0.02, BELT + 0.13, dash, bevel=0.03)
bpy.ops.mesh.primitive_torus_add(major_radius=0.17, minor_radius=0.016, location=(DX0 + 0.42, -0.36, BELT + 0.20),
                                 rotation=(0, math.radians(62), 0))
K._finish(bpy.context.active_object, dash)
rot_box(DX0 + 0.33, -0.36, BELT + 0.16, 0.20, 0.04, 0.04, dash, ry=math.radians(-28))

# ------------------------------------------------------------------------------------ laterais
def gaps_between(arches, a, b):
    segs, x = [], a
    for g0, g1 in sorted(arches):
        if g0 > x: segs.append((x, g0))
        x = max(x, g1)
    if x < b: segs.append((x, b))
    return segs
arches = [(ax - ARCH_R - 0.05, ax + ARCH_R + 0.05) for ax in (XF, XR)]
z_low = WR + ARCH_R + 0.02
for s in (-1, 1):
    y0, y1 = sorted((s * HW, s * (HW + 0.014)))
    # Friso de borracha (não passa na frente do pneu)
    for a, b in gaps_between(arches, X0 + 0.20, X1 - 0.20):
        K.box(a, b, y0, y1, S["strip_z"] - 0.022, S["strip_z"] + 0.022, rubber, bevel=0.004)
    yg0, yg1 = sorted((s * HW, s * (HW + 0.0015)))
    for da, db in S["doors"]:
        for x in (da, db):
            K.box(x - 0.0025, x + 0.0025, yg0, yg1, S["strip_z"] - 0.24, BELT - 0.025, ink)
        K.box(db - 0.20, db - 0.07, *sorted((s * HW, s * (HW + 0.02))), BELT - 0.11, BELT - 0.08, chrome, bevel=0.006)
    # Retrovisor
    mx = S["mirror_x"]
    K.box(mx - 0.05, mx + 0.05, *sorted((s * (HW - 0.02), s * (HW + 0.10))), BELT + 0.04, BELT + 0.07, plastic)
    K.box(mx - 0.03, mx + 0.07, *sorted((s * (HW + 0.06), s * (HW + 0.22))), BELT + 0.04, BELT + 0.15, plastic, bevel=0.015)
    K.box(mx - 0.035, mx - 0.03, *sorted((s * (HW + 0.075), s * (HW + 0.205))), BELT + 0.05, BELT + 0.14, mirror)
# Tampa do tanque (lado direito, atrás)
K.box(XR - 0.55, XR - 0.40, HW, HW + 0.003, BELT - 0.20, BELT - 0.07, paint)
K.box(XR - 0.555, XR - 0.395, HW, HW + 0.0015, BELT - 0.205, BELT - 0.065, ink)
# Antena no para-lama dianteiro esquerdo
K.cyl(XF + 0.30, -HW + 0.10, BELT + 0.29, 0.004, 0.58, chrome, verts=6)
K.cyl(XF + 0.30, -HW + 0.10, BELT + 0.01, 0.014, 0.03, chrome, verts=8)
# Limpadores sobre o capô, na base do para-brisa
for y in (-0.30, 0.22):
    rot_box(dx + 0.06, y, bot[0][1] + 0.025, 0.02, 0.48, 0.015, ink, rz=math.radians(8))

# ------------------------------------------------------------------------------------ frente
FX = X0
style, ly, lz, lw, lh = S["lamp"]
gy = S["grille"]
for s in (-1, 1):
    if style == "round":
        K.cyl(FX - 0.010, s * ly, lz, lw + 0.022, 0.03, chrome, axis='X', verts=24)
        K.cyl(FX - 0.022, s * ly, lz, lw, 0.02, head, axis='X', verts=24)
    else:
        K.box(FX - 0.016, FX + 0.01, s * ly - lw - 0.018, s * ly + lw + 0.018, lz - lh - 0.018, lz + lh + 0.018, chrome, bevel=0.006)
        K.box(FX - 0.022, FX - 0.016, s * ly - lw, s * ly + lw, lz - lh, lz + lh, head)
    ay = s * (ly + lw + 0.09)
    K.box(FX - 0.018, FX + 0.01, ay - 0.06, ay + 0.06, lz - lh, lz + lh * 0.5, amber, bevel=0.004)
gz0, gz1 = lz - lh - 0.01, lz + lh + 0.01
K.box(FX - 0.014, FX + 0.01, -gy, gy, gz0, gz1, chrome, bevel=0.006)
K.box(FX - 0.016, FX - 0.010, -gy + 0.025, gy - 0.025, gz0 + 0.02, gz1 - 0.02, ink)
for i in range(5):
    z = gz0 + 0.03 + i * (gz1 - gz0 - 0.06) / 4
    K.box(FX - 0.020, FX - 0.012, -gy + 0.03, gy - 0.03, z - 0.006, z + 0.006, chrome)
K.text(S["badge"].split()[0], FX - 0.021, 0.0, gz1 + 0.035, 0.035, chrome, rot=(math.pi / 2, 0, -math.pi / 2), extrude=0.003)

bmat = chrome if S["bumper"] == "chrome" else plastic
bz0 = S["body"][0][1] + 0.02
bz1 = bz0 + 0.17
def bumper(xa, xb):
    K.box(xa, xb, -HW - 0.01, HW + 0.01, bz0, bz1, bmat, bevel=0.035)
    xr = xa - 0.003 if xa < 0 else xb + 0.003
    K.box(min(xr, xr + (0.012 if xa > 0 else -0.012)), max(xr, xr + (0.012 if xa > 0 else -0.012)),
          -HW + 0.10, HW - 0.10, bz0 + 0.07, bz0 + 0.11, rubber)
bumper(FX - 0.08, FX + 0.22)
def plate_at(x, face):
    """face -1 = olha para -X (frente), +1 = olha para +X (trás)."""
    pz = bz1 + 0.085 if face < 0 else bz1 + 0.10
    xa, xb = sorted((x, x + face * 0.008))
    K.box(xa, xb, -0.20, 0.20, pz - 0.065, pz + 0.065, plate, bevel=0.004)
    xt = x + face * 0.0105
    rot = (math.pi / 2, 0, -math.pi / 2) if face < 0 else (math.pi / 2, 0, math.pi / 2)
    K.text(S["plate"], xt, 0.0, pz - 0.012, 0.085, ink, rot=rot, extrude=0.0015)
    K.text("PORTO  BR", xt, 0.0, pz + 0.047, 0.022, ink, rot=rot, extrude=0.0015)
plate_at(FX - 0.08, -1)

# ------------------------------------------------------------------------------------ traseira
RX = X1
tstyle, ty, tz, tw, th = S["tail"]
for s in (-1, 1):
    K.box(RX - 0.01, RX + 0.014, s * ty - tw - 0.012, s * ty + tw + 0.012, tz - th - 0.012, tz + th + 0.012, plastic, bevel=0.005)
    if tstyle == "h":
        K.box(RX + 0.014, RX + 0.020, s * ty - tw, s * ty + tw * 0.25, tz - th, tz + th, tail)
        K.box(RX + 0.014, RX + 0.020, s * ty + tw * 0.25, s * ty + tw * 0.62, tz - th, tz + th, reverse)
        K.box(RX + 0.014, RX + 0.020, s * ty + tw * 0.62, s * ty + tw, tz - th, tz + th, amber)
    else:
        K.box(RX + 0.014, RX + 0.020, s * ty - tw, s * ty + tw, tz - th * 0.2, tz + th, tail)
        K.box(RX + 0.014, RX + 0.020, s * ty - tw, s * ty + tw, tz - th * 0.6, tz - th * 0.2, reverse)
        K.box(RX + 0.014, RX + 0.020, s * ty - tw, s * ty + tw, tz - th, tz - th * 0.6, amber)
bumper(RX - 0.22, RX + 0.08)
plate_at(RX + 0.08, 1)
if "bed" not in S:
    K.text(S["badge"], RX + 0.003, -0.42, tz + th + 0.06, 0.04, chrome, rot=(math.pi / 2, 0, math.pi / 2), extrude=0.003)
else:
    K.text(S["badge"], RX + 0.003, 0.0, 0.92, 0.07, chrome, rot=(math.pi / 2, 0, math.pi / 2), extrude=0.003)
    bx0, bx1, bz = S["bed"]
    # Caixas de roda dentro da caçamba + frisos da tampa
    for s in (-1, 1):
        K.box(XR - ARCH_R - 0.03, XR + ARCH_R + 0.03, *sorted((s * (HW - 0.06), s * (HW - 0.32))), bz, bz + 0.20, paint, bevel=0.03)
    for k in range(5):
        y = -0.60 + k * 0.30
        K.box(bx0 + 0.05, bx1 - 0.05, y - 0.02, y + 0.02, bz, bz + 0.012, paint)
    K.box(RX - 0.003, RX + 0.001, -HW + 0.08, HW - 0.08, 0.98, 0.99, ink)
K.cyl(RX - 0.05, 0.45, S["body"][0][1] + 0.02, 0.024, 0.30, steel, axis='X', verts=12)

# ------------------------------------------------------------------------------------ fecha a carroceria
body_all = K.join(list(K.PARTS), "Body")
K.PARTS.clear()

# ------------------------------------------------------------------------------------ rodas
wheels = []
TW = S["tire"]
for ax, axn in ((XF, "F"), (XR, "R")):
    for s, sn in ((-1, "L"), (1, "R")):
        y = s * (HW - 0.05 - TW / 2)
        parts = [K.cyl(ax, y, WR, WR, TW, tire, axis='Y', verts=32, bevel=0.035),
                 K.cyl(ax, y + s * 0.004, WR, WR * 0.64, TW + 0.006, steel, axis='Y', verts=24),
                 K.cyl(ax, y + s * 0.012, WR, WR * 0.46, TW + 0.02, chrome, axis='Y', verts=24, bevel=0.01),
                 K.cyl(ax, y + s * 0.018, WR, WR * 0.12, TW + 0.03, steel, axis='Y', verts=12)]
        for k in range(4):
            a = k * math.pi / 2 + math.pi / 4
            parts.append(K.cyl(ax + math.cos(a) * WR * 0.24, y + s * 0.02, WR + math.sin(a) * WR * 0.24,
                               0.013, TW + 0.03, steel, axis='Y', verts=6))
        w = K.join(parts, f"Wheel_{axn}{sn}")
        K.PARTS.clear()
        bpy.ops.object.origin_set(type='ORIGIN_GEOMETRY', center='BOUNDS')
        wheels.append(w)

objs = [body_all] + wheels
if len(argv) > 3 and argv[3] == "preview":
    # Prévia de geometria (sem bake): 4 vistas no Workbench com a cor do material.
    sc = bpy.context.scene
    sc.render.engine = 'BLENDER_WORKBENCH'
    sc.display.shading.color_type = 'MATERIAL'
    sc.display.shading.light = 'STUDIO'
    for mt in bpy.data.materials:
        bsdf = mt.node_tree.nodes.get("Principled BSDF") if mt.use_nodes else None
        col = None
        for nd in (mt.node_tree.nodes if mt.use_nodes else []):
            if nd.type == 'BSDF_PRINCIPLED':
                src = nd.inputs['Base Color']
                col = src.default_value if not src.is_linked else None
        mt.diffuse_color = col if col is not None else (0.5, 0.5, 0.5, 1)
    for mt, c in ((paint, p["base"]), (glass.data.materials[0], (0.1, 0.2, 0.25))):
        mt.diffuse_color = (c[0], c[1], c[2], 1)
    sc.render.resolution_x, sc.render.resolution_y = 900, 560
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
    bpy.context.collection.objects.link(cam); sc.camera = cam
    cam.data.lens = 50
    views = {"34": Vector((-6.0, -5.0, 2.6)), "side": Vector((0.0, -9.0, 0.9)),
             "back": Vector((7.5, 3.0, 2.0)), "front": Vector((-8.0, 0.0, 1.0))}
    for nm, pos in views.items():
        cam.location = pos
        d = Vector((0, 0, 0.7)) - pos
        cam.rotation_euler = d.to_track_quat('-Z', 'Y').to_euler()
        sc.render.filepath = os.path.join(TEX_DIR, f"preview_{KIND}_{nm}.png")
        bpy.ops.render.render(write_still=True)
    print("PREVIEW OK")
    sys.exit(0)
K.unwrap(objs, margin=0.003)
K.bake_all(objs, TEX_DIR, 2048)
K.collapse_materials(objs)
for o in wheels + [glass]:
    o.parent = body_all
K.export_fbx(OUT_FBX)
tris = sum(len(p.vertices) - 2 for o in objs + [glass] for p in o.data.polygons)
print("LAMP %s lampX=%.3f lampY=%.3f" % (KIND, ly, lz))
print("RESULT %s tris=%d" % (KIND, tris))
print("OK", OUT_FBX)
