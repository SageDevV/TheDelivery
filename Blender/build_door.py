"""Portas de apartamento REALISTAS (Ato 3/4) — por script, 0 créditos.

Folha almofadada de madeira (montantes, travessas, 4 almofadas com campo em relevo), ferragens: maçaneta de alavanca
com espelho longo dos dois lados, cilindro de tetra-chave, olho mágico, número em placa de latão, 3 dobradiças.
A porta do vizinho que abre com corrente ganha o trinco de corrente na face de dentro. Guarnição (batente +
alisares) pintada para emoldurar o vão da parede.

Eixos do Blender (o FBX vira Unity assim: +X do Blender -> -X do Unity, +Y -> -Z, +Z -> +Y):
  folha: dobradiça em x=0, a folha vai para -X (Unity +X); altura em Z centrada (−H/2..H/2); espessura em Y.
  out=+1: a face do CORREDOR é +Y do Blender (= -Z do Unity); out=-1: é -Y (= +Z do Unity).
  guarnição: centrada em x=0, chão em z=0, parede em y=±0,075.

Uso: blender --background --python build_door.py -- <saida.fbx> <pasta_texturas> [<pasta_previas>] [previewonly]
"""
import bpy, bmesh, sys, math, os, random
sys.path.append(os.path.dirname(os.path.abspath(__file__)))
import realistic_kit as K

argv = sys.argv[sys.argv.index("--") + 1:]
OUT_FBX, TEX_DIR = argv[0], argv[1]
PREV_DIR = argv[2] if len(argv) > 2 else None
PREVIEW_ONLY = len(argv) > 3 and argv[3] == "previewonly"
random.seed(302)
K.reset_scene()

def lin(c): return tuple(x ** 2.2 for x in c)

# ------------------------------------------------------------------ materiais
def wood_material():
    """Folheado de madeira: veios em FIOS LONGOS na vertical (ruído esticado em Z), poros finos, verniz acetinado
    com brilho irregular, um pouco mais escuro nas bordas (AO do próprio objeto)."""
    m = bpy.data.materials.new("DoorWood"); m.use_nodes = True
    nt = m.node_tree; nt.nodes.clear()
    out = nt.nodes.new('ShaderNodeOutputMaterial'); p = nt.nodes.new('ShaderNodeBsdfPrincipled')
    nt.links.new(p.outputs[0], out.inputs['Surface'])
    tc = nt.nodes.new('ShaderNodeTexCoord')
    def stretched(scale_xy, scale_z, detail, rough=0.6):
        mp = nt.nodes.new('ShaderNodeMapping'); mp.inputs['Scale'].default_value = (scale_xy, scale_xy, scale_z)
        nt.links.new(tc.outputs['Object'], mp.inputs['Vector'])
        n = nt.nodes.new('ShaderNodeTexNoise'); n.inputs['Scale'].default_value = 1.0
        n.inputs['Detail'].default_value = detail; n.inputs['Roughness'].default_value = rough
        nt.links.new(mp.outputs[0], n.inputs['Vector']); return n.outputs['Fac']
    def math_(op, a, b):
        mm = nt.nodes.new('ShaderNodeMath'); mm.operation = op
        for i, v in enumerate((a, b)):
            if isinstance(v, (int, float)): mm.inputs[i].default_value = v
            else: nt.links.new(v, mm.inputs[i])
        return mm.outputs[0]
    grain = stretched(55.0, 1.2, 8.0)          # faixas longas de veio
    fine = stretched(700.0, 6.0, 4.0)          # fibras e poros
    blot = stretched(6.0, 2.0, 3.0)            # manchas largas do tingimento
    f = math_('ADD', math_('MULTIPLY', grain, 0.65), math_('ADD', math_('MULTIPLY', fine, 0.2), math_('MULTIPLY', blot, 0.25)))
    ramp = nt.nodes.new('ShaderNodeValToRGB'); cr = ramp.color_ramp
    stops = [(0.30, (0.22, 0.11, 0.06)), (0.50, (0.38, 0.20, 0.11)), (0.62, (0.46, 0.26, 0.15)), (0.78, (0.55, 0.33, 0.19))]
    while len(cr.elements) < len(stops): cr.elements.new(0.5)
    for el, (pos, col) in zip(cr.elements, stops): el.position = pos; el.color = (*lin(col), 1.0)
    nt.links.new(f, ramp.inputs['Fac'])
    ao = nt.nodes.new('ShaderNodeAmbientOcclusion'); ao.only_local = True; ao.inputs['Distance'].default_value = 0.03
    aof = math_('ADD', 0.45, math_('MULTIPLY', ao.outputs['AO'], 0.55))
    mix = nt.nodes.new('ShaderNodeMix'); mix.data_type = 'RGBA'; mix.blend_type = 'MULTIPLY'; mix.inputs['Factor'].default_value = 1.0
    nt.links.new(ramp.outputs['Color'], mix.inputs[6]); nt.links.new(aof, mix.inputs[7])
    nt.links.new(mix.outputs[2], p.inputs['Base Color'])
    nt.links.new(math_('ADD', 0.30, math_('MULTIPLY', blot, 0.25)), p.inputs['Roughness'])
    bump = nt.nodes.new('ShaderNodeBump'); bump.inputs['Strength'].default_value = 0.18; bump.inputs['Distance'].default_value = 0.001
    nt.links.new(math_('ADD', fine, math_('MULTIPLY', grain, 0.5)), bump.inputs['Height']); nt.links.new(bump.outputs['Normal'], p.inputs['Normal'])
    K.MATS["DoorWood"] = m
    return m
WOOD = wood_material()
BRASS = K.material("Brass", lin((0.80, 0.64, 0.34)), rough=0.30, metal=1.0, dirt=0.35, dirt_color=lin((0.25, 0.20, 0.10)), color_var=0.12)
STEEL = K.material("Steel", lin((0.78, 0.78, 0.80)), rough=0.26, metal=1.0, dirt=0.15, color_var=0.06, rough_var=0.15)
DARK = K.material("Engrave", lin((0.08, 0.07, 0.06)), rough=0.6)
LENS = K.material("Lens", lin((0.05, 0.06, 0.08)), rough=0.06)
PAINT = K.material("FramePaint", lin((0.86, 0.84, 0.78)), rough=0.55, dirt=0.35, dirt_color=lin((0.42, 0.38, 0.32)), ground_dirt=0.35,
                   wear=0.18, wear_color=lin((0.62, 0.56, 0.46)), color_var=0.05, bump=0.05)

def box(x0, x1, y0, y1, z0, z1, mat, bevel=0.0, seg=2):
    return K.box(min(x0, x1), max(x0, x1), min(y0, y1), max(y0, y1), min(z0, z1), max(z0, z1), mat, bevel=bevel, segments=seg)

def cylY(x, y, z, r, depth, mat, verts=24, bevel=0.0):
    return K.cyl(x, y, z, r, depth, mat, axis='Y', verts=verts, bevel=bevel)

def cylZ(x, y, z, r, depth, mat, verts=16, bevel=0.0):
    return K.cyl(x, y, z, r, depth, mat, axis='Z', verts=verts, bevel=bevel)

def text_on_face(s, x, z, yface, out, size, mat):
    # texto deitado na face (lendo de quem olha de fora): rotação para ficar em pé, virado para ±Y
    rot = (math.pi / 2, 0, math.pi) if out > 0 else (math.pi / 2, 0, 0)
    ob = K.text(s, x, yface, z, size, mat, rot=rot, extrude=0.0012, align='CENTER')
    return ob

# ------------------------------------------------------------------ folha
T = 0.045           # espessura da folha
def leaf(name, W, H, out, number, chain=False):
    start = len(K.PARTS)
    h2, t2 = H / 2, T / 2
    st, rt_top, rt_bot, lock0, lock1, mul = 0.115, 0.12, 0.22, -0.11, 0.07, 0.05
    # montantes e travessas (a moldura da folha)
    box(0, -st, -t2, t2, -h2, h2, WOOD, 0.003)
    box(-W + st, -W, -t2, t2, -h2, h2, WOOD, 0.003)
    box(-st, -W + st, -t2, t2, h2 - rt_top, h2, WOOD, 0.003)
    box(-st, -W + st, -t2, t2, -h2, -h2 + rt_bot, WOOD, 0.003)
    box(-st, -W + st, -t2, t2, lock0, lock1, WOOD, 0.003)
    box(-W / 2 + mul, -W / 2 - mul, -t2, t2, -h2 + rt_bot, lock0, WOOD, 0.003)
    box(-W / 2 + mul, -W / 2 - mul, -t2, t2, lock1, h2 - rt_top, WOOD, 0.003)
    # 4 almofadas: tábua rebaixada + campo em relevo chanfrado
    for (xa, xb) in ((-st, -W / 2 + mul), (-W / 2 - mul, -W + st)):
        for (za, zb) in ((-h2 + rt_bot, lock0), (lock1, h2 - rt_top)):
            box(xa, xb, -0.011, 0.011, za, zb, WOOD, 0.001)
            box(xa - 0.035 if xa > xb else xa + 0.035, xb + 0.035 if xa > xb else xb - 0.035, -0.019, 0.019,
                za + 0.035, zb - 0.035, WOOD, 0.010, 3)
    # dobradiças (lado de dentro)
    for z in (h2 - 0.25, 0.0, -h2 + 0.25):
        cylZ(0.004, -out * (t2 - 0.004), z, 0.008, 0.10, STEEL, 16, 0.001)
        box(0.0, -0.028, -out * (t2 + 0.0015), -out * t2, z - 0.05, z + 0.05, STEEL, 0.0005)
    # maçaneta de alavanca com espelho longo, dos dois lados
    hx = -W + 0.065
    for side in (1, -1):
        yf = side * t2
        box(hx - 0.024, hx + 0.024, yf, yf + side * 0.008, -0.17, 0.11, STEEL, 0.004, 3)       # espelho
        cylY(hx, yf + side * 0.022, -0.07, 0.012, 0.028, STEEL, 24, 0.002)                       # roseta/haste
        box(hx - 0.010, hx + 0.125, yf + side * 0.030, yf + side * 0.046, -0.080, -0.060, STEEL, 0.005, 3)   # alavanca
        if side == out:
            cylY(hx, yf + side * 0.010, 0.05, 0.0135, 0.008, BRASS, 24, 0.001)                  # cilindro da tetra
            box(hx - 0.002, hx + 0.002, yf + side * 0.0135, yf + side * 0.0145, 0.042, 0.058, DARK)   # fenda da chave
        else:
            cylY(hx, yf + side * 0.010, 0.05, 0.016, 0.008, STEEL, 24, 0.001)
            box(hx - 0.004, hx + 0.004, yf + side * 0.014, yf + side * 0.026, 0.035, 0.065, STEEL, 0.002)   # tranqueta
    # olho mágico (atravessa a folha)
    px, pz = -W / 2, 0.53
    cylY(px, 0, pz, 0.0075, T + 0.006, STEEL, 20)
    cylY(px, out * (t2 + 0.002), pz, 0.0135, 0.004, BRASS, 24, 0.0008)
    cylY(px, out * (t2 + 0.0042), pz, 0.0065, 0.001, LENS, 20)
    cylY(px, -out * (t2 + 0.0015), pz, 0.011, 0.003, STEEL, 24, 0.0006)
    # número em placa de latão, por fora
    pz2 = 0.70
    box(px - 0.075, px + 0.075, out * t2, out * (t2 + 0.004), pz2 - 0.032, pz2 + 0.032, BRASS, 0.0015, 2)
    text_on_face(number, px, pz2, out * (t2 + 0.0042), out, 0.05, DARK)
    # corrente de segurança (face de dentro), pendurada da guia até a borda
    if chain:
        yi = -out * t2
        cz = 0.42
        box(hx - 0.06, hx + 0.03, yi, yi - out * 0.006, cz - 0.012, cz + 0.012, BRASS, 0.002)
        # pino na ponta da guia e a corrente pendurada (elos alongados, alternando 90°, encaixados)
        cylY(hx - 0.05, yi - out * 0.010, cz, 0.006, 0.012, BRASS, 12)
        yc = yi - out * 0.014
        n_links, pitch = 15, 0.0115
        for k in range(n_links):
            z = cz - 0.010 - k * pitch
            x = hx - 0.05 + 0.006 * math.sin(k * 0.5)              # leve balanço
            bpy.ops.mesh.primitive_torus_add(major_radius=0.0072, minor_radius=0.0017, major_segments=14, minor_segments=6,
                                             location=(x, yc, z))
            o = bpy.context.active_object
            o.scale = (0.62, 1.0, 1.0)                               # elo oval
            o.rotation_euler = (math.pi / 2, 0, (math.pi / 2) if k % 2 else 0.0)   # em pé; elos alternados a 90°
            bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
            K._finish(o, BRASS)
        bpy.ops.mesh.primitive_uv_sphere_add(segments=12, ring_count=8, radius=0.0065, location=(hx - 0.05, yc, cz - 0.010 - n_links * pitch - 0.004))
        K._finish(bpy.context.active_object, BRASS)                 # a bolota da ponta
    parts = K.PARTS[start:]
    return K.join(parts, name)

# ------------------------------------------------------------------ guarnição (batente + alisares)
def frame(name, Wo, Ho, D=0.15):
    start = len(K.PARTS)
    d2 = D / 2 + 0.001
    cw, ct = 0.07, 0.018
    # batente (forro do vão)
    box(Wo / 2, Wo / 2 - 0.012, -d2, d2, 0, Ho, PAINT, 0.002)
    box(-Wo / 2, -Wo / 2 + 0.012, -d2, d2, 0, Ho, PAINT, 0.002)
    box(-Wo / 2, Wo / 2, -d2, d2, Ho - 0.012, Ho, PAINT, 0.002)
    # alisares dos dois lados, com um friso
    for s in (1, -1):
        y0, y1 = s * d2, s * (d2 + ct)
        box(Wo / 2 - 0.01, Wo / 2 + cw, y0, y1, 0, Ho - 0.011, PAINT, 0.004, 3)
        box(-Wo / 2 + 0.01, -Wo / 2 - cw, y0, y1, 0, Ho - 0.011, PAINT, 0.004, 3)
        box(-Wo / 2 - cw, Wo / 2 + cw, y0, y1, Ho - 0.01, Ho + cw, PAINT, 0.004, 3)
        yb0, yb1 = s * (d2 + ct), s * (d2 + ct + 0.005)
        box(Wo / 2 + cw - 0.018, Wo / 2 + cw - 0.008, yb0, yb1, 0, Ho + cw - 0.018, PAINT, 0.002)
        box(-Wo / 2 - cw + 0.018, -Wo / 2 - cw + 0.008, yb0, yb1, 0, Ho + cw - 0.018, PAINT, 0.002)
        box(-Wo / 2 - cw + 0.008, Wo / 2 + cw - 0.008, yb0, yb1, Ho + cw - 0.018, Ho + cw - 0.008, PAINT, 0.002)
    return K.join(K.PARTS[start:], name)

# Medidas dos vãos (DoorProbe, 2026-11-24): apê 0,935 x 2,10; vizinhos 0,99 x 2,06. Batente de 1,2 cm dentro do vão + folga de 3 mm.
L302 = leaf("Door_302", 0.905, 2.06, +1, "302")
L301 = leaf("Door_301", 0.960, 2.03, +1, "301")
L303 = leaf("Door_303", 0.960, 2.03, -1, "303", chain=True)
F_APT = frame("Frame_Apt", 0.935, 2.10)
F_NB = frame("Frame_Neighbor", 0.99, 2.06)
ALL = [L302, L301, L303, F_APT, F_NB]

# ------------------------------------------------------------------ prévias
def previews():
    sc = bpy.context.scene
    sc.render.resolution_x, sc.render.resolution_y = 800, 1000
    sc.cycles.samples = 40
    sc.view_settings.view_transform = 'Standard'
    w = bpy.data.worlds.new("w"); sc.world = w; w.use_nodes = True
    w.node_tree.nodes['Background'].inputs['Color'].default_value = (0.3, 0.3, 0.32, 1)
    w.node_tree.nodes['Background'].inputs['Strength'].default_value = 0.5
    # parede de fundo com o vão (para ver a guarnição no lugar)
    wallm = K.material("PrevWall", lin((0.62, 0.60, 0.56)), rough=0.8)
    walls = [K.box(-1.6, -0.4675, -0.075, 0.075, 0, 2.6, wallm), K.box(0.4675, 1.6, -0.075, 0.075, 0, 2.6, wallm), K.box(-0.4675, 0.4675, -0.075, 0.075, 2.10, 2.6, wallm),
             K.box(-1.6, 1.6, -1.5, 1.5, -0.02, 0, K.material("PrevFloor", lin((0.35, 0.30, 0.26)), rough=0.7))]
    bpy.ops.object.light_add(type='AREA', location=(1.2, 2.2, 2.4)); L = bpy.context.active_object
    L.data.energy = 320; L.data.size = 1.2; L.rotation_euler = (math.radians(-55), 0, math.radians(150))
    bpy.ops.object.light_add(type='AREA', location=(-1.5, -2.0, 1.8)); L2 = bpy.context.active_object
    L2.data.energy = 90; L2.data.size = 1.5; L2.rotation_euler = (math.radians(60), 0, math.radians(-35))
    bpy.ops.object.camera_add(); cam = bpy.context.active_object; sc.camera = cam; cam.data.lens = 35
    tgt = bpy.data.objects.new("t", None); bpy.context.collection.objects.link(tgt)
    tr = cam.constraints.new('TRACK_TO'); tr.target = tgt; tr.track_axis = 'TRACK_NEGATIVE_Z'; tr.up_axis = 'UP_Y'
    # a folha 302 montada no vão (dobradiça à esquerda de quem olha de fora): em x=-0.4625 indo para +X? a folha vai para -X,
    # então gira 180° em Z e põe a dobradiça em +0.4625
    L302.location = (0.4625, 0, 0.015 + 2.06 / 2); L302.rotation_euler = (0, 0, 0)
    F_APT.location = (0, 0, 0)
    def shot(fn, show, cam_loc, tgt_loc, lens=35):
        for o in bpy.data.objects:
            if o.type == 'MESH': o.hide_render = not (o in show or o in walls)
        cam.location = cam_loc; tgt.location = tgt_loc; cam.data.lens = lens
        sc.render.filepath = os.path.join(PREV_DIR, fn); bpy.ops.render.render(write_still=True)
    show = [L302, F_APT]
    shot("01_corredor.png", show, (0.25, 3.0, 1.2), (0, 0, 1.08))
    shot("02_tres_quartos.png", show, (1.6, 2.4, 1.5), (0, 0, 1.05))
    shot("03_ferragens.png", show, (-0.05, 0.75, 1.15), (-0.30, 0, 1.15), 50)
    shot("04_numero_olho.png", show, (0.05, 0.55, 1.62), (0, 0, 1.62), 50)
    shot("05_dentro.png", show, (-0.25, -3.0, 1.2), (0, 0, 1.08))
    # 303 com a corrente: troca a folha
    L303.location = (0.489, 0, 0.015 + 2.03 / 2); L303.rotation_euler = (0, 0, math.radians(-60))   # aberta, para ver a corrente
    F_NB.location = (0, 0, 0)
    shot("06_vizinho_corrente.png", [L303, F_NB], (0.75, 1.15, 1.45), (0.05, 0.75, 1.36), 40)
    for o in (L302, L303, F_APT, F_NB):
        o.location = (0, 0, 0); o.rotation_euler = (0, 0, 0)
    for o in walls: bpy.data.objects.remove(o)

if PREV_DIR:
    os.makedirs(PREV_DIR, exist_ok=True)
    previews()
    for o in ALL: o.hide_render = False
    if PREVIEW_ONLY: sys.exit(0)

# ------------------------------------------------------------------ bake + FBX
for o in list(bpy.data.objects):
    if o not in ALL: bpy.data.objects.remove(o)
K.unwrap(ALL, margin=0.003)
bpy.context.scene.cycles.samples = 16
K.bake_all(ALL, TEX_DIR, size=2048)
K.collapse_materials(ALL, "DoorBaked")
K.export_fbx(OUT_FBX)
print("[build_door] OK:", OUT_FBX, [(o.name, len(o.data.polygons)) for o in ALL])
