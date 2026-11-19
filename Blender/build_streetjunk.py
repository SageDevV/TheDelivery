"""LIXO E JORNAIS DE RUA para o TheDelivery (Blender, background) — 10 assets realistas, cada um com
geometria por script + materiais procedurais com desgaste assados em PBR (realistic_kit).
A impressão dos jornais vem da textura já assada da folha (Models/Newspaper/.../base_color.png).

Uso: blender --background --python build_streetjunk.py -- <pasta_saida> <base_color_do_jornal.png> [nomes...]
Saída: <pasta_saida>/<Nome>/<Nome>.fbx + <Nome>_textures/*.png
"""
import bpy, bmesh, sys, math, os, random
from mathutils import Vector, noise
sys.path.append(os.path.dirname(os.path.abspath(__file__)))
import realistic_kit as K

argv = sys.argv[sys.argv.index("--") + 1:]
OUT_DIR, NEWS_TEX = argv[0], argv[1]
ONLY = set(argv[2:])
TEX = 1024

# ------------------------------------------------------------------ utilidades
def news_image():
    return bpy.data.images.load(NEWS_TEX, check_existing=True)

def print_material(name, region, *, box=False, dirt=0.4, crumple=0.0):
    """Papel impresso: amostra a textura da folha. region = (u0, u1, v0, v1) do atlas; o mapeamento
    sai das coordenadas de objeto (plano XY -> região) ou em projeção de caixa (box=True)."""
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    nt.nodes.clear()
    out = K._n(nt, 'ShaderNodeOutputMaterial')
    bsdf = K._n(nt, 'ShaderNodeBsdfPrincipled')
    nt.links.new(bsdf.outputs[0], out.inputs['Surface'])
    tc = K._n(nt, 'ShaderNodeTexCoord')
    img = K._n(nt, 'ShaderNodeTexImage')
    img.image = news_image()
    if box:
        img.projection = 'BOX'
        img.projection_blend = 0.3
        mp = K._n(nt, 'ShaderNodeMapping')
        mp.inputs['Scale'].default_value = (2.2, 2.2, 2.2)
        nt.links.new(tc.outputs['Object'], mp.inputs['Vector'])
        nt.links.new(mp.outputs[0], img.inputs['Vector'])
    else:
        u0, u1, v0, v1, w, h = region
        sep = K._n(nt, 'ShaderNodeSeparateXYZ')
        nt.links.new(tc.outputs['Object'], sep.inputs[0])
        comb = K._n(nt, 'ShaderNodeCombineXYZ')
        u = K._math(nt, 'ADD', u0, K._math(nt, 'MULTIPLY', K._math(nt, 'ADD', sep.outputs['X'], w / 2), (u1 - u0) / w))
        v = K._math(nt, 'ADD', v0, K._math(nt, 'MULTIPLY', K._math(nt, 'ADD', sep.outputs['Y'], h / 2), (v1 - v0) / h))
        nt.links.new(u, comb.inputs['X'])
        nt.links.new(v, comb.inputs['Y'])
        nt.links.new(comb.outputs[0], img.inputs['Vector'])
    color = img.outputs['Color']
    ao = K._n(nt, 'ShaderNodeAmbientOcclusion')
    ao.samples = 16
    ao.inputs['Distance'].default_value = 0.05
    cav = K._math(nt, 'MULTIPLY', K._math(nt, 'SUBTRACT', 1.0, ao.outputs['AO']), 1.4 * dirt + crumple, clamp=True)
    color = K._mix_color(nt, cav, color, (0.22, 0.20, 0.16))
    nt.links.new(color, bsdf.inputs['Base Color'])
    bsdf.inputs['Roughness'].default_value = 0.82
    vor = K._n(nt, 'ShaderNodeTexVoronoi')
    vor.feature = 'DISTANCE_TO_EDGE'
    vor.inputs['Scale'].default_value = 30.0
    nt.links.new(tc.outputs['Object'], vor.inputs['Vector'])
    crease = K._math(nt, 'SUBTRACT', 1.0, K._math(nt, 'MULTIPLY', vor.outputs['Distance'], 12.0, clamp=True))
    b = K._n(nt, 'ShaderNodeBump')
    b.inputs['Strength'].default_value = 0.25 + crumple * 0.4
    b.inputs['Distance'].default_value = 0.001
    nt.links.new(crease, b.inputs['Height'])
    nt.links.new(b.outputs['Normal'], bsdf.inputs['Normal'])
    return m

def finish(name, objs, smooth=False):
    ob = K.join(objs, name)
    for p in ob.data.polygons:
        p.use_smooth = smooth
    K.unwrap([ob], margin=0.01)
    d = os.path.join(OUT_DIR, name)
    K.bake_all([ob], os.path.join(d, name + "_textures"), TEX)
    K.collapse_materials([ob])
    K.export_fbx(os.path.join(d, name + ".fbx"))
    print("RESULT %s tris=%d dims=%s" % (name, sum(len(p.vertices) - 2 for p in ob.data.polygons), [round(v, 3) for v in ob.dimensions]))

def displace(ob, strength, size, seed=0):
    tex = bpy.data.textures.new("d", 'CLOUDS')
    tex.noise_scale = size
    tex.noise_depth = 2
    mod = ob.modifiers.new("disp", 'DISPLACE')
    mod.texture = tex
    mod.strength = strength
    mod.texture_coords = 'OBJECT'
    bpy.context.view_layer.objects.active = ob
    ob.select_set(True)
    bpy.ops.object.modifier_apply(modifier=mod.name)

def subdivide(ob, cuts):
    bpy.ops.object.select_all(action='DESELECT')
    ob.select_set(True)
    bpy.context.view_layer.objects.active = ob
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.mesh.subdivide(number_cuts=cuts)
    bpy.ops.object.mode_set(mode='OBJECT')

# Regiões do atlas da folha (u0,u1,v0,v1) — fora: v 0,5..1 (direita = capa); dentro: v 0..0,5
FRONT_TOP = (0.5, 1.0, 0.75, 1.0)      # metade de cima da capa
CLASSIF_TOP = (0.0, 0.5, 0.75, 1.0)    # classificados (contracapa)
INNER_PAGE = (0.0, 0.5, 0.05, 0.45)

# ------------------------------------------------------------------ 1/2) jornal dobrado e pacote
def folded_paper(x, y, z, rot, scale=1.0):
    W, D, T = 0.32 * scale, 0.28 * scale, 0.012
    top = print_material("FoldTop", FRONT_TOP + (W, D))
    bot = print_material("FoldBot", CLASSIF_TOP + (W, D))
    edge = K.material("PaperEdge", (0.78, 0.75, 0.66), 0.85, dirt=0.6, seams=('Z', 0.0016), color_var=0.08)
    parts = []
    body = K.box(-W / 2, W / 2, -D / 2, D / 2, 0, T, edge, bevel=0.002, segments=1)
    parts.append(body)
    pt = K.box(-W / 2 + 0.001, W / 2 - 0.001, -D / 2 + 0.001, D / 2 - 0.001, T, T + 0.0008, top)
    pb = K.box(-W / 2 + 0.001, W / 2 - 0.001, -D / 2 + 0.001, D / 2 - 0.001, -0.0008, 0.0, bot)
    parts += [pt, pb]
    for p in parts:
        subdivide(p, 4)
        for v in p.data.vertices:  # leve ondulação de papel dobrado
            v.co.z += 0.004 * math.sin(v.co.x * 9.0 + y * 3.0) * (abs(v.co.y) / D + 0.3)
        p.rotation_euler = (0, 0, rot)
        p.location = (x, y, z)
    return parts

def build_folded():
    K.reset_scene()
    finish("FoldedNewspaper", folded_paper(0, 0, 0, 0))

def build_bundle():
    K.reset_scene()
    random.seed(7)
    parts = []
    for i in range(5):
        parts += folded_paper(random.uniform(-0.012, 0.012), random.uniform(-0.01, 0.01), i * 0.017,
                              math.radians(random.uniform(-4, 4)) + (math.pi if i % 2 else 0))
    twine = K.material("Twine", (0.55, 0.45, 0.30), 0.9, dirt=0.5, bump=0.5, bump_scale=300)
    H = 5 * 0.017 + 0.004
    for sx in (-0.07, 0.07):  # dois barbantes cruzando o pacote no sentido do comprimento
        parts.append(K.box(sx - 0.004, sx + 0.004, -0.147, 0.147, H, H + 0.003, twine))
        parts.append(K.box(sx - 0.004, sx + 0.004, -0.147, 0.147, -0.004, -0.001, twine))
        for sy in (-0.147, 0.147):
            parts.append(K.box(sx - 0.004, sx + 0.004, sy - 0.003 * (sy < 0), sy + 0.003 * (sy > 0), -0.004, H + 0.003, twine))
    parts.append(K.cyl(0.07, 0.0, H + 0.006, 0.012, 0.008, twine, verts=10))  # nó
    finish("NewspaperBundle", parts)

# ------------------------------------------------------------------ 3) bola de jornal amassado
def build_crumpled():
    K.reset_scene()
    bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=4, radius=0.06)
    ob = bpy.context.active_object
    displace(ob, 0.035, 0.035)
    displace(ob, 0.012, 0.012)
    ob.scale = (1.05, 0.95, 0.85)
    bpy.ops.object.transform_apply(scale=True)
    ob.data.materials.append(print_material("Crumple", None, box=True, dirt=0.5, crumple=0.6))
    K.PARTS.append(ob)
    finish("CrumpledPaper", [ob])

# ------------------------------------------------------------------ 4) pedaço rasgado de página
def build_torn():
    K.reset_scene()
    W, D = 0.30, 0.22
    random.seed(3)
    # Contorno irregular (polígono serrilhado) triangulado em leque, frente e verso
    pts = []
    n = 40
    for i in range(n):
        a = i / n * 2 * math.pi
        r = 1.0 + random.uniform(-0.08, 0.05) + 0.06 * math.sin(a * 7)
        pts.append((math.cos(a) * W / 2 * r * (1.12 if abs(math.cos(a)) > 0.7 else 1.0),
                    math.sin(a) * D / 2 * r))
    front = print_material("TornFront", INNER_PAGE + (W, D), dirt=0.35)
    back = print_material("TornBack", (0.5, 1.0, 0.1, 0.4) + (W, D), dirt=0.35)
    objs = []
    for face_mat, zz, flip in ((front, 0.0002, False), (back, -0.0002, True)):
        me = bpy.data.meshes.new("t")
        bm = bmesh.new()
        c = bm.verts.new((0, 0, zz))
        ring = [bm.verts.new((x, y, zz + 0.003 * math.sin(x * 25))) for x, y in pts]
        for i in range(n):
            f = [c, ring[i], ring[(i + 1) % n]]
            if flip: f.reverse()
            bm.faces.new(f)
        bm.to_mesh(me); bm.free()
        ob = bpy.data.objects.new("t", me)
        bpy.context.collection.objects.link(ob)
        ob.data.materials.append(face_mat)
        subdivide(ob, 2)
        K.PARTS.append(ob)
        objs.append(ob)
    finish("TornPage", objs)

# ------------------------------------------------------------------ 5) lata amassada
def build_can():
    K.reset_scene()
    label = bpy.data.materials.new("CanLabel")
    label.use_nodes = True
    nt = label.node_tree; nt.nodes.clear()
    out = K._n(nt, 'ShaderNodeOutputMaterial'); bsdf = K._n(nt, 'ShaderNodeBsdfPrincipled')
    nt.links.new(bsdf.outputs[0], out.inputs['Surface'])
    sep = K._n(nt, 'ShaderNodeSeparateXYZ'); tc = K._n(nt, 'ShaderNodeTexCoord')
    nt.links.new(tc.outputs['Object'], sep.inputs[0])
    ang = K._math(nt, 'ARCTAN2', sep.outputs['Y'], sep.outputs['X'])
    wave = K._math(nt, 'SINE', K._math(nt, 'ADD', K._math(nt, 'MULTIPLY', ang, 2.0), K._math(nt, 'MULTIPLY', sep.outputs['Z'], 40.0)))
    band = K._math(nt, 'GREATER_THAN', wave, 0.55)
    col = K._mix_color(nt, band, (0.62, 0.04, 0.04), (0.92, 0.92, 0.90))
    scr = K._noise(nt, 60.0, 8.0, tc.outputs['Object'])
    scratch = K._math(nt, 'GREATER_THAN', scr.outputs['Fac'], 0.68)
    col = K._mix_color(nt, scratch, col, (0.70, 0.70, 0.72))
    ao = K._n(nt, 'ShaderNodeAmbientOcclusion'); ao.samples = 16; ao.inputs['Distance'].default_value = 0.02
    col = K._mix_color(nt, K._math(nt, 'MULTIPLY', K._math(nt, 'SUBTRACT', 1.0, ao.outputs['AO']), 1.5, clamp=True), col, (0.15, 0.12, 0.09))
    nt.links.new(col, bsdf.inputs['Base Color'])
    nt.links.new(K._math(nt, 'ADD', 0.25, K._math(nt, 'MULTIPLY', scratch, 0.3)), bsdf.inputs['Roughness'])
    nt.links.new(K._math(nt, 'ADD', 0.55, K._math(nt, 'MULTIPLY', scratch, 0.4)), bsdf.inputs['Metallic'])
    metal = K.material("CanMetal", (0.72, 0.72, 0.74), 0.28, 1.0, dirt=0.6, bump=0.05)
    body = K.cyl(0, 0, 0.06, 0.033, 0.12, label, verts=24)
    subdivide(body, 6)
    top = K.cyl(0, 0, 0.1205, 0.030, 0.003, metal, verts=24)
    bot = K.cyl(0, 0, -0.0005, 0.030, 0.003, metal, verts=24)
    tab = K.box(-0.006, 0.006, 0.004, 0.016, 0.122, 0.1235, metal)
    can = K.join([body, top, bot, tab], "can")
    # Amassado: achata no meio e enruga
    for v in can.data.vertices:
        z = v.co.z
        pinch = math.exp(-((z - 0.055) / 0.03) ** 2)
        r = Vector((v.co.x, v.co.y, 0))
        v.co.x -= r.x * 0.35 * pinch
        v.co.z = z * 0.68 + 0.01 * pinch
        v.co.y += 0.004 * math.sin(v.co.x * 160 + z * 90) * pinch
    displace(can, 0.004, 0.01)
    K.PARTS.clear(); K.PARTS.append(can)
    finish("CrushedCan", [can])

# ------------------------------------------------------------------ 6) garrafa PET
def build_bottle():
    K.reset_scene()
    prof = [(0.000, 0.000), (0.028, 0.000), (0.032, 0.010), (0.033, 0.020), (0.033, 0.140), (0.030, 0.160),
            (0.018, 0.190), (0.013, 0.205), (0.013, 0.215), (0.000, 0.215)]
    me = bpy.data.meshes.new("b")
    bm = bmesh.new()
    seg = 20
    rings = []
    for r, z in prof:
        rings.append([bm.verts.new((r * math.cos(i / seg * 2 * math.pi), r * math.sin(i / seg * 2 * math.pi), z)) for i in range(seg)])
    for a, b in zip(rings[:-1], rings[1:]):
        for i in range(seg):
            bm.faces.new([a[i], a[(i + 1) % seg], b[(i + 1) % seg], b[i]])
    bm.to_mesh(me); bm.free()
    ob = bpy.data.objects.new("bottle", me)
    bpy.context.collection.objects.link(ob)
    plastic = K.material("PET", (0.55, 0.68, 0.58), 0.12, dirt=0.5, ground_dirt=0.0, color_var=0.05, streaks=0.3,
                         dirt_color=(0.30, 0.28, 0.22))
    ob.data.materials.append(plastic)
    K.PARTS.append(ob)
    label = K.material("BottleLabel", (0.08, 0.30, 0.62), 0.4, dirt=0.4, wear=0.4, wear_color=(0.85, 0.85, 0.82))
    lab = K.cyl(0, 0, 0.085, 0.0335, 0.06, label, verts=20)
    cap = K.material("Cap", (0.05, 0.25, 0.65), 0.45, dirt=0.3)
    capo = K.cyl(0, 0, 0.222, 0.015, 0.018, cap, verts=14, bevel=0.002)
    bottle = K.join([ob, lab, capo], "bottle")
    # Meio amassada: achata um lado
    for v in bottle.data.vertices:
        if v.co.z < 0.16:
            v.co.x *= 0.78 + 0.1 * math.sin(v.co.z * 60)
    displace(bottle, 0.003, 0.015)
    K.PARTS.clear(); K.PARTS.append(bottle)
    finish("PlasticBottle", [bottle], smooth=True)

# ------------------------------------------------------------------ 7) copo LANCHES 24H
def build_cup():
    K.reset_scene()
    cupm = bpy.data.materials.new("CupPrint")
    cupm.use_nodes = True
    nt = cupm.node_tree; nt.nodes.clear()
    out = K._n(nt, 'ShaderNodeOutputMaterial'); bsdf = K._n(nt, 'ShaderNodeBsdfPrincipled')
    nt.links.new(bsdf.outputs[0], out.inputs['Surface'])
    tc = K._n(nt, 'ShaderNodeTexCoord'); sep = K._n(nt, 'ShaderNodeSeparateXYZ')
    nt.links.new(tc.outputs['Object'], sep.inputs[0])
    ang = K._math(nt, 'ARCTAN2', sep.outputs['Y'], sep.outputs['X'])
    stripes = K._math(nt, 'GREATER_THAN', K._math(nt, 'SINE', K._math(nt, 'MULTIPLY', ang, 6.0)), 0.0)
    band = K._math(nt, 'MULTIPLY', K._math(nt, 'GREATER_THAN', sep.outputs['Z'], 0.045), K._math(nt, 'LESS_THAN', sep.outputs['Z'], 0.085))
    col = K._mix_color(nt, stripes, (0.90, 0.88, 0.82), (0.70, 0.10, 0.06))
    col = K._mix_color(nt, band, col, (0.92, 0.82, 0.20))
    ao = K._n(nt, 'ShaderNodeAmbientOcclusion'); ao.samples = 16; ao.inputs['Distance'].default_value = 0.03
    col = K._mix_color(nt, K._math(nt, 'MULTIPLY', K._math(nt, 'SUBTRACT', 1.0, ao.outputs['AO']), 1.6, clamp=True), col, (0.30, 0.25, 0.18))
    dn = K._noise(nt, 25.0, 8.0, tc.outputs['Object'])
    col = K._mix_color(nt, K._math(nt, 'MULTIPLY', K._math(nt, 'SUBTRACT', dn.outputs['Fac'], 0.55), 2.0, clamp=True), col, (0.35, 0.30, 0.22))
    nt.links.new(col, bsdf.inputs['Base Color'])
    bsdf.inputs['Roughness'].default_value = 0.6
    me = bpy.data.meshes.new("c")
    bm = bmesh.new(); seg = 22
    prof = [(0.0, 0.0), (0.032, 0.0), (0.033, 0.003), (0.045, 0.118), (0.047, 0.12)]
    rings = [[bm.verts.new((r * math.cos(i / seg * 2 * math.pi), r * math.sin(i / seg * 2 * math.pi), z)) for i in range(seg)] for r, z in prof]
    for a, b in zip(rings[:-1], rings[1:]):
        for i in range(seg):
            bm.faces.new([a[i], a[(i + 1) % seg], b[(i + 1) % seg], b[i]])
    bm.to_mesh(me); bm.free()
    cup = bpy.data.objects.new("cup", me); bpy.context.collection.objects.link(cup)
    cup.data.materials.append(cupm); K.PARTS.append(cup)
    lidm = K.material("Lid", (0.88, 0.88, 0.86), 0.35, dirt=0.4)
    lid = K.cyl(0, 0, 0.123, 0.048, 0.008, lidm, verts=22, bevel=0.002)
    straw = K.material("Straw", (0.75, 0.08, 0.06), 0.3, dirt=0.2)
    st = K.cyl(0.01, 0, 0.16, 0.0035, 0.09, straw, verts=8)
    st.rotation_euler = (0, math.radians(12), 0)
    cupo = K.join([cup, lid, st], "cupj")
    for v in cupo.data.vertices:  # amassadinho de um lado
        if v.co.x > 0.02 and v.co.z < 0.1:
            v.co.x -= 0.006 * math.sin(v.co.z * 30) ** 2
    K.PARTS.clear(); K.PARTS.append(cupo)
    finish("LanchesCup", [cupo], smooth=True)

# ------------------------------------------------------------------ 8) sacola plástica
def build_bag():
    K.reset_scene()
    W, H = 0.35, 0.45
    mat = K.material("PlasticBag", (0.86, 0.86, 0.84), 0.35, dirt=0.5, color_var=0.06, streaks=0.3,
                     bump=0.6, bump_scale=18, dirt_color=(0.40, 0.38, 0.32))
    objs = []
    for zz, flip in ((0.0015, False), (-0.0015, True)):
        me = bpy.data.meshes.new("bag")
        bm = bmesh.new()
        NX, NY = 14, 18
        vs = {}
        for i in range(NX + 1):
            for j in range(NY + 1):
                x = -W / 2 + W * i / NX
                y = -H / 2 + H * j / NY
                # alças: recorta o meio do topo
                if y > H / 2 - 0.10 and abs(x) < 0.07:
                    continue
                z = zz + 0.03 * noise.noise(Vector((x * 9, y * 9, 1.3))) + 0.015 * noise.noise(Vector((x * 25, y * 25, 4.0)))
                vs[(i, j)] = bm.verts.new((x, y, z if not flip else z - 2 * zz))
        for i in range(NX):
            for j in range(NY):
                q = [vs.get((i, j)), vs.get((i + 1, j)), vs.get((i + 1, j + 1)), vs.get((i, j + 1))]
                if None in q: continue
                if flip: q.reverse()
                bm.faces.new(q)
        bm.to_mesh(me); bm.free()
        ob = bpy.data.objects.new("bag", me)
        bpy.context.collection.objects.link(ob)
        ob.data.materials.append(mat)
        K.PARTS.append(ob); objs.append(ob)
    finish("PlasticBag", objs, smooth=True)

# ------------------------------------------------------------------ 9) saco de lixo preto
def build_trashbag():
    K.reset_scene()
    bpy.ops.mesh.primitive_uv_sphere_add(segments=32, ring_count=16, radius=0.3)
    ob = bpy.context.active_object
    ob.scale = (0.95, 0.85, 1.0)
    bpy.ops.object.transform_apply(scale=True)
    for v in ob.data.vertices:
        if v.co.z < -0.18: v.co.z = -0.18 + (v.co.z + 0.18) * 0.25   # fundo achatado no chão
        if v.co.z > 0.2: v.co.x *= 0.7; v.co.y *= 0.7                   # gargalo
    displace(ob, 0.06, 0.12)      # volumes do conteúdo
    displace(ob, 0.012, 0.03)     # rugas do plástico
    mat = K.material("TrashBag", (0.025, 0.025, 0.028), 0.28, dirt=0.6, ground_dirt=0.8, color_var=0.03,
                     bump=0.5, bump_scale=35, dirt_color=(0.12, 0.11, 0.09))
    ob.data.materials.append(mat)
    K.PARTS.append(ob)
    knot = K.cyl(0, 0, 0.33, 0.045, 0.06, mat, verts=12)
    ears = []
    for s in (-1, 1):
        bpy.ops.mesh.primitive_uv_sphere_add(segments=10, ring_count=6, radius=0.05, location=(s * 0.05, 0, 0.38))
        e = bpy.context.active_object
        e.scale = (1.4, 0.5, 0.8)
        bpy.ops.object.transform_apply(scale=True)
        e.data.materials.append(mat)
        K.PARTS.append(e); ears.append(e)
    bag = K.join([ob, knot] + ears, "tb")
    zmin = min(v.co.z for v in bag.data.vertices)
    for v in bag.data.vertices:
        v.co.z -= zmin  # fundo apoiado em z = 0 (origem no chão)
    K.PARTS.clear(); K.PARTS.append(bag)
    finish("TrashBag", [bag], smooth=True)

# ------------------------------------------------------------------ 10) caixa de papelão
def build_box():
    K.reset_scene()
    card = K.material("Cardboard", (0.52, 0.38, 0.22), 0.85, dirt=0.7, ground_dirt=0.8, color_var=0.10,
                      seams=('Z', 0.004), streaks=0.3, wear=0.3, wear_color=(0.62, 0.50, 0.33),
                      dirt_color=(0.25, 0.18, 0.11))
    tape = K.material("Tape", (0.62, 0.50, 0.30), 0.3, dirt=0.4)
    ink = K.material("BoxInk", (0.06, 0.05, 0.05), 0.7)
    W, D, H, T = 0.60, 0.40, 0.30, 0.006
    parts = [K.box(-W / 2, W / 2, -D / 2, D / 2, 0, T, card)]                         # fundo
    parts += [K.box(-W / 2, W / 2, -D / 2, -D / 2 + T, 0, H, card), K.box(-W / 2, W / 2, D / 2 - T, D / 2, 0, H, card)]
    parts += [K.box(-W / 2, -W / 2 + T, -D / 2, D / 2, 0, H, card), K.box(W / 2 - T, W / 2, -D / 2, D / 2, 0, H, card)]
    # abas abertas, caídas para fora
    for s in (-1, 1):
        # Abas compridas: dobradiça na borda de cima (y = ±D/2), caídas para fora ~30°
        f = K.box(-W / 2 + 0.01, W / 2 - 0.01, 0, D / 2 - 0.02, -T / 2, T / 2, card)
        f.rotation_euler = (math.radians(-30 if s > 0 else 210), 0, 0)
        f.location = (0, s * D / 2, H)
        # Abas curtas: dobradiça em x = ±W/2
        f2 = K.box(0, D / 2 - 0.02, -D / 2 + 0.01, D / 2 - 0.01, -T / 2, T / 2, card)
        f2.rotation_euler = (0, math.radians(30 if s > 0 else 150), 0)
        f2.location = (s * W / 2, 0, H)
    parts.append(K.box(-0.04, 0.04, -D / 2 - 0.001, -D / 2 + 0.0005, 0.05, H, tape))
    parts.append(K.text("ENTREGAS", 0.0, -D / 2 - 0.0015, 0.16, 0.06, ink, rot=(math.pi / 2, 0, 0), extrude=0.0008))
    parts.append(K.text("FRÁGIL", 0.0, -D / 2 - 0.0015, 0.09, 0.035, ink, rot=(math.pi / 2, 0, 0), extrude=0.0008))
    box = K.join(parts, "cb")
    # Amassado: um canto afundado e as paredes empenadas
    for v in box.data.vertices:
        k = max(0.0, (v.co.x / (W / 2)) * (v.co.y / (D / 2)))
        v.co.z -= 0.06 * k * (v.co.z / H)
        v.co.x += 0.01 * math.sin(v.co.z * 18) * (abs(v.co.y) > D / 2 - 0.01)
    K.PARTS.clear(); K.PARTS.append(box)
    finish("CardboardBox", [box])

BUILDERS = {"FoldedNewspaper": build_folded, "NewspaperBundle": build_bundle, "CrumpledPaper": build_crumpled,
            "TornPage": build_torn, "CrushedCan": build_can, "PlasticBottle": build_bottle, "LanchesCup": build_cup,
            "PlasticBag": build_bag, "TrashBag": build_trashbag, "CardboardBox": build_box}
for name, fn in BUILDERS.items():
    if ONLY and name not in ONLY:
        continue
    try:
        fn()
        K.PARTS.clear()
        K.MATS.clear()
    except Exception as e:
        import traceback; traceback.print_exc()
        print("FALHOU", name, e)
        K.PARTS.clear(); K.MATS.clear()
print("OK todos")
