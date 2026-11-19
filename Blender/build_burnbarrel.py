"""BARRIL DE FOGO (tambor de 200 L usado como fogueira de rua) para o TheDelivery — Blender, background.

Medidas reais de um tambor de aço de 55 galões: Ø 572 mm, altura 880 mm, chapa ~1,5 mm, dois frisos
de rolagem (a 1/3 e 2/3 da altura), aro de cima enrolado, tampa cortada fora. Furos de ventilação
perto do fundo (o fogo puxa ar por eles). Dentro: cama de cinza e brasas (emissivas, vistas pelos
furos e pela boca) e tábuas carbonizadas saindo pela borda. O fogo em si é animado no Unity
(partículas) — aqui só o que é sólido.

Materiais com desgaste do realistic_kit + chamuscado de calor subindo da boca (a tinta queima e vira
óxido escuro no terço de cima), assados em PBR 2048.

Uso: blender --background --python build_burnbarrel.py -- <pasta_saida> [variantes...]
Saída: <pasta_saida>/<Nome>/<Nome>.fbx + <Nome>_textures/*.png   (Nome = BurnBarrel_A/B/C)
"""
import bpy, bmesh, sys, math, os, random
from mathutils import Vector, noise
sys.path.append(os.path.dirname(os.path.abspath(__file__)))
import realistic_kit as K

argv = sys.argv[sys.argv.index("--") + 1:]
OUT_DIR = argv[0]
ONLY = set(argv[1:])
TEX = 2048

R, H, T = 0.286, 0.88, 0.0015       # raio, altura, chapa
SEG = 56                            # segmentos em volta

VARIANTS = {
    # nome: (tinta, semente, nº de furos, intensidade da ferrugem)
    "BurnBarrel_A": ((0.07, 0.17, 0.30), 11, 8, 0.8),    # azul desbotado
    "BurnBarrel_B": ((0.33, 0.07, 0.04), 23, 6, 1.0),    # vermelho óxido
    "BurnBarrel_C": ((0.16, 0.18, 0.09), 37, 10, 0.9),   # verde-oliva
}


def scorch(mat, z0, z1, color=(0.045, 0.035, 0.03), rough_add=0.25):
    """Chamuscado de calor: mistura 'color' na cor base de z0 (nada) a z1 (tudo), com borda irregular."""
    nt = mat.node_tree
    p = K._principled(mat)
    base_in = p.inputs['Base Color']
    src = base_in.links[0].from_socket if base_in.is_linked else K._source(nt, base_in)
    tc = K._n(nt, 'ShaderNodeTexCoord')
    sep = K._n(nt, 'ShaderNodeSeparateXYZ')
    nt.links.new(tc.outputs['Object'], sep.inputs[0])
    n = K._noise(nt, 7.0, 8.0, tc.outputs['Object'])
    zj = K._math(nt, 'ADD', sep.outputs['Z'], K._math(nt, 'MULTIPLY', K._math(nt, 'SUBTRACT', n.outputs['Fac'], 0.5), 0.22))
    fac = K._math(nt, 'DIVIDE', K._math(nt, 'SUBTRACT', zj, z0), z1 - z0)
    fac = K._math(nt, 'MINIMUM', K._math(nt, 'MAXIMUM', fac, 0.0), 1.0)
    fac = K._math(nt, 'POWER', fac, 1.4)
    mix = K._mix_color(nt, fac, src, color)
    nt.links.new(mix, base_in)
    r_in = p.inputs['Roughness']
    rs = r_in.links[0].from_socket if r_in.is_linked else K._source(nt, r_in)
    nt.links.new(K._math(nt, 'MINIMUM', K._math(nt, 'ADD', rs, K._math(nt, 'MULTIPLY', fac, rough_add)), 1.0), r_in)


def profile():
    """Perfil externo (r, z) de baixo para cima: friso do fundo, corpo, 2 frisos de rolagem, aro."""
    pts = [(R + 0.004, 0.0), (R + 0.004, 0.018), (R, 0.024)]
    def rib(zc):
        return [(R, zc - 0.022), (R + 0.007, zc - 0.010), (R + 0.008, zc), (R + 0.007, zc + 0.010), (R, zc + 0.022)]
    z = 0.024
    for zc in (H / 3, 2 * H / 3):
        while z + 0.06 < zc - 0.022:
            z += 0.06
            pts.append((R, z))
        pts += rib(zc)
        z = zc + 0.022
    while z + 0.06 < H - 0.02:
        z += 0.06
        pts.append((R, z))
    pts += [(R, H - 0.012), (R + 0.006, H - 0.006), (R + 0.007, H)]
    return pts


def shell(paint_mat, seed, holes):
    rnd = random.Random(seed)
    out = profile()
    inner = [(r - T - (0.004 if r > R + 0.003 else 0.0), z) for r, z in out]
    loop = out + list(reversed(inner))          # perfil fechado (anel com espessura)
    bm = bmesh.new()
    rings = []
    for i in range(SEG):
        a = 2 * math.pi * i / SEG
        rings.append([bm.verts.new((r * math.cos(a), r * math.sin(a), z)) for r, z in loop])
    n = len(loop)
    for i in range(SEG):
        A, B = rings[i], rings[(i + 1) % SEG]
        for j in range(n):
            k = (j + 1) % n
            bm.faces.new((A[j], B[j], B[k], A[k]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    me = bpy.data.meshes.new("shell")
    bm.to_mesh(me); bm.free()
    ob = bpy.data.objects.new("shell", me)
    bpy.context.collection.objects.link(ob)
    K._finish(ob, paint_mat)

    # Furos de ventilação (boolean), em volta, perto do fundo — feitos a talhadeira, meio tortos.
    cutters = []
    for i in range(holes):
        a = 2 * math.pi * (i + rnd.uniform(-0.15, 0.15)) / holes
        z = 0.085 + rnd.uniform(-0.012, 0.012)
        bpy.ops.mesh.primitive_cylinder_add(vertices=10, radius=rnd.uniform(0.018, 0.026), depth=0.08,
                                            location=(R * math.cos(a), R * math.sin(a), z),
                                            rotation=(math.pi / 2, 0, a + math.pi / 2))
        cutters.append(bpy.context.active_object)
    bpy.ops.object.select_all(action='DESELECT')
    for c in cutters: c.select_set(True)
    bpy.context.view_layer.objects.active = cutters[0]
    bpy.ops.object.join()
    cutter = bpy.context.active_object
    mod = ob.modifiers.new("holes", 'BOOLEAN')
    mod.operation = 'DIFFERENCE'
    mod.object = cutter
    mod.solver = 'EXACT'
    bpy.context.view_layer.objects.active = ob
    bpy.ops.object.modifier_apply(modifier=mod.name)
    bpy.data.objects.remove(cutter, do_unlink=True)

    # Amassados: deslocamento radial por (ângulo, altura), igual por dentro e por fora.
    off = Vector((rnd.uniform(0, 50), rnd.uniform(0, 50), rnd.uniform(0, 50)))
    dents = [(rnd.uniform(0, 2 * math.pi), rnd.uniform(0.2, 0.75), rnd.uniform(0.06, 0.12), rnd.uniform(0.010, 0.022))
             for _ in range(3)]
    for v in ob.data.vertices:
        x, y, z = v.co
        r = math.hypot(x, y)
        if r < 0.05: continue
        a = math.atan2(y, x)
        d = 0.0035 * noise.noise(Vector((math.cos(a) * 2.2, math.sin(a) * 2.2, z * 3.0)) + off)
        for da, dz, rad, depth in dents:
            ang = math.atan2(math.sin(a - da), math.cos(a - da)) * R
            q = (ang * ang + (z - dz) ** 2) / (rad * rad)
            if q < 1.0: d -= depth * (1 - q) ** 2
        # aro de cima levemente ovalizado (pancadas)
        d -= 0.006 * max(0.0, (z - (H - 0.08)) / 0.08) * (0.5 + 0.5 * math.cos(2 * (a - dents[0][0])))
        s = (r + d) / r
        v.co.x *= s; v.co.y *= s
    return ob


def contents(rnd):
    ash = K.material("Ash", (0.16, 0.15, 0.14), 0.95, dirt=0.6, dirt_color=(0.05, 0.05, 0.05), bump=0.6,
                     bump_scale=60.0, color_var=0.25)
    ember = K.material("Embers", (0.20, 0.05, 0.015), 0.9, bump=0.8, bump_scale=30.0, color_var=0.3,
                       emission=(1.0, 0.30, 0.04), emission_strength=1.0)
    char = K.material("CharredWood", (0.035, 0.028, 0.024), 0.92, wood=True, dirt=0.5,
                      dirt_color=(0.01, 0.01, 0.01), bump=0.9, bump_scale=25.0, color_var=0.3)
    wood = K.material("CharredWoodEnd", (0.28, 0.17, 0.09), 0.85, wood=True, dirt=0.8,
                      dirt_color=(0.03, 0.02, 0.02), streaks=0.5)
    parts = []
    floor = K.cyl(0, 0, 0.012, R - 0.004, 0.006, ash, verts=SEG)        # fundo
    parts.append(floor)
    bed = K.cyl(0, 0, 0.60, R - 0.010, 0.04, ash, verts=SEG)           # cinza (cheio até aqui)
    parts.append(bed)
    # brasas: pedaços irregulares sobre a cinza
    for i in range(26):
        a = rnd.uniform(0, 2 * math.pi); rr = rnd.uniform(0.0, R - 0.06)
        bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=1, radius=rnd.uniform(0.025, 0.05),
                                              location=(rr * math.cos(a), rr * math.sin(a), 0.62 + rnd.uniform(0, 0.03)))
        o = bpy.context.active_object
        o.scale = (1.0, rnd.uniform(0.6, 1.0), rnd.uniform(0.4, 0.7))
        o.rotation_euler = (rnd.uniform(0, 3), rnd.uniform(0, 3), rnd.uniform(0, 3))
        bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
        parts.append(K._finish(o, ember))
    # brasa no fundo (vista pelos furos)
    parts.append(K.cyl(0, 0, 0.03, R - 0.02, 0.02, ember, verts=SEG))
    # tábuas e galhos carbonizados, cruzados, alguns saindo pela borda
    for i in range(7):
        L = rnd.uniform(0.45, 0.75); w = rnd.uniform(0.05, 0.09); t = rnd.uniform(0.018, 0.03)
        pl = K.box(-L / 2, L / 2, -w / 2, w / 2, -t / 2, t / 2, char, bevel=0.004)
        # ponta menos queimada (madeira à mostra na ponta de fora)
        end = K.box(L / 2 - 0.04, L / 2 + 0.002, -w / 2 - 0.001, w / 2 + 0.001, -t / 2 - 0.001, t / 2 + 0.001, wood)
        o = K.join([pl, end], "plank")
        a = rnd.uniform(0, 2 * math.pi)
        rise = rnd.uniform(0.22, 0.45)                       # quanto a ponta de cima sobe acima da cinza
        tilt = math.asin(min(0.95, rise / L))
        reach = (R - 0.04) - 0.5 * L * math.cos(tilt)         # ponta de cima encostada perto da borda
        o.rotation_euler = (rnd.uniform(-0.25, 0.25), -tilt, a)
        o.location = (reach * math.cos(a), reach * math.sin(a), 0.63 + 0.5 * L * math.sin(tilt))
        bpy.ops.object.select_all(action='DESELECT'); o.select_set(True)
        bpy.context.view_layer.objects.active = o
        bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
        # não atravessar a parede do tambor: encolhe radialmente o que passar do raio interno abaixo da boca
        for v in o.data.vertices:
            r = math.hypot(v.co.x, v.co.y)
            if v.co.z < H - 0.005 and r > R - 0.012:
                s = (R - 0.012) / r; v.co.x *= s; v.co.y *= s
        K.PARTS.append(o) if o not in K.PARTS else None
        parts.append(o)
    return parts


def build(name):
    paint, seed, holes, rust = VARIANTS[name]
    K.reset_scene()
    rnd = random.Random(seed)
    steel = K.material("BarrelPaint", paint, 0.55, 0.35, dirt=0.55, dirt_color=(0.07, 0.06, 0.05), ground_dirt=0.7,
                       # sem 'wear': na chapa de 1,5 mm o Bevel acha quina em todo lado e assa retalhos retangulares
                       wear=0.0, rust=rust, color_var=0.10, rough_var=0.18,
                       bump=0.35, bump_scale=55.0, streaks=0.7)
    scorch(steel, H * 0.52, H * 0.98)
    parts = [shell(steel, seed, holes)] + contents(rnd)
    ob = K.join(parts, name)
    for p in ob.data.polygons:
        p.use_smooth = False
    K.unwrap([ob], margin=0.006)
    d = os.path.join(OUT_DIR, name)
    K.bake_all([ob], os.path.join(d, name + "_textures"), TEX)
    K.collapse_materials([ob])
    K.export_fbx(os.path.join(d, name + ".fbx"))
    print("RESULT %s tris=%d dims=%s" % (name, sum(len(p.vertices) - 2 for p in ob.data.polygons), [round(v, 3) for v in ob.dimensions]))


for name in VARIANTS:
    if ONLY and name not in ONLY:
        continue
    try:
        build(name)
    except Exception as e:
        import traceback; traceback.print_exc()
        print("FALHOU", name, e)
    K.PARTS.clear(); K.MATS.clear()
print("OK todos")
