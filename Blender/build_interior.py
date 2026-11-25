"""Acabamento do prédio (Atos 3/4, 2026-11-24) — por script, 0 créditos. Três grupos, cada um com seu FBX e atlas:

  kitchen : Stove (fogão 4 bocas com forno), DiningTable, DiningChair, Hoodie (moletom jogado no encosto)
  decor   : PictureFrame_A/B/C (quadros com pintura), Bookshelf (estante com livros), Doormat, Extinguisher
            (extintor com suporte), ExtinguisherSign (placa), ElectricPanel (quadro de luz)
  arch    : WindowFrame_Living / _Bedroom / _Bath (caixilhos de alumínio + peitoril), BalconyRailing (corrimão e capa
            de granito na mureta), BalconyDoor (porta de correr de 3 folhas, aberta) + BalconyDoorGlass (vidros, à parte)

Convenção: móveis de chão — origem no centro da BASE, frente para -Y do Blender (= +Z no Unity).
           de parede — origem no centro da face de TRÁS (encostada na parede), frente para -Y.
           caixilhos — largura em X centrada, altura em Z a partir de 0 (base do vão), espessura em Y centrada.
Uso: blender --background --python build_interior.py -- <grupo> <saida.fbx> <pasta_texturas> [<pasta_previas>]
"""
import bpy, bmesh, sys, math, os, random
from mathutils import Vector, noise
sys.path.append(os.path.dirname(os.path.abspath(__file__)))
import realistic_kit as K

argv = sys.argv[sys.argv.index("--") + 1:]
GROUP, OUT_FBX, TEX_DIR = argv[0], argv[1], argv[2]
PREV_DIR = argv[3] if len(argv) > 3 else None
random.seed(42)
K.reset_scene()

def lin(c): return tuple(x ** 2.2 for x in c)
def box(x0, x1, y0, y1, z0, z1, mat, bevel=0.0, seg=2):
    return K.box(min(x0, x1), max(x0, x1), min(y0, y1), max(y0, y1), min(z0, z1), max(z0, z1), mat, bevel=bevel, segments=seg)
def cyl(x, y, z, r, d, mat, axis='Z', verts=24, bevel=0.0): return K.cyl(x, y, z, r, d, mat, axis=axis, verts=verts, bevel=bevel)

def simple_mat(name, nodes_fn):
    m = bpy.data.materials.new(name); m.use_nodes = True
    nt = m.node_tree; nt.nodes.clear()
    out = nt.nodes.new('ShaderNodeOutputMaterial'); p = nt.nodes.new('ShaderNodeBsdfPrincipled')
    nt.links.new(p.outputs[0], out.inputs['Surface'])
    tc = nt.nodes.new('ShaderNodeTexCoord')
    nodes_fn(nt, p, tc)
    K.MATS[name] = m
    return m

def ramp(nt, fac, stops):
    r = nt.nodes.new('ShaderNodeValToRGB'); cr = r.color_ramp
    while len(cr.elements) < len(stops): cr.elements.new(0.5)
    for el, (pos, col) in zip(cr.elements, stops): el.position = pos; el.color = (*lin(col), 1.0)
    nt.links.new(fac, r.inputs['Fac']); return r.outputs['Color']

def noise_tex(nt, vec, scale, detail=6.0, sx=1.0, sy=1.0, sz=1.0):
    mp = nt.nodes.new('ShaderNodeMapping'); mp.inputs['Scale'].default_value = (sx, sy, sz)
    nt.links.new(vec, mp.inputs['Vector'])
    n = nt.nodes.new('ShaderNodeTexNoise'); n.inputs['Scale'].default_value = scale; n.inputs['Detail'].default_value = detail
    nt.links.new(mp.outputs[0], n.inputs['Vector']); return n.outputs['Fac']

def finish_group(objs_by_name):
    return [K.join(v, k) if len(v) > 1 else rename(v[0], k) for k, v in objs_by_name.items()]

def rename(o, n): o.name = n; o.data.name = n; return o

# ====================================================================== KITCHEN
def build_kitchen():
    enamel = K.material("Enamel", lin((0.90, 0.89, 0.86)), rough=0.22, dirt=0.25, dirt_color=lin((0.45, 0.40, 0.33)), color_var=0.03)
    blackglass = K.material("StoveGlass", lin((0.04, 0.04, 0.045)), rough=0.08)
    steel = K.material("SteelB", lin((0.74, 0.74, 0.76)), rough=0.3, metal=1.0, dirt=0.2, color_var=0.05)
    iron = K.material("CastIron", lin((0.08, 0.08, 0.08)), rough=0.55, metal=0.6, dirt=0.2)
    knobm = K.material("Knob", lin((0.12, 0.12, 0.13)), rough=0.4)
    wood = K.material("TableWood", lin((0.52, 0.34, 0.20)), rough=0.45, wood=True, dirt=0.15, wear=0.12, wear_color=lin((0.65, 0.48, 0.32)), color_var=0.08)
    legm = K.material("ChairWood", lin((0.36, 0.22, 0.13)), rough=0.5, wood=True, dirt=0.18, wear=0.15, wear_color=lin((0.55, 0.40, 0.26)))
    seatm = K.material("SeatFabric", lin((0.42, 0.40, 0.36)), rough=0.85, bump=0.4, bump_scale=300.0, dirt=0.2, color_var=0.06)
    cloth = K.material("Hoodie", lin((0.20, 0.24, 0.32)), rough=0.9, bump=0.5, bump_scale=260.0, dirt=0.15, color_var=0.08)

    parts = {}
    # --- fogão (0,55 x 0,60 x 0,90), frente para -Y
    W, D, H = 0.55, 0.60, 0.88
    s = len(K.PARTS)
    box(-W / 2, W / 2, -D / 2 + 0.02, D / 2, 0.06, H - 0.04, enamel, 0.008)                    # corpo
    box(-W / 2 + 0.01, W / 2 - 0.01, -D / 2 + 0.02, D / 2 - 0.02, 0.0, 0.06, iron, 0.004)      # rodapé
    box(-W / 2, W / 2, -D / 2, D / 2, H - 0.04, H - 0.025, steel, 0.004)                       # mesa inox
    for (bx, by, r) in ((-0.13, -0.12, 0.05), (0.13, -0.12, 0.04), (-0.13, 0.14, 0.04), (0.13, 0.14, 0.055)):
        cyl(bx, by, H - 0.02, r, 0.012, iron, 'Z', 24, 0.002)                                   # queimadores
        cyl(bx, by, H - 0.012, r * 0.55, 0.008, iron, 'Z', 16)
        for k in range(4):                                                                       # grelha
            a = k * math.pi / 4
            box(bx - math.cos(a) * 0.09, bx + math.cos(a) * 0.09, by - math.sin(a) * 0.09 - 0.004, by + math.sin(a) * 0.09 + 0.004, H - 0.018, H - 0.006, iron) if k % 2 == 0 else None
        box(bx - 0.09, bx + 0.09, by - 0.004, by + 0.004, H - 0.018, H - 0.006, iron)
        box(bx - 0.004, bx + 0.004, by - 0.09, by + 0.09, H - 0.018, H - 0.006, iron)
    box(-W / 2, W / 2, -D / 2 - 0.01, -D / 2 + 0.03, H - 0.16, H - 0.04, enamel, 0.006)          # painel dos botões
    for k in range(5):
        cyl(-0.2 + k * 0.1, -D / 2 - 0.015, H - 0.10, 0.018, 0.03, knobm, 'Y', 20, 0.003)
    box(-W / 2 + 0.03, W / 2 - 0.03, -D / 2 - 0.005, -D / 2 + 0.02, 0.10, H - 0.20, enamel, 0.01)   # porta do forno
    box(-W / 2 + 0.08, W / 2 - 0.08, -D / 2 - 0.007, -D / 2, 0.22, H - 0.30, blackglass, 0.004)     # visor
    cyl(0, -D / 2 - 0.045, H - 0.235, 0.011, W - 0.14, steel, 'X', 16, 0.002)                      # puxador
    box(-W / 2 + 0.08, -W / 2 + 0.10, -D / 2 - 0.045, -D / 2, H - 0.245, H - 0.225, steel)
    box(W / 2 - 0.10, W / 2 - 0.08, -D / 2 - 0.045, -D / 2, H - 0.245, H - 0.225, steel)
    parts["Stove"] = K.PARTS[s:]

    # --- mesa (0,90 x 0,70 x 0,76)
    s = len(K.PARTS)
    box(-0.45, 0.45, -0.35, 0.35, 0.725, 0.76, wood, 0.006, 3)
    box(-0.40, 0.40, -0.30, -0.28, 0.65, 0.725, wood, 0.003); box(-0.40, 0.40, 0.28, 0.30, 0.65, 0.725, wood, 0.003)
    box(-0.42, -0.40, -0.30, 0.30, 0.65, 0.725, wood, 0.003); box(0.40, 0.42, -0.30, 0.30, 0.65, 0.725, wood, 0.003)
    for (lx, ly) in ((-0.40, -0.30), (0.40, -0.30), (-0.40, 0.30), (0.40, 0.30)):
        box(lx - 0.025, lx + 0.025, ly - 0.025, ly + 0.025, 0.0, 0.725, wood, 0.004)
    parts["DiningTable"] = K.PARTS[s:]

    # --- cadeira (assento 0,45; encosto até 0,90), frente para -Y, encosto em +Y
    def chair():
        s = len(K.PARTS)
        for (lx, ly) in ((-0.19, -0.19), (0.19, -0.19)):
            box(lx - 0.018, lx + 0.018, ly - 0.018, ly + 0.018, 0.0, 0.43, legm, 0.004)
        for lx in (-0.19, 0.19):
            box(lx - 0.018, lx + 0.018, 0.17, 0.206, 0.0, 0.90, legm, 0.004)                         # pernas de trás = montantes do encosto
        box(-0.215, 0.215, -0.215, 0.215, 0.43, 0.455, legm, 0.004)                                   # assento (base)
        box(-0.20, 0.20, -0.20, 0.17, 0.455, 0.48, seatm, 0.012, 3)                                   # estofado
        for z in (0.62, 0.80):
            box(-0.19, 0.19, 0.172, 0.204, z, z + 0.06 if z > 0.7 else z + 0.04, legm, 0.006)            # travessas do encosto
        box(-0.19, 0.19, -0.19, -0.172, 0.12, 0.15, legm); box(-0.19, 0.19, 0.172, 0.19, 0.12, 0.15, legm)
        return K.PARTS[s:]
    parts["DiningChair"] = chair()

    # --- moletom jogado no encosto (tecido dobrado sobre a travessa de cima, mangas penduradas)
    s = len(K.PARTS)
    bm = bmesh.new()
    nu, nv = 24, 40
    top_y, top_z = 0.188, 0.87
    rows = []
    for j in range(nv + 1):
        t = j / nv                       # 0 = frente embaixo, 1 = atrás embaixo
        # caminho: desce pela frente do encosto, passa por cima da travessa, desce por trás
        if t < 0.45:
            k = t / 0.45; y = top_y - 0.03 - 0.02 * math.sin(k * 3); z = top_z - 0.38 * (1 - k)
        elif t < 0.55:
            k = (t - 0.45) / 0.10; a = math.pi * k; y = top_y - 0.03 * math.cos(a); z = top_z + 0.03 * math.sin(a) + 0.02
        else:
            k = (t - 0.55) / 0.45; y = top_y + 0.03 + 0.03 * math.sin(k * 2.5); z = top_z - 0.46 * k
        row = []
        hang = min(1.0, abs(t - 0.5) / 0.45)            # 0 no alto do encosto, 1 nas barras
        for i in range(nu + 1):
            u = i / nu
            # tecido largado: estreita e torce para baixo, dobras verticais fundas, barra irregular
            x = (-0.24 + 0.48 * u) * (1.0 - 0.18 * hang) + 0.03 * hang * noise.noise(Vector((t * 3, 4.0, 0.5)))
            w = 0.03 * noise.noise(Vector((x * 6, t * 5, 1.0))) + 0.01 * noise.noise(Vector((x * 22, t * 16, 2.0)))
            w += 0.014 * hang * math.sin(x * 38 + 1.7 * noise.noise(Vector((x * 3, t * 2, 3.0))))
            sag = 0.02 * math.sin(u * math.pi) * (1 if t < 0.45 or t > 0.55 else 0)
            dz = hang ** 2 * (0.05 * noise.noise(Vector((x * 5, 7.0, 1.0 if t < 0.5 else 2.0))) + 0.03 * math.sin(u * math.pi))
            row.append(bm.verts.new((x, y + w + (sag if t > 0.5 else -sag), z + dz)))
        rows.append(row)
    for j in range(nv):
        for i in range(nu): bm.faces.new((rows[j][i], rows[j][i + 1], rows[j + 1][i + 1], rows[j + 1][i]))
    me = bpy.data.meshes.new("HoodieBody"); bm.to_mesh(me); bm.free()
    ob = bpy.data.objects.new("HoodieBody", me); bpy.context.collection.objects.link(ob)
    so = ob.modifiers.new("so", 'SOLIDIFY'); so.thickness = 0.008
    sb = ob.modifiers.new("sb", 'SUBSURF'); sb.levels = 1
    bpy.context.view_layer.objects.active = ob; bpy.ops.object.modifier_apply(modifier="so"); bpy.ops.object.modifier_apply(modifier="sb")
    for p in ob.data.polygons: p.use_smooth = True
    K._finish(ob, cloth)
    for sx in (-1, 1):                                                                       # mangas penduradas na frente
        cyl(sx * 0.17, top_y - 0.06, top_z - 0.20, 0.045, 0.30, cloth, 'Z', 16, 0.01)   # de 0,52 a 0,82
        cyl(sx * 0.17, top_y - 0.06, top_z - 0.365, 0.04, 0.04, cloth, 'Z', 16, 0.01)   # punho apoiado no assento
    parts["Hoodie"] = K.PARTS[s:]
    return finish_group(parts)

# ====================================================================== DECOR
def art_material(name, kind):
    def fn(nt, p, tc):
        g = tc.outputs['Generated']
        sep = nt.nodes.new('ShaderNodeSeparateXYZ'); nt.links.new(g, sep.inputs[0])
        if kind == 0:   # paisagem: céu em degradê, mar, sol
            sky = ramp(nt, sep.outputs['Z'], [(0.0, (0.15, 0.20, 0.30)), (0.45, (0.25, 0.42, 0.55)), (0.55, (0.90, 0.62, 0.40)), (1.0, (0.35, 0.45, 0.65))])
            nt.links.new(sky, p.inputs['Base Color'])
        elif kind == 1: # abstrato: blocos de cor (Voronoi)
            v = nt.nodes.new('ShaderNodeTexVoronoi'); v.inputs['Scale'].default_value = 3.5
            nt.links.new(g, v.inputs['Vector'])
            c = ramp(nt, v.outputs['Distance'], [(0.0, (0.80, 0.35, 0.20)), (0.3, (0.92, 0.78, 0.50)), (0.6, (0.20, 0.35, 0.40)), (1.0, (0.10, 0.10, 0.12))])
            nt.links.new(c, p.inputs['Base Color'])
        else:           # botânico: folhas escuras em fundo creme
            n = nt.nodes.new('ShaderNodeTexWave'); n.inputs['Scale'].default_value = 5.0; n.inputs['Distortion'].default_value = 12.0
            nt.links.new(g, n.inputs['Vector'])
            c = ramp(nt, n.outputs['Fac'], [(0.0, (0.92, 0.88, 0.78)), (0.55, (0.90, 0.86, 0.75)), (0.62, (0.22, 0.38, 0.22)), (1.0, (0.12, 0.25, 0.15))])
            nt.links.new(c, p.inputs['Base Color'])
        p.inputs['Roughness'].default_value = 0.7
    return simple_mat(name, fn)

def build_decor():
    framewood = K.material("FrameWood", lin((0.18, 0.12, 0.08)), rough=0.4, wood=True, wear=0.1, wear_color=lin((0.4, 0.3, 0.2)))
    passe = K.material("Passepartout", lin((0.93, 0.92, 0.88)), rough=0.85)
    shelfw = K.material("ShelfWood", lin((0.62, 0.48, 0.34)), rough=0.5, wood=True, dirt=0.12, color_var=0.06)
    coir = simple_mat("Coir", lambda nt, p, tc: (nt.links.new(ramp(nt, noise_tex(nt, tc.outputs['Object'], 400.0, 12.0, 1, 6, 1), [(0.3, (0.38, 0.25, 0.13)), (0.7, (0.62, 0.45, 0.26))]), p.inputs['Base Color']), setattr(p.inputs['Roughness'], 'default_value', 0.95)))
    rubber = K.material("MatRubber", lin((0.07, 0.07, 0.07)), rough=0.8)
    red = K.material("ExtRed", lin((0.72, 0.05, 0.04)), rough=0.25, dirt=0.15, color_var=0.04)
    black = K.material("ExtBlack", lin((0.05, 0.05, 0.05)), rough=0.5)
    steel = K.material("SteelC", lin((0.7, 0.7, 0.72)), rough=0.32, metal=1.0, dirt=0.25)
    white = K.material("SignWhite", lin((0.95, 0.95, 0.93)), rough=0.6)
    panel = K.material("PanelGrey", lin((0.62, 0.63, 0.63)), rough=0.5, metal=0.3, dirt=0.35, dirt_color=lin((0.3, 0.28, 0.25)), wear=0.12, wear_color=lin((0.45, 0.45, 0.45)))
    yellow = K.material("WarnYellow", lin((0.95, 0.78, 0.10)), rough=0.5)
    parts = {}

    # --- quadros (de parede): moldura + passe-partout + pintura
    for idx, (w, h) in enumerate(((0.50, 0.70), (0.60, 0.42), (0.42, 0.56))):
        s = len(K.PARTS)
        fw, fd = 0.035, 0.025
        box(-w / 2, w / 2, -fd, 0, h - fw, h, framewood, 0.004); box(-w / 2, w / 2, -fd, 0, 0, fw, framewood, 0.004)
        box(-w / 2, -w / 2 + fw, -fd, 0, 0, h, framewood, 0.004); box(w / 2 - fw, w / 2, -fd, 0, 0, h, framewood, 0.004)
        box(-w / 2 + fw, w / 2 - fw, -0.012, -0.004, fw, h - fw, passe)
        art = box(-w / 2 + fw + 0.05, w / 2 - fw - 0.05, -0.0135, -0.012, fw + 0.05, h - fw - 0.05, art_material(f"Art{idx}", idx))
        parts[f"PictureFrame_{'ABC'[idx]}"] = K.PARTS[s:]

    # --- estante (0,80 x 0,30 x 1,80) com livros
    s = len(K.PARTS)
    W, D, H = 0.80, 0.30, 1.80
    box(-W / 2, -W / 2 + 0.02, -D / 2, D / 2, 0, H, shelfw, 0.003); box(W / 2 - 0.02, W / 2, -D / 2, D / 2, 0, H, shelfw, 0.003)
    box(-W / 2, W / 2, D / 2 - 0.01, D / 2, 0, H, shelfw)                                       # fundo
    levels = [0.0, 0.38, 0.74, 1.10, 1.46, H - 0.02]
    for z in levels: box(-W / 2 + 0.02, W / 2 - 0.02, -D / 2, D / 2 - 0.01, z, z + 0.02, shelfw, 0.002)
    palette = [(0.55, 0.12, 0.10), (0.12, 0.22, 0.40), (0.15, 0.35, 0.22), (0.85, 0.78, 0.60), (0.25, 0.25, 0.27), (0.70, 0.45, 0.15), (0.40, 0.20, 0.35)]
    bookmats = [K.material(f"Book{i}", lin(c), rough=0.6, color_var=0.08) for i, c in enumerate(palette)]
    for li, z in enumerate(levels[:-1]):
        x = -W / 2 + 0.03
        while x < W / 2 - 0.08:
            if random.random() < 0.08: x += random.uniform(0.04, 0.10); continue
            t = random.uniform(0.02, 0.045); hh = random.uniform(0.20, 0.30); dd = random.uniform(0.15, 0.22)
            lean = 0.0
            b = box(x, x + t, -D / 2 + 0.02, -D / 2 + 0.02 + dd, z + 0.02, z + 0.02 + hh, random.choice(bookmats), 0.002)
            x += t + 0.002
        if li == 2:   # uma pilha deitada
            for k in range(3):
                box(W / 2 - 0.25, W / 2 - 0.05, -D / 2 + 0.03, -D / 2 + 0.2, z + 0.02 + k * 0.035, z + 0.05 + k * 0.035, random.choice(bookmats), 0.002)
    parts["Bookshelf"] = K.PARTS[s:]

    # --- capacho (0,60 x 0,40)
    s = len(K.PARTS)
    box(-0.30, 0.30, -0.20, 0.20, 0.0, 0.006, rubber, 0.003)
    box(-0.28, 0.28, -0.18, 0.18, 0.006, 0.018, coir, 0.004)
    parts["Doormat"] = K.PARTS[s:]

    # --- extintor (parede): suporte + cilindro vermelho + válvula + mangueira + rótulo
    s = len(K.PARTS)
    box(-0.06, 0.06, -0.012, 0, 0.0, 0.20, steel, 0.003)                                        # suporte na parede
    cyl(0, -0.09, 0.26, 0.075, 0.42, red, 'Z', 32, 0.01)                                          # corpo (de 0,05 a 0,47; cyl é centrado)
    bpy.ops.mesh.primitive_uv_sphere_add(segments=32, ring_count=16, radius=0.075, location=(0, -0.09, 0.47))
    o = bpy.context.active_object; o.scale = (1, 1, 0.35); bpy.ops.object.transform_apply(scale=True); K._finish(o, red)
    cyl(0, -0.09, 0.52, 0.02, 0.06, black, 'Z', 16)                                               # válvula
    box(-0.012, 0.012, -0.12, -0.06, 0.55, 0.565, black, 0.002); box(-0.012, 0.012, -0.13, -0.07, 0.575, 0.585, black, 0.002)
    cyl(0.03, -0.112, 0.545, 0.016, 0.006, white, 'Y', 20)                                         # manômetro
    curve = bpy.data.curves.new("hose", 'CURVE'); curve.dimensions = '3D'; curve.bevel_depth = 0.008
    sp = curve.splines.new('BEZIER'); sp.bezier_points.add(2)
    for pt, co in zip(sp.bezier_points, ((-0.02, -0.12, 0.53), (-0.09, -0.15, 0.35), (-0.05, -0.16, 0.15))):
        pt.co = co; pt.handle_left_type = pt.handle_right_type = 'AUTO'
    ho = bpy.data.objects.new("hose", curve); bpy.context.collection.objects.link(ho)
    bpy.context.view_layer.objects.active = ho; ho.select_set(True); bpy.ops.object.convert(target='MESH'); K._finish(bpy.context.active_object, black)
    box(-0.05, 0.05, -0.166, -0.162, 0.20, 0.33, white)                                            # rótulo
    parts["Extinguisher"] = K.PARTS[s:]

    # --- placa do extintor
    s = len(K.PARTS)
    box(-0.10, 0.10, -0.004, 0, 0.0, 0.30, red, 0.002)
    t = K.text("EXTINTOR", 0, -0.0045, 0.05, 0.03, white, rot=(math.pi / 2, 0, 0), extrude=0.0006)
    box(-0.025, 0.025, -0.0048, -0.004, 0.12, 0.25, white)                                         # silhueta do extintor
    parts["ExtinguisherSign"] = K.PARTS[s:]

    # --- quadro de luz (0,40 x 0,55 x 0,12)
    s = len(K.PARTS)
    box(-0.20, 0.20, -0.12, 0, 0.0, 0.55, panel, 0.006, 3)
    box(-0.185, 0.185, -0.123, -0.12, 0.015, 0.535, panel, 0.003)                                  # porta
    box(0.15, 0.17, -0.135, -0.123, 0.24, 0.31, black, 0.003)                                      # trinco
    box(-0.06, 0.06, -0.124, -0.1235, 0.40, 0.48, yellow)                                          # adesivo de perigo
    K.text("QUADRO DE LUZ", 0, -0.1245, 0.08, 0.025, black, rot=(math.pi / 2, 0, 0), extrude=0.0005)
    K.text("PERIGO", 0, -0.1245, 0.415, 0.022, black, rot=(math.pi / 2, 0, 0), extrude=0.0005)
    parts["ElectricPanel"] = K.PARTS[s:]
    return finish_group(parts)

# ====================================================================== ARCH
WINDOWS = {  # largura x altura do VIDRO atual (SceneDump 2026-11-24) e número de folhas
    "WindowFrame_Living": (4.47, 0.83, 4),
    "WindowFrame_Bedroom": (2.03, 0.96, 2),
    "WindowFrame_Bath": (1.01, 1.04, 1),
}
def build_arch():
    alu = K.material("Aluminum", lin((0.78, 0.78, 0.78)), rough=0.35, metal=1.0, dirt=0.3, dirt_color=lin((0.35, 0.33, 0.30)), color_var=0.04)
    granite = K.material("SillGranite", lin((0.55, 0.53, 0.50)), rough=0.3, dirt=0.2, color_var=0.15, bump=0.1, bump_scale=200.0)
    glassm = K.material("GlassPreview", lin((0.6, 0.7, 0.75)), rough=0.05)
    parts = {}
    for name, (W, H, n) in WINDOWS.items():
        s = len(K.PARTS)
        f, d = 0.045, 0.07                                                                   # marco DENTRO do vão (W x H = vidro atual)
        box(-W / 2, W / 2, -d / 2, d / 2, H - f, H, alu, 0.003); box(-W / 2, W / 2, -d / 2, d / 2, 0, f, alu, 0.003)
        box(-W / 2, -W / 2 + f, -d / 2, d / 2, f, H - f, alu, 0.003); box(W / 2 - f, W / 2, -d / 2, d / 2, f, H - f, alu, 0.003)
        X0, X1, Z0, Z1 = -W / 2 + f, W / 2 - f, f, H - f
        if n == 1:   # maxim-ar: uma folha com moldura e alavanca
            ff = 0.032
            box(X0, X1, -0.012, 0.012, Z1 - ff, Z1, alu, 0.002); box(X0, X1, -0.012, 0.012, Z0, Z0 + ff, alu, 0.002)
            box(X0, X0 + ff, -0.012, 0.012, Z0, Z1, alu, 0.002); box(X1 - ff, X1, -0.012, 0.012, Z0, Z1, alu, 0.002)
            box(-0.06, 0.06, -0.03, -0.012, Z0 + 0.008, Z0 + 0.024, alu, 0.004)
        else:        # de correr: folhas em trilhos alternados, sobrepostas 2 cm
            sw = (X1 - X0) / n
            for k in range(n):
                x0, x1 = X0 + k * sw - (0.01 if k else 0), X0 + (k + 1) * sw + (0.01 if k < n - 1 else 0)
                y0 = -0.026 if k % 2 == 0 else 0.004
                ff = 0.03
                box(x0, x1, y0, y0 + 0.022, Z1 - ff, Z1, alu, 0.002); box(x0, x1, y0, y0 + 0.022, Z0, Z0 + ff, alu, 0.002)
                box(x0, x0 + ff, y0, y0 + 0.022, Z0, Z1, alu, 0.002); box(x1 - ff, x1, y0, y0 + 0.022, Z0, Z1, alu, 0.002)
                hx = x1 - ff if k % 2 == 0 else x0 + ff + 0.012
                box(hx - 0.012, hx, y0 - 0.012 if k % 2 == 0 else y0 + 0.022, y0 if k % 2 == 0 else y0 + 0.034, Z0 + (Z1 - Z0) * 0.4, Z0 + (Z1 - Z0) * 0.6, alu, 0.002)
        box(-W / 2 - 0.06, W / 2 + 0.06, -0.13, 0.13, -0.025, 0.0, granite, 0.004)          # peitoril (sai 5 cm dos dois lados da parede de 15 cm)
        parts[name] = K.PARTS[s:]

    # --- guarda-corpo da sacada: capa de granito na mureta (1,05 m) + corrimão baixo de alumínio (comprimento em X)
    L = 1.49
    s = len(K.PARTS)
    box(-L / 2, L / 2, -0.11, 0.11, 0.0, 0.03, granite, 0.004)
    n_posts = 4
    for k in range(n_posts):
        x = -L / 2 + 0.08 + k * (L - 0.16) / (n_posts - 1)
        cyl(x, 0, 0.08, 0.018, 0.10, alu, 'Z', 16)                                            # de 0,03 a 0,13 (cyl é centrado)
        cyl(x, 0, 0.033, 0.032, 0.006, alu, 'Z', 20)
    cyl(0, 0, 0.145, 0.025, L - 0.02, alu, 'X', 24, 0.004)
    parts["BalconyRailing"] = K.PARTS[s:]

    # --- porta de correr da sacada: vão 1,35 x 2,01, três folhas empilhadas na ponta -X (aberta)
    W, H = 1.35, 2.01
    s = len(K.PARTS)
    f, d = 0.045, 0.12
    box(-W / 2, W / 2, -d / 2, d / 2, H - f, H, alu, 0.003); box(-W / 2, W / 2, -d / 2, d / 2, 0, 0.012, alu, 0.002)   # marco e trilho
    for y in (-0.035, 0.0, 0.035): box(-W / 2, W / 2, y - 0.003, y + 0.003, 0.012, 0.022, alu)
    box(-W / 2, -W / 2 + f, -d / 2, d / 2, 0, H, alu, 0.003); box(W / 2 - f, W / 2, -d / 2, d / 2, 0, H, alu, 0.003)
    pw = 0.46
    glass_parts = []
    for k, y in enumerate((-0.035, 0.0, 0.035)):
        x0 = -W / 2 + f + k * 0.015; x1 = x0 + pw; ff = 0.04
        box(x0, x1, y - 0.012, y + 0.012, H - f - ff, H - f, alu, 0.002); box(x0, x1, y - 0.012, y + 0.012, 0.022, 0.022 + ff * 2, alu, 0.002)
        box(x0, x0 + ff, y - 0.012, y + 0.012, 0.022, H - f, alu, 0.002); box(x1 - ff, x1, y - 0.012, y + 0.012, 0.022, H - f, alu, 0.002)
        box(x1 - ff - 0.015, x1 - ff, y - 0.03, y - 0.012, 0.95, 1.10, alu, 0.003)                                  # puxador
    parts["BalconyDoor"] = K.PARTS[s:]
    s = len(K.PARTS)
    for k, y in enumerate((-0.035, 0.0, 0.035)):
        x0 = -W / 2 + f + k * 0.015; x1 = x0 + pw; ff = 0.04
        box(x0 + ff, x1 - ff, y - 0.003, y + 0.003, 0.022 + ff * 2, H - f - ff, glassm)
    parts["BalconyDoorGlass"] = K.PARTS[s:]
    return finish_group(parts)

ALL = {"kitchen": build_kitchen, "decor": build_decor, "arch": build_arch}[GROUP]()

# ------------------------------------------------------------------ prévia (uma imagem com tudo enfileirado)
if PREV_DIR:
    os.makedirs(PREV_DIR, exist_ok=True)
    sc = bpy.context.scene; sc.render.resolution_x, sc.render.resolution_y = 1400, 800; sc.cycles.samples = 32
    sc.view_settings.view_transform = 'Standard'
    w = bpy.data.worlds.new("w"); sc.world = w; w.use_nodes = True
    w.node_tree.nodes['Background'].inputs['Color'].default_value = (0.35, 0.35, 0.37, 1); w.node_tree.nodes['Background'].inputs['Strength'].default_value = 0.8
    x = 0.0; offs = []
    for o in ALL:
        dims = o.dimensions; o.location.x = x + dims.x / 2; offs.append(o); x += dims.x + 0.25
    bpy.ops.object.light_add(type='SUN', rotation=(math.radians(50), 0, math.radians(30))); bpy.context.active_object.data.energy = 3.0
    bpy.ops.object.camera_add(); cam = bpy.context.active_object; sc.camera = cam
    cam.data.type = 'ORTHO'; cam.data.ortho_scale = max(x, 2.2) * 1.05
    cam.location = (x / 2, -6, 1.2 + (0.6 if GROUP == 'arch' else 0)); cam.rotation_euler = (math.radians(84), 0, 0)
    sc.render.filepath = os.path.join(PREV_DIR, GROUP + ".png"); bpy.ops.render.render(write_still=True)
    for o in ALL: o.location = (0, 0, 0)
    for o in list(bpy.data.objects):
        if o.type in ('LIGHT', 'CAMERA'): bpy.data.objects.remove(o)

# ------------------------------------------------------------------ bake + FBX
for o in list(bpy.data.objects):
    if o not in ALL: bpy.data.objects.remove(o)
K.unwrap(ALL, margin=0.004)
bpy.context.scene.cycles.samples = 16
K.bake_all(ALL, TEX_DIR, size=2048)
K.collapse_materials(ALL, GROUP + "Baked")
K.export_fbx(OUT_FBX)
print(f"[build_interior] OK {GROUP}:", OUT_FBX, [(o.name, len(o.data.polygons)) for o in ALL])
