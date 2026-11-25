"""Acabamento da RUA do Interlúdio (Road, 2026-11-25) — por script, 0 créditos. A caminhada da Clear (café -> prédio)
ganha cidade de verdade ao longo dos 5 trechos (A..E): mobiliário urbano, comércio no térreo, nomes de rua, cartazes e
a portaria do Edifício Porto Sereno.

  furniture : Bench, TrashBin (papeleira no poste), Hydrant, MailBox ("CORREIO"), Orelhao, TrafficLight, Newsstand
              (banca: "DIÁRIO DO PORTO — buscas pela moradora do Porto Sereno")
  signs     : StreetSign_1..4 (placas de esquina: R. das Gaivotas, Av. dos Pescadores, R. do Farol, R. das Marés,
              R. do Porto), PosterWall (lambe-lambe: show, compro ouro, a DESAPARECIDA), PolePoster (o cartaz da Helena
              para poste), GraffitiTag_A/B (pichação), DeliveryAd (painel do ponto de ônibus: "The Delivery")
  shops     : Shop_Padaria, Shop_Farmacia, Shop_Bar, Shop_Mercadinho, Shop_Chaveiro, Shop_Lavanderia (vitrine acesa +
              letreiro + toldo), Shutter (porta de enrolar fechada, pichada, "ALUGA-SE")
  entrance  : PortoSerenoEntrance (marquise com o nome, porta de vidro da portaria acesa, número 118, interfone,
              vasos e degrau)

Convenção: frente para -Y do Blender (= +Z no Unity). Chão: origem no centro da BASE. Fachada: origem no centro da
face de TRÁS, embaixo (encosta na parede do prédio).
Uso: blender --background --python build_street.py -- <grupo> <saida.fbx> <pasta_texturas> [<pasta_previas>|-] [preview_only]
"""
import bpy, bmesh, sys, math, os, random
from mathutils import Vector, Matrix
sys.path.append(os.path.dirname(os.path.abspath(__file__)))
import realistic_kit as K

argv = sys.argv[sys.argv.index("--") + 1:]
GROUP, OUT_FBX, TEX_DIR = argv[0], argv[1], argv[2]
PREV_DIR = argv[3] if len(argv) > 3 and argv[3] != "-" else None
PREVIEW_ONLY = len(argv) > 4 and argv[4] == "preview_only"
random.seed(23)
K.reset_scene()

def lin(c): return tuple(x ** 2.2 for x in c)
def box(x0, x1, y0, y1, z0, z1, mat, bevel=0.0, seg=2):
    return K.box(min(x0, x1), max(x0, x1), min(y0, y1), max(y0, y1), min(z0, z1), max(z0, z1), mat, bevel=bevel, segments=seg)
def cyl(x, y, z, r, d, mat, axis='Z', verts=24, bevel=0.0): return K.cyl(x, y, z, r, d, mat, axis=axis, verts=verts, bevel=bevel)
FRONT = (math.pi / 2, 0, 0)
def text(s, x, y, z, size, mat, align='CENTER', rot=FRONT):
    bpy.ops.object.text_add(location=(x, y, z), rotation=rot)
    ob = bpy.context.active_object
    ob.data.body = s; ob.data.size = size; ob.data.extrude = 0.0; ob.data.resolution_u = 3
    ob.data.align_x = align; ob.data.align_y = 'CENTER'
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
def strip(points, width, y, mat, depth=0.0015, taper=True):
    bm = bmesh.new(); n = len(points); left, right = [], []
    for i, (px, pz) in enumerate(points):
        a = points[max(0, i - 1)]; b = points[min(n - 1, i + 1)]
        tx, tz = b[0] - a[0], b[1] - a[1]; L = math.hypot(tx, tz) or 1.0
        nx, nz = -tz / L, tx / L
        w = width * (math.sin(math.pi * i / (n - 1)) * 0.7 + 0.3 if taper else 1.0) / 2
        left.append(bm.verts.new((px + nx * w, y, pz + nz * w))); right.append(bm.verts.new((px - nx * w, y, pz - nz * w)))
    for i in range(n - 1): bm.faces.new((left[i], left[i + 1], right[i + 1], right[i]))
    me = bpy.data.meshes.new("strip"); bm.to_mesh(me); bm.free()
    o = bpy.data.objects.new("strip", me); bpy.context.collection.objects.link(o); o.data.materials.append(mat)
    if depth:
        md = o.modifiers.new("s", 'SOLIDIFY'); md.thickness = depth; md.offset = 1
        bpy.context.view_layer.objects.active = o; bpy.ops.object.modifier_apply(modifier="s")
    K.PARTS.append(o); return o
def leaf(cx, cy, cz, length, width, mat, yaw=0.0, pitch=0.0):
    bm = bmesh.new(); n = 7; pts = []
    for i in range(n):
        t = i / (n - 1); w = math.sin(math.pi * t) * width / 2
        pts.append(((t - 0.5) * length, w, 0.02 * math.sin(math.pi * t) * length))
    vs = [bm.verts.new(p) for p in pts] + [bm.verts.new((p[0], -p[1], p[2])) for p in pts[1:-1][::-1]]
    bm.faces.new(vs)
    me = bpy.data.meshes.new("leaf"); bm.to_mesh(me); bm.free()
    o = bpy.data.objects.new("leaf", me); bpy.context.collection.objects.link(o); o.data.materials.append(mat)
    o.rotation_euler = (pitch, 0, yaw); o.location = (cx, cy, cz)
    md = o.modifiers.new("s", 'SOLIDIFY'); md.thickness = 0.003
    bpy.context.view_layer.objects.active = o; bpy.ops.object.modifier_apply(modifier="s")
    K.PARTS.append(o); return o

def mats():
    M = {}
    M['steel'] = K.material("Galvanized", lin((0.62, 0.63, 0.64)), rough=0.4, metal=1.0, dirt=0.3, rust=0.08)
    M['darkmetal'] = K.material("DarkMetal", lin((0.12, 0.13, 0.14)), rough=0.45, metal=0.8, dirt=0.3)
    M['black'] = K.material("BlackPlastic", lin((0.04, 0.04, 0.045)), rough=0.45, dirt=0.15)
    M['white'] = K.material("WhitePaint", lin((0.88, 0.88, 0.86)), rough=0.45, dirt=0.3)
    M['paper'] = K.material("Paper", lin((0.93, 0.92, 0.88)), rough=0.85, color_var=0.04, dirt=0.2)
    M['old'] = K.material("OldPaper", lin((0.90, 0.86, 0.74)), rough=0.85, color_var=0.08, dirt=0.3, dirt_color=lin((0.6, 0.52, 0.38)))
    M['ink'] = K.material("Ink", lin((0.05, 0.05, 0.06)), rough=0.6)
    M['red'] = K.material("RedPaint", lin((0.72, 0.08, 0.06)), rough=0.4, dirt=0.2)
    M['wood'] = K.material("BenchWood", lin((0.45, 0.28, 0.14)), rough=0.6, wood=True, dirt=0.3, color_var=0.1)
    M['concrete'] = K.material("Concrete", lin((0.55, 0.55, 0.53)), rough=0.9, color_var=0.08, dirt=0.4)
    M['glass'] = K.material("ShopGlassLit", lin((0.80, 0.66, 0.45)), rough=0.1, emission=(1.0, 0.76, 0.46), emission_strength=0.9)
    M['glassdark'] = K.material("DarkGlass", lin((0.06, 0.07, 0.08)), rough=0.08, metal=0.3)
    M['alu'] = K.material("Aluminum", lin((0.72, 0.72, 0.73)), rough=0.35, metal=1.0, dirt=0.2)
    M['leaf'] = K.material("LeafDark", lin((0.08, 0.25, 0.10)), rough=0.45, color_var=0.12)
    M['terracotta'] = K.material("Planter", lin((0.30, 0.30, 0.31)), rough=0.85, dirt=0.3)
    M['blueplate'] = K.material("StreetBlue", lin((0.06, 0.20, 0.50)), rough=0.4)
    M['spray'] = K.material("SprayBlack", lin((0.06, 0.06, 0.07)), rough=0.7)
    return M

# ====================================================================== FURNITURE
def build_furniture():
    M = mats(); P = {}
    # --- banco de praça (1,8 m): ripas de madeira em pés de ferro
    s = len(K.PARTS)
    for x in (-0.75, 0.75):
        box(x - 0.04, x + 0.04, -0.25, 0.25, 0.0, 0.42, M['darkmetal'], 0.01)
        box(x - 0.04, x + 0.04, 0.17, 0.25, 0.42, 0.85, M['darkmetal'], 0.01)
    for i in range(4): box(-0.9, 0.9, -0.24 + i * 0.105, -0.16 + i * 0.105, 0.42, 0.46, M['wood'], 0.006)
    for i in range(3): box(-0.9, 0.9, 0.2, 0.24, 0.55 + i * 0.1, 0.62 + i * 0.1, M['wood'], 0.006)
    P["Bench"] = K.PARTS[s:]
    # --- papeleira laranja num poste
    s = len(K.PARTS)
    orange = K.material("BinOrange", lin((0.92, 0.45, 0.08)), rough=0.45, dirt=0.35)
    cyl(0, 0.0, 0.5, 0.035, 1.0, M['steel'], 'Z', 12)
    cyl(0, -0.12, 0.85, 0.16, 0.42, orange, 'Z', 24, 0.01)
    cyl(0, -0.12, 1.065, 0.165, 0.02, M['darkmetal'], 'Z', 24, 0.005)
    text("LIXO", 0, -0.281, 0.88, 0.06, M['white'])
    P["TrashBin"] = K.PARTS[s:]
    # --- hidrante vermelho
    s = len(K.PARTS)
    cyl(0, 0, 0.04, 0.14, 0.08, M['red'], 'Z', 20, 0.01)
    cyl(0, 0, 0.36, 0.10, 0.56, M['red'], 'Z', 20, 0.01)
    cyl(0, 0, 0.68, 0.11, 0.08, M['red'], 'Z', 20, 0.01)
    bpy.ops.mesh.primitive_uv_sphere_add(segments=16, ring_count=8, radius=0.1, location=(0, 0, 0.72)); K._finish(bpy.context.active_object, M['red'])
    for a in (0, math.pi):
        cyl(math.cos(a) * 0.13, math.sin(a) * 0.13, 0.48, 0.045, 0.08, M['red'], 'X', 12, 0.005)
    cyl(0, -0.13, 0.42, 0.06, 0.08, M['red'], 'Y', 14, 0.006)
    P["Hydrant"] = K.PARTS[s:]
    # --- caixa de correio amarela
    s = len(K.PARTS)
    yellow = K.material("MailYellow", lin((0.98, 0.78, 0.05)), rough=0.4, dirt=0.3)
    box(-0.06, 0.06, -0.06, 0.06, 0.0, 0.8, M['darkmetal'], 0.005)
    box(-0.24, 0.24, -0.2, 0.2, 0.8, 1.4, yellow, 0.03)
    box(-0.15, 0.15, -0.205, -0.2, 1.22, 1.26, M['black'])
    text("CORREIO", 0, -0.2055, 1.1, 0.06, K.material("MailBlue", lin((0.05, 0.20, 0.55)), rough=0.4))
    P["MailBox"] = K.PARTS[s:]
    # --- orelhão (concha laranja num poste, com o telefone)
    s = len(K.PARTS)
    shell = K.material("OrelhaoOrange", lin((0.95, 0.45, 0.10)), rough=0.35, dirt=0.25)
    cyl(0, 0.0, 1.1, 0.04, 2.2, M['steel'], 'Z', 12)
    bpy.ops.mesh.primitive_uv_sphere_add(segments=24, ring_count=12, radius=0.5, location=(0, -0.32, 1.95))
    sh = bpy.context.active_object; sh.scale = (0.85, 0.75, 1.0)
    bm = bmesh.new(); bm.from_mesh(sh.data)
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if v.co.y < -0.18 or v.co.z < -0.55], context='VERTS')
    bm.to_mesh(sh.data); bm.free()
    md = sh.modifiers.new("s", 'SOLIDIFY'); md.thickness = 0.02; bpy.context.view_layer.objects.active = sh; bpy.ops.object.modifier_apply(modifier="s")
    K._finish(sh, shell)
    box(-0.13, 0.13, -0.2, -0.08, 1.25, 1.6, K.material("PhoneGrey", lin((0.30, 0.31, 0.33)), rough=0.4, dirt=0.3), 0.01)
    box(-0.03, 0.03, -0.22, -0.2, 1.38, 1.55, M['black'], 0.01)
    P["Orelhao"] = K.PARTS[s:]
    # --- semáforo (poste + caixa com 3 focos; o vermelho aceso)
    s = len(K.PARTS)
    cyl(0, 0, 1.6, 0.06, 3.2, M['darkmetal'], 'Z', 16)
    box(-0.16, 0.16, -0.14, 0.14, 2.3, 3.2, M['black'], 0.02)
    red_on = K.material("LightRed", lin((1.0, 0.1, 0.05)), rough=0.2, emission=(1.0, 0.1, 0.05), emission_strength=5.0)
    off = K.material("LightOff", lin((0.08, 0.08, 0.08)), rough=0.2)
    for i, m in enumerate((red_on, off, off)):
        cyl(0, -0.15, 3.03 - i * 0.29, 0.09, 0.02, m, 'Y', 24)
        box(-0.12, 0.12, -0.27, -0.14, 3.03 - i * 0.29 + 0.08, 3.03 - i * 0.29 + 0.1, M['black'])
    P["TrafficLight"] = K.PARTS[s:]
    # --- banca de jornal (2,4 x 1,6 x 2,4), aberta, com a manchete na frente
    s = len(K.PARTS)
    green = K.material("KioskGreen", lin((0.10, 0.32, 0.22)), rough=0.45, dirt=0.35)
    box(-1.2, 1.2, -0.8, 0.8, 0.0, 2.3, green, 0.02)
    box(-1.3, 1.3, -1.0, 0.9, 2.3, 2.45, green, 0.02)
    box(-1.1, 1.1, -0.81, -0.79, 0.2, 2.1, K.material("KioskInside", lin((0.85, 0.75, 0.55)), rough=0.6, emission=(1.0, 0.82, 0.55), emission_strength=0.6))
    for r in range(4):     # revistas penduradas
        for c in range(8):
            col = random.choice([(0.8, 0.2, 0.2), (0.2, 0.4, 0.8), (0.9, 0.8, 0.2), (0.9, 0.9, 0.9), (0.3, 0.7, 0.4)])
            box(-1.0 + c * 0.25, -0.82 + c * 0.25, -0.83, -0.815, 0.5 + r * 0.38, 0.8 + r * 0.38, K.material(f"Mag{r}{c}", lin(col), rough=0.4))
    box(-1.25, 1.25, -1.01, -0.98, 2.3, 2.45, K.material("KioskSign", lin((0.95, 0.95, 0.9)), rough=0.5))
    text("BANCA DO PORTO", 0, -1.015, 2.375, 0.1, green)
    box(0.5, 1.2, -1.0, -0.82, 0.0, 0.05, M['black'])
    board = [box(0.55, 1.15, -1.05, -0.95, 0.05, 0.95, M['paper'], 0.005)]
    for li, ln in enumerate(["DIÁRIO DO PORTO", "", "BUSCAS PELA", "MORADORA DO", "PORTO SERENO", "CONTINUAM"]):
        if ln: board.append(text(ln, 0.85, -1.051, 0.85 - li * 0.11, 0.07 if li else 0.06, M['red'] if li == 0 else M['ink']))
    P["Newsstand"] = K.PARTS[s:]
    return finish_group(P)

# ====================================================================== SIGNS
def build_signs():
    M = mats(); P = {}
    names = {1: ("R. das Gaivotas", "Av. dos Pescadores"), 2: ("Av. dos Pescadores", "R. do Farol"),
             3: ("R. do Farol", "R. das Marés"), 4: ("R. das Marés", "R. do Porto")}
    for k, (a, b) in names.items():
        s = len(K.PARTS)
        cyl(0, 0, 1.4, 0.035, 2.8, M['darkmetal'], 'Z', 12)
        for i, (nm, yaw) in enumerate(((a, 0.0), (b, math.pi / 2))):
            ps = len(K.PARTS)
            z = 2.55 - i * 0.24
            box(0.04, 0.84, -0.015, 0.015, z - 0.1, z + 0.1, M['blueplate'], 0.004)
            for side in (-1, 1):
                text(nm, 0.44, side * 0.0155, z, 0.08, M['white'], rot=(math.pi / 2, 0, 0 if side < 0 else math.pi))
            rot_parts(K.PARTS[ps:], yaw, 'Z', (0, 0, 0))
        P[f"StreetSign_{k}"] = K.PARTS[s:]
    # --- lambe-lambe: show, compro ouro, a desaparecida, aulas (na parede)
    s = len(K.PARTS)
    def poster(cx, cz, w, h, col, lines, ink, ang=0.0, missing=False):
        ps = len(K.PARTS)
        box(cx - w / 2, cx + w / 2, -0.002, 0, cz - h / 2, cz + h / 2, K.material(f"Lambe{len(K.PARTS)}", lin(col), rough=0.8, color_var=0.1, dirt=0.3))
        if missing:
            text("DESAPARECIDA", cx, -0.0025, cz + h / 2 - 0.05, 0.05, M['red'])
            box(cx - 0.1, cx + 0.1, -0.003, -0.0022, cz - 0.02, cz + 0.17, K.material("PhotoBack", lin((0.42, 0.45, 0.50)), rough=0.6))
            cyl(cx, -0.0034, cz + 0.11, 0.04, 0.0006, K.material("PhotoSilhouette", lin((0.16, 0.15, 0.15)), rough=0.6), 'Y', 20)
            for li, ln in enumerate(["Helena M., 31 anos", "moradora do 405", "portaria ou 190"]):
                text(ln, cx, -0.0025, cz - 0.08 - li * 0.04, 0.028, M['ink'])
        else:
            for li, ln in enumerate(lines):
                text(ln, cx, -0.0025, cz + h / 2 - 0.08 - li * (h / (len(lines) + 1)), 0.07 if li == 0 else 0.045, ink)
        rot_parts(K.PARTS[ps:], math.radians(ang), 'Y', (cx, 0, cz))
    for r in range(2):
        for c in range(3):
            poster(-0.75 + c * 0.42, 1.0 + r * 0.62, 0.4, 0.58, (0.95, 0.85, 0.2), ["OS MARÉS", "ao vivo", "sexta 21h", "bar do Zé"], M['ink'], random.uniform(-3, 3))
    poster(0.55, 1.3, 0.42, 0.6, (0.92, 0.90, 0.84), [], M['ink'], 2, missing=True)
    poster(1.05, 1.0, 0.4, 0.5, (0.95, 0.95, 0.95), ["COMPRO", "OURO", "pago à vista", "9 9132-0080"], M['red'], -2)
    poster(1.05, 1.6, 0.4, 0.5, (0.2, 0.2, 0.25), ["AULAS DE", "REFORÇO", "matemática", "e física"], M['white'], 3)
    P["PosterWall"] = K.PARTS[s:]
    # --- o cartaz da Helena para poste (A4, com fita)
    s = len(K.PARTS)
    poster(0, 0.15, 0.21, 0.30, (0.92, 0.90, 0.84), [], M['ink'], 0, missing=True)
    for x in (-0.09, 0.09): box(x - 0.025, x + 0.025, -0.004, -0.003, 0.285, 0.31, K.material("Tape", lin((0.86, 0.84, 0.72)), rough=0.4))
    P["PolePoster"] = K.PARTS[s:]
    # --- pichações (tags) para muro/porta
    for ti in range(2):
        s = len(K.PARTS)
        for k in range(5 + ti * 2):
            x0 = -0.8 + k * 0.32
            pts = [(x0 + j * 0.05, 0.4 + math.sin(j * 1.7 + k + ti) * 0.25 + j * 0.03) for j in range(7)]
            strip(pts, 0.045, -0.002, M['spray'] if ti == 0 else K.material("SprayRed", lin((0.6, 0.05, 0.08)), rough=0.7), depth=0.0005, taper=False)
        strip([(-0.85, 0.2), (0.0, 0.12), (0.9, 0.22)], 0.03, -0.0025, M['spray'], depth=0.0005, taper=False)
        P[f"GraffitiTag_{'AB'[ti]}"] = K.PARTS[s:]
    # --- painel de anúncio do ponto de ônibus: The Delivery
    s = len(K.PARTS)
    red = K.material("DeliveryRed", lin((0.85, 0.12, 0.08)), rough=0.4, emission=(0.9, 0.15, 0.1), emission_strength=0.8)
    box(-0.6, 0.6, -0.04, 0, 0, 1.7, M['alu'], 0.01)
    box(-0.55, 0.55, -0.045, -0.04, 0.05, 1.65, red)
    cream = K.material("AdCream", lin((0.98, 0.95, 0.88)), rough=0.4, emission=(1.0, 0.97, 0.9), emission_strength=0.6)
    text("The Delivery", 0, -0.046, 1.35, 0.14, cream)
    text("seu 1º pedido", 0, -0.046, 1.0, 0.09, cream)
    text("GRÁTIS", 0, -0.046, 0.82, 0.16, cream)
    box(-0.12, 0.12, -0.047, -0.045, 0.28, 0.52, cream)
    text("baixe o app", 0, -0.046, 0.18, 0.06, cream)
    P["DeliveryAd"] = K.PARTS[s:]
    return finish_group(P)

# ====================================================================== SHOPS
def build_shops():
    M = mats(); P = {}
    shops = [("Padaria", "PADARIA PÃO DOURADO", (0.55, 0.25, 0.10), [(0.85, 0.65, 0.25), (0.95, 0.95, 0.9)]),
             ("Farmacia", "FARMÁCIA", (0.05, 0.45, 0.20), [(0.1, 0.5, 0.25), (0.95, 0.95, 0.95)]),
             ("Bar", "BAR DO ZÉ", (0.55, 0.08, 0.06), [(0.7, 0.1, 0.08), (0.95, 0.85, 0.3)]),
             ("Mercadinho", "MERCADINHO BOM PREÇO", (0.10, 0.25, 0.55), [(0.15, 0.3, 0.65), (0.95, 0.95, 0.95)]),
             ("Chaveiro", "CHAVEIRO 24H", (0.85, 0.6, 0.05), [(0.9, 0.65, 0.1), (0.15, 0.15, 0.15)]),
             ("Lavanderia", "LAVANDERIA", (0.25, 0.55, 0.75), [(0.3, 0.6, 0.8), (0.95, 0.95, 0.95)])]
    W = 3.6
    for key, title, signcol, awn in shops:
        s = len(K.PARTS)
        # vitrine acesa + porta (térreo): moldura de alumínio
        box(-W / 2, W / 2, -0.06, 0, 0, 0.45, M['concrete'], 0.01)                       # rodapé de alvenaria
        box(-W / 2 + 0.1, 0.3, -0.05, -0.03, 0.45, 2.5, M['glass'])                    # vitrine
        shelf = K.material("ShelfDark", lin((0.18, 0.12, 0.08)), rough=0.6)
        for zz in (0.95, 1.45, 1.95): box(-W / 2 + 0.15, 0.25, -0.052, -0.05, zz, zz + 0.03, shelf)   # prateleiras vistas pelo vidro
        for zz in (0.95, 1.45, 1.95):
            x = -W / 2 + 0.2
            while x < 0.15:
                w = random.uniform(0.06, 0.16); h = random.uniform(0.12, 0.32)
                col = random.choice([(0.7, 0.25, 0.15), (0.2, 0.35, 0.6), (0.85, 0.7, 0.3), (0.9, 0.9, 0.85), (0.3, 0.55, 0.3), (0.5, 0.3, 0.5)])
                box(x, x + w, -0.053, -0.051, zz + 0.03, zz + 0.03 + h, K.material(f"Prod{int(col[0]*10)}{int(col[1]*10)}", lin(col), rough=0.5))
                x += w + random.uniform(0.02, 0.06)
        box(0.5, W / 2 - 0.1, -0.05, -0.03, 0.0, 2.3, M['glassdark'])                   # porta
        for x in (-W / 2 + 0.05, 0.4, W / 2 - 0.05): box(x - 0.05, x + 0.05, -0.08, 0, 0, 2.6, M['alu'], 0.005)
        box(-W / 2, W / 2, -0.08, 0, 2.5, 2.6, M['alu'], 0.005)
        # letreiro
        sc = K.material(f"Sign{key}", lin(signcol), rough=0.45, dirt=0.2)
        box(-W / 2, W / 2, -0.12, 0, 2.6, 3.25, sc, 0.01)
        lit = K.material(f"SignText{key}", lin((0.98, 0.97, 0.92)), rough=0.4, emission=(1.0, 0.98, 0.92), emission_strength=2.0)
        text(title, 0, -0.121, 2.92, 0.26 if len(title) < 14 else 0.19, lit)
        # toldo listrado (inclinado), abaixo do letreiro
        a1 = K.material(f"Awn{key}1", lin(awn[0]), rough=0.8, dirt=0.25); a2 = K.material(f"Awn{key}2", lin(awn[1]), rough=0.8, dirt=0.25)
        n = 12; parts = []
        for i in range(n):
            x0 = -W / 2 + i * W / n
            parts.append(box(x0, x0 + W / n, -1.0, 0.0, 2.48, 2.52, a1 if i % 2 == 0 else a2))
            parts.append(box(x0, x0 + W / n, -1.02, -0.98, 2.2, 2.5, a1 if i % 2 == 0 else a2))   # sanefa
        rot_parts(parts, math.radians(-14), 'X', (0, 0, 2.52))
        if key == "Farmacia":   # a cruz verde em neon, de lado (bandeira)
            neon = K.material("NeonGreen", lin((0.1, 1.0, 0.3)), rough=0.3, emission=(0.1, 1.0, 0.3), emission_strength=6.0)
            box(W / 2 - 0.35, W / 2 - 0.29, -0.6, 0, 3.0, 3.06, M['darkmetal'])
            box(W / 2 - 0.37, W / 2 - 0.27, -0.85, -0.55, 2.62, 3.12, neon, 0.01)
            box(W / 2 - 0.37, W / 2 - 0.27, -0.95, -0.45, 2.77, 2.97, neon, 0.01)
        P[f"Shop_{key}"] = K.PARTS[s:]
    # --- porta de enrolar fechada, pichada, com "ALUGA-SE"
    s = len(K.PARTS)
    sh = K.material("ShutterMetal", lin((0.55, 0.56, 0.57)), rough=0.5, metal=0.7, dirt=0.5, rust=0.2)
    for i in range(36): box(-1.6, 1.6, -0.06 if i % 2 else -0.05, 0, i * 0.075, i * 0.075 + 0.075, sh)
    box(-1.7, 1.7, -0.25, 0, 2.7, 3.0, M['darkmetal'], 0.01)
    for k in range(6):
        x0 = -1.2 + k * 0.4
        strip([(x0 + j * 0.06, 0.9 + math.sin(j * 1.9 + k) * 0.3) for j in range(6)], 0.06, -0.065, M['spray'], depth=0.0005, taper=False)
    box(0.5, 1.3, -0.07, -0.065, 1.5, 2.0, K.material("AlugaSign", lin((0.95, 0.95, 0.2)), rough=0.5))
    text("ALUGA-SE", 0.9, -0.0705, 1.82, 0.1, M['red'])
    text("9 9871-2210", 0.9, -0.0705, 1.65, 0.07, M['ink'])
    P["Shutter"] = K.PARTS[s:]
    return finish_group(P)

# ====================================================================== ENTRANCE
def build_entrance():
    M = mats(); P = {}
    s = len(K.PARTS)
    W = 4.2
    box(-W / 2, W / 2, -0.8, 0.0, 0.0, 0.15, M['concrete'], 0.01)                          # degrau
    granite = K.material("GraniteDark", lin((0.12, 0.12, 0.13)), rough=0.3, color_var=0.15)
    box(-W / 2, -W / 2 + 0.5, -0.1, 0.0, 0.15, 3.1, granite, 0.01)                          # colunas de granito
    box(W / 2 - 0.5, W / 2, -0.1, 0.0, 0.15, 3.1, granite, 0.01)
    lobby = K.material("LobbyGlass", lin((0.55, 0.48, 0.36)), rough=0.08, emission=(1.0, 0.82, 0.55), emission_strength=0.45)
    box(-W / 2 + 0.5, W / 2 - 0.5, -0.05, -0.03, 0.15, 2.75, lobby)                         # porta de vidro da portaria
    for x in (-W / 2 + 0.55, -0.02, 0.02, W / 2 - 0.55): box(x - 0.04, x + 0.04, -0.08, 0, 0.15, 2.8, M['alu'], 0.005)
    box(-W / 2 + 0.5, W / 2 - 0.5, -0.08, 0, 2.75, 2.85, M['alu'], 0.005)
    for x in (-0.12, 0.12): box(x - 0.012, x + 0.012, -0.14, -0.08, 0.9, 1.6, M['alu'], 0.006)  # puxadores
    # marquise com o nome
    box(-W / 2 - 0.3, W / 2 + 0.3, -1.3, 0.0, 3.1, 3.35, M['concrete'], 0.02)
    brass = K.material("Brass", lin((0.78, 0.62, 0.30)), rough=0.3, metal=1.0, dirt=0.2)
    text("EDIFÍCIO PORTO SERENO", 0, -1.302, 3.225, 0.17, brass)
    downl = K.material("Downlight", lin((1, 0.9, 0.7)), rough=0.3, emission=(1.0, 0.85, 0.6), emission_strength=6.0)
    for x in (-1.2, 0, 1.2): cyl(x, -0.65, 3.095, 0.06, 0.01, downl, 'Z', 16)
    # número e interfone
    box(W / 2 - 0.45, W / 2 - 0.05, -0.103, -0.1, 2.2, 2.5, brass, 0.004)
    text("118", W / 2 - 0.25, -0.1035, 2.35, 0.18, M['ink'])
    box(W / 2 - 0.4, W / 2 - 0.1, -0.13, -0.1, 1.2, 1.65, M['steel'], 0.01)
    for r in range(4):
        for c in range(2): cyl(W / 2 - 0.32 + c * 0.14, -0.133, 1.32 + r * 0.07, 0.018, 0.006, M['darkmetal'], 'Y', 12)
    box(W / 2 - 0.33, W / 2 - 0.17, -0.133, -0.13, 1.58, 1.62, M['black'])
    # vasos com espada-de-são-jorge nas laterais
    for x in (-W / 2 - 0.45, W / 2 + 0.45):
        box(x - 0.25, x + 0.25, -0.45, 0.05, 0.0, 0.6, M['terracotta'], 0.02)
        for i in range(11):
            h = random.uniform(0.55, 0.85)
            o = leaf(0, 0, 0, h, 0.08, M['leaf'])
            o.rotation_euler = (0, math.radians(-90) + random.uniform(-0.18, 0.18), random.uniform(0, 6.28))
            o.location = (x + random.uniform(-0.12, 0.12), -0.2 + random.uniform(-0.12, 0.12), 0.6 + h / 2)
    P["PortoSerenoEntrance"] = K.PARTS[s:]
    return finish_group(P)

ALL = {"furniture": build_furniture, "signs": build_signs, "shops": build_shops, "entrance": build_entrance}[GROUP]()

def preview_lineup():
    sc = bpy.context.scene; sc.render.engine = 'CYCLES'; sc.cycles.samples = 24; sc.cycles.use_denoising = True
    sc.render.resolution_x, sc.render.resolution_y = 1800, 800
    sc.view_settings.view_transform = 'Standard'
    w = bpy.data.worlds.new("w"); sc.world = w; w.use_nodes = True
    w.node_tree.nodes['Background'].inputs['Color'].default_value = (0.32, 0.33, 0.36, 1); w.node_tree.nodes['Background'].inputs['Strength'].default_value = 0.7
    x = 0.0; maxh = 0.0
    for o in ALL:
        bpy.context.view_layer.update()
        bb = [o.matrix_world @ Vector(c) for c in o.bound_box]
        minx = min(v.x for v in bb); maxx = max(v.x for v in bb); minz = min(v.z for v in bb); maxz = max(v.z for v in bb)
        o.location.x += x - minx; o.location.z -= minz
        x += (maxx - minx) + 0.25; maxh = max(maxh, maxz - minz)
    bpy.ops.object.light_add(type='SUN', rotation=(math.radians(55), 0, math.radians(25))); bpy.context.active_object.data.energy = 3.0
    bpy.ops.object.camera_add(); cam = bpy.context.active_object; sc.camera = cam
    cam.data.type = 'ORTHO'; cam.data.ortho_scale = max(x, maxh * 2.25) * 1.02
    cam.location = (x / 2, -12, maxh * 0.5); cam.rotation_euler = (math.radians(90), 0, 0)
    sc.render.filepath = os.path.join(PREV_DIR, GROUP + "_pecas.png"); bpy.ops.render.render(write_still=True)

if PREV_DIR:
    os.makedirs(PREV_DIR, exist_ok=True)
    preview_lineup()
    if PREVIEW_ONLY: sys.exit(0)
    for o in ALL: o.location = (0, 0, 0); o.rotation_euler = (0, 0, 0)
    for o in list(bpy.data.objects):
        if o.type in ('LIGHT', 'CAMERA'): bpy.data.objects.remove(o)

for o in list(bpy.data.objects):
    if o not in ALL: bpy.data.objects.remove(o)
K.unwrap(ALL, margin=0.003)
bpy.context.scene.cycles.samples = 16
K.bake_all(ALL, TEX_DIR, size=2048)
K.collapse_materials(ALL, GROUP + "Baked")
K.export_fbx(OUT_FBX)
print(f"[build_street] OK {GROUP}:", OUT_FBX, [(o.name, len(o.data.polygons)) for o in ALL])
