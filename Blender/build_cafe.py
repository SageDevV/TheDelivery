"""Acabamento da CAFETERIA (Ato 1, 2026-11-25) — por script, 0 créditos. O café ganha nome e vida: "Grão do Porto".

  counter : EspressoMachine (2 grupos, manômetros, vaporizador), Grinder (moedor com grãos), PosTerminal (tablet +
            maquininha), TipCan ("caixinha"), CarrotCake (bolo de cenoura com cobertura, fatia tirada), PaoDeQueijo
            (cesta), CupStack (xícaras e pires), MilkPitcher, NapkinBox, MenuBoard (cardápio de giz), CafeSign (letreiro
            "GRÃO DO PORTO"), NeonCafe ("café" em neon)
  tables  : Centerpiece (porta-guardanapo, açucareiro, vasinho com flor, plaquinha do Wi-Fi), UsedCups (xícara, pires,
            prato com farelo e garfo), Newspaper (Diário do Porto dobrado: a moradora desaparecida do Porto Sereno),
            Textbook ("Algoritmos e Estruturas de Dados"), StudyNotes (caderno com a árvore binária e a recursão), Pens
            (canetas e marca-texto), Phone, Backpack (a mochila da Clear), SodaCan
  walls   : WallClock (COM ponteiros, 17h48 — o oposto do relógio do pesadelo), BulletinBoard (mural: violão, república,
            show, gato perdido, "The Delivery: 1º pedido grátis"), WallShelf (prateleira com plantas e sacos de café),
            HangingPlant, ACUnit, CoatRack (com guarda-chuva), FloorPlant, DoorMat ("BEM-VINDO")
            (o letreiro no vidro saiu a pedido do usuário, 2026-11-25)

Convenção (igual a build_reception.py): frente para -Y do Blender (= +Z no Unity).
  chão/mesa — origem no centro da BASE; parede — origem no centro da face de TRÁS, embaixo.
Uso: blender --background --python build_cafe.py -- <grupo> <saida.fbx> <pasta_texturas> [<pasta_previas>|-] [preview_only]
"""
import bpy, bmesh, sys, math, os, random
from mathutils import Vector, Matrix
sys.path.append(os.path.dirname(os.path.abspath(__file__)))
import realistic_kit as K

argv = sys.argv[sys.argv.index("--") + 1:]
GROUP, OUT_FBX, TEX_DIR = argv[0], argv[1], argv[2]
PREV_DIR = argv[3] if len(argv) > 3 and argv[3] != "-" else None
PREVIEW_ONLY = len(argv) > 4 and argv[4] == "preview_only"
random.seed(19)
K.reset_scene()

def lin(c): return tuple(x ** 2.2 for x in c)
def box(x0, x1, y0, y1, z0, z1, mat, bevel=0.0, seg=2):
    return K.box(min(x0, x1), max(x0, x1), min(y0, y1), max(y0, y1), min(z0, z1), max(z0, z1), mat, bevel=bevel, segments=seg)
def cyl(x, y, z, r, d, mat, axis='Z', verts=24, bevel=0.0): return K.cyl(x, y, z, r, d, mat, axis=axis, verts=verts, bevel=bevel)
FRONT = (math.pi / 2, 0, 0)
FLAT = (0, 0, 0)
def text(s, x, y, z, size, mat, align='CENTER', rot=FRONT, mirror=False, font=None):
    """Texto CHATO e de baixa resolução (sem relevo, curva com 3 segmentos)."""
    bpy.ops.object.text_add(location=(x, y, z), rotation=rot)
    ob = bpy.context.active_object
    ob.data.body = s; ob.data.size = size; ob.data.extrude = 0.0; ob.data.resolution_u = 3
    ob.data.align_x = align; ob.data.align_y = 'CENTER'
    if mirror: ob.scale.x = -1
    bpy.ops.object.convert(target='MESH')
    return K._finish(bpy.context.active_object, mat)
def rename(o, n): o.name = n; o.data.name = n; return o
def finish_group(d):
    out = []
    for k, v in d.items():
        o = K.join(v, k) if len(v) > 1 else rename(v[0], k)
        bpy.ops.object.select_all(action='DESELECT'); o.select_set(True); bpy.context.view_layer.objects.active = o
        bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
        out.append(o)
    return out
def rot_parts(parts, angle, axis='X', pivot=(0, 0, 0)):
    M = Matrix.Translation(Vector(pivot)) @ Matrix.Rotation(angle, 4, axis) @ Matrix.Translation(-Vector(pivot))
    for o in parts: o.matrix_world = M @ o.matrix_world
def strip(points, width, y, mat, depth=0.0015, taper=True, plane='XZ'):
    """Faixa fina seguindo uma polilinha: no plano XZ (em y, de pé) ou XY (em z=y, deitada)."""
    bm = bmesh.new(); n = len(points); left, right = [], []
    for i, (px, pz) in enumerate(points):
        a = points[max(0, i - 1)]; b = points[min(n - 1, i + 1)]
        tx, tz = b[0] - a[0], b[1] - a[1]; L = math.hypot(tx, tz) or 1.0
        nx, nz = -tz / L, tx / L
        w = width * (math.sin(math.pi * i / (n - 1)) * 0.85 + 0.15 if taper else 1.0) / 2
        def V(u, v): return bm.verts.new((u, y, v) if plane == 'XZ' else (u, v, y))
        left.append(V(px + nx * w, pz + nz * w)); right.append(V(px - nx * w, pz - nz * w))
    for i in range(n - 1): bm.faces.new((left[i], left[i + 1], right[i + 1], right[i]))
    me = bpy.data.meshes.new("strip"); bm.to_mesh(me); bm.free()
    o = bpy.data.objects.new("strip", me); bpy.context.collection.objects.link(o); o.data.materials.append(mat)
    if depth:
        md = o.modifiers.new("s", 'SOLIDIFY'); md.thickness = depth; md.offset = 1
        bpy.context.view_layer.objects.active = o; bpy.ops.object.modifier_apply(modifier="s")
    K.PARTS.append(o); return o
def ring(x, y, z, r, thick, mat, axis='Y', verts=32):
    """Anel (circunferência) fino no plano perpendicular ao eixo."""
    bpy.ops.mesh.primitive_torus_add(major_radius=r, minor_radius=thick, major_segments=verts, minor_segments=6, location=(x, y, z),
                                     rotation=(math.pi / 2, 0, 0) if axis == 'Y' else (0, 0, 0))
    return K._finish(bpy.context.active_object, mat)
def leaf(cx, cy, cz, length, width, mat, yaw=0.0, pitch=0.0):
    """Folha: losango alongado curvado, deitada no plano XY e girada (yaw em Z, pitch em X)."""
    bm = bmesh.new(); n = 7; pts = []
    for i in range(n):
        t = i / (n - 1); w = math.sin(math.pi * t) * width / 2
        pts.append(((t - 0.5) * length, w, 0.02 * math.sin(math.pi * t) * length));
    vs = [bm.verts.new(p) for p in pts] + [bm.verts.new((p[0], -p[1], p[2])) for p in pts[1:-1][::-1]]
    bm.faces.new(vs)
    me = bpy.data.meshes.new("leaf"); bm.to_mesh(me); bm.free()
    o = bpy.data.objects.new("leaf", me); bpy.context.collection.objects.link(o); o.data.materials.append(mat)
    o.rotation_euler = (pitch, 0, yaw); o.location = (cx, cy, cz)
    md = o.modifiers.new("s", 'SOLIDIFY'); md.thickness = 0.002
    bpy.context.view_layer.objects.active = o; bpy.ops.object.modifier_apply(modifier="s")
    K.PARTS.append(o); return o

def mats():
    M = {}
    M['steel'] = K.material("BrushedSteel", lin((0.74, 0.74, 0.75)), rough=0.28, metal=1.0, dirt=0.15, color_var=0.03, bump=0.04, bump_scale=600.0)
    M['chrome'] = K.material("Chrome", lin((0.85, 0.85, 0.86)), rough=0.12, metal=1.0, dirt=0.08)
    M['black'] = K.material("BlackPlastic", lin((0.04, 0.04, 0.045)), rough=0.45, dirt=0.1)
    M['white'] = K.material("WhitePlastic", lin((0.90, 0.90, 0.88)), rough=0.4, dirt=0.15, color_var=0.03)
    M['ceramic'] = K.material("Ceramic", lin((0.93, 0.92, 0.89)), rough=0.2, color_var=0.02)
    M['coffee'] = K.material("CoffeeLiquid", lin((0.20, 0.11, 0.05)), rough=0.1)
    M['paper'] = K.material("Paper", lin((0.93, 0.92, 0.88)), rough=0.85, color_var=0.03)
    M['newsprint'] = K.material("Newsprint", lin((0.82, 0.80, 0.74)), rough=0.9, color_var=0.05)
    M['ink'] = K.material("Ink", lin((0.05, 0.05, 0.06)), rough=0.6)
    M['blue_ink'] = K.material("BlueInk", lin((0.08, 0.14, 0.42)), rough=0.6)
    M['red'] = K.material("RedPaint", lin((0.72, 0.08, 0.06)), rough=0.4)
    M['wood'] = K.material("Wood", lin((0.42, 0.27, 0.15)), rough=0.5, wood=True, dirt=0.12, color_var=0.08)
    M['darkwood'] = K.material("DarkWood", lin((0.22, 0.13, 0.08)), rough=0.45, wood=True, dirt=0.15, color_var=0.08)
    M['chalk'] = K.material("Chalkboard", lin((0.10, 0.12, 0.11)), rough=0.9, color_var=0.06, dirt=0.3, dirt_color=lin((0.5, 0.5, 0.5)))
    M['chalkw'] = K.material("ChalkWhite", lin((0.88, 0.88, 0.85)), rough=0.95)
    M['chalky'] = K.material("ChalkYellow", lin((0.95, 0.82, 0.35)), rough=0.95)
    M['chalkp'] = K.material("ChalkPink", lin((0.95, 0.55, 0.62)), rough=0.95)
    M['cork'] = K.material("Cork", lin((0.62, 0.45, 0.28)), rough=0.9, color_var=0.15, bump=0.15, bump_scale=300.0)
    M['leaf'] = K.material("LeafGreen", lin((0.16, 0.38, 0.14)), rough=0.5, color_var=0.15)
    M['leaf2'] = K.material("LeafDark", lin((0.08, 0.25, 0.10)), rough=0.45, color_var=0.12)
    M['terracotta'] = K.material("Terracotta", lin((0.62, 0.32, 0.20)), rough=0.85, color_var=0.08, dirt=0.25)
    M['soil'] = K.material("Soil", lin((0.15, 0.10, 0.07)), rough=0.95, color_var=0.2)
    M['beans'] = K.material("CoffeeBeans", lin((0.25, 0.13, 0.06)), rough=0.4, color_var=0.25, bump=0.4, bump_scale=900.0)
    M['kraft'] = K.material("Kraft", lin((0.62, 0.47, 0.30)), rough=0.85, color_var=0.06)
    M['fabric'] = K.material("Fabric", lin((0.20, 0.21, 0.24)), rough=0.9, color_var=0.06, bump=0.1, bump_scale=800.0)
    M['brass'] = K.material("Brass", lin((0.78, 0.62, 0.30)), rough=0.3, metal=1.0, dirt=0.2)
    return M

# ====================================================================== COUNTER
def build_counter():
    M = mats(); P = {}
    gauge_face = K.material("GaugeFace", lin((0.94, 0.93, 0.88)), rough=0.3)
    # --- máquina de expresso (0,70 x 0,50 x 0,46), 2 grupos
    s = len(K.PARTS)
    box(-0.35, 0.35, -0.25, 0.25, 0.06, 0.46, M['steel'], 0.02)
    box(-0.36, 0.36, -0.26, 0.26, 0.0, 0.06, M['black'], 0.01)                        # base
    box(-0.33, 0.33, -0.30, -0.05, 0.06, 0.075, M['chrome'], 0.004)                   # bandeja de pingo
    for i in range(12): box(-0.30 + i * 0.054, -0.29 + i * 0.054, -0.30, -0.05, 0.075, 0.08, M['black'])
    for k in (-1, 1):
        cyl(k * 0.16, -0.27, 0.30, 0.045, 0.06, M['chrome'], 'Y', 24, 0.004)           # grupo
        cyl(k * 0.16, -0.27, 0.255, 0.04, 0.03, M['chrome'], 'Z', 24, 0.003)            # porta-filtro
        h = box(k * 0.16 - 0.012, k * 0.16 + 0.012, -0.42, -0.27, 0.245, 0.265, M['black'], 0.006)
        cyl(k * 0.16, -0.265, 0.39, 0.032, 0.012, gauge_face, 'Y', 32)                   # manômetro
        ring(k * 0.16, -0.272, 0.39, 0.032, 0.004, M['chrome'])
        box(k * 0.16 - 0.0015, k * 0.16 + 0.0015, -0.274, -0.273, 0.39, 0.415, M['red'])
    for r in (-0.05, 0.05): box(r - 0.008, r + 0.008, -0.27, -0.25, 0.34, 0.36, M['black'], 0.003)   # botões
    w = cyl(0.31, -0.24, 0.30, 0.006, 0.22, M['chrome'], 'Z', 10)                          # vaporizador
    rot_parts([w], math.radians(-20), 'X', (0.31, -0.24, 0.40))
    for x in (-0.3, 0.3): box(x - 0.006, x + 0.006, -0.22, 0.22, 0.46, 0.50, M['chrome'], 0.003)   # trilho de xícaras
    for i in range(4): cyl(-0.2 + i * 0.13, 0.0, 0.50, 0.03, 0.05, M['ceramic'], 'Z', 20, 0.003)
    text("GRÃO", 0.0, -0.2505, 0.15, 0.04, M['black'])
    P["EspressoMachine"] = K.PARTS[s:]

    # --- moedor (base + funil com grãos)
    s = len(K.PARTS)
    box(-0.09, 0.09, -0.12, 0.10, 0.0, 0.32, M['black'], 0.02)
    cyl(0, 0.0, 0.40, 0.085, 0.16, K.material("SmokedLid", lin((0.30, 0.20, 0.12)), rough=0.1), 'Z', 32, 0.004)
    cyl(0, 0.0, 0.37, 0.08, 0.09, M['beans'], 'Z', 32)
    cyl(0, -0.125, 0.24, 0.022, 0.01, M['chrome'], 'Y', 20)
    box(-0.05, 0.05, -0.16, -0.12, 0.08, 0.10, M['chrome'], 0.003)
    P["Grinder"] = K.PARTS[s:]

    # --- caixa: tablet no suporte + maquininha + bobina
    s = len(K.PARTS)
    screen = K.material("PosScreen", lin((0.15, 0.32, 0.45)), rough=0.2, emission=(0.35, 0.6, 0.8), emission_strength=1.2)
    cyl(0, 0.02, 0.01, 0.06, 0.02, M['white'], 'Z', 24, 0.003)
    p = box(-0.012, 0.012, 0.01, 0.04, 0.01, 0.20, M['white'], 0.004)
    t = [box(-0.13, 0.13, -0.01, 0.0, 0.14, 0.32, M['black'], 0.006), box(-0.12, 0.12, -0.0115, -0.01, 0.15, 0.31, screen)]
    rot_parts(t, math.radians(-15), 'X', (0, 0, 0.2))
    box(0.18, 0.26, -0.06, 0.08, 0.0, 0.035, M['black'], 0.008)                        # maquininha
    box(0.19, 0.25, -0.04, 0.02, 0.035, 0.037, K.material("PosKeys", lin((0.5, 0.5, 0.52)), rough=0.4))
    P["PosTerminal"] = K.PARTS[s:]

    # --- caixinha (lata com etiqueta)
    s = len(K.PARTS)
    cyl(0, 0, 0.07, 0.045, 0.14, M['steel'], 'Z', 28, 0.002)
    cyl(0, 0, 0.07, 0.0455, 0.06, M['paper'], 'Z', 28)
    text("CAIXINHA", 0, -0.0462, 0.075, 0.012, M['ink'])
    text(":)", 0, -0.0462, 0.058, 0.012, M['red'])
    P["TipCan"] = K.PARTS[s:]

    # --- bolo de cenoura com cobertura de chocolate, numa boleira; uma fatia tirada
    s = len(K.PARTS)
    cake = K.material("CarrotCake", lin((0.85, 0.52, 0.18)), rough=0.8, color_var=0.08)
    choco = K.material("ChocoTopping", lin((0.17, 0.08, 0.04)), rough=0.25)
    cyl(0, 0, 0.01, 0.15, 0.02, M['ceramic'], 'Z', 40, 0.004)
    cyl(0, 0, 0.06, 0.03, 0.08, M['ceramic'], 'Z', 20)
    cyl(0, 0, 0.105, 0.16, 0.012, M['ceramic'], 'Z', 40, 0.003)
    c = cyl(0, 0, 0.155, 0.12, 0.085, cake, 'Z', 40)
    cyl(0, 0, 0.205, 0.123, 0.016, choco, 'Z', 40, 0.004)
    # fatia tirada: cunha escura recortada (um bloco de "vão" por cima — lê como o recorte)
    for k in range(3):
        box(0.0, 0.125, -0.01 - k * 0.012, 0.0 - k * 0.012 + 0.012, 0.112, 0.214, M['ceramic'])
    P["CarrotCake"] = K.PARTS[s:]

    # --- cesta de pão de queijo
    s = len(K.PARTS)
    basket = K.material("Wicker", lin((0.55, 0.38, 0.20)), rough=0.8, color_var=0.15, bump=0.3, bump_scale=200.0)
    cyl(0, 0, 0.035, 0.13, 0.07, basket, 'Z', 32, 0.006)
    pq = K.material("PaoDeQueijo", lin((0.92, 0.75, 0.42)), rough=0.7, color_var=0.1)
    for i in range(9):
        a = i * 2.4; r = 0.06 if i < 7 else 0.0
        bpy.ops.mesh.primitive_uv_sphere_add(segments=12, ring_count=8, radius=0.032, location=(math.cos(a) * r, math.sin(a) * r, 0.085 + (0.02 if i >= 7 else 0)))
        o = bpy.context.active_object; o.scale.z = 0.8; K._finish(o, pq)
    P["PaoDeQueijo"] = K.PARTS[s:]

    # --- pilha de xícaras e pires
    s = len(K.PARTS)
    for j in range(2):
        for i in range(3):
            x = -0.08 + j * 0.16
            cyl(x, 0, 0.006 + i * 0.012, 0.065, 0.01, M['ceramic'], 'Z', 28, 0.003)
        for i in range(3):
            cyl(x, 0, 0.07 + i * 0.05, 0.04, 0.055, M['ceramic'], 'Z', 24, 0.004)
    P["CupStack"] = K.PARTS[s:]

    # --- leiteira e porta-guardanapo
    s = len(K.PARTS)
    cyl(0, 0, 0.06, 0.04, 0.12, M['chrome'], 'Z', 24, 0.004)
    cyl(0.03, -0.03, 0.115, 0.012, 0.03, M['chrome'], 'Z', 10)
    P["MilkPitcher"] = K.PARTS[s:]
    s = len(K.PARTS)
    box(-0.08, 0.08, -0.05, 0.05, 0.0, 0.1, M['chrome'], 0.006)
    box(-0.07, 0.07, -0.04, 0.04, 0.1, 0.12, M['paper'])
    P["NapkinBox"] = K.PARTS[s:]

    # --- cardápio de giz (1,10 x 0,85, moldura de madeira) — parede
    s = len(K.PARTS)
    W, H = 1.10, 0.85
    box(-W / 2, W / 2, -0.012, 0, 0, H, M['chalk'])
    for (x0, x1, z0, z1) in ((-W / 2, W / 2, 0, 0.04), (-W / 2, W / 2, H - 0.04, H), (-W / 2, -W / 2 + 0.04, 0.04, H - 0.04), (W / 2 - 0.04, W / 2, 0.04, H - 0.04)):
        box(x0, x1, -0.025, 0, z0, z1, M['darkwood'], 0.004)
    y = -0.0125
    text("CARDÁPIO", 0, y, H - 0.10, 0.065, M['chalkw'])
    strip([(-0.25, H - 0.145), (0.25, H - 0.142)], 0.004, y - 0.0002, M['chalky'], depth=0.0003, taper=False)
    items = [("Expresso", "6"), ("Cappuccino", "9"), ("Café com leite", "7"), ("Pão de queijo", "5"), ("Misto quente", "12"),
             ("Bolo do dia", "8"), ("Suco natural", "10")]
    for i, (n, pr) in enumerate(items):
        zz = H - 0.20 - i * 0.075
        text(n, -W / 2 + 0.08, y, zz, 0.038, M['chalkw'], align='LEFT')
        text("R$ " + pr, W / 2 - 0.25, y, zz, 0.038, M['chalky'], align='LEFT')
    text("* bolo de cenoura hoje!", 0, y, 0.08, 0.03, M['chalkp'])
    # xicrinha desenhada a giz
    ring(W / 2 - 0.13, y - 0.0002, H - 0.10, 0.03, 0.0025, M['chalkw'])
    for k in range(3): strip([(W / 2 - 0.14 + k * 0.01, H - 0.06 + j * 0.006) for j in range(6)], 0.003, y - 0.0002, M['chalkw'], 0.0003, False)
    P["MenuBoard"] = K.PARTS[s:]

    # --- letreiro de madeira "GRÃO DO PORTO" (1,6 x 0,32) — parede
    s = len(K.PARTS)
    box(-0.8, 0.8, -0.03, 0, 0, 0.32, M['darkwood'], 0.008)
    cream = K.material("SignCream", lin((0.93, 0.86, 0.70)), rough=0.5)
    text("GRÃO DO PORTO", 0, -0.0305, 0.19, 0.11, cream)
    text("café  ·  desde 1998", 0, -0.0305, 0.07, 0.045, M['brass'])
    P["CafeSign"] = K.PARTS[s:]

    # --- "café" em neon (tubo emissivo quente sobre placa escura)
    s = len(K.PARTS)
    neon = K.material("NeonWarm", lin((1.0, 0.55, 0.45)), rough=0.3, emission=(1.0, 0.45, 0.35), emission_strength=8.0)
    box(-0.32, 0.32, -0.01, 0, 0, 0.26, M['black'], 0.006)
    text("café", 0, -0.012, 0.12, 0.16, neon)
    ring(0.24, -0.012, 0.20, 0.025, 0.004, neon)
    P["NeonCafe"] = K.PARTS[s:]
    return finish_group(P)

# ====================================================================== TABLES
def build_tables():
    M = mats(); P = {}
    # --- centro de mesa: porta-guardanapo, açucareiro, vasinho com flor, plaquinha do Wi-Fi
    s = len(K.PARTS)
    box(-0.13, -0.05, -0.03, 0.03, 0.0, 0.08, M['chrome'], 0.004)
    box(-0.125, -0.055, -0.025, 0.025, 0.08, 0.10, M['paper'])
    cyl(0.0, 0.0, 0.05, 0.03, 0.10, K.material("SugarGlass", lin((0.92, 0.92, 0.9)), rough=0.15), 'Z', 20, 0.003)
    cyl(0.0, 0.0, 0.11, 0.022, 0.025, M['chrome'], 'Z', 20, 0.003)
    cyl(0.08, 0.0, 0.04, 0.022, 0.08, K.material("VaseBlue", lin((0.20, 0.35, 0.55)), rough=0.2), 'Z', 18, 0.003)
    st = cyl(0.08, 0.0, 0.13, 0.002, 0.12, M['leaf2'], 'Z', 6)
    petal = K.material("Petal", lin((0.95, 0.70, 0.20)), rough=0.5)
    for i in range(6):
        leaf(0.08 + math.cos(i * 1.05) * 0.012, math.sin(i * 1.05) * 0.012, 0.19, 0.03, 0.014, petal, yaw=i * 1.05, pitch=0.3)
    leaf(0.08, 0.01, 0.11, 0.05, 0.02, M['leaf'], yaw=0.6, pitch=-0.5)
    box(0.13, 0.20, -0.01, 0.01, 0.0, 0.01, M['black'])
    t = [box(0.135, 0.195, -0.002, 0.002, 0.01, 0.09, K.material("Acrylic", lin((0.85, 0.85, 0.85)), rough=0.1))]
    t.append(text("Wi-Fi", 0.165, -0.0025, 0.07, 0.012, M['ink']))
    t.append(text("cafezinho", 0.165, -0.0025, 0.045, 0.008, M['ink']))
    P["Centerpiece"] = K.PARTS[s:]

    # --- xícara usada no pires + prato com farelo e garfo
    s = len(K.PARTS)
    cyl(0, 0, 0.005, 0.07, 0.01, M['ceramic'], 'Z', 28, 0.003)
    cyl(0, 0, 0.045, 0.042, 0.07, M['ceramic'], 'Z', 24, 0.004)
    cyl(0, 0, 0.072, 0.036, 0.004, M['coffee'], 'Z', 24)
    ring(0.05, 0, 0.05, 0.016, 0.005, M['ceramic'], 'Y', 16)
    cyl(0.25, 0.02, 0.006, 0.10, 0.012, M['ceramic'], 'Z', 32, 0.003)
    crumb = K.material("Crumb", lin((0.75, 0.48, 0.20)), rough=0.8)
    for i in range(14): cyl(0.25 + random.uniform(-0.06, 0.06), 0.02 + random.uniform(-0.06, 0.06), 0.013, random.uniform(0.003, 0.008), 0.004, crumb, 'Z', 6)
    f = box(0.20, 0.33, 0.06, 0.07, 0.013, 0.016, M['steel'], 0.002)
    P["UsedCups"] = K.PARTS[s:]

    # --- jornal dobrado: Diário do Porto
    s = len(K.PARTS)
    box(-0.15, 0.15, -0.21, 0.21, 0.0, 0.006, M['newsprint'])
    y = 0.0062
    text("DIÁRIO DO PORTO", 0, 0.17, y, 0.034, M['ink'], rot=FLAT)
    strip([(-0.14, 0.15), (0.14, 0.15)], 0.003, y, M['ink'], 0.0002, False, plane='XY')
    text("Moradora do Porto Sereno", 0, 0.115, y, 0.020, M['ink'], rot=FLAT)
    text("segue desaparecida", 0, 0.088, y, 0.020, M['ink'], rot=FLAT)
    box(-0.13, -0.02, -0.03, 0.065, y, y + 0.0002, K.material("NewsPhoto", lin((0.35, 0.35, 0.36)), rough=0.8))
    for i in range(14):
        yy = 0.055 - i * 0.012
        strip([(0.0, yy), (0.13, yy)], 0.004, y, K.material("NewsGrey", lin((0.45, 0.45, 0.45)), rough=0.9), 0.0001, False, plane='XY')
    for i in range(8):
        yy = -0.05 - i * 0.012
        strip([(-0.13, yy), (0.13, yy)], 0.004, y, K.material("NewsGrey", lin((0.45, 0.45, 0.45)), rough=0.9), 0.0001, False, plane='XY')
    text("Polícia pede informações; família faz apelo", 0, -0.165, y, 0.011, M['ink'], rot=FLAT)
    P["Newspaper"] = K.PARTS[s:]

    # --- livro "Algoritmos e Estruturas de Dados" (fechado, grosso)
    s = len(K.PARTS)
    cover = K.material("BookCover", lin((0.10, 0.30, 0.42)), rough=0.45)
    box(-0.12, 0.12, -0.17, 0.17, 0.0, 0.045, cover, 0.003)
    box(-0.115, 0.12, -0.165, 0.165, 0.004, 0.041, M['paper'])
    text("ALGORITMOS", 0, 0.06, 0.0452, 0.026, M['white'], rot=FLAT)
    text("e Estruturas de Dados", 0, 0.03, 0.0452, 0.015, M['white'], rot=FLAT)
    for (cx, cy) in ((0, -0.04), (-0.04, -0.08), (0.04, -0.08)): ring(cx, cy, 0.0455, 0.012, 0.002, K.material("CoverYellow", lin((0.95, 0.8, 0.2)), rough=0.4), 'Z')
    text("4ª edição", 0, -0.14, 0.0452, 0.012, M['white'], rot=FLAT)
    P["Textbook"] = K.PARTS[s:]

    # --- caderno aberto: a árvore binária e a recursão; 2 canetas e marca-texto
    s = len(K.PARTS)
    cov = K.material("NotebookCover", lin((0.55, 0.12, 0.15)), rough=0.55)
    box(-0.21, 0.21, -0.14, 0.14, 0, 0.004, cov, 0.002)
    for k in (-1, 1):
        box(-0.2 if k < 0 else 0.004, -0.004 if k < 0 else 0.2, -0.135, 0.135, 0.004, 0.010, M['paper'])
    yv = 0.0102
    rule = K.material("RuleBlue", lin((0.6, 0.74, 0.9)), rough=0.8)
    for i in range(16):
        yy = -0.115 + i * 0.015
        strip([(-0.19, yy), (-0.012, yy)], 0.0012, yv, rule, 0.0001, False, plane='XY')
        strip([(0.012, yy), (0.19, yy)], 0.0012, yv, rule, 0.0001, False, plane='XY')
    # página esquerda: a árvore binária (nós = anéis, arestas = traços)
    nodes = {1: (-0.1, 0.09), 2: (-0.15, 0.04), 3: (-0.05, 0.04), 4: (-0.175, -0.01), 5: (-0.125, -0.01), 6: (-0.075, -0.01), 7: (-0.025, -0.01)}
    for a, b in ((1, 2), (1, 3), (2, 4), (2, 5), (3, 6), (3, 7)):
        (x0, y0), (x1, y1) = nodes[a], nodes[b]
        dx, dy = x1 - x0, y1 - y0; L = math.hypot(dx, dy); ux, uy = dx / L, dy / L
        strip([(x0 + ux * 0.011, y0 + uy * 0.011), (x1 - ux * 0.011, y1 - uy * 0.011)], 0.0016, yv + 0.0001, M['blue_ink'], 0.0001, False, plane='XY')
    for i, (x, yy) in nodes.items():
        ring(x, yy, yv + 0.0002, 0.010, 0.0012, M['blue_ink'], 'Z', 20)
        text(str(i * 3 % 10 + 1), x, yy, yv + 0.0002, 0.009, M['blue_ink'], rot=FLAT)
    text("árvore binária", -0.1, 0.12, yv + 0.0002, 0.012, M['blue_ink'], rot=FLAT)
    for li, ln in enumerate(["recursão:", "fat(n) = n * fat(n-1)", "caso base: fat(0) = 1", "", "PROVA SEXTA!!", "revisar c/ Marina sáb."]):
        if ln: text(ln, 0.025, 0.11 - li * 0.022, yv + 0.0002, 0.0105, M['red'] if "PROVA" in ln else M['blue_ink'], rot=FLAT, align='LEFT')
    for i in range(12): cyl(0, -0.12 + i * 0.022, 0.012, 0.005, 0.012, M['steel'], 'X', 8)
    P["StudyNotes"] = K.PARTS[s:]

    # --- canetas e marca-texto (peça separada: o caderno vai em cima do livro, e as canetas ficam na mesa)
    s = len(K.PARTS)
    pen = [cyl(-0.012, 0.0, 0.005, 0.005, 0.14, M['blue_ink'], 'Y', 10), cyl(0.012, 0.01, 0.005, 0.005, 0.14, M['black'], 'Y', 10)]
    rot_parts(pen, math.radians(12), 'Z', (0, 0, 0))
    hl = [cyl(0.045, -0.01, 0.009, 0.009, 0.11, K.material("Highlighter", lin((0.95, 0.95, 0.2)), rough=0.4), 'Y', 6)]
    rot_parts(hl, math.radians(-25), 'Z', (0.045, -0.01, 0))
    P["Pens"] = K.PARTS[s:]

    # --- celular deitado, tela escura
    s = len(K.PARTS)
    box(-0.036, 0.036, -0.075, 0.075, 0, 0.008, M['black'], 0.006)
    box(-0.033, 0.033, -0.071, 0.071, 0.008, 0.0083, K.material("PhoneGlass", lin((0.02, 0.02, 0.03)), rough=0.05))
    P["Phone"] = K.PARTS[s:]

    # --- mochila da Clear (de pé no chão, encostada): corpo arredondado, bolso frontal, alças, zíper
    s = len(K.PARTS)
    bag = K.material("BackpackFabric", lin((0.14, 0.15, 0.17)), rough=0.85, color_var=0.05, bump=0.1, bump_scale=900.0, dirt=0.2)
    def soft(x0, x1, y0, y1, z0, z1, m, lvl=2):
        o = box(x0, x1, y0, y1, z0, z1, m, 0.0)
        md = o.modifiers.new("ss", 'SUBSURF'); md.levels = lvl; md.render_levels = lvl
        bpy.context.view_layer.objects.active = o; bpy.ops.object.modifier_apply(modifier="ss"); return o
    strapm = K.material("Strap", lin((0.08, 0.08, 0.09)), rough=0.7)
    soft(-0.16, 0.16, -0.10, 0.10, 0.0, 0.44, bag)                                     # corpo (vira "almofada")
    soft(-0.12, 0.12, -0.15, -0.06, 0.04, 0.24, bag)                                   # bolso da frente
    for k in (-1, 1):                                                                  # alças (curvas, atrás)
        strip([(k * 0.09, 0.40), (k * 0.11, 0.30), (k * 0.11, 0.16), (k * 0.09, 0.06)], 0.045, 0.105, strapm, depth=0.012, taper=False)
    box(-0.12, 0.12, -0.152, -0.148, 0.225, 0.232, M['steel'])                            # zíper do bolso
    strip([(-0.14, 0.36), (0.0, 0.43), (0.14, 0.36)], 0.006, -0.101, M['steel'], depth=0.002, taper=False)   # zíper de cima
    cyl(0, 0.0, 0.455, 0.035, 0.025, strapm, 'Y', 12)                                   # alça de mão
    box(0.05, 0.085, -0.157, -0.152, 0.10, 0.155, K.material("Keychain", lin((0.9, 0.4, 0.6)), rough=0.4), 0.006)   # chaveiro
    P["Backpack"] = K.PARTS[s:]

    # --- lata de refrigerante (guaraná genérico)
    s = len(K.PARTS)
    green = K.material("CanGreen", lin((0.10, 0.45, 0.18)), rough=0.3, metal=0.6)
    cyl(0, 0, 0.06, 0.033, 0.12, green, 'Z', 28, 0.004)
    cyl(0, 0, 0.122, 0.028, 0.004, M['steel'], 'Z', 28)
    text("GUARANÁ", 0, -0.0335, 0.065, 0.011, M['white'])
    P["SodaCan"] = K.PARTS[s:]
    return finish_group(P)

# ====================================================================== WALLS
def build_walls():
    M = mats(); P = {}
    # --- relógio de parede COM ponteiros (17h48: fim de tarde)
    s = len(K.PARTS)
    R = 0.17
    cyl(0, -0.025, R, R, 0.05, M['darkwood'], 'Y', 64, 0.006)
    cyl(0, -0.051, R, R - 0.014, 0.002, M['paper'], 'Y', 64)
    for h in range(12):
        a = math.radians(h * 30); big = h % 3 == 0
        l, w = (0.024, 0.006) if big else (0.012, 0.003)
        t = box(-w / 2, w / 2, -0.0525, -0.052, R + (R - 0.024) - l, R + (R - 0.024), M['ink'])
        rot_parts([t], a, 'Y', (0, -0.052, R))
    hh = box(-0.004, 0.004, -0.056, -0.054, R, R + 0.075, M['ink'])          # horas: 17h48 -> ~174°
    rot_parts([hh], math.radians(174), 'Y', (0, -0.055, R))
    mm = box(-0.003, 0.003, -0.058, -0.056, R, R + 0.12, M['ink'])           # minutos: 48 -> 288°
    rot_parts([mm], math.radians(288), 'Y', (0, -0.057, R))
    ss = box(-0.001, 0.001, -0.060, -0.058, R - 0.02, R + 0.13, M['red'])    # segundos
    rot_parts([ss], math.radians(60), 'Y', (0, -0.059, R))
    cyl(0, -0.061, R, 0.007, 0.006, M['brass'], 'Y', 16)
    P["WallClock"] = K.PARTS[s:]

    # --- mural de recados (1,0 x 0,7): violão, república, show, gato perdido, The Delivery
    s = len(K.PARTS)
    W, H = 1.0, 0.7
    box(-W / 2, W / 2, -0.012, 0, 0, H, M['cork'])
    for (x0, x1, z0, z1) in ((-W / 2, W / 2, H - 0.025, H), (-W / 2, W / 2, 0, 0.025), (-W / 2, -W / 2 + 0.025, 0, H), (W / 2 - 0.025, W / 2, 0, H)):
        box(x0, x1, -0.022, 0, z0, z1, M['wood'], 0.003)
    flyers = [
        (-0.33, 0.48, 0.20, 0.24, -3, (0.98, 0.95, 0.80), ["AULAS DE", "VIOLÃO", "", "iniciantes", "tel. 9 8812-4410"]),
        (-0.08, 0.50, 0.19, 0.26, 2, (0.85, 0.92, 0.98), ["VAGA EM", "REPÚBLICA", "feminina", "perto da facul", "R$ 650"]),
        (0.18, 0.47, 0.20, 0.28, -2, (0.20, 0.20, 0.22), ["OS MARÉS", "ao vivo", "SEXTA 21h", "bar do Zé"]),
        (0.38, 0.22, 0.17, 0.22, 4, (0.98, 0.98, 0.98), ["PERDEU", "UM GATO?", "laranja,", "coleira azul"]),
        (-0.30, 0.17, 0.22, 0.20, 2, (0.95, 0.30, 0.25), ["The Delivery", "1º PEDIDO", "GRÁTIS", "baixe o app"]),
        (0.02, 0.18, 0.18, 0.17, -4, (0.99, 0.92, 0.45), ["sorteio de", "um bolo!", "deixe seu nome", "no caixa"]),
    ]
    pins = [M['red'], K.material("PinBlue", lin((0.1, 0.25, 0.7)), rough=0.4), K.material("PinYellow", lin((0.95, 0.8, 0.1)), rough=0.4)]
    for i, (cx, cz, w, h, ang, col, lines) in enumerate(flyers):
        ps = len(K.PARTS)
        dark = sum(col) < 1.0
        box(cx - w / 2, cx + w / 2, -0.0135, -0.012, cz - h / 2, cz + h / 2, K.material(f"Flyer{i}", lin(col), rough=0.8))
        ink = M['white'] if dark or i == 4 else M['ink']
        for li, ln in enumerate(lines):
            if ln: text(ln, cx, -0.0140, cz + h / 2 - 0.035 - li * 0.038, 0.026 if li < 2 else 0.017, ink)
        if i == 4:
            box(cx - 0.03, cx + 0.03, -0.0141, -0.014, cz - h / 2 + 0.012, cz - h / 2 + 0.06, M['white'])
        rot_parts(K.PARTS[ps:], math.radians(ang), 'Y', (cx, -0.013, cz))
        cyl(cx, -0.018, cz + h / 2 - 0.012, 0.006, 0.008, pins[i % 3], 'Y', 12)
    P["BulletinBoard"] = K.PARTS[s:]

    # --- prateleira de parede (1,2 m): 3 vasinhos, 2 sacos de café, livros
    s = len(K.PARTS)
    box(-0.6, 0.6, -0.22, 0, 0.0, 0.03, M['wood'], 0.004)
    for x in (-0.45, 0.45):
        box(x - 0.015, x + 0.015, -0.18, 0, -0.12, 0.0, M['black'], 0.003)
    def pot(x, h, r):
        cyl(x, -0.11, 0.03 + h / 2, r, h, M['terracotta'], 'Z', 20, 0.003)
        cyl(x, -0.11, 0.03 + h - 0.005, r - 0.006, 0.01, M['soil'], 'Z', 20)
    pot(-0.48, 0.09, 0.05)
    for i in range(7): leaf(-0.48 + math.cos(i) * 0.03, -0.11 + math.sin(i) * 0.03, 0.15, 0.08, 0.035, M['leaf'], yaw=i * 0.9, pitch=-0.6)
    pot(0.05, 0.08, 0.045)
    for i in range(9):   # suculenta: roseta
        a = i * 0.7; leaf(0.05 + math.cos(a) * 0.015, -0.11 + math.sin(a) * 0.015, 0.125, 0.04, 0.02, M['leaf2'], yaw=a, pitch=-0.9)
    pot(0.45, 0.09, 0.05)
    for i in range(6):   # jiboia caindo pela borda
        pts = [(0.45 + (i - 2.5) * 0.02, 0.13 - j * 0.04) for j in range(8)]
        for j, (px, pz) in enumerate(pts[1:]):
            leaf(px, -0.2, pz, 0.035, 0.025, M['leaf'], yaw=0.3 * j, pitch=1.2)
    for k, x in enumerate((-0.25, -0.12)):
        box(x - 0.055, x + 0.055, -0.16, -0.06, 0.03, 0.24, M['kraft'], 0.01)
        text("GRÃO", x, -0.1605, 0.17, 0.022, M['darkwood'])
        text("torra média" if k == 0 else "torra escura", x, -0.1605, 0.13, 0.011, M['darkwood'])
    for i, c in enumerate(((0.6, 0.2, 0.2), (0.2, 0.3, 0.5), (0.85, 0.75, 0.5), (0.3, 0.45, 0.3))):
        box(0.18 + i * 0.035, 0.21 + i * 0.035, -0.19, -0.03, 0.03, 0.22 - i * 0.012, K.material(f"Spine{i}", lin(c), rough=0.6), 0.003)
    P["WallShelf"] = K.PARTS[s:]

    # --- planta pendurada (macramê) — origem no gancho do teto
    s = len(K.PARTS)
    for a in range(3):
        ang = a * 2.094
        strip([(0, 0), (math.cos(ang) * 0.09, -0.55)], 0.006, math.sin(ang) * 0.09 * 0.5, K.material("Rope", lin((0.85, 0.78, 0.62)), rough=0.9), 0.003, False)
    cyl(0, 0, -0.62, 0.09, 0.14, M['terracotta'], 'Z', 24, 0.004)
    for i in range(10):
        a = i * 0.63
        for j in range(5):
            leaf(math.cos(a) * (0.09 + j * 0.01), math.sin(a) * (0.09 + j * 0.01), -0.58 - j * 0.07, 0.05, 0.03, M['leaf'], yaw=a + j * 0.4, pitch=1.0)
    P["HangingPlant"] = K.PARTS[s:]

    # --- ar-condicionado split
    s = len(K.PARTS)
    box(-0.42, 0.42, -0.22, 0, 0, 0.29, M['white'], 0.03, 3)
    for i in range(6): box(-0.38, 0.38, -0.225, -0.215, 0.03 + i * 0.012, 0.036 + i * 0.012, K.material("Vent", lin((0.75, 0.75, 0.74)), rough=0.5))
    cyl(0.33, -0.222, 0.22, 0.004, 0.002, K.material("LedBlue", lin((0.2, 0.5, 1.0)), rough=0.3, emission=(0.2, 0.5, 1.0), emission_strength=3.0), 'Y', 8)
    P["ACUnit"] = K.PARTS[s:]

    # --- cabideiro com guarda-chuva e um cachecol
    s = len(K.PARTS)
    cyl(0, 0, 0.015, 0.2, 0.03, M['darkwood'], 'Z', 32, 0.006)
    cyl(0, 0, 0.9, 0.018, 1.75, M['darkwood'], 'Z', 12)
    for i in range(4):
        a = i * math.pi / 2
        hk = cyl(math.cos(a) * 0.06, math.sin(a) * 0.06, 1.68, 0.008, 0.14, M['darkwood'], 'Z', 8)
        rot_parts([hk], math.radians(35), 'X' if i % 2 == 0 else 'Y', (0, 0, 1.62))
    umb = K.material("Umbrella", lin((0.10, 0.12, 0.30)), rough=0.6)
    ux = 0.11
    cyl(ux, 0.0, 1.12, 0.035, 0.85, umb, 'Z', 8)                                       # fechado, pendurado
    cyl(ux, 0.0, 0.66, 0.005, 0.08, M['black'], 'Z', 6)                                # ponteira
    ring(ux - 0.03, 0.0, 1.58, 0.03, 0.006, M['black'], 'Y', 16)                       # cabo em gancho
    cyl(ux, 0.0, 1.55, 0.007, 0.04, M['black'], 'Z', 6)
    sc = K.material("Scarf", lin((0.70, 0.25, 0.22)), rough=0.9)
    box(0.04, 0.10, -0.03, 0.03, 1.15, 1.62, sc, 0.02)
    P["CoatRack"] = K.PARTS[s:]

    # --- planta de chão (costela-de-adão) num vaso grande
    s = len(K.PARTS)
    cyl(0, 0, 0.2, 0.2, 0.4, M['terracotta'], 'Z', 28, 0.008)
    cyl(0, 0, 0.395, 0.185, 0.02, M['soil'], 'Z', 28)
    for i in range(13):
        a = i * 0.48 + random.uniform(-0.2, 0.2); h = 0.35 + random.uniform(0, 0.45)
        stem = cyl(math.cos(a) * 0.06, math.sin(a) * 0.06, 0.4 + h / 2, 0.006, h, M['leaf2'], 'Z', 6)
        rot_parts([stem], 0.35, 'X', (0, 0, 0.4)); rot_parts([stem], a, 'Z', (0, 0, 0))
        tip = Vector((math.cos(a) * (0.06 + math.sin(0.35) * h), math.sin(a) * (0.06 + math.sin(0.35) * h), 0.4 + math.cos(0.35) * h))
        leaf(tip.x, tip.y, tip.z, 0.36, 0.30, M['leaf2'], yaw=a, pitch=0.35)
    P["FloorPlant"] = K.PARTS[s:]

    # --- capacho "BEM-VINDO" (0,8 x 0,5) — chão
    s = len(K.PARTS)
    mat = K.material("Coir", lin((0.55, 0.40, 0.22)), rough=0.95, color_var=0.12, bump=0.3, bump_scale=500.0)
    box(-0.4, 0.4, -0.25, 0.25, 0, 0.018, mat, 0.004)
    text("BEM-VINDO", 0, 0, 0.0182, 0.075, K.material("CoirDark", lin((0.15, 0.10, 0.06)), rough=0.95), rot=FLAT)
    P["DoorMat"] = K.PARTS[s:]

    return finish_group(P)

ALL = {"counter": build_counter, "tables": build_tables, "walls": build_walls}[GROUP]()

# ------------------------------------------------------------------ prévia (tudo enfileirado de frente)
def preview_lineup():
    sc = bpy.context.scene; sc.render.engine = 'CYCLES'; sc.cycles.samples = 24; sc.cycles.use_denoising = True
    sc.render.resolution_x, sc.render.resolution_y = 1800, 800
    sc.view_settings.view_transform = 'Standard'
    w = bpy.data.worlds.new("w"); sc.world = w; w.use_nodes = True
    w.node_tree.nodes['Background'].inputs['Color'].default_value = (0.32, 0.33, 0.36, 1); w.node_tree.nodes['Background'].inputs['Strength'].default_value = 0.7
    x = 0.0; maxh = 0.0
    flat = {"Newspaper", "Textbook", "StudyNotes", "Phone", "DoorMat"}
    for o in ALL:
        if o.name in flat: o.rotation_euler = (math.radians(65), 0, 0); bpy.context.view_layer.update()
        bb = [o.matrix_world @ Vector(c) for c in o.bound_box]
        minx = min(v.x for v in bb); maxx = max(v.x for v in bb); minz = min(v.z for v in bb); maxz = max(v.z for v in bb)
        o.location.x += x - minx; o.location.z -= minz
        x += (maxx - minx) + 0.08; maxh = max(maxh, maxz - minz)
    bpy.ops.object.light_add(type='SUN', rotation=(math.radians(55), 0, math.radians(25))); bpy.context.active_object.data.energy = 3.0
    bpy.ops.object.camera_add(); cam = bpy.context.active_object; sc.camera = cam
    cam.data.type = 'ORTHO'; cam.data.ortho_scale = max(x, maxh * 2.25) * 1.02
    cam.location = (x / 2, -8, maxh * 0.5); cam.rotation_euler = (math.radians(90), 0, 0)
    sc.render.filepath = os.path.join(PREV_DIR, GROUP + "_pecas.png"); bpy.ops.render.render(write_still=True)

if PREV_DIR:
    os.makedirs(PREV_DIR, exist_ok=True)
    preview_lineup()
    if PREVIEW_ONLY: sys.exit(0)
    for o in ALL: o.location = (0, 0, 0); o.rotation_euler = (0, 0, 0)
    for o in list(bpy.data.objects):
        if o.type in ('LIGHT', 'CAMERA'): bpy.data.objects.remove(o)

# ------------------------------------------------------------------ bake + FBX
for o in list(bpy.data.objects):
    if o not in ALL: bpy.data.objects.remove(o)
K.unwrap(ALL, margin=0.003)
bpy.context.scene.cycles.samples = 16
K.bake_all(ALL, TEX_DIR, size=2048)
K.collapse_materials(ALL, GROUP + "Baked")
K.export_fbx(OUT_FBX)
print(f"[build_cafe] OK {GROUP}:", OUT_FBX, [(o.name, len(o.data.polygons)) for o in ALL])
