"""Hambúrguer do delivery (Ato 3) — REALISTA, por script (Blender, background). 0 créditos.

Peças (metros, Z para cima, origem no centro da base do pão):
  pão de baixo (0–0,020) · carne (0,020–0,036, borda irregular, tostada) · queijo derretendo
  (lâmina que escorre nas pontas) · tomate · alface franzida (passa da borda) · pão de cima (cúpula
  com gergelim). Ø ~11 cm, ~8 cm de altura (um x-burger de verdade).

Estados (um objeto cada, mesma UV/atlas):
  Burger_Full, Burger_Bite1, Burger_Bite2, Burger_Bite3 — as mordidas são booleanas com um
  "molde de arcada" (elipse com recortes de dentes). Cada camada mostra o SEU miolo no corte:
  miolo de pão, carne por dentro, queijo, tomate, alface clara.
  Wrapper — papel manteiga quadriculado, aberto e amassado (a comida desembrulhada na bancada).

Materiais procedurais ASSADOS em PBR (realistic_kit.bake_all). Também renderiza prévias para a
avaliação (frente, trás, lado, 3/4, cada mordida, e sobre o papel).

Uso: blender --background --python build_burger.py -- <saida.fbx> <pasta_texturas> [<pasta_previas>]
"""
import bpy, bmesh, sys, math, os, random
from mathutils import Vector, noise
sys.path.append(os.path.dirname(os.path.abspath(__file__)))
import realistic_kit as K

argv = sys.argv[sys.argv.index("--") + 1:]
OUT_FBX, TEX_DIR = argv[0], argv[1]
PREV_DIR = argv[2] if len(argv) > 2 else None
PREVIEW_ONLY = len(argv) > 3 and argv[3] in ("previewonly", "photos")
PHOTOS = len(argv) > 3 and argv[3] == "photos"   # só as fotos do cardápio do app (celular do Ato 3)
os.makedirs(TEX_DIR, exist_ok=True)
random.seed(77)

K.reset_scene()

# ============================================================== materiais (escala de comida)
def _nt(name):
    m = bpy.data.materials.new(name); m.use_nodes = True
    nt = m.node_tree; nt.nodes.clear()
    out = nt.nodes.new('ShaderNodeOutputMaterial'); out.location = (900, 0)
    p = nt.nodes.new('ShaderNodeBsdfPrincipled'); p.location = (600, 0)
    nt.links.new(p.outputs[0], out.inputs['Surface'])
    tc = nt.nodes.new('ShaderNodeTexCoord')
    return m, nt, p, tc.outputs['Object']

def _noise(nt, vec, scale, detail=6.0, rough=0.6):
    n = nt.nodes.new('ShaderNodeTexNoise')
    n.inputs['Scale'].default_value = scale; n.inputs['Detail'].default_value = detail
    n.inputs['Roughness'].default_value = rough
    nt.links.new(vec, n.inputs['Vector']); return n.outputs['Fac']

def lin(c):
    """As cores deste arquivo estão em sRGB (como num seletor de cor); o Blender quer linear."""
    return tuple(x ** 2.2 for x in c)

def _ramp(nt, fac, stops):
    r = nt.nodes.new('ShaderNodeValToRGB')
    cr = r.color_ramp
    while len(cr.elements) < len(stops): cr.elements.new(0.5)
    for el, (pos, col) in zip(cr.elements, stops):
        el.position = pos; el.color = (*lin(col), 1.0)
    nt.links.new(fac, r.inputs['Fac']); return r.outputs['Color']

def _math(nt, op, a, b=None):
    m = nt.nodes.new('ShaderNodeMath'); m.operation = op
    for i, v in enumerate((a, b)):
        if v is None: continue
        if isinstance(v, (int, float)): m.inputs[i].default_value = v
        else: nt.links.new(v, m.inputs[i])
    return m.outputs[0]

def _bump(nt, p, height, strength, dist=0.0015):
    b = nt.nodes.new('ShaderNodeBump')
    b.inputs['Strength'].default_value = strength; b.inputs['Distance'].default_value = dist
    nt.links.new(height, b.inputs['Height']); nt.links.new(b.outputs['Normal'], p.inputs['Normal'])

def _zfac(nt, vec, z0, z1):
    """0 em z0, 1 em z1 (coordenada do objeto)."""
    sep = nt.nodes.new('ShaderNodeSeparateXYZ'); nt.links.new(vec, sep.inputs[0])
    mr = nt.nodes.new('ShaderNodeMapRange'); mr.clamp = True
    mr.inputs['From Min'].default_value = z0; mr.inputs['From Max'].default_value = z1
    nt.links.new(sep.outputs['Z'], mr.inputs['Value']); return mr.outputs[0]

def _voronoi(nt, vec, scale, feature='F1', out='Distance', rand=1.0):
    n = nt.nodes.new('ShaderNodeTexVoronoi'); n.feature = feature
    n.inputs['Scale'].default_value = scale
    try: n.inputs['Randomness'].default_value = rand
    except KeyError: pass
    nt.links.new(vec, n.inputs['Vector']); return n.outputs[out]

def _ao(nt, color, strength, dist=0.012):
    """Sombra de contato entre as camadas (AO do próprio objeto), multiplicada na cor e ASSADA na textura."""
    ao = nt.nodes.new('ShaderNodeAmbientOcclusion'); ao.samples = 16; ao.only_local = True
    ao.inputs['Distance'].default_value = dist
    fac = _math(nt, 'ADD', 1.0 - strength, _math(nt, 'MULTIPLY', ao.outputs['AO'], strength))
    mix = nt.nodes.new('ShaderNodeMix'); mix.data_type = 'RGBA'; mix.blend_type = 'MULTIPLY'
    mix.inputs['Factor'].default_value = 1.0
    nt.links.new(color, mix.inputs[6]); nt.links.new(fac, mix.inputs[7])
    return mix.outputs[2]

def _add(nt, *terms):
    """soma de (socket, peso) e constantes."""
    acc = None
    for t in terms:
        s = _math(nt, 'MULTIPLY', t[0], t[1]) if isinstance(t, tuple) else t
        acc = s if acc is None else _math(nt, 'ADD', acc, s)
    return acc

def mat_crust():
    """Casca do pão de cima (brioche): faixa clara na base (onde o pão cresceu), dourado que escurece para o topo,
    manchas de forno, brilho de gema irregular, poros finos e rugas."""
    m, nt, p, v = _nt("BunCrust")
    z = _zfac(nt, v, 0.050, 0.081)
    mott = _noise(nt, v, 32.0, 6.0); fine = _noise(nt, v, 260.0, 10.0)
    f = _add(nt, (z, 0.78), (mott, 0.38), (fine, 0.08), -0.16)
    col = _ramp(nt, f, [(0.0, (0.90, 0.78, 0.58)), (0.16, (0.82, 0.63, 0.40)), (0.40, (0.67, 0.45, 0.25)),
                        (0.68, (0.53, 0.33, 0.17)), (0.95, (0.41, 0.24, 0.12))])
    nt.links.new(_ao(nt, col, 0.55), p.inputs['Base Color'])
    nt.links.new(_add(nt, (mott, 0.25), 0.42), p.inputs['Roughness'])   # brilho de gema fraco e irregular
    _bump(nt, p, _add(nt, (_noise(nt, v, 900.0, 4.0), 0.35), (_noise(nt, v, 70.0, 5.0), 0.65)), 0.34, 0.0012)
    return m

def mat_crust_low():
    m, nt, p, v = _nt("BunBottom")
    mott = _noise(nt, v, 40.0, 6.0)
    z = _zfac(nt, v, 0.0, 0.016)
    col = _ramp(nt, _add(nt, (mott, 0.6), (z, 0.35)), [(0.2, (0.70, 0.46, 0.22)), (0.5, (0.86, 0.66, 0.40)), (0.8, (0.92, 0.78, 0.54))])
    nt.links.new(_ao(nt, col, 0.5), p.inputs['Base Color'])
    nt.links.new(_add(nt, (mott, 0.25), 0.45), p.inputs['Roughness'])
    _bump(nt, p, _noise(nt, v, 700.0, 6.0), 0.25, 0.0012)
    return m

def mat_crumb(name="Crumb", toast=False):
    """Miolo do pão: alvéolos (Voronoi) e fibras. toast=True: a face que foi na chapa (dourada em manchas)."""
    m, nt, p, v = _nt(name)
    pores = _voronoi(nt, v, 1100.0)
    big = _voronoi(nt, v, 260.0)
    n = _noise(nt, v, 45.0, 4.0)
    base = _ramp(nt, _add(nt, (pores, 0.7), (big, 0.3)), [(0.0, (0.74, 0.62, 0.44)), (0.2, (0.90, 0.80, 0.62)), (0.6, (0.95, 0.88, 0.71)), (1.0, (0.97, 0.91, 0.76))])
    if toast:
        tm = _ramp(nt, n, [(0.35, (1, 1, 1)), (0.55, (0.86, 0.68, 0.46)), (0.75, (0.66, 0.44, 0.22))])
        mix = nt.nodes.new('ShaderNodeMix'); mix.data_type = 'RGBA'; mix.blend_type = 'MULTIPLY'
        mix.inputs['Factor'].default_value = 1.0; nt.links.new(base, mix.inputs[6]); nt.links.new(tm, mix.inputs[7])
        base = mix.outputs[2]
    nt.links.new(_ao(nt, base, 0.6, 0.008), p.inputs['Base Color'])
    p.inputs['Roughness'].default_value = 0.88
    _bump(nt, p, _add(nt, (pores, 0.7), (big, 0.3)), 0.4, 0.0012)
    return m

def mat_patty(interior=False):
    m, nt, p, v = _nt("PattyIn" if interior else "Patty")
    if interior:
        cells = _voronoi(nt, v, 600.0, 'F1', 'Color')
        sep = nt.nodes.new('ShaderNodeSeparateColor'); nt.links.new(cells, sep.inputs[0])
        n = _noise(nt, v, 300.0, 6.0)
        f = _add(nt, (sep.outputs[0], 0.6), (n, 0.4))
        col = _ramp(nt, f, [(0.2, (0.30, 0.15, 0.10)), (0.45, (0.44, 0.23, 0.16)), (0.7, (0.55, 0.32, 0.23)), (0.9, (0.74, 0.56, 0.46))])
        nt.links.new(_ao(nt, col, 0.5, 0.006), p.inputs['Base Color'])
        p.inputs['Roughness'].default_value = 0.55
        _bump(nt, p, _voronoi(nt, v, 600.0), 0.6, 0.0015)
        return m
    n = _noise(nt, v, 220.0, 14.0, 0.75)
    cr = _voronoi(nt, v, 380.0)
    big = _noise(nt, v, 38.0, 4.0)
    f = _add(nt, (n, 0.55), (big, 0.25), (cr, 0.35), -0.05)
    col = _ramp(nt, f, [(0.25, (0.09, 0.045, 0.025)), (0.42, (0.22, 0.11, 0.055)), (0.60, (0.34, 0.19, 0.10)), (0.80, (0.45, 0.28, 0.15))])
    nt.links.new(_ao(nt, col, 0.65, 0.01), p.inputs['Base Color'])
    nt.links.new(_add(nt, (n, 0.45), 0.18), p.inputs['Roughness'])      # gordura: manchas brilhantes
    _bump(nt, p, _add(nt, (n, 1.0), (cr, 0.6)), 0.85, 0.002)
    return m

def mat_cheese():
    m, nt, p, v = _nt("Cheese")
    n = _noise(nt, v, 45.0, 3.0)
    col = _ramp(nt, n, [(0.3, (0.96, 0.73, 0.33)), (0.7, (0.92, 0.62, 0.25))])
    nt.links.new(_ao(nt, col, 0.45, 0.008), p.inputs['Base Color'])
    p.inputs['Roughness'].default_value = 0.3
    _bump(nt, p, _noise(nt, v, 120.0, 3.0), 0.12, 0.001)
    return m

def mat_tomato(interior=False):
    m, nt, p, v = _nt("TomatoIn" if interior else "Tomato")
    if interior:
        gel = _voronoi(nt, v, 110.0, 'SMOOTH_F1') if True else None
        seeds = _voronoi(nt, v, 900.0)
        f = _add(nt, (gel, 0.8), (_math(nt, 'LESS_THAN', seeds, 0.12), 0.4))
        col = _ramp(nt, f, [(0.15, (0.80, 0.10, 0.06)), (0.35, (0.94, 0.38, 0.22)), (0.7, (0.97, 0.55, 0.30)), (0.95, (0.96, 0.86, 0.52))])
        p.inputs['Roughness'].default_value = 0.12
    else:
        col = _ramp(nt, _noise(nt, v, 90.0, 4.0), [(0.4, (0.80, 0.14, 0.07)), (0.7, (0.66, 0.08, 0.05))])
        p.inputs['Roughness'].default_value = 0.16
    nt.links.new(_ao(nt, col, 0.5, 0.008), p.inputs['Base Color'])
    return m

def mat_lettuce(interior=False):
    """Folha de alface: mais clara perto do talo (centro), escura na borda; nervuras claras (bordas de Voronoi)."""
    m, nt, p, v = _nt("LettuceIn" if interior else "Lettuce")
    sep = nt.nodes.new('ShaderNodeSeparateXYZ'); nt.links.new(v, sep.inputs[0])
    r = _math(nt, 'SQRT', _add(nt, _math(nt, 'MULTIPLY', sep.outputs['X'], sep.outputs['X']), _math(nt, 'MULTIPLY', sep.outputs['Y'], sep.outputs['Y'])))
    rad = _math(nt, 'DIVIDE', r, 0.06)
    ang = nt.nodes.new('ShaderNodeMath'); ang.operation = 'ARCTAN2'; nt.links.new(sep.outputs['Y'], ang.inputs[0]); nt.links.new(sep.outputs['X'], ang.inputs[1])
    wob = _math(nt, 'MULTIPLY', _noise(nt, v, 40.0, 3.0), 1.6)
    rib = _math(nt, 'ABSOLUTE', _math(nt, 'SINE', _add(nt, (ang.outputs[0], 7.0), wob)))
    veins = _add(nt, (rib, 1.0), (_noise(nt, v, 260.0, 2.0), 0.08))
    vm = _math(nt, 'LESS_THAN', veins, 0.07)
    n = _noise(nt, v, 70.0, 6.0)
    f = _add(nt, (rad, 0.75), (n, 0.35), (vm, -0.25))
    if interior:
        col = _ramp(nt, f, [(0.2, (0.85, 0.92, 0.66)), (0.9, (0.64, 0.80, 0.40))])
    else:
        col = _ramp(nt, f, [(0.15, (0.82, 0.87, 0.60)), (0.45, (0.56, 0.70, 0.34)), (0.75, (0.40, 0.57, 0.22)), (1.0, (0.30, 0.46, 0.16))])
    nt.links.new(_ao(nt, col, 0.55, 0.008), p.inputs['Base Color'])
    p.inputs['Roughness'].default_value = 0.38
    _bump(nt, p, _add(nt, (vm, 0.5), (n, 0.5)), 0.3, 0.0008)
    return m

def mat_sauce():
    m, nt, p, v = _nt("Sauce")
    col = _ramp(nt, _noise(nt, v, 120.0, 3.0), [(0.3, (0.96, 0.84, 0.62)), (0.7, (0.92, 0.70, 0.46))])
    nt.links.new(_ao(nt, col, 0.4, 0.006), p.inputs['Base Color'])
    p.inputs['Roughness'].default_value = 0.12
    return m

def mat_sesame():
    m, nt, p, v = _nt("Sesame")
    col = _ramp(nt, _noise(nt, v, 400.0, 3.0), [(0.3, (0.95, 0.88, 0.70)), (0.7, (0.84, 0.72, 0.50))])
    nt.links.new(col, p.inputs['Base Color'])
    p.inputs['Roughness'].default_value = 0.42
    return m

def mat_wrapper():
    """Papel manteiga quadriculado (vermelho apagado) com manchas de gordura translúcidas e amassados."""
    m, nt, p, v = _nt("Wrapper")
    sep = nt.nodes.new('ShaderNodeSeparateXYZ'); nt.links.new(v, sep.inputs[0])
    def stripes(c):
        f = _math(nt, 'FRACT', _math(nt, 'DIVIDE', c, 0.024))
        return _math(nt, 'LESS_THAN', f, 0.5)
    sx, sy = stripes(sep.outputs['X']), stripes(sep.outputs['Y'])
    check = _math(nt, 'MULTIPLY', _math(nt, 'ADD', sx, sy), 0.5)
    red = _ramp(nt, check, [(0.0, (0.94, 0.92, 0.87)), (0.45, (0.94, 0.92, 0.87)), (0.5, (0.86, 0.50, 0.46)), (0.95, (0.86, 0.50, 0.46)), (1.0, (0.74, 0.22, 0.20))])
    grease = _math(nt, 'MULTIPLY', _math(nt, 'SUBTRACT', _noise(nt, v, 14.0, 5.0), 0.52), 3.0)
    grease = _math(nt, 'MINIMUM', _math(nt, 'MAXIMUM', grease, 0.0), 1.0)
    mix = nt.nodes.new('ShaderNodeMix'); mix.data_type = 'RGBA'; mix.blend_type = 'MULTIPLY'
    nt.links.new(grease, mix.inputs['Factor']); nt.links.new(red, mix.inputs[6])
    mix.inputs[7].default_value = (*lin((0.78, 0.66, 0.44)), 1.0)
    nt.links.new(_ao(nt, mix.outputs[2], 0.35, 0.02), p.inputs['Base Color'])
    nt.links.new(_math(nt, 'SUBTRACT', 0.72, _math(nt, 'MULTIPLY', grease, 0.5)), p.inputs['Roughness'])
    _bump(nt, p, _noise(nt, v, 25.0, 8.0), 0.25, 0.003)
    return m

M = dict(crust=mat_crust(), crust_low=mat_crust_low(), crumb=mat_crumb(), crumb_toast=mat_crumb("CrumbToast", True),
         patty=mat_patty(), patty_in=mat_patty(True), cheese=mat_cheese(), tomato=mat_tomato(),
         tomato_in=mat_tomato(True), lettuce=mat_lettuce(), lettuce_in=mat_lettuce(True), sesame=mat_sesame(),
         sauce=mat_sauce(), wrapper=mat_wrapper())

# ============================================================== geometria
def link(me, name):
    ob = bpy.data.objects.new(name, me); bpy.context.collection.objects.link(ob); return ob

def set_mats(ob, mats):
    ob.data.materials.clear()
    for m in mats: ob.data.materials.append(m)

def disc_solid(name, r, z0, z1, segs=64, rings=6, top_mat=0, side_mat=0, bot_mat=0, edge_noise=0.0, top_noise=0.0, seed=0, round_bottom=0.0):
    """Disco fechado (camada): anéis concêntricos em cima e embaixo, borda com ruído."""
    bm = bmesh.new()
    def ring(rr, z, k):
        out = []
        for i in range(segs):
            a = 2 * math.pi * i / segs
            e = 1.0 + edge_noise * noise.noise(Vector((math.cos(a) * 3 + seed, math.sin(a) * 3, k)))
            out.append(bm.verts.new((math.cos(a) * rr * e, math.sin(a) * rr * e, z)))
        return out
    top_rings, bot_rings = [], []
    for j in range(rings, 0, -1):
        f = j / rings
        zt = z1 + top_noise * noise.noise(Vector((seed, f * 5, 1.0))) * (1 - f * 0.5)
        top_rings.append(ring(r * f, zt, 1.0))
    for j in range(1, rings + 1):
        f = j / rings
        zb = z0 + round_bottom * (1 - math.cos(min(1.0, (1 - f) * 2.2) * math.pi / 2)) * 0 + (round_bottom * (f ** 6))
        bot_rings.append(ring(r * f, zb, 2.0))
    ct = bm.verts.new((0, 0, z1)); cb = bm.verts.new((0, 0, z0))
    faces_top, faces_side, faces_bot = [], [], []
    inner = top_rings[-1]
    for i in range(segs): faces_top.append(bm.faces.new((ct, inner[i], inner[(i + 1) % segs])))
    for a_r, b_r in zip(top_rings[1:], top_rings[:-1]):
        for i in range(segs): faces_top.append(bm.faces.new((a_r[i], b_r[i], b_r[(i + 1) % segs], a_r[(i + 1) % segs])))
    outer_t, outer_b = top_rings[0], bot_rings[-1]
    for i in range(segs): faces_side.append(bm.faces.new((outer_b[i], outer_b[(i + 1) % segs], outer_t[(i + 1) % segs], outer_t[i])))
    for a_r, b_r in zip(bot_rings[:-1], bot_rings[1:]):
        for i in range(segs): faces_bot.append(bm.faces.new((a_r[i], a_r[(i + 1) % segs], b_r[(i + 1) % segs], b_r[i])))
    inner_b = bot_rings[0]
    for i in range(segs): faces_bot.append(bm.faces.new((cb, inner_b[(i + 1) % segs], inner_b[i])))
    for f in faces_top: f.material_index = top_mat
    for f in faces_side: f.material_index = side_mat
    for f in faces_bot: f.material_index = bot_mat
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    me = bpy.data.meshes.new(name); bm.to_mesh(me); bm.free()
    return link(me, name)

def smooth(ob, angle=50):
    for p in ob.data.polygons: p.use_smooth = True
    try:
        ob.data.set_sharp_from_angle(angle=math.radians(angle))
    except Exception:
        pass

from mathutils import Matrix, Quaternion

def apply_mods(ob):
    bpy.context.view_layer.objects.active = ob
    for md in list(ob.modifiers): bpy.ops.object.modifier_apply(modifier=md.name)

def displace(ob, kind, scale, strength, subsurf=0):
    if subsurf:
        s = ob.modifiers.new("s", 'SUBSURF'); s.levels = subsurf; s.render_levels = subsurf
    t = bpy.data.textures.new(ob.name + kind, kind)
    t.noise_scale = scale
    if hasattr(t, 'noise_depth'): t.noise_depth = 4
    d = ob.modifiers.new("d", 'DISPLACE'); d.texture = t; d.strength = strength; d.mid_level = 0.5
    apply_mods(ob)

# --- pão de baixo (1,65 cm): borda bem arredondada, topo tostado de miolo
bun_b = disc_solid("BunBottom", 0.0525, 0.0, 0.0165, segs=96, rings=8, top_mat=1, side_mat=0, bot_mat=0, edge_noise=0.014, seed=1)
set_mats(bun_b, [M['crust_low'], M['crumb_toast']])
mod = bun_b.modifiers.new("bv", 'BEVEL'); mod.width = 0.0065; mod.segments = 6; mod.limit_method = 'ANGLE'
apply_mods(bun_b)
displace(bun_b, 'CLOUDS', 0.012, 0.0009)
smooth(bun_b, 40)

# --- carne (1,7 cm): passa um pouco da borda do pão, borda craquelada, topo irregular
patty = disc_solid("Patty", 0.0568, 0.0165, 0.0335, segs=128, rings=10, edge_noise=0.065, top_noise=0.0018, seed=4)
set_mats(patty, [M['patty']])
displace(patty, 'CLOUDS', 0.009, 0.0032, subsurf=1)
displace(patty, 'VORONOI', 0.0035, 0.0014)
smooth(patty, 75)


# --- queijo derretido: quadrado de cantos arredondados, cai sobre a borda da carne e escorre nas pontas
bm = bmesh.new()
bmesh.ops.create_grid(bm, x_segments=44, y_segments=44, size=1.0)
P = 5.0
for vv in bm.verts:
    u, w = vv.co.x, vv.co.y
    s = max(abs(u), abs(w)); q = (abs(u) ** P + abs(w) ** P) ** (1 / P) if s > 0 else 1.0
    k = (s / q) if q > 0 else 1.0
    vv.co.x, vv.co.y = u * k * 0.0545, w * k * 0.0545
bmesh.ops.rotate(bm, verts=bm.verts, cent=(0, 0, 0), matrix=Matrix.Rotation(math.radians(38), 3, 'Z'))
for vv in bm.verts:
    r = math.hypot(vv.co.x, vv.co.y)
    # dobra sobre a borda da carne: o que passa do raio 0,0515 vira QUEDA pela lateral (abraça a carne)
    EDGE = 0.0545
    out = max(0.0, r - EDGE)
    bend = min(out, 0.004)                       # a curva da dobra
    fall = max(0.0, out - 0.004)                 # o resto cai quase na vertical (escorre)
    z = 0.0339 + 0.0004 * noise.noise(vv.co * 150) - bend * 0.9 - fall * 1.05
    if r > 0:
        nr = min(r, EDGE) + bend * 1.1 + fall * 0.05 + 0.0032
        vv.co.x, vv.co.y = vv.co.x / r * nr, vv.co.y / r * nr
    vv.co.z = z
me = bpy.data.meshes.new("Cheese"); bm.to_mesh(me); bm.free()
cheese = link(me, "Cheese"); set_mats(cheese, [M['cheese']])
so = cheese.modifiers.new("so", 'SOLIDIFY'); so.thickness = 0.0018; so.offset = 1.0
sub = cheese.modifiers.new("sb", 'SUBSURF'); sub.levels = 1
apply_mods(cheese); smooth(cheese, 85)

# --- tomate: duas rodelas sobrepostas
toms = []
for i, (ox, oy, rr) in enumerate([(0.014, 0.006, 0.034), (-0.016, -0.004, 0.033)]):
    t = disc_solid("Tomato%d" % i, rr, 0.0358 + i * 0.0004, 0.0412 + i * 0.0004, segs=56, rings=5, top_mat=1, side_mat=0, bot_mat=1, edge_noise=0.025, seed=9 + i)
    t.location = (ox, oy, 0); bpy.context.view_layer.objects.active = t
    bpy.ops.object.transform_apply(location=True, rotation=False, scale=False)
    set_mats(t, [M['tomato'], M['tomato_in']]); toms.append(t)
bpy.ops.object.select_all(action='DESELECT')
for t in toms: t.select_set(True)
bpy.context.view_layer.objects.active = toms[0]; bpy.ops.object.join()
tom = bpy.context.active_object; tom.name = "Tomato"; smooth(tom, 60)

# --- alface: folha rasgada e franzida de forma irregular (não uma onda perfeita)
bm = bmesh.new()
segs, rings = 160, 12
grid = []
for j in range(rings + 1):
    f = j / rings
    row = []
    for i in range(segs):
        a = 2 * math.pi * i / segs
        ca, sa = math.cos(a), math.sin(a)
        torn = 1 + 0.10 * noise.noise(Vector((ca * 1.6, sa * 1.6, 3.0))) + 0.04 * noise.noise(Vector((ca * 6, sa * 6, 4.0)))
        rr = 0.0600 * f * torn
        phase = a * 9 + 2.8 * noise.noise(Vector((ca, sa, 1.0)))
        amp = 0.0034 + 0.0022 * noise.noise(Vector((ca * 2, sa * 2, 5.0)))
        z = 0.0418 + (f ** 2.0) * (amp * math.sin(phase) + 0.0012 * math.sin(a * 27 + 1.3)) - (f ** 3) * 0.0045 * (1 + noise.noise(Vector((ca * 3, sa * 3, 6.0))))
        row.append(bm.verts.new((ca * rr, sa * rr, z)))
    grid.append(row)
c = bm.verts.new((0, 0, 0.0418))
for i in range(segs): bm.faces.new((c, grid[1][i], grid[1][(i + 1) % segs]))
for j in range(1, rings):
    for i in range(segs): bm.faces.new((grid[j][i], grid[j + 1][i], grid[j + 1][(i + 1) % segs], grid[j][(i + 1) % segs]))
me = bpy.data.meshes.new("Lettuce"); bm.to_mesh(me); bm.free()
lettuce = link(me, "Lettuce"); set_mats(lettuce, [M['lettuce']])
so = lettuce.modifiers.new("so", 'SOLIDIFY'); so.thickness = 0.0013; so.offset = 0.0
sb = lettuce.modifiers.new("sb", 'SUBSURF'); sb.levels = 1
apply_mods(lettuce); smooth(lettuce, 85)

# --- pão de cima: topo mais chato, laterais mais retas, "coroa" na base, irregular
BT_Z, BT_R, BT_H = 0.0470, 0.0545, 0.0335
def top_bun_point(x, y, z):
    a = math.atan2(y, x); zz = max(z, 0.0)
    crown = 1 + 0.035 * (1 - zz) ** 10
    nl = 0.016 * noise.noise(Vector((math.cos(a) * 2.5 + 7, math.sin(a) * 2.5, zz * 1.5)))
    nh = 0.003 * noise.noise(Vector((x * 14, y * 14, z * 14)))
    R = BT_R * crown * (1 + nl + nh)
    H = BT_H * (1 + 0.05 * noise.noise(Vector((x * 2, y * 2, 9.0))))
    return Vector((x * R * 1.015, y * R, BT_Z + (zz ** 0.72) * H * (1 + nh * 0.6)))
bm = bmesh.new()
bmesh.ops.create_uvsphere(bm, u_segments=96, v_segments=48, radius=1.0)
for vv in list(bm.verts):
    if vv.co.z < -0.0001: vv.co.z = 0.0
bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=0.0005)
for vv in bm.verts:
    vv.co = top_bun_point(vv.co.x, vv.co.y, vv.co.z)
for f in bm.faces:
    f.material_index = 1 if all(abs(v.co.z - BT_Z) < 1e-5 for v in f.verts) else 0
me = bpy.data.meshes.new("BunTop"); bm.to_mesh(me); bm.free()
bun_t = link(me, "BunTop"); set_mats(bun_t, [M['crust'], M['crumb_toast']]); smooth(bun_t, 60)
# gergelim: menores, mais numerosos, meio afundados
seeds = []
for k in range(78):
    for _ in range(80):
        th = random.uniform(0, 2 * math.pi); ph = random.uniform(0.0, 1.15)
        x, y, z = math.sin(ph) * math.cos(th), math.sin(ph) * math.sin(th), math.cos(ph)
        if z > 0.30: break
    p = top_bun_point(x, y, z)
    nrm = Vector((x / BT_R, y / BT_R, z / BT_H)).normalized()
    p -= nrm * 0.0002
    bpy.ops.mesh.primitive_uv_sphere_add(segments=8, ring_count=5, radius=1.0, location=p)
    s = bpy.context.active_object
    s.scale = (0.0021 * random.uniform(0.85, 1.1), 0.0011, 0.0006)
    s.rotation_mode = 'QUATERNION'
    s.rotation_quaternion = nrm.to_track_quat('Z', 'Y') @ Quaternion((0, 0, 1), random.uniform(0, math.pi))
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    set_mats(s, [M['sesame']]); seeds.append(s)
bpy.ops.object.select_all(action='DESELECT')
for s in seeds: s.select_set(True)
bpy.context.view_layer.objects.active = seeds[0]; bpy.ops.object.join()
sesame = bpy.context.active_object; sesame.name = "Sesame"

LAYERS = [  # (objeto, material do miolo no corte)
    (bun_b, M['crumb']), (patty, M['patty_in']), (cheese, M['cheese']), (tom, M['tomato_in']),
    (lettuce, M['lettuce_in']), (bun_t, M['crumb']), (sesame, M['sesame'])]

# ============================================================== mordidas
def bite_cutter(name, ang_deg, depth, seed):
    """Molde da arcada: perfil de elipse com recortes de dente, empilhado em anéis com ruído — a parede da
    mordida sai irregular (esfarelada), não um corte reto de faca."""
    a0 = math.radians(ang_deg)
    cx, cy = math.cos(a0) * (0.056 + 0.022 - depth), math.sin(a0) * (0.056 + 0.022 - depth)
    bm = bmesh.new()
    n, levels = 72, 14
    rings = []
    for L in range(levels):
        zz = -0.02 + (0.13 * L / (levels - 1))
        ring = []
        for i in range(n):
            t = 2 * math.pi * i / n
            teeth = 1.0 + 0.06 * abs(math.sin(t * 7 + seed)) + 0.05 * noise.noise(Vector((math.cos(t) * 3 + seed, math.sin(t) * 3, zz * 70)))
            # boca: morde mais fundo no meio da altura (pão de cima/baixo resistem mais nas pontas)
            mid = 1.0 + 0.10 * math.exp(-((zz - 0.04) / 0.03) ** 2)
            ex, ey = math.cos(t) * 0.030 * teeth * mid, math.sin(t) * 0.036 * teeth
            x = cx + ex * math.cos(a0) - ey * math.sin(a0)
            y = cy + ex * math.sin(a0) + ey * math.cos(a0)
            ring.append(bm.verts.new((x, y, zz)))
        rings.append(ring)
    bm.faces.new(rings[0][::-1]); bm.faces.new(rings[-1])
    for L in range(levels - 1):
        a_r, b_r = rings[L], rings[L + 1]
        for i in range(n): bm.faces.new((a_r[i], a_r[(i + 1) % n], b_r[(i + 1) % n], b_r[i]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    me = bpy.data.meshes.new(name); bm.to_mesh(me); bm.free()
    ob = link(me, name); ob.hide_render = True; return ob

BITES = [bite_cutter("Bite1", 0, 0.0, 0.3), bite_cutter("Bite2", 38, 0.004, 1.7), bite_cutter("Bite3", -40, 0.006, 2.9)]

def make_state(name, n_bites):
    parts = []
    for ob, inner in LAYERS:
        d = ob.copy(); d.data = ob.data.copy(); bpy.context.collection.objects.link(d)
        for b in BITES[:n_bites]:
            b.data.materials.clear(); b.data.materials.append(inner)
            mod = d.modifiers.new("bite", 'BOOLEAN'); mod.operation = 'DIFFERENCE'; mod.object = b
            mod.solver = 'EXACT'
            try: mod.material_mode = 'TRANSFER'
            except Exception: pass
            bpy.context.view_layer.objects.active = d; bpy.ops.object.modifier_apply(modifier=mod.name)
        if len(d.data.polygons): parts.append(d)
        else: bpy.data.objects.remove(d)
    bpy.ops.object.select_all(action='DESELECT')
    for p in parts: p.select_set(True)
    bpy.context.view_layer.objects.active = parts[0]; bpy.ops.object.join()
    st = bpy.context.active_object; st.name = name; st.data.name = name
    return st

STATES = [make_state("Burger_Full", 0), make_state("Burger_Bite1", 1), make_state("Burger_Bite2", 2), make_state("Burger_Bite3", 3)]
for ob, _ in LAYERS: bpy.data.objects.remove(ob)
for b in BITES: bpy.data.objects.remove(b)

# ============================================================== papel (aberto, amassado)
bm = bmesh.new()
bmesh.ops.create_grid(bm, x_segments=60, y_segments=60, size=0.16)
for vv in bm.verts:
    x, y = vv.co.x, vv.co.y
    r = max(abs(x), abs(y))
    lift = max(0.0, r - 0.10) ** 1.3 * 0.55          # as bordas sobem (o papel ainda guarda a forma da dobra)
    crumple = 0.0025 * noise.noise(Vector((x * 60, y * 60, 0.5))) + 0.0012 * noise.noise(Vector((x * 160, y * 160, 2.0)))
    vv.co.z = 0.0006 + lift + crumple * (0.3 + r * 6)
me = bpy.data.meshes.new("Wrapper"); bm.to_mesh(me); bm.free()
wrapper = link(me, "Wrapper"); set_mats(wrapper, [M['wrapper']])
so = wrapper.modifiers.new("so", 'SOLIDIFY'); so.thickness = 0.0005
bpy.context.view_layer.objects.active = wrapper; bpy.ops.object.modifier_apply(modifier="so"); smooth(wrapper, 70)

ALL = STATES + [wrapper]

# ============================================================== prévias (antes do bake)
def previews():
    sc = bpy.context.scene
    sc.render.resolution_x, sc.render.resolution_y = 900, 700
    sc.cycles.samples = 48
    sc.view_settings.view_transform = 'Standard'   # como o Unity mostra a textura (sem AgX)
    w = bpy.data.worlds.new("w"); sc.world = w; w.use_nodes = True
    w.node_tree.nodes['Background'].inputs['Color'].default_value = (0.32, 0.32, 0.34, 1)
    w.node_tree.nodes['Background'].inputs['Strength'].default_value = 0.45
    bpy.ops.object.light_add(type='AREA', location=(0.25, -0.3, 0.45)); L = bpy.context.active_object
    L.data.energy = 9; L.data.size = 0.4; L.rotation_euler = (math.radians(45), 0, math.radians(40))
    bpy.ops.object.light_add(type='AREA', location=(-0.3, 0.2, 0.25)); L2 = bpy.context.active_object
    L2.data.energy = 3; L2.data.size = 0.5; L2.rotation_euler = (math.radians(60), 0, math.radians(-130))
    bpy.ops.object.camera_add(); cam = bpy.context.active_object; sc.camera = cam
    cam.data.lens = 70
    target = bpy.data.objects.new("t", None); bpy.context.collection.objects.link(target)
    tr = cam.constraints.new('TRACK_TO'); tr.target = target; tr.track_axis = 'TRACK_NEGATIVE_Z'; tr.up_axis = 'UP_Y'
    def shot(fname, show, az, el, dist=0.36, tz=0.04):
        for o in ALL: o.hide_render = o not in show
        target.location = (0, 0, tz)
        a, e = math.radians(az), math.radians(el)
        cam.location = (math.cos(a) * math.cos(e) * dist, math.sin(a) * math.cos(e) * dist, tz + math.sin(e) * dist)
        sc.render.filepath = os.path.join(PREV_DIR, fname); bpy.ops.render.render(write_still=True)
    full = STATES[0]
    if PHOTOS:
        # fotos de cardápio do app (fundo transparente): herói 3/4, de frente e de cima
        sc.render.film_transparent = True
        sc.render.image_settings.color_mode = 'RGBA'
        sc.render.resolution_x, sc.render.resolution_y = 640, 512
        cam.data.lens = 85
        shot("menu_burger_hero.png", [full], 35, 22, dist=0.52)
        shot("menu_burger_front.png", [full], 0, 8, dist=0.5)
        shot("menu_burger_top.png", [full], 60, 50, dist=0.52)
        return
    shot("01_frente.png", [full], 0, 12); shot("02_tras.png", [full], 180, 12)
    shot("03_lado.png", [full], 90, 5); shot("04_tres_quartos.png", [full], 35, 35)
    for i, st in enumerate(STATES[1:], 1):
        shot(f"0{4 + i}_mordida{i}.png", [st], 15, 30)
    shot("08_no_papel.png", [full, wrapper], 30, 40, dist=0.5, tz=0.02)

if PREV_DIR:
    os.makedirs(PREV_DIR, exist_ok=True)
    previews()
    for o in ALL: o.hide_render = False
    if PREVIEW_ONLY: sys.exit(0)

# ============================================================== bake + FBX
# peso no jogo: ~54 mil faces por estágio é demais para um objeto de mão; o relevo fino vai no normal map
for st in STATES:
    dm = st.modifiers.new("dec", 'DECIMATE'); dm.ratio = 0.33
    bpy.context.view_layer.objects.active = st; bpy.ops.object.modifier_apply(modifier="dec")
K.unwrap(ALL, margin=0.004)
bpy.context.scene.cycles.samples = 16
K.bake_all(ALL, TEX_DIR, size=2048)
K.collapse_materials(ALL, "BurgerBaked")
for o in list(bpy.data.objects):
    if o not in ALL: bpy.data.objects.remove(o)
K.export_fbx(OUT_FBX)
print("[build_burger] OK:", OUT_FBX, [o.name for o in ALL], [len(o.data.polygons) for o in ALL])
