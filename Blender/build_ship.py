"""Navio naufragado do Ato 5 (2026-11-26) — por script, 0 créditos. Três grupos, cada um com seu FBX e atlas:

  machines : Generator (gerador diesel de base-skid, tanque, escapamento, painel e manivela) + GeneratorLamp (a lâmpada
             de status, à parte: o Unity pinta por MaterialPropertyBlock), GateLeaf (portão de carga 4x4 m: quadro,
             nervuras, travessas em X, faixa de risco, estêncil), GatePanel (caixa com 5 soquetes e plaqueta) +
             GatePanelLamp, EngineBlock (motor diesel de 6 cilindros), PipeRun (feixe de 3 canos, 6 m, flanges e
             suportes)
  cell     : CellBars (módulo de grade 1 m), CellDoor (porta de grade 1 m, origem na dobradiça), Bucket (balde
             galvanizado com alça), Mattress (colchão manchado), LoosePanel (chapa 60 cm com 4 parafusos, uma quina
             torta), Key (chave antiga), KeyPost (poste de cantoneira com prego)
  cargo    : ContainerOpen (contêiner de 20 pés aberto na frente — esconderijo; uma porta escancarada), ContainerClosed
             (fechado, com barras de trava e código), Barrel (tambor 200 L), Crate (caixote de ripas), Locker (armário
             aberto — esconderijo), Bunk (beliche de ferro com colchões)

Convenção: frente para -Y do Blender (= +Z no Unity). Peças de chão: origem no centro da BASE.
  CellDoor: origem na DOBRADIÇA (borda x=0, embaixo), a folha cresce para +X.
  GateLeaf / GatePanel: origem no centro da base (o portão é visto de frente, plano em X/Z, espessura em Y).
Medidas reais (m). Uso:
  blender --background --python build_ship.py -- <grupo> <saida.fbx> <pasta_texturas> [<pasta_previas>|-] [preview_only]
"""
import bpy, bmesh, sys, math, os, random
from mathutils import Vector
sys.path.append(os.path.dirname(os.path.abspath(__file__)))
import realistic_kit as K

argv = sys.argv[sys.argv.index("--") + 1:]
GROUP, OUT_FBX, TEX_DIR = argv[0], argv[1], argv[2]
PREV_DIR = argv[3] if len(argv) > 3 and argv[3] != "-" else None
PREVIEW_ONLY = len(argv) > 4 and argv[4] == "preview_only"
random.seed(7)
K.reset_scene()

def lin(c): return tuple(x ** 2.2 for x in c)
def box(x0, x1, y0, y1, z0, z1, mat, bevel=0.0, seg=2):
    return K.box(min(x0, x1), max(x0, x1), min(y0, y1), max(y0, y1), min(z0, z1), max(z0, z1), mat, bevel=bevel, segments=seg)
def cyl(x, y, z, r, d, mat, axis='Z', verts=24, bevel=0.0): return K.cyl(x, y, z, r, d, mat, axis=axis, verts=verts, bevel=bevel)
def rename(o, n): o.name = n; o.data.name = n; return o
def group(parts):
    return [K.join(v, k) if len(v) > 1 else rename(v[0], k) for k, v in parts.items()]

def cone(x, y, z, r1, r2, d, mat, verts=28, open_top=False, wall=0.0):
    bpy.ops.mesh.primitive_cone_add(vertices=verts, radius1=r1, radius2=r2, depth=d, location=(x, y, z))
    ob = bpy.context.active_object
    if open_top:   # tira a tampa de cima (balde aberto) e dá espessura à parede
        bm = bmesh.new(); bm.from_mesh(ob.data)
        top = max(v.co.z for v in bm.verts)
        kill = [f for f in bm.faces if all(abs(v.co.z - top) < 1e-5 for v in f.verts)]
        bmesh.ops.delete(bm, geom=kill, context='FACES')
        if wall > 0: bmesh.ops.solidify(bm, geom=bm.faces[:], thickness=-wall)
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
        bm.to_mesh(ob.data); bm.free()
    return K._finish(ob, mat)

def torus(x, y, z, R, r, mat, rot=(0, 0, 0), arc=1.0, maj=32, mnr=8):
    bpy.ops.mesh.primitive_torus_add(major_radius=R, minor_radius=r, major_segments=maj, minor_segments=mnr, location=(x, y, z), rotation=rot)
    ob = bpy.context.active_object
    if arc < 1.0:   # corta parte do anel (alça)
        bm = bmesh.new(); bm.from_mesh(ob.data)
        kill = [v for v in bm.verts if v.co.z < -R * 0.05]
        bmesh.ops.delete(bm, geom=kill, context='VERTS'); bm.to_mesh(ob.data); bm.free()
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    return K._finish(ob, mat)

def rivets(xs, y, zs, mat, r=0.012, axis='Y'):
    for x in xs:
        for z in zs:
            cyl(x, y, z, r, 0.012, mat, axis=axis, verts=8)

# ---------------------------------------------------------------- materiais
# Aço pintado do navio: cinza-esverdeado, ferrugem e escorridos (sem 'wear' em chapa fina — vira ruído)
HULL = dict(dirt=0.5, dirt_color=lin((0.16, 0.12, 0.08)), rust=0.7, streaks=0.55, bump=0.25, bump_scale=60.0)
def mats():
    M = {}
    M['genpaint'] = K.material("GenPaint", lin((0.66, 0.50, 0.12)), rough=0.5, metal=0.15, dirt=0.7, dirt_color=lin((0.14, 0.11, 0.07)),
                               wear=0.6, wear_color=lin((0.20, 0.19, 0.18)), rust=0.45, streaks=0.35, bump=0.2)
    M['steel'] = K.material("DarkSteel", lin((0.16, 0.16, 0.16)), rough=0.55, metal=0.85, dirt=0.6, rust=0.5, bump=0.3, bump_scale=80.0)
    M['oily'] = K.material("OilySteel", lin((0.10, 0.10, 0.10)), rough=0.3, metal=0.9, dirt=0.8, dirt_color=lin((0.05, 0.04, 0.03)), rust=0.2)
    M['hull'] = K.material("HullPaint", lin((0.34, 0.38, 0.35)), rough=0.6, metal=0.2, **HULL)
    M['hulldark'] = K.material("HullDark", lin((0.20, 0.22, 0.21)), rough=0.6, metal=0.3, **HULL)
    M['rustbare'] = K.material("RustBare", lin((0.36, 0.17, 0.08)), rough=0.85, metal=0.3, dirt=0.4, rust=1.0, bump=0.6, bump_scale=90.0)
    M['yellow'] = K.material("HazardYellow", lin((0.78, 0.60, 0.08)), rough=0.6, dirt=0.5, rust=0.5, streaks=0.4)
    M['black'] = K.material("HazardBlack", lin((0.05, 0.05, 0.05)), rough=0.6, dirt=0.3, rust=0.4)
    M['stencil'] = K.material("Stencil", lin((0.85, 0.83, 0.76)), rough=0.7, dirt=0.6, rust=0.3)
    M['rubber'] = K.material("Rubber", lin((0.04, 0.04, 0.04)), rough=0.85, dirt=0.4)
    M['gauge'] = K.material("GaugeFace", lin((0.88, 0.86, 0.80)), rough=0.3, dirt=0.5)
    M['glass'] = K.material("LampGlass", lin((0.6, 0.08, 0.05)), rough=0.25, emission=lin((0.6, 0.05, 0.02)), emission_strength=0.6)
    M['galv'] = K.material("Galvanized", lin((0.55, 0.56, 0.54)), rough=0.45, metal=0.85, dirt=0.6, rust=0.7, bump=0.3, bump_scale=50.0)
    M['fabric'] = K.material("MattressFabric", lin((0.62, 0.58, 0.48)), rough=0.95, dirt=0.9, dirt_color=lin((0.30, 0.22, 0.12)),
                             rust=0.35, color_var=0.15, bump=0.4, bump_scale=25.0)
    M['brass'] = K.material("OldBrass", lin((0.55, 0.42, 0.18)), rough=0.4, metal=1.0, dirt=0.7, dirt_color=lin((0.12, 0.10, 0.06)))
    M['wood'] = K.material("CrateWood", lin((0.48, 0.36, 0.22)), rough=0.8, wood=True, dirt=0.6, rust=0.15, bump=0.3)
    M['contred'] = K.material("ContainerRed", lin((0.46, 0.13, 0.08)), rough=0.6, metal=0.3, dirt=0.4, rust=0.8, streaks=0.7, bump=0.2)
    M['contblue'] = K.material("ContainerBlue", lin((0.10, 0.20, 0.32)), rough=0.6, metal=0.3, dirt=0.4, rust=0.8, streaks=0.7, bump=0.2)
    M['floorwood'] = K.material("ContainerFloor", lin((0.30, 0.22, 0.14)), rough=0.85, wood=True, dirt=0.8)
    M['lockergrey'] = K.material("LockerGrey", lin((0.36, 0.40, 0.38)), rough=0.6, metal=0.35, dirt=0.8, dirt_color=lin((0.15, 0.10, 0.06)),
                                 rust=1.0, streaks=0.8, bump=0.3, bump_scale=60.0, color_var=0.12)
    M['sludge'] = K.material("Sludge", lin((0.10, 0.07, 0.04)), rough=0.15, dirt=0.3)
    return M

# =================================================================== MACHINES
def build_machines():
    M = mats(); parts = {}

    # ---------- Generator: 1,4 x 0,9 x ~1,3 m (frente = painel, -Y)
    s = len(K.PARTS)
    for y in (-0.36, 0.36):                       # trilhos do skid
        box(-0.75, 0.75, y - 0.05, y + 0.05, 0, 0.1, M['steel'], bevel=0.008)
    for x in (-0.6, 0.6):
        box(x - 0.04, x + 0.04, -0.42, 0.42, 0.1, 0.14, M['steel'], bevel=0.005)
    box(-0.62, 0.42, -0.36, 0.36, 0.14, 0.92, M['genpaint'], bevel=0.025)                 # carcaça do motor
    box(0.42, 0.66, -0.4, 0.4, 0.14, 1.0, M['genpaint'], bevel=0.02)                     # caixa do radiador
    for k in range(9):                                                                     # grade do radiador
        z = 0.25 + k * 0.08
        box(0.655, 0.675, -0.33, 0.33, z, z + 0.035, M['steel'])
    cyl(-0.12, 0.0, 1.06, 0.2, 0.9, M['genpaint'], axis='X', verts=28, bevel=0.01)       # tanque
    cyl(-0.5, 0.0, 1.26, 0.035, 0.06, M['steel'], verts=12)                                # tampa do tanque
    for x in (-0.5, 0.25):                                                                 # cintas do tanque
        box(x - 0.02, x + 0.02, -0.215, 0.215, 0.86, 1.27, M['steel'])
    cyl(-0.5, 0.28, 1.15, 0.045, 0.7, M['rustbare'], verts=14)                              # escapamento
    cyl(-0.5, 0.28, 1.52, 0.06, 0.06, M['rustbare'], verts=14)
    box(-0.55, 0.15, -0.44, -0.36, 0.42, 0.86, M['steel'], bevel=0.01)                     # painel de controle
    for i, x in enumerate((-0.42, -0.24)):                                                 # manômetros
        cyl(x, -0.45, 0.72, 0.055, 0.02, M['steel'], axis='Y', verts=20)
        cyl(x, -0.462, 0.72, 0.045, 0.006, M['gauge'], axis='Y', verts=20)
        box(x - 0.003, x + 0.003, -0.468, -0.465, 0.72, 0.76, M['black'])
    for i, x in enumerate((-0.05, 0.05)):                                                  # chaves
        box(x - 0.02, x + 0.02, -0.47, -0.44, 0.58, 0.62, M['black'])
    K.text("PERIGO", -0.2, -0.442, 0.49, 0.07, M['black'], rot=(math.pi / 2, 0, 0))
    # manivela de partida (lado -X)
    cyl(-0.66, 0.1, 0.55, 0.03, 0.12, M['steel'], axis='X', verts=12)
    box(-0.74, -0.71, 0.08, 0.12, 0.38, 0.6, M['steel'])
    cyl(-0.78, 0.1, 0.4, 0.022, 0.1, M['rubber'], axis='X', verts=12)
    parts["Generator"] = K.PARTS[s:]
    s = len(K.PARTS)
    parts["GeneratorLamp"] = None
    # base do LED presa ao topo da carcaça (antes ele flutuava): flange + haste curta + gaiola de proteção
    s2 = len(K.PARTS)
    cyl(-0.45, -0.28, 0.935, 0.055, 0.03, M['steel'], verts=20)
    cyl(-0.45, -0.28, 0.965, 0.022, 0.04, M['steel'], verts=12)
    for k in range(4):
        a = k * math.pi / 2 + math.pi / 4
        cyl(-0.45 + 0.052 * math.cos(a), -0.28 + 0.052 * math.sin(a), 1.035, 0.006, 0.1, M['steel'], verts=6)
    torus(-0.45, -0.28, 1.085, 0.052, 0.006, M['steel'], maj=20, mnr=5)
    parts["Generator"] += K.PARTS[s2:]
    s = len(K.PARTS)
    cyl(-0.45, -0.28, 1.03, 0.042, 0.07, M['glass'], axis='Z', verts=20)                      # lâmpada de status, sobre a base
    parts["GeneratorLamp"] = K.PARTS[s:]

    # ---------- GateLeaf: 4 x 4 m, espessura 0,25 (Y)
    s = len(K.PARTS)
    W, H, T = 4.0, 4.0, 0.25
    box(-W / 2, W / 2, -0.04, 0.04, 0, H, M['hull'])                                       # chapa
    for x0, x1 in ((-W / 2, -W / 2 + 0.16), (W / 2 - 0.16, W / 2)):                       # montantes do quadro
        box(x0, x1, -T / 2, T / 2, 0, H, M['hulldark'], bevel=0.01)
    for z0 in (0.0, H - 0.16):
        box(-W / 2, W / 2, -T / 2, T / 2, z0, z0 + 0.16, M['hulldark'], bevel=0.01)
    for z in (1.0, 2.0, 3.0):                                                               # nervuras horizontais
        box(-W / 2 + 0.16, W / 2 - 0.16, -0.11, -0.04, z - 0.05, z + 0.05, M['hulldark'], bevel=0.005)
    for sgn in (1, -1):                                                                     # travessas em X (frente)
        L = math.hypot(W - 0.32, H - 0.32); a = math.atan2(H - 0.32, W - 0.32) * sgn
        b = box(-L / 2, L / 2, -0.1, -0.05, -0.04, 0.04, M['hulldark'])
        b.rotation_euler = (0, -a, 0); b.location = (0, 0, H / 2)
        bpy.context.view_layer.objects.active = b; b.select_set(True)
        bpy.ops.object.select_all(action='DESELECT'); b.select_set(True)
        bpy.ops.object.transform_apply(location=True, rotation=True, scale=False)
    for k in range(10):                                                                     # faixa de risco (base)
        x0 = -W / 2 + 0.16 + k * 0.368
        box(x0, x0 + 0.368, -0.115, -0.105, 0.2, 0.55, M['yellow' if k % 2 == 0 else 'black'])
    rivets([-W / 2 + 0.08, W / 2 - 0.08], -0.13, [0.3 + 0.35 * i for i in range(11)], M['hulldark'])
    K.text("PORÃO 2", 0, -0.12, 2.5, 0.38, M['stencil'], rot=(math.pi / 2, 0, 0))
    K.text("NÃO OBSTRUIR", 0, -0.12, 1.5, 0.2, M['stencil'], rot=(math.pi / 2, 0, 0))
    parts["GateLeaf"] = K.PARTS[s:]

    # ---------- GatePanel: caixa 1,2 x 0,6 x 0,1 (montada na parede; origem embaixo, no centro)
    s = len(K.PARTS)
    box(-0.6, 0.6, -0.05, 0.05, 0, 0.6, M['hulldark'], bevel=0.01)
    box(-0.56, 0.56, -0.06, -0.05, 0.04, 0.56, M['steel'])
    for i in range(5):
        x = -0.44 + i * 0.22
        torus(x, -0.068, 0.36, 0.05, 0.012, M['steel'], rot=(math.pi / 2, 0, 0), maj=20, mnr=6)
        K.text(str(i + 1), x, -0.065, 0.2, 0.06, M['stencil'], rot=(math.pi / 2, 0, 0))
    K.text("GERADORES", 0, -0.065, 0.5, 0.06, M['stencil'], rot=(math.pi / 2, 0, 0))
    cyl(0.62, 0.0, 0.3, 0.02, 0.6, M['steel'], axis='Z', verts=10)                          # eletroduto
    parts["GatePanel"] = K.PARTS[s:]
    s = len(K.PARTS)
    cyl(0, -0.07, 0.04, 0.038, 0.03, M['glass'], axis='Y', verts=18)                        # 1 lâmpada (o Unity instancia 5)
    parts["GatePanelLamp"] = K.PARTS[s:]

    # ---------- EngineBlock: motor diesel 4 x 2,4 x 2,8 m
    s = len(K.PARTS)
    box(-2.0, 2.0, -1.2, 1.2, 0, 0.3, M['steel'], bevel=0.02)                              # berço
    box(-1.8, 1.6, -0.9, 0.9, 0.3, 1.6, M['hulldark'], bevel=0.04)                         # cárter/bloco
    for k in range(6):                                                                      # cabeçotes
        x = -1.5 + k * 0.52
        box(x - 0.22, x + 0.22, -0.6, 0.6, 1.6, 2.15, M['hulldark'], bevel=0.03)
        box(x - 0.18, x + 0.18, -0.5, 0.5, 2.15, 2.35, M['steel'], bevel=0.03)
        cyl(x, -0.75, 1.95, 0.07, 0.35, M['rustbare'], axis='Y', verts=14)                  # coletor
    cyl(-0.2, -0.95, 1.95, 0.12, 3.4, M['rustbare'], axis='X', verts=20)                     # coletor de escape
    cyl(1.8, 0, 1.0, 0.85, 0.3, M['steel'], axis='X', verts=40, bevel=0.01)                  # volante
    for k in range(8):
        a = k * math.pi / 4
        box(1.94, 1.98, 0.75 * math.cos(a) - 0.04, 0.75 * math.cos(a) + 0.04, 1.0 + 0.75 * math.sin(a) - 0.04, 1.0 + 0.75 * math.sin(a) + 0.04, M['oily'])
    cyl(-1.9, 0.5, 2.3, 0.07, 1.2, M['rustbare'], axis='Z', verts=14)
    K.text("MAN B&W", 0, -0.91, 1.0, 0.18, M['stencil'], rot=(math.pi / 2, 0, 0))
    parts["EngineBlock"] = K.PARTS[s:]

    # ---------- PipeRun: feixe de 3 canos, 6 m ao longo de X (origem no centro do feixe)
    s = len(K.PARTS)
    for i, (y, z, r) in enumerate(((0, 0, 0.09), (0.25, 0.04, 0.06), (-0.22, 0.03, 0.05))):
        cyl(0, y, z, r, 6.0, M['rustbare' if i == 0 else 'hull'], axis='X', verts=18)
        for x in (-2.0, 1.0):
            cyl(x, y, z, r + 0.03, 0.05, M['steel'], axis='X', verts=18)
    for x in (-2.6, 0, 2.6):                                                                 # suportes
        box(x - 0.03, x + 0.03, -0.35, 0.35, 0.1, 0.16, M['steel'])
        box(x - 0.03, x + 0.03, -0.35, -0.29, 0.16, 0.5, M['steel'])
        box(x - 0.03, x + 0.03, 0.29, 0.35, 0.16, 0.5, M['steel'])
    torus(1.4, -0.32, 0.03, 0.12, 0.015, M['yellow'], rot=(0, math.pi / 2, 0), maj=20, mnr=6)  # volante da válvula
    cyl(1.4, -0.24, 0.03, 0.02, 0.18, M['steel'], axis='Y', verts=8)
    parts["PipeRun"] = K.PARTS[s:]
    return group(parts)

# =================================================================== CELL
def build_cell():
    M = mats(); parts = {}
    # ---------- CellBars: módulo de 1 m x 3 m (barras em X, origem no centro da base)
    s = len(K.PARTS)
    box(-0.5, 0.5, -0.04, 0.04, 0.0, 0.1, M['steel'])
    box(-0.5, 0.5, -0.04, 0.04, 2.9, 3.0, M['steel'])
    box(-0.5, 0.5, -0.025, 0.025, 1.08, 1.14, M['steel'])
    for k in range(6):
        cyl(-0.42 + k * 0.168, 0, 1.5, 0.022, 2.8, M['steel'], verts=12)
    rivets([-0.45, 0.45], -0.045, [0.05, 2.95], M['steel'], r=0.014)
    parts["CellBars"] = K.PARTS[s:]
    # ---------- CellDoor: folha de 1 m (origem na dobradiça, cresce +X), 2,8 m
    s = len(K.PARTS)
    box(0.0, 1.0, -0.035, 0.035, 0.05, 0.13, M['steel'])
    box(0.0, 1.0, -0.035, 0.035, 2.72, 2.8, M['steel'])
    box(0.0, 0.07, -0.035, 0.035, 0.05, 2.8, M['steel'])
    box(0.93, 1.0, -0.035, 0.035, 0.05, 2.8, M['steel'])
    box(0.07, 0.93, -0.02, 0.02, 1.08, 1.14, M['steel'])
    for k in range(5):
        cyl(0.18 + k * 0.16, 0, 1.43, 0.02, 2.6, M['steel'], verts=12)
    for z in (0.4, 2.4):                                                                        # dobradiças
        cyl(-0.03, 0, z, 0.035, 0.18, M['oily'], verts=14)
    box(0.8, 1.0, -0.09, -0.035, 0.98, 1.24, M['hulldark'], bevel=0.008)                      # caixa da fechadura
    cyl(0.9, -0.095, 1.16, 0.016, 0.012, M['black'], axis='Y', verts=12)                       # buraco da chave
    box(0.895, 0.905, -0.096, -0.09, 1.11, 1.15, M['black'])
    parts["CellDoor"] = K.PARTS[s:]
    # ---------- Bucket: balde galvanizado (r 0,16 -> 0,13, h 0,3)
    s = len(K.PARTS)
    cone(0, 0, 0.15, 0.13, 0.16, 0.3, M['galv'], open_top=True, wall=0.004)
    cone(0, 0, 0.06, 0.125, 0.135, 0.1, M['sludge'])                                            # fundo: borra escura
    torus(0, 0, 0.3, 0.16, 0.008, M['galv'], maj=28, mnr=6)                                      # borda
    torus(0, 0, 0.3, 0.155, 0.006, M['steel'], rot=(math.pi / 2, 0, 0), arc=0.5, maj=24, mnr=5)  # alça
    for z in (0.08, 0.22): torus(0, 0, z, 0.137 + z * 0.1, 0.005, M['galv'], maj=28, mnr=5)
    parts["Bucket"] = K.PARTS[s:]
    # ---------- Mattress: 1,9 x 0,9 x 0,16
    s = len(K.PARTS)
    box(-0.95, 0.95, -0.45, 0.45, 0, 0.16, M['fabric'], bevel=0.05, seg=4)
    for k in range(6):                                                                         # botões/costura
        for j in range(2):
            cyl(-0.7 + k * 0.28, -0.18 + j * 0.36, 0.161, 0.012, 0.006, M['fabric'], verts=8)
    parts["Mattress"] = K.PARTS[s:]
    # ---------- LoosePanel: chapa 0,6 x 0,6 (de pé, frente -Y), 4 parafusos, uma quina dobrada
    s = len(K.PARTS)
    ob = box(-0.3, 0.3, -0.003, 0.003, 0.0, 0.6, M['hull'])
    bm = bmesh.new(); bm.from_mesh(ob.data)
    bmesh.ops.subdivide_edges(bm, edges=[e for e in bm.edges if abs(e.verts[0].co.y - e.verts[1].co.y) < 1e-6], cuts=20, use_grid_fill=True)
    for v in bm.verts:
        t = max(0.0, (v.co.x + (v.co.z - 0.3)) - 0.3)
        v.co.y -= t * t * 0.9                                                                    # quina de cima-direita torta
    bm.to_mesh(ob.data); bm.free()
    for x, z in ((-0.25, 0.05), (0.25, 0.05), (-0.25, 0.55)):
        cyl(x, -0.01, z, 0.018, 0.015, M['rustbare'], axis='Y', verts=6)
    parts["LoosePanel"] = K.PARTS[s:]
    # ---------- Key: chave antiga de 12 cm (de pé, no plano X/Z)
    s = len(K.PARTS)
    torus(0, 0, 0.1, 0.022, 0.005, M['brass'], rot=(math.pi / 2, 0, 0), maj=20, mnr=6)
    cyl(0, 0, 0.045, 0.0055, 0.09, M['brass'], verts=10)
    box(0.0, 0.022, -0.003, 0.003, 0.0, 0.018, M['brass'])
    box(0.0, 0.014, -0.003, 0.003, 0.022, 0.03, M['brass'])
    parts["Key"] = K.PARTS[s:]
    # ---------- KeyPost: cantoneira 2,2 m com prego e plaqueta
    s = len(K.PARTS)
    box(-0.05, 0.05, -0.05, -0.04, 0, 2.2, M['hulldark'])
    box(-0.05, -0.04, -0.05, 0.05, 0, 2.2, M['hulldark'])
    box(-0.09, 0.09, -0.09, 0.09, 0, 0.012, M['steel'])
    cyl(-0.08, 0.0, 1.5, 0.004, 0.07, M['steel'], axis='X', verts=8)                          # prego
    box(-0.06, -0.051, -0.08, 0.08, 1.7, 1.82, M['stencil'])
    parts["KeyPost"] = K.PARTS[s:]
    return group(parts)

# =================================================================== CARGO
def corrugated_wall(x0, x1, y, z0, z1, mat, pitch=0.28, depth=0.04, along='X'):
    n = int((x1 - x0) / pitch)
    for k in range(n):
        a = x0 + k * pitch
        if along == 'X':
            box(a, a + pitch * 0.5, y - depth / 2, y + depth / 2, z0, z1, mat)
        else:
            box(y - depth / 2, y + depth / 2, a, a + pitch * 0.5, z0, z1, mat)

def container(M, mat, open_front):
    W, L, H = 2.44, 6.06, 2.59
    hx, hy = W / 2, L / 2
    for x in (-hx, hx - 0.15):                                                                 # pilares de canto
        for y in (-hy, hy - 0.15):
            box(x, x + 0.15, y, y + 0.15, 0, H, M['hulldark'], bevel=0.01)
    for y0 in (-hy, hy - 0.15):                                                                # longarinas sup/inf
        for z0 in (0, H - 0.15):
            box(-hx, hx, y0, y0 + 0.15, z0, z0 + 0.15, mat)
    for x0 in (-hx, hx - 0.12):
        for z0 in (0, H - 0.15):
            box(x0, x0 + 0.12, -hy, hy, z0, z0 + 0.15, mat)
    box(-hx + 0.05, hx - 0.05, -hy + 0.1, hy - 0.1, H - 0.05, H - 0.02, mat)                    # teto
    box(-hx + 0.1, hx - 0.1, -hy + 0.1, hy - 0.1, 0.1, 0.15, M['floorwood'])                    # assoalho
    for x in (-hx + 0.03, hx - 0.03):                                                          # laterais corrugadas
        box(x - 0.012, x + 0.012, -hy + 0.15, hy - 0.15, 0.15, H - 0.15, mat)
        corrugated_wall(-hy + 0.15, hy - 0.15, x + (0.03 if x > 0 else -0.03), 0.15, H - 0.15, mat, along='Y')
    box(-hx + 0.12, hx - 0.12, hy - 0.04, hy - 0.016, 0.15, H - 0.15, mat)                       # fundo
    corrugated_wall(-hx + 0.15, hx - 0.15, hy, 0.15, H - 0.15, mat)
    for c in (0.0,):                                                                           # cantoneiras de canto (twistlock)
        for x in (-hx + 0.075, hx - 0.075):
            for y in (-hy + 0.075, hy - 0.075):
                box(x - 0.09, x + 0.09, y - 0.09, y + 0.09, H - 0.02, H + 0.01, M['steel'])
    if open_front:
        # uma porta escancarada, encostada na lateral de fora (+X)
        box(hx + 0.02, hx + 0.06, -hy - 1.2, -hy - 0.02, 0.1, H - 0.1, mat)
        for k in range(4):
            box(hx + 0.06, hx + 0.08, -hy - 1.1 + k * 0.28, -hy - 1.0 + k * 0.28, 0.15, H - 0.15, mat)
        cyl(hx + 0.1, -hy - 0.6, H / 2, 0.022, H - 0.4, M['steel'], verts=10)
    else:
        box(-hx + 0.15, hx - 0.15, -hy + 0.015, -hy + 0.04, 0.15, H - 0.15, mat)                 # 2 portas
        box(-0.006, 0.006, -hy - 0.002, -hy + 0.016, 0.15, H - 0.15, M['black'])
        for x in (-0.85, -0.3, 0.3, 0.85):                                                      # barras de trava
            cyl(x, -hy - 0.03, H / 2, 0.022, H - 0.25, M['steel'], verts=10)
            box(x - 0.06, x + 0.06, -hy - 0.06, -hy, 1.0, 1.08, M['steel'])
        K.text("TDLU 482913 4", -0.62, -hy - 0.005, 2.2, 0.11, M['stencil'], rot=(math.pi / 2, 0, 0))
        K.text("MAX GROSS 30480 KG", -0.62, -hy - 0.005, 1.95, 0.06, M['stencil'], rot=(math.pi / 2, 0, 0))

def build_cargo():
    M = mats(); parts = {}
    s = len(K.PARTS); container(M, M['contred'], True); parts["ContainerOpen"] = K.PARTS[s:]
    s = len(K.PARTS); container(M, M['contblue'], False); parts["ContainerClosed"] = K.PARTS[s:]
    # ---------- Barrel: tambor 200 L (r 0,29, h 0,88)
    s = len(K.PARTS)
    cyl(0, 0, 0.44, 0.29, 0.88, M['rustbare'], verts=32)
    for z in (0.01, 0.29, 0.59, 0.87): torus(0, 0, z, 0.29, 0.012, M['rustbare'], maj=32, mnr=6)
    cyl(0.15, 0.05, 0.885, 0.03, 0.012, M['steel'], verts=12)
    parts["Barrel"] = K.PARTS[s:]
    # ---------- Crate: caixote de ripas 0,8 m
    s = len(K.PARTS)
    S = 0.8; h = S / 2
    for k in range(5):                                                                          # ripas das faces
        z0 = 0.02 + k * 0.158
        for (a, b, c, d) in ((-h, h, -h, -h + 0.02), (-h, h, h - 0.02, h)):
            box(a, b, c, d, z0, z0 + 0.13, M['wood'])
        for (a, b) in ((-h, -h + 0.02), (h - 0.02, h)):
            box(a, b, -h, h, z0, z0 + 0.13, M['wood'])
    for x in (-h, h - 0.06):
        for y in (-h - 0.01, h - 0.05):
            box(x, x + 0.06, y, y + 0.06, 0, S, M['wood'])
    for k in range(5): box(-h, h, -h + k * 0.16, -h + k * 0.16 + 0.13, S - 0.02, S, M['wood'])
    K.text("FRÁGIL", 0, -h - 0.012, 0.45, 0.09, M['black'], rot=(math.pi / 2, 0, 0))
    parts["Crate"] = K.PARTS[s:]
    # ---------- Locker: 0,9 x 0,7 x 2,1, frente aberta, porta escancarada para +X
    s = len(K.PARTS)
    Wl, Dl, Hl = 0.9, 0.7, 2.1; t = 0.02
    box(-Wl / 2, -Wl / 2 + t, -Dl / 2, Dl / 2, 0.08, Hl, M['lockergrey'])
    box(Wl / 2 - t, Wl / 2, -Dl / 2, Dl / 2, 0.08, Hl, M['lockergrey'])
    box(-Wl / 2, Wl / 2, Dl / 2 - t, Dl / 2, 0.08, Hl, M['lockergrey'])
    box(-Wl / 2, Wl / 2, -Dl / 2, Dl / 2, Hl - t, Hl, M['lockergrey'])
    box(-Wl / 2, Wl / 2, -Dl / 2, Dl / 2, 0.08, 0.1, M['lockergrey'])
    box(-Wl / 2 + 0.03, Wl / 2 - 0.03, -Dl / 2 + 0.03, Dl / 2 - 0.03, 0, 0.08, M['black'])       # rodapé
    box(-Wl / 2 + t, Wl / 2 - t, -Dl / 2 + 0.05, Dl / 2 - t, 1.75, 1.77, M['lockergrey'])         # prateleira
    cyl(0, 0.05, 1.6, 0.01, Wl - 0.05, M['steel'], axis='X', verts=8)                               # cabideiro
    door_x = Wl / 2 + 0.01
    box(door_x, door_x + 0.02, -Dl / 2 - Wl + 0.04, -Dl / 2 - 0.02, 0.1, Hl - 0.02, M['lockergrey'])  # porta aberta 90°
    for k in range(6):                                                                               # aletas de ventilação
        z = 1.65 + k * 0.04
        box(door_x - 0.004, door_x, -Dl / 2 - 0.7, -Dl / 2 - 0.25, z, z + 0.015, M['black'])
    box(door_x - 0.03, door_x, -Dl / 2 - Wl + 0.12, -Dl / 2 - Wl + 0.15, 0.95, 1.15, M['steel'])     # puxador
    K.text("17", door_x - 0.002, -Dl / 2 - 0.47, 1.45, 0.08, M['stencil'], rot=(math.pi / 2, 0, -math.pi / 2))
    parts["Locker"] = K.PARTS[s:]
    # ---------- Bunk: beliche 2,0 x 0,9 x 1,7
    s = len(K.PARTS)
    for x in (-1.0, 0.96):
        for y in (-0.45, 0.41):
            box(x, x + 0.04, y, y + 0.04, 0, 1.7, M['lockergrey'])
    for z in (0.35, 1.25):
        box(-1.0, 1.0, -0.45, -0.41, z, z + 0.05, M['lockergrey'])
        box(-1.0, 1.0, 0.41, 0.45, z, z + 0.05, M['lockergrey'])
        for k in range(9): box(-0.95 + k * 0.23, -0.93 + k * 0.23, -0.41, 0.41, z, z + 0.02, M['steel'])
        box(-0.95, 0.95, -0.4, 0.4, z + 0.05, z + 0.17, M['fabric'], bevel=0.04, seg=3)
    for x in (-1.0, 0.96):
        for z in (0.9, 1.5): box(x, x + 0.04, -0.45, 0.45, z, z + 0.03, M['lockergrey'])
    parts["Bunk"] = K.PARTS[s:]
    return group(parts)


# =================================================================== MATERIAIS EXTRAS (dressing, 2026-11-26)
import mathutils

def mats_extra(M):
    M['orange'] = K.material("SafetyOrange", lin((0.85, 0.32, 0.06)), rough=0.6, dirt=0.6, dirt_color=lin((0.2, 0.12, 0.06)), rust=0.25, streaks=0.3, color_var=0.1)
    M['red'] = K.material("ExtRed", lin((0.55, 0.06, 0.04)), rough=0.4, metal=0.2, dirt=0.6, wear=0.5, wear_color=lin((0.25, 0.22, 0.2)), rust=0.5)
    M['white'] = K.material("SignWhite", lin((0.82, 0.80, 0.74)), rough=0.6, dirt=0.7, rust=0.45, streaks=0.5)
    M['signyellow'] = K.material("SignYellow", lin((0.80, 0.62, 0.08)), rough=0.6, dirt=0.6, rust=0.45, streaks=0.4)
    M['signgreen'] = K.material("SignGreen", lin((0.05, 0.38, 0.16)), rough=0.6, dirt=0.6, rust=0.4, streaks=0.4)
    M['rope'] = K.material("Rope", lin((0.55, 0.46, 0.30)), rough=0.95, dirt=0.7, dirt_color=lin((0.2, 0.15, 0.08)), bump=0.6, bump_scale=120.0)
    M['tarp'] = K.material("Tarp", lin((0.22, 0.27, 0.18)), rough=0.85, dirt=0.7, dirt_color=lin((0.14, 0.12, 0.08)), color_var=0.15, bump=0.4, bump_scale=30.0)
    M['sack'] = K.material("Burlap", lin((0.46, 0.38, 0.25)), rough=0.95, dirt=0.8, color_var=0.15, bump=0.5, bump_scale=90.0)
    M['paper'] = K.material("OldPaper", lin((0.75, 0.70, 0.56)), rough=0.9, dirt=0.6, dirt_color=lin((0.35, 0.28, 0.16)), color_var=0.12)
    M['bottle'] = K.material("BottleGlass", lin((0.10, 0.22, 0.10)), rough=0.15, dirt=0.4)
    M['bakelite'] = K.material("Bakelite", lin((0.12, 0.08, 0.05)), rough=0.35, dirt=0.5)
    M['lampglass'] = K.material("LampGlassOff", lin((0.55, 0.55, 0.50)), rough=0.25, dirt=0.8, dirt_color=lin((0.2, 0.18, 0.12)))
    M['grate'] = K.material("Grating", lin((0.18, 0.18, 0.17)), rough=0.5, metal=0.6, dirt=0.6, rust=0.6)
    return M

def stencil(s, x, y, z, size, mat):
    return K.text(s, x, y, z, size, mat, rot=(math.pi / 2, 0, 0))

def xform(objs, m):
    # primitivos (cilindro, anel) têm origem onde nasceram: leva a posição para a malha antes de girar no mundo
    for o in objs:
        o.data.transform(mathutils.Matrix.Translation(o.location)); o.location = (0, 0, 0)
        o.data.transform(m)

# =================================================================== STRUCTURE
def build_structure():
    M = mats_extra(mats()); parts = {}
    # BulkheadDoor: porta estanque 0,9 x 1,7 em moldura 1,3 x 2,2 (frente -Y), entreaberta 35° na dobradiça (x=-0,45)
    s = len(K.PARTS)
    box(-0.65, -0.45, -0.1, 0.1, 0, 2.2, M['hulldark'], bevel=0.02)
    box(0.45, 0.65, -0.1, 0.1, 0, 2.2, M['hulldark'], bevel=0.02)
    box(-0.65, 0.65, -0.1, 0.1, 2.0, 2.2, M['hulldark'], bevel=0.02)
    box(-0.65, 0.65, -0.1, 0.1, 0, 0.25, M['hulldark'], bevel=0.02)
    for z in (0.6, 1.6): cyl(-0.47, -0.12, z, 0.05, 0.22, M['oily'], verts=14)
    s2 = len(K.PARTS)
    box(-0.45, 0.45, -0.03, 0.03, 0.27, 1.98, M['hull'], bevel=0.03)
    box(-0.41, 0.41, -0.06, -0.03, 0.31, 1.94, M['hulldark'], bevel=0.01)
    torus(0.0, -0.1, 1.12, 0.16, 0.02, M['steel'], rot=(math.pi / 2, 0, 0), maj=24, mnr=6)
    cyl(0.0, -0.08, 1.12, 0.03, 0.06, M['steel'], axis='Y', verts=12)
    for a in range(4):
        ang = a * math.pi / 2
        box(0.15 * math.cos(ang) - 0.012, 0.15 * math.cos(ang) + 0.012, -0.11, -0.09, 1.12 + 0.15 * math.sin(ang) - 0.012, 1.12 + 0.15 * math.sin(ang) + 0.012, M['steel'])
    for zz in (0.5, 1.75): box(0.33, 0.45, -0.08, -0.03, zz - 0.04, zz + 0.04, M['steel'])
    T = mathutils.Matrix.Translation((-0.45, -0.1, 0)) @ mathutils.Matrix.Rotation(math.radians(-35), 4, 'Z') @ mathutils.Matrix.Translation((0.45, 0.1, 0))
    xform(K.PARTS[s2:], T)
    stencil("ESTANQUE", 0.0, -0.105, 2.08, 0.08, M['stencil'])
    parts["BulkheadDoor"] = K.PARTS[s:]
    # Porthole: escotilha redonda (de parede, frente -Y, origem no centro de trás)
    s = len(K.PARTS)
    torus(0, -0.03, 0.0, 0.24, 0.035, M['brass'], rot=(math.pi / 2, 0, 0), maj=32, mnr=8)
    cyl(0, -0.02, 0.0, 0.215, 0.012, M['bottle'], axis='Y', verts=32)
    for k in range(8):
        a = k * math.pi / 4
        cyl(0.3 * math.cos(a), -0.02, 0.3 * math.sin(a), 0.016, 0.03, M['brass'], axis='Y', verts=8)
    box(0.26, 0.34, -0.06, -0.01, -0.04, 0.04, M['brass'])
    parts["Porthole"] = K.PARTS[s:]
    # WallLadder: escada de parede 3,2 m
    s = len(K.PARTS)
    for x in (-0.22, 0.22): box(x - 0.025, x + 0.025, -0.02, 0.02, 0, 3.2, M['steel'])
    for k in range(11): cyl(0, 0, 0.25 + k * 0.28, 0.014, 0.44, M['steel'], axis='X', verts=8)
    for z in (0.4, 2.8):
        for x in (-0.22, 0.22): box(x - 0.02, x + 0.02, 0.02, 0.15, z - 0.02, z + 0.02, M['steel'])
    parts["WallLadder"] = K.PARTS[s:]
    # SteelStairs: 8 degraus, sobe 2,4 m em 3 m (+Y), 1 m de largura, corrimão dos dois lados
    s = len(K.PARTS)
    L, H = 3.0, 2.4; ang = math.atan2(H, L); Ls = math.hypot(L, H)
    for x in (-0.52, 0.48):
        b = box(x, x + 0.04, 0, Ls, -0.18, 0.0, M['hulldark'])
        b.data.transform(mathutils.Matrix.Rotation(ang, 4, 'X'))
    for k in range(8):
        y = (k + 0.5) * L / 8; z = (k + 1) * H / 8
        box(-0.48, 0.48, y - 0.17, y + 0.17, z - 0.03, z, M['grate'])
    for x in (-0.5, 0.5):
        for k in range(4):
            y = k * L / 3; z = y * H / L
            cyl(x, y, z + 0.5, 0.018, 1.0, M['steel'], verts=8)
        r = box(x - 0.02, x + 0.02, 0, Ls, -0.02, 0.02, M['steel'])
        r.data.transform(mathutils.Matrix.Translation((0, 0, 1.0)) @ mathutils.Matrix.Rotation(ang, 4, 'X'))   # passa no topo dos postes
    parts["SteelStairs"] = K.PARTS[s:]
    # CatwalkRail: guarda-corpo 2 m
    s = len(K.PARTS)
    for x in (-1.0, 0.0, 1.0): cyl(x, 0, 0.55, 0.022, 1.1, M['signyellow'], verts=10)
    for z in (0.55, 1.08): cyl(0, 0, z, 0.02, 2.0, M['signyellow'], axis='X', verts=10)
    box(-1.0, 1.0, -0.004, 0.004, 0.0, 0.12, M['signyellow'])
    parts["CatwalkRail"] = K.PARTS[s:]
    # WallLamp: luminária de antepara com gaiola, apagada
    s = len(K.PARTS)
    box(-0.11, 0.11, -0.04, 0.0, -0.11, 0.11, M['hulldark'], bevel=0.01)
    cyl(0, -0.1, 0, 0.075, 0.12, M['lampglass'], axis='Y', verts=20)
    for k in range(6):
        a = k * math.pi / 3
        box(0.085 * math.cos(a) - 0.005, 0.085 * math.cos(a) + 0.005, -0.17, -0.04, 0.085 * math.sin(a) - 0.005, 0.085 * math.sin(a) + 0.005, M['steel'])
    torus(0, -0.17, 0, 0.085, 0.006, M['steel'], rot=(math.pi / 2, 0, 0), maj=20, mnr=5)
    parts["WallLamp"] = K.PARTS[s:]
    # VentDuct: duto 0,5 x 0,35, 3 m (X)
    s = len(K.PARTS)
    box(-1.5, 1.5, -0.25, 0.25, -0.175, 0.175, M['galv'], bevel=0.01)
    for x in (-1.0, 0.0, 1.0): box(x - 0.02, x + 0.02, -0.27, 0.27, -0.195, 0.195, M['steel'])
    box(1.2, 1.5, -0.26, -0.24, -0.12, 0.12, M['black'])
    for k in range(5): box(1.22 + k * 0.055, 1.24 + k * 0.055, -0.27, -0.25, -0.11, 0.11, M['steel'])
    parts["VentDuct"] = K.PARTS[s:]
    # BreakerPanel: quadro de disjuntores aberto
    s = len(K.PARTS)
    box(-0.3, 0.3, -0.18, 0.0, -0.45, 0.45, M['lockergrey'], bevel=0.01)
    box(-0.27, 0.27, -0.181, -0.17, -0.42, 0.42, M['black'])
    for r_ in range(4):
        for c_ in range(6):
            box(-0.22 + c_ * 0.085, -0.17 + c_ * 0.085, -0.2, -0.17, 0.3 - r_ * 0.2, 0.36 - r_ * 0.2, M['bakelite'])
            dz = 0.02 if (r_ + c_) % 3 else -0.02
            box(-0.2 + c_ * 0.085, -0.19 + c_ * 0.085, -0.22, -0.2, 0.32 - r_ * 0.2 + dz, 0.34 - r_ * 0.2 + dz, M['white'])
    box(0.3, 0.32, -0.75, -0.18, -0.45, 0.45, M['lockergrey'])
    cyl(0, -0.09, 0.7, 0.025, 0.5, M['steel'], verts=10)
    parts["BreakerPanel"] = K.PARTS[s:]
    # ValvePipe: cano vertical 3 m com válvula e manômetro
    s = len(K.PARTS)
    cyl(0, 0, 1.5, 0.07, 3.0, M['rustbare'], verts=16)
    for z in (0.9, 2.1): cyl(0, 0, z, 0.1, 0.05, M['steel'], verts=16)
    cyl(0, -0.12, 1.3, 0.05, 0.16, M['steel'], axis='Y', verts=12)
    torus(0, -0.22, 1.3, 0.13, 0.016, M['red'], rot=(math.pi / 2, 0, 0), maj=24, mnr=6)
    cyl(0.12, -0.03, 1.65, 0.06, 0.03, M['steel'], axis='Y', verts=18)
    cyl(0.12, -0.05, 1.65, 0.05, 0.006, M['gauge'], axis='Y', verts=18)
    parts["ValvePipe"] = K.PARTS[s:]
    return group(parts)

# =================================================================== DECK (carga e convés)
def build_deck():
    M = mats_extra(mats()); parts = {}
    s = len(K.PARTS)
    torus(0, -0.06, 0, 0.3, 0.07, M['orange'], rot=(math.pi / 2, 0, 0), maj=36, mnr=12)
    for k in range(4):
        a = k * math.pi / 2 + math.pi / 4
        torus(0.3 * math.cos(a), -0.06, 0.3 * math.sin(a), 0.073, 0.012, M['white'], rot=(0, -a, 0), maj=14, mnr=4)
    stencil("ESTRELA DO SUL", 0, -0.135, 0.0, 0.04, M['black'])
    cyl(0, 0, 0.42, 0.01, 0.08, M['steel'], axis='Y', verts=8)
    parts["LifeRing"] = K.PARTS[s:]
    s = len(K.PARTS)
    box(-0.25, 0.25, -0.08, 0.0, -0.3, 0.25, M['orange'], bevel=0.05, seg=3)
    box(-0.06, 0.06, -0.09, -0.07, -0.3, 0.25, M['black'])
    for z in (-0.15, 0.05): box(-0.25, 0.25, -0.09, -0.08, z, z + 0.03, M['white'])
    cyl(0, 0, 0.32, 0.01, 0.1, M['steel'], axis='Y', verts=8)
    parts["LifeJacket"] = K.PARTS[s:]
    s = len(K.PARTS)
    cyl(0, 0, 0.3, 0.08, 0.55, M['red'], verts=20, bevel=0.01)
    cone(0, 0, 0.6, 0.08, 0.04, 0.06, M['red'], verts=20)
    cyl(0, 0, 0.66, 0.025, 0.06, M['steel'], verts=12)
    box(-0.06, 0.06, -0.015, 0.015, 0.68, 0.71, M['black'])
    cyl(0.07, -0.02, 0.5, 0.012, 0.35, M['black'], verts=8)
    box(-0.1, 0.1, 0.08, 0.1, 0.15, 0.45, M['steel'])
    parts["FireExtinguisher"] = K.PARTS[s:]
    s = len(K.PARTS)
    for k in range(6): torus(0, 0, 0.03 + k * 0.035, 0.32 - (k % 2) * 0.02, 0.025, M['rope'], maj=28, mnr=6)
    parts["RopeCoil"] = K.PARTS[s:]
    s = len(K.PARTS)
    rnd = random.Random(3)
    for k in range(40):
        a = rnd.random() * math.tau; rr = rnd.random() * 0.35; z = 0.03 + rnd.random() * 0.12 * (1 - rr / 0.35)
        torus(rr * math.cos(a), rr * math.sin(a), z, 0.045, 0.012, M['oily'], rot=(rnd.random() * 3, rnd.random() * 3, rnd.random() * 3), maj=10, mnr=4)
    parts["ChainPile"] = K.PARTS[s:]
    s = len(K.PARTS)
    for k in range(22):
        torus(0, 0, -0.045 - k * 0.075, 0.045, 0.012, M['oily'], rot=(math.pi / 2, 0, (k % 2) * math.pi / 2), maj=10, mnr=4)
    hook = torus(0, 0, -1.82, 0.08, 0.02, M['steel'], rot=(math.pi / 2, 0, 0), maj=16, mnr=6)
    cyl(0, 0, 0.0, 0.05, 0.03, M['steel'], verts=10)
    parts["HangingChain"] = K.PARTS[s:]
    s = len(K.PARTS)
    for k in range(7): box(-0.6, 0.6, -0.5 + k * 0.155, -0.5 + k * 0.155 + 0.1, 0.122, 0.144, M['wood'])
    for y in (-0.45, 0, 0.45): box(-0.6, 0.6, y - 0.05, y + 0.05, 0.0, 0.122, M['wood'])
    parts["Pallet"] = K.PARTS[s:]
    s = len(K.PARTS)
    for (x, y, z, rz) in ((-0.3, 0, 0, 5), (0.3, 0.02, 0, -8), (0, 0, 0.2, 12), (-0.05, 0.42, 0, 80)):
        # saco de estopa: um "travesseiro" (esfera achatada, cintura apertada nas pontas)
        bpy.ops.mesh.primitive_uv_sphere_add(segments=20, ring_count=12, radius=1.0)
        b = bpy.context.active_object
        for v in b.data.vertices:
            v.co.x *= 0.33; v.co.y *= 0.21 * (1.0 - 0.25 * abs(v.co.x / 0.33) ** 3); v.co.z = v.co.z * 0.13 * (1.0 - 0.3 * abs(v.co.x / 0.33) ** 2)
            v.co.z = max(v.co.z, -0.1)
        b.data.transform(mathutils.Matrix.Translation((x, y, z + 0.1)) @ mathutils.Matrix.Rotation(math.radians(rz), 4, 'Z'))
        K._finish(b, M['sack'])
    parts["SackPile"] = K.PARTS[s:]
    s = len(K.PARTS)
    box(-0.6, 0.6, -0.5, 0.5, 0, 1.0, M['wood'], bevel=0.01)
    me = bpy.data.meshes.new("tarp"); bm = bmesh.new()
    bmesh.ops.create_grid(bm, x_segments=30, y_segments=26, size=0.95)
    for v in bm.verts:
        x, y = v.co.x, v.co.y * 0.88
        dx = max(0.0, abs(x) - 0.625); dy = max(0.0, abs(y) - 0.525); d = math.hypot(dx, dy)   # o topo passa da borda do caixote
        if d <= 0: z = 1.03 + 0.012 * math.sin(x * 9) * math.cos(y * 7)
        else:
            z = 1.03 - d * 9.0 + 0.025 * math.sin(x * 17 + y * 11)
            if dx > 0: x = math.copysign(0.63 + min(dx, 0.3) * 0.08, x)
            if dy > 0: y = math.copysign(0.53 + min(dy, 0.3) * 0.08, y)
        v.co = Vector((x, y, max(z, 0.06 + 0.04 * math.sin(x * 23 + y * 19))))
    bmesh.ops.solidify(bm, geom=bm.faces[:], thickness=0.01)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    bm.to_mesh(me); bm.free()
    ob = bpy.data.objects.new("tarp", me); bpy.context.collection.objects.link(ob); K._finish(ob, M['tarp'])
    parts["TarpCrate"] = K.PARTS[s:]
    s = len(K.PARTS)
    box(-0.25, 0.25, -0.11, 0.11, 0, 0.2, M['red'], bevel=0.01)
    box(-0.25, 0.25, -0.11, 0.11, 0.2, 0.26, M['red'], bevel=0.01)
    box(-0.12, 0.12, -0.015, 0.015, 0.26, 0.3, M['black'])
    parts["Toolbox"] = K.PARTS[s:]
    s = len(K.PARTS)
    box(-0.15, 0.12, -0.012, 0.012, 0, 0.012, M['steel'])
    torus(0.15, 0, 0.006, 0.03, 0.01, M['steel'], maj=12, mnr=4)
    box(-0.2, -0.15, -0.025, 0.025, 0, 0.014, M['steel'])
    parts["Wrench"] = K.PARTS[s:]
    s = len(K.PARTS)
    cyl(0, 0, 0.12, 0.08, 0.24, M['contblue'], verts=20)
    cyl(0.03, 0, 0.255, 0.015, 0.03, M['steel'], verts=8)
    parts["OilCan"] = K.PARTS[s:]
    s = len(K.PARTS)
    rnd = random.Random(9)
    for k in range(3):
        b = box(-0.8, 0.8, -0.07, 0.07, -0.015, 0.015, M['floorwood'])
        b.data.transform(mathutils.Matrix.Translation((rnd.uniform(-0.5, 0.5), rnd.uniform(-0.6, 0.6), 0)) @ mathutils.Matrix.Rotation(rnd.uniform(0, 3.1), 4, 'Z'))
    parts["FloatingPlanks"] = K.PARTS[s:]
    return group(parts)

# =================================================================== CREW (tripulação e sinalização)
def build_crew():
    M = mats_extra(mats()); parts = {}
    s = len(K.PARTS)
    box(-1.0, 1.0, -0.4, 0.4, 0.72, 0.76, M['lockergrey'], bevel=0.01)
    for x in (-0.9, 0.9):
        for y in (-0.32, 0.32): box(x - 0.025, x + 0.025, y - 0.025, y + 0.025, 0, 0.72, M['steel'])
    for y in (-0.75, 0.75):
        box(-1.0, 1.0, y - 0.15, y + 0.15, 0.44, 0.47, M['wood'])
        for x in (-0.9, 0.9): box(x - 0.02, x + 0.02, y - 0.02, y + 0.02, 0, 0.44, M['steel'])
    for (x, y) in ((-0.4, 0.1), (0.3, -0.15), (0.6, 0.2)): cyl(x, y, 0.81, 0.04, 0.1, M['galv'], verts=14)
    parts["MessTable"] = K.PARTS[s:]
    s = len(K.PARTS)
    for x in (-0.2, 0.2):
        for y in (-0.2, 0.2): box(x - 0.02, x + 0.02, y - 0.02, y + 0.02, 0, 0.45, M['steel'])
    box(-0.22, 0.22, -0.22, 0.22, 0.45, 0.48, M['wood'])
    for x in (-0.2, 0.2): box(x - 0.02, x + 0.02, 0.18, 0.22, 0.48, 0.9, M['steel'])
    box(-0.22, 0.22, 0.18, 0.22, 0.7, 0.88, M['wood'])
    xform(K.PARTS[s:], mathutils.Matrix.Translation((0, 0, 0.22)) @ mathutils.Matrix.Rotation(math.radians(90), 4, 'Y') @ mathutils.Matrix.Translation((0, 0, -0.22)))
    parts["ChairTipped"] = K.PARTS[s:]
    s = len(K.PARTS)
    box(-0.6, 0.6, -0.3, 0.3, 0.72, 0.76, M['wood'])
    for x in (-0.55, 0.55):
        for y in (-0.25, 0.25): box(x - 0.025, x + 0.025, y - 0.025, y + 0.025, 0, 0.72, M['wood'])
    box(-0.25, 0.25, -0.12, 0.12, 0.76, 1.04, M['bakelite'], bevel=0.02)
    box(-0.2, 0.05, -0.125, -0.12, 0.84, 0.99, M['paper'])
    for x in (0.1, 0.18): cyl(x, -0.13, 0.9, 0.025, 0.03, M['steel'], axis='Y', verts=12)
    cyl(0.2, 0.0, 1.15, 0.006, 0.25, M['steel'], verts=6)
    box(-0.5, -0.3, -0.1, 0.1, 0.76, 0.78, M['paper'])
    parts["RadioDesk"] = K.PARTS[s:]
    s = len(K.PARTS)
    box(-0.18, 0.18, -0.006, 0.0, -0.25, 0.25, M['paper'])
    stencil("MARÇO", 0, -0.008, 0.18, 0.05, M['black'])
    for r_ in range(4):
        for c_ in range(6):
            box(-0.15 + c_ * 0.05, -0.12 + c_ * 0.05, -0.009, -0.007, 0.08 - r_ * 0.07, 0.1 - r_ * 0.07, M['bakelite'] if (r_ * 6 + c_) < 17 else M['red'])
    parts["Calendar"] = K.PARTS[s:]
    s = len(K.PARTS)
    box(-0.12, 0.12, -0.01, 0.0, -0.17, 0.17, M['wood'])
    box(-0.1, 0.1, -0.012, -0.01, -0.15, 0.12, M['paper'])
    box(-0.04, 0.04, -0.02, -0.01, 0.12, 0.16, M['steel'])
    parts["Clipboard"] = K.PARTS[s:]
    for nm, txt, mat, txtmat, w in (("SignEngine", "CASA DE MÁQUINAS", 'signyellow', 'black', 0.9), ("SignDanger", "PERIGO  ALTA TENSÃO", 'signyellow', 'black', 0.9),
                                    ("SignExit", "SAÍDA DE EMERGÊNCIA", 'signgreen', 'white', 0.9), ("SignHold", "PORÃO 1", 'white', 'black', 0.6),
                                    ("SignQuarters", "ALOJAMENTO", 'white', 'black', 0.7)):
        s = len(K.PARTS)
        box(-w / 2, w / 2, -0.008, 0.0, -0.1, 0.1, M[mat], bevel=0.004)
        stencil(txt, 0, -0.011, 0.0, 0.055 if len(txt) > 12 else 0.08, M[txtmat])
        for x in (-w / 2 + 0.03, w / 2 - 0.03): cyl(x, -0.009, 0.06, 0.008, 0.004, M['steel'], axis='Y', verts=6)
        parts[nm] = K.PARTS[s:]
    s = len(K.PARTS)
    rnd = random.Random(4)
    for k in range(5):
        x, y = rnd.uniform(-0.3, 0.3), rnd.uniform(-0.3, 0.3)
        if k < 3:
            cyl(x, y, 0.11, 0.035, 0.22, M['bottle'], verts=12); cyl(x, y, 0.26, 0.013, 0.08, M['bottle'], verts=8)
        else:
            b = cyl(0, 0, 0.0, 0.035, 0.22, M['bottle'], axis='X', verts=12)
            b.data.transform(mathutils.Matrix.Translation((x, y, 0.035)) @ mathutils.Matrix.Rotation(rnd.uniform(0, 3), 4, 'Z'))
    parts["Bottles"] = K.PARTS[s:]
    return group(parts)

ALL = {"machines": build_machines, "cell": build_cell, "cargo": build_cargo,
       "structure": build_structure, "deck": build_deck, "crew": build_crew}[GROUP]()

# ------------------------------------------------------------------ prévias
def _scene_setup(w=1600, h=700):
    sc = bpy.context.scene; sc.render.engine = 'CYCLES'; sc.cycles.samples = 28; sc.cycles.use_denoising = True
    sc.render.resolution_x, sc.render.resolution_y = w, h
    sc.view_settings.view_transform = 'Standard'
    wd = bpy.data.worlds.new("w"); sc.world = wd; wd.use_nodes = True
    wd.node_tree.nodes['Background'].inputs['Color'].default_value = (0.30, 0.31, 0.33, 1)
    wd.node_tree.nodes['Background'].inputs['Strength'].default_value = 0.75
    bpy.ops.object.light_add(type='SUN', rotation=(math.radians(55), 0, math.radians(25))); bpy.context.active_object.data.energy = 3.2
    return sc

def preview_lineup():
    sc = _scene_setup()
    x = 0.0; maxh = 0.0
    for o in ALL:
        d = o.dimensions; bb = [o.matrix_world @ Vector(c) for c in o.bound_box]
        o.location.x += x - min(v.x for v in bb); o.location.z -= min(v.z for v in bb)
        x += d.x + 0.25; maxh = max(maxh, d.z)
    bpy.ops.object.camera_add(); cam = bpy.context.active_object; sc.camera = cam
    cam.data.type = 'ORTHO'; cam.data.ortho_scale = max(x, maxh * 2.3) * 1.04
    for tag, ang in (("frente", 0.0), ("tres_quartos", 35.0)):
        a = math.radians(ang); cx = x / 2
        cam.location = (cx + 30 * math.sin(a), -30 * math.cos(a), maxh * 0.5 + 6 * math.sin(a))
        cam.rotation_euler = (math.radians(90 - 11 * math.sin(a)), 0, a)
        sc.render.filepath = os.path.join(PREV_DIR, f"{GROUP}_{tag}.png"); bpy.ops.render.render(write_still=True)
    for o in ALL: o.location = (0, 0, 0)
    bpy.data.objects.remove(cam)

HEROES = {"machines": ["Generator", "GateLeaf", "EngineBlock"], "cell": ["CellDoor", "Bucket", "LoosePanel"],
          "cargo": ["ContainerOpen", "Locker", "Crate"],
          "structure": ["BulkheadDoor", "SteelStairs", "BreakerPanel"], "deck": ["LifeRing", "TarpCrate", "ChainPile"],
          "crew": ["MessTable", "RadioDesk", "SignDanger"]}[GROUP]

def preview_heroes():
    sc = bpy.context.scene; sc.render.resolution_x, sc.render.resolution_y = 600, 600
    keep = {o.name: o.hide_render for o in ALL}
    bpy.ops.object.camera_add(); cam = bpy.context.active_object; sc.camera = cam
    for name in HEROES:
        hero = next(o for o in ALL if o.name == name)
        for o in ALL: o.hide_render = (o is not hero) and not (name == "Generator" and o.name == "GeneratorLamp")
        bb = [hero.matrix_world @ Vector(c) for c in hero.bound_box]
        c = sum(bb, Vector()) / 8; r = max((v - c).length for v in bb)
        for i, (lbl, ang) in enumerate((("frente", 0), ("direita", 90), ("tras", 180), ("esquerda", 270))):
            a = math.radians(ang + 25)
            cam.data.type = 'PERSP'; cam.data.lens = 50
            dist = r * 3.4
            cam.location = (c.x + dist * math.sin(a), c.y - dist * math.cos(a), c.z + dist * 0.35)
            d = c - cam.location; cam.rotation_euler = d.to_track_quat('-Z', 'Y').to_euler()
            sc.render.filepath = os.path.join(PREV_DIR, f"{GROUP}_{name}_{i}{lbl}.png"); bpy.ops.render.render(write_still=True)
    for o in ALL: o.hide_render = keep[o.name]
    bpy.data.objects.remove(cam)

if PREV_DIR:
    os.makedirs(PREV_DIR, exist_ok=True)
    preview_lineup()
    preview_heroes()
    with open(os.path.join(PREV_DIR, f"{GROUP}_info.txt"), "w", encoding="utf-8") as f:
        for o in ALL:
            d = o.dimensions
            f.write(f"{o.name}: {d.x:.2f} x {d.y:.2f} x {d.z:.2f} m, {len(o.data.polygons)} faces\n")
    if PREVIEW_ONLY: sys.exit(0)
    for o in list(bpy.data.objects):
        if o.type in ('LIGHT', 'CAMERA'): bpy.data.objects.remove(o)

# ------------------------------------------------------------------ bake + FBX
for o in list(bpy.data.objects):
    if o not in ALL: bpy.data.objects.remove(o)
# origem de TODAS as peças no zero do mundo (a convenção 'origem no centro da base' vale para a malha; peças que nasceram
# de primitivos — cilindro, cone, anel — tinham a origem no lugar onde foram criadas)
bpy.ops.object.select_all(action='SELECT')
bpy.context.view_layer.objects.active = ALL[0]
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
K.unwrap(ALL, margin=0.003)
bpy.context.scene.cycles.samples = 16
K.bake_all(ALL, TEX_DIR, size=2048)
K.collapse_materials(ALL, GROUP + "Baked")
K.export_fbx(OUT_FBX)
print(f"[build_ship] OK {GROUP}:", OUT_FBX, [(o.name, len(o.data.polygons)) for o in ALL])
