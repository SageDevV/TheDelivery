"""Acabamento do PESADELO (Prólogo, 2026-11-25) — por script, 0 créditos. O corredor do pesadelo é o corredor do prédio
(Edifício Porto Sereno) SONHADO: começa normal demais, repete (302, 302, 302...) e se desmancha até a beira do abismo.

  hall  : PendantFixture (calha fluorescente pendurada em correntes longas; difusor emissivo), WallClock (relógio SEM
          ponteiros), PictureFrame_A/B/C (fotos com o rosto borrado), MissingPoster (o cartaz da Helena, igual ao da
          Recepção), MissingPosterTorn (rasgado), StickyNotes (post-its de estudo: "ACORDA"), ElevatorFront (porta do
          elevador fechada + indicador "3", na parede do fundo do spawn), NumberPlate_3O2 (placa de número errada)
  decay : ClawMarks (4 sulcos de garra), WallCrack (rachadura ramificada), PaperSheet (folha de caderno no chão),
          PaperBall (folha amassada), Notebook (caderno aberto, espiral), WallChunk_A/B/C (pedaços de parede:
          reboco + tijolo), BrokenEdge (beira do piso quebrada, com ferragem)

Convenção (igual a build_reception.py): frente para -Y do Blender (= +Z no Unity).
  chão — origem no centro da BASE; parede — origem no centro da face de TRÁS, embaixo; teto — origem no ponto de
  fixação (em cima), pendurado para -Z.
Uso: blender --background --python build_nightmare.py -- <grupo> <saida.fbx> <pasta_texturas> [<pasta_previas>] [preview_only]
"""
import bpy, bmesh, sys, math, os, random
from mathutils import Vector
sys.path.append(os.path.dirname(os.path.abspath(__file__)))
import realistic_kit as K

argv = sys.argv[sys.argv.index("--") + 1:]
GROUP, OUT_FBX, TEX_DIR = argv[0], argv[1], argv[2]
PREV_DIR = argv[3] if len(argv) > 3 and argv[3] != "-" else None
PREVIEW_ONLY = len(argv) > 4 and argv[4] == "preview_only"
random.seed(11)
K.reset_scene()

def lin(c): return tuple(x ** 2.2 for x in c)
def box(x0, x1, y0, y1, z0, z1, mat, bevel=0.0, seg=2):
    return K.box(min(x0, x1), max(x0, x1), min(y0, y1), max(y0, y1), min(z0, z1), max(z0, z1), mat, bevel=bevel, segments=seg)
def cyl(x, y, z, r, d, mat, axis='Z', verts=24, bevel=0.0): return K.cyl(x, y, z, r, d, mat, axis=axis, verts=verts, bevel=bevel)
FRONT = (math.pi / 2, 0, 0)
def text(s, x, y, z, size, mat, extrude=0.0, align='CENTER', rot=FRONT):
    """Texto CHATO e de baixa resolução (o cartaz repete dezenas de vezes no corredor: com relevo e curva fina,
    cada um tinha 29 mil polígonos). O relevo de 0,2 mm não aparece; a curva com 3 segmentos lê igual."""
    bpy.ops.object.text_add(location=(x, y, z), rotation=rot)
    ob = bpy.context.active_object
    ob.data.body = s; ob.data.size = size; ob.data.extrude = 0.0; ob.data.resolution_u = 3
    ob.data.align_x = align; ob.data.align_y = 'CENTER'
    bpy.ops.object.convert(target='MESH')
    return K._finish(bpy.context.active_object, mat)

def simple_mat(name, fn):
    m = bpy.data.materials.new(name); m.use_nodes = True
    nt = m.node_tree; nt.nodes.clear()
    out = nt.nodes.new('ShaderNodeOutputMaterial'); p = nt.nodes.new('ShaderNodeBsdfPrincipled')
    nt.links.new(p.outputs[0], out.inputs['Surface'])
    fn(nt, p, nt.nodes.new('ShaderNodeTexCoord'))
    K.MATS[name] = m
    return m
def ramp(nt, fac, stops):
    r = nt.nodes.new('ShaderNodeValToRGB'); cr = r.color_ramp
    while len(cr.elements) < len(stops): cr.elements.new(0.5)
    for el, (pos, col) in zip(cr.elements, stops): el.position = pos; el.color = (*lin(col), 1.0)
    nt.links.new(fac, r.inputs['Fac']); return r.outputs['Color']
def noise_fac(nt, vec, scale, detail=6.0, sx=1.0, sy=1.0, sz=1.0, distortion=0.0):
    mp = nt.nodes.new('ShaderNodeMapping'); mp.inputs['Scale'].default_value = (sx, sy, sz)
    nt.links.new(vec, mp.inputs['Vector'])
    n = nt.nodes.new('ShaderNodeTexNoise'); n.inputs['Scale'].default_value = scale; n.inputs['Detail'].default_value = detail
    n.inputs['Distortion'].default_value = distortion
    nt.links.new(mp.outputs[0], n.inputs['Vector']); return n.outputs['Fac']
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
    from mathutils import Matrix
    M = Matrix.Translation(Vector(pivot)) @ Matrix.Rotation(angle, 4, axis) @ Matrix.Translation(-Vector(pivot))
    for o in parts: o.matrix_world = M @ o.matrix_world
def strip(points, width, y, mat, depth=0.0015, taper=True):
    """Faixa fina no plano XZ (em y, frente -Y) seguindo uma polilinha — sulco, rachadura, risco."""
    bm = bmesh.new(); n = len(points)
    left, right = [], []
    for i, (px, pz) in enumerate(points):
        a = points[max(0, i - 1)]; b = points[min(n - 1, i + 1)]
        tx, tz = b[0] - a[0], b[1] - a[1]; L = math.hypot(tx, tz) or 1.0
        nx, nz = -tz / L, tx / L
        w = width * (math.sin(math.pi * i / (n - 1)) * 0.85 + 0.15 if taper else 1.0) / 2
        left.append(bm.verts.new((px + nx * w, y, pz + nz * w))); right.append(bm.verts.new((px - nx * w, y, pz - nz * w)))
    for i in range(n - 1):
        bm.faces.new((left[i], left[i + 1], right[i + 1], right[i]))
    me = bpy.data.meshes.new("strip"); bm.to_mesh(me); bm.free()
    o = bpy.data.objects.new("strip", me); bpy.context.collection.objects.link(o)
    o.data.materials.append(mat)
    if depth:
        md = o.modifiers.new("s", 'SOLIDIFY'); md.thickness = depth; md.offset = 1
        bpy.context.view_layer.objects.active = o; bpy.ops.object.modifier_apply(modifier="s")
    K.PARTS.append(o); return o

def prism(pts, axis, a0, a1, mat):
    """Prisma de contorno irregular: pts 2D extrudados de a0 a a1. axis 'Y' = contorno no plano XZ (peça de parede);
    axis 'Z' = contorno no plano XY (laje)."""
    bm = bmesh.new()
    def v(p, a): return bm.verts.new((p[0], a, p[1]) if axis == 'Y' else (p[0], p[1], a))
    lo = [v(p, a0) for p in pts]; hi = [v(p, a1) for p in pts]
    bm.faces.new(lo[::-1]); bm.faces.new(hi)
    n = len(pts)
    for i in range(n):
        j = (i + 1) % n; bm.faces.new((lo[i], lo[j], hi[j], hi[i]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    me = bpy.data.meshes.new("prism"); bm.to_mesh(me); bm.free()
    o = bpy.data.objects.new("prism", me); bpy.context.collection.objects.link(o); o.data.materials.append(mat)
    K.PARTS.append(o); return o
def blob(cx, cz, r, n=14, jag=0.35):
    return [(cx + math.cos(2 * math.pi * i / n) * r * random.uniform(1 - jag, 1 + jag * 0.5),
             cz + math.sin(2 * math.pi * i / n) * r * random.uniform(1 - jag, 1 + jag * 0.5)) for i in range(n)]

def mats():
    M = {}
    M['steel'] = K.material("BrushedSteel", lin((0.66, 0.67, 0.69)), rough=0.34, metal=1.0, dirt=0.25, color_var=0.04, bump=0.05, bump_scale=600.0)
    M['enamel'] = K.material("WhiteEnamel", lin((0.82, 0.82, 0.80)), rough=0.4, dirt=0.35, wear=0.08, wear_color=lin((0.3, 0.28, 0.25)))
    M['chain'] = K.material("ChainMetal", lin((0.35, 0.35, 0.36)), rough=0.45, metal=1.0, dirt=0.3, rust=0.15)
    M['black'] = K.material("BlackPlastic", lin((0.04, 0.04, 0.045)), rough=0.45, dirt=0.1)
    M['paper'] = K.material("Paper", lin((0.93, 0.92, 0.88)), rough=0.85, color_var=0.03)
    M['old'] = K.material("OldPaper", lin((0.90, 0.86, 0.74)), rough=0.85, color_var=0.08, dirt=0.25, dirt_color=lin((0.6, 0.52, 0.38)))
    M['ink'] = K.material("Ink", lin((0.05, 0.05, 0.06)), rough=0.6)
    M['blue_ink'] = K.material("BlueInk", lin((0.08, 0.12, 0.38)), rough=0.6)
    M['red'] = K.material("RedPaint", lin((0.70, 0.06, 0.05)), rough=0.35)
    M['wood'] = K.material("DarkWood", lin((0.26, 0.16, 0.10)), rough=0.45, wood=True, dirt=0.15, color_var=0.08)
    M['frame_gold'] = K.material("OldGilt", lin((0.55, 0.44, 0.22)), rough=0.38, metal=0.8, dirt=0.35, wear=0.1)
    M['tape'] = K.material("Tape", lin((0.86, 0.84, 0.72)), rough=0.4)
    M['plaster'] = K.material("Plaster", lin((0.70, 0.71, 0.72)), rough=0.85, color_var=0.05, dirt=0.3, bump=0.08, bump_scale=120.0)
    M['brick'] = K.material("Brick", lin((0.42, 0.20, 0.13)), rough=0.85, color_var=0.15, dirt=0.4, bump=0.1, bump_scale=60.0)
    M['concrete'] = K.material("Concrete", lin((0.45, 0.45, 0.44)), rough=0.9, color_var=0.08, dirt=0.4, bump=0.12, bump_scale=80.0)
    M['rebar'] = K.material("Rebar", lin((0.25, 0.20, 0.16)), rough=0.6, metal=0.7, rust=0.6)
    M['gouge'] = K.material("Gouge", lin((0.05, 0.045, 0.045)), rough=0.9)
    M['gouge_edge'] = K.material("GougeEdge", lin((0.78, 0.76, 0.72)), rough=0.95, color_var=0.1)
    M['crack'] = K.material("Crack", lin((0.03, 0.03, 0.035)), rough=0.95)
    return M

def photo_mat(name, seed):
    """Foto antiga com as pessoas sem rosto: fundo de estúdio, silhuetas e um BORRÃO onde deveria estar o rosto."""
    def fn(nt, p, tc):
        # fundo em degradê + grão; as "pessoas" e os rostos vêm de manchas de ruído esticadas (borradas, não desenhadas)
        n1 = noise_fac(nt, tc.outputs['Object'], 3.0 + seed, 4.0, 1.0, 1.0, 0.25, distortion=1.5)
        n2 = noise_fac(nt, tc.outputs['Object'], 18.0 + seed * 3, 2.0, 1.0, 1.0, 1.0)
        mix = nt.nodes.new('ShaderNodeMath'); mix.operation = 'MULTIPLY_ADD'
        nt.links.new(n1, mix.inputs[0]); mix.inputs[1].default_value = 0.8; nt.links.new(n2, mix.inputs[2])
        col = ramp(nt, mix.outputs[0], [(0.35, (0.16, 0.13, 0.11)), (0.55, (0.42, 0.36, 0.30)), (0.70, (0.66, 0.58, 0.48)), (0.85, (0.80, 0.74, 0.64))])
        nt.links.new(col, p.inputs['Base Color']); p.inputs['Roughness'].default_value = 0.35
    return simple_mat(name, fn)

# ====================================================================== HALL
def build_hall():
    M = mats(); parts = {}
    diffuser = K.material("FluoDiffuser", lin((0.92, 0.95, 1.0)), rough=0.3, emission=(0.85, 0.92, 1.0), emission_strength=6.0)

    # --- calha fluorescente pendente: 1,25 x 0,22 x 0,08, presa ao teto por 2 correntes de 2,0 m (pé-direito do sonho é 5,3 m)
    s = len(K.PARTS)
    L, Wd, H, drop = 1.25, 0.22, 0.08, 2.0
    for k in (-1, 1):
        cyl(k * 0.45, 0, -0.012, 0.045, 0.024, M['enamel'], 'Z', 24, 0.003)                     # canopla no teto
        n = int(drop / 0.035)
        for i in range(n):                                                                       # corrente: elos alternados
            z = -0.03 - i * 0.035
            if i % 2 == 0: box(k * 0.45 - 0.006, k * 0.45 + 0.006, -0.0015, 0.0015, z - 0.03, z, M['chain'], 0.001)
            else: box(k * 0.45 - 0.0015, k * 0.45 + 0.0015, -0.006, 0.006, z - 0.03, z, M['chain'], 0.001)
    z0 = -0.03 - drop
    box(-L / 2, L / 2, -Wd / 2, Wd / 2, z0 - H, z0, M['enamel'], 0.006)                           # corpo
    box(-L / 2 + 0.015, L / 2 - 0.015, -Wd / 2 + 0.015, Wd / 2 - 0.015, z0 - H - 0.004, z0 - H + 0.002, diffuser)   # difusor aceso
    for k in (-1, 1): box(k * (L / 2) - 0.004 * k, k * (L / 2), -Wd / 2, Wd / 2, z0 - H - 0.006, z0 + 0.004, M['enamel'], 0.002)
    parts["PendantFixture"] = K.PARTS[s:]

    # --- relógio de parede SEM ponteiros (só o pino no centro): o tempo do sonho não anda
    s = len(K.PARTS)
    R = 0.16
    cyl(0, -0.025, R, R, 0.05, M['black'], 'Y', 64, 0.006)
    cyl(0, -0.051, R, R - 0.012, 0.002, M['paper'], 'Y', 64)
    for h in range(12):
        a = math.radians(h * 30); big = h % 3 == 0
        l, w = (0.022, 0.006) if big else (0.012, 0.003)
        r_out = R - 0.022
        t = box(-w / 2, w / 2, -0.0525, -0.052, R + r_out - l, R + r_out, M['ink'])
        rot_parts([t], a, 'Y', (0, -0.052, R))
    for i, nume in enumerate(("12", "3", "6", "9")):
        a = math.radians(i * 90); rr = 0.095
        text(nume, math.sin(a) * rr, -0.0525, R + math.cos(a) * rr - 0.008, 0.022, M['ink'], extrude=0.0004)
    cyl(0, -0.055, R, 0.006, 0.008, M['black'], 'Y', 16)
    parts["WallClock"] = K.PARTS[s:]

    # --- quadros com o rosto RISCADO (3 variações): moldura dourada velha + foto sépia com pessoas; no lugar de cada
    #     rosto, rabiscos claros por cima (alguém riscou) — lê de longe como "fotos de família sem rosto"
    sepia = K.material("PhotoSepia", lin((0.62, 0.52, 0.40)), rough=0.35, color_var=0.15)
    figure = K.material("PhotoFigure", lin((0.22, 0.17, 0.13)), rough=0.4, color_var=0.08)
    scratch = K.material("PhotoScratch", lin((0.86, 0.82, 0.74)), rough=0.6)
    layouts = (((0.36, 0.46), [(0.0, 1.0)]), ((0.50, 0.38), [(-0.12, 0.8), (0.0, 0.95), (0.12, 0.85)]), ((0.30, 0.40), [(-0.05, 0.9), (0.06, 0.75)]))
    for vi, ((fw, fh), people) in enumerate(layouts):
        s = len(K.PARTS)
        fr = 0.035
        box(-fw / 2, fw / 2, -0.025, 0, 0, fr, M['frame_gold'], 0.003)                   # baixo e cima inteiros,
        box(-fw / 2, fw / 2, -0.025, 0, fh - fr, fh, M['frame_gold'], 0.003)
        box(-fw / 2, -fw / 2 + fr, -0.025, 0, fr, fh - fr, M['frame_gold'], 0.003)       # laterais entre eles (sem sobrepor)
        box(fw / 2 - fr, fw / 2, -0.025, 0, fr, fh - fr, M['frame_gold'], 0.003)
        y = -0.010
        box(-fw / 2 + fr, fw / 2 - fr, y - 0.001, y, fr, fh - fr, sepia)
        ph = fh - 2 * fr
        for (px, sc) in people:
            hr = 0.045 * sc * (ph / 0.39)
            base = fr
            sh = box(px - hr * 1.9, px + hr * 1.9, y - 0.0016, y - 0.0011, base, base + ph * 0.42 * sc, figure, 0.01)
            cyl(px, y - 0.0014, base + ph * 0.42 * sc + hr * 1.05, hr, 0.0006, figure, 'Y', 28)
            # os riscos: zigue-zague claro por cima do rosto
            cz = base + ph * 0.42 * sc + hr * 1.05
            for k in range(3):
                pts = [(px - hr * 1.1 + i * hr * 0.275, cz + (hr * 0.7 if i % 2 else -hr * 0.7) * (1 - k * 0.25) + k * 0.004) for i in range(9)]
                strip(pts, 0.004, y - 0.0022, scratch, depth=0.0003, taper=False)
        parts[f"PictureFrame_{'ABC'[vi]}"] = K.PARTS[s:]

    # --- o cartaz da Helena (o MESMO da Recepção) e a versão rasgada
    def poster(torn):
        cx, cz, w, h = 0.0, 0.1485, 0.21, 0.297
        photo = K.material("PhotoBack", lin((0.42, 0.45, 0.50)), rough=0.6, color_var=0.1)
        silh = K.material("PhotoSilhouette", lin((0.16, 0.15, 0.15)), rough=0.6)
        y0 = -0.001
        hcut = h * (0.58 if torn else 1.0)
        box(cx - w / 2, cx + w / 2, y0, 0, cz + h / 2 - hcut, cz + h / 2, M['old'])
        if torn:   # borda rasgada: dentes irregulares
            xs = [cx - w / 2 + i * w / 14 for i in range(15)]
            for i in range(14):
                d = random.uniform(0.004, 0.018)
                box(xs[i], xs[i + 1], y0, 0, cz + h / 2 - hcut - d, cz + h / 2 - hcut, M['old'])
        text("DESAPARECIDA", cx, y0 - 0.0004, cz + h / 2 - 0.032, 0.026, M['red'], extrude=0.0003)
        px0, px1, pz0, pz1 = cx - 0.055, cx + 0.055, cz + 0.005, cz + 0.105
        box(px0, px1, y0 - 0.0008, y0, pz0, pz1, photo)
        cyl(cx, y0 - 0.0011, pz0 + 0.062, 0.019, 0.0005, silh, 'Y', 24)
        box(cx - 0.012, cx + 0.012, y0 - 0.0011, y0 - 0.0008, pz0 + 0.03, pz0 + 0.048, silh)
        box(cx - 0.042, cx + 0.042, y0 - 0.0011, y0 - 0.0008, pz0, pz0 + 0.032, silh, 0.008)
        info = ["Helena M., 31 anos", "moradora do 405", "", "Vista pela última vez em 09/11,", "voltando do trabalho à noite.",
                "", "Qualquer informação:", "portaria ou 190"]
        for li, ln in enumerate(info):
            zz = cz - 0.015 - li * 0.0145
            if ln and (not torn or zz > cz + h / 2 - hcut + 0.01):
                text(ln, cx, y0 - 0.0004, zz, 0.0105 if li > 1 else 0.012, M['ink'], extrude=0.0002)
        for (tx, tz, ta) in ((cx - w / 2, cz + h / 2, 40), (cx + w / 2, cz + h / 2, -40)):
            t = box(tx - 0.03, tx + 0.03, y0 - 0.0016, y0 - 0.0010, tz - 0.009, tz + 0.009, M['tape'])
            rot_parts([t], math.radians(ta), 'Y', (tx, -0.001, tz))
    s = len(K.PARTS); poster(False); parts["MissingPoster"] = K.PARTS[s:]
    s = len(K.PARTS); poster(True); parts["MissingPosterTorn"] = K.PARTS[s:]

    # --- post-its de estudo (ela dormiu estudando): um deles diz ACORDA
    s = len(K.PARTS)
    yellow = K.material("PostItYellow", lin((0.98, 0.90, 0.38)), rough=0.8, color_var=0.04)
    pink = K.material("PostItPink", lin((0.98, 0.55, 0.70)), rough=0.8, color_var=0.04)
    notes = [(-0.10, 0.20, 4, yellow, ["PROVA", "SEXTA!"]), (0.0, 0.24, -3, pink, ["cap. 7", "rever"]),
             (0.10, 0.19, 6, yellow, ["não", "dormir"]), (-0.05, 0.09, -5, yellow, ["f'(x) = ?", "x² + 1"]),
             (0.06, 0.08, 2, pink, ["ACORDA"]), (-0.13, 0.05, 8, yellow, ["302"])]
    for (nx, nz, ang, m, lines) in notes:
        ps = len(K.PARTS)
        box(nx - 0.038, nx + 0.038, -0.0012, 0, nz - 0.038, nz + 0.038, m)
        for li, ln in enumerate(lines):
            text(ln, nx, -0.0016, nz + 0.012 - li * 0.022, 0.016 if ln != "ACORDA" else 0.015, M['blue_ink'] if ln != "ACORDA" else M['red'], extrude=0.0002)
        rot_parts(K.PARTS[ps:], math.radians(ang), 'Y', (nx, 0, nz))
    parts["StickyNotes"] = K.PARTS[s:]

    # --- frente do elevador (parede do fundo do spawn): batente, 2 folhas de inox, indicador "3" e botoeira
    s = len(K.PARTS)
    Wf, Hf = 1.10, 2.15
    box(-Wf / 2 - 0.10, -Wf / 2, -0.06, 0, 0, Hf + 0.10, M['steel'], 0.004)
    box(Wf / 2, Wf / 2 + 0.10, -0.06, 0, 0, Hf + 0.10, M['steel'], 0.004)
    box(-Wf / 2, Wf / 2, -0.06, 0, Hf, Hf + 0.10, M['steel'], 0.004)
    for k in (-1, 1):
        box(0.003 * k, k * Wf / 2, -0.035, -0.015, 0.0, Hf, M['steel'], 0.003)
    red_em = K.material("RedLED", lin((0.9, 0.05, 0.03)), rough=0.4, emission=(1.0, 0.05, 0.02), emission_strength=4.0)
    amber_em = K.material("AmberLED", lin((1.0, 0.6, 0.1)), rough=0.4, emission=(1.0, 0.55, 0.1), emission_strength=2.5)
    box(-0.12, 0.12, -0.07, -0.06, Hf + 0.16, Hf + 0.26, M['steel'], 0.004)
    box(-0.10, 0.10, -0.072, -0.07, Hf + 0.172, Hf + 0.248, M['black'])
    text("3", -0.02, -0.0735, Hf + 0.21, 0.055, red_em, extrude=0.0005)
    box(Wf / 2 + 0.20, Wf / 2 + 0.31, -0.008, 0, 1.0, 1.22, M['steel'], 0.003)
    for zz in (1.06, 1.16): cyl(Wf / 2 + 0.255, -0.010, zz, 0.018, 0.004, amber_em if zz > 1.1 else M['steel'], 'Y', 24)
    parts["ElevatorFront"] = K.PARTS[s:]

    # --- placa de número ERRADA (3O2 com O de letra, torta) para colar por cima de portas no trecho da repetição
    s = len(K.PARTS)
    brass = K.material("Brass", lin((0.78, 0.62, 0.30)), rough=0.3, metal=1.0, dirt=0.3)
    box(-0.07, 0.07, -0.004, 0, -0.03, 0.03, brass, 0.003)
    text("3O2", 0, -0.0045, -0.012, 0.036, M['ink'], extrude=0.0006)
    parts["NumberPlate_3O2"] = K.PARTS[s:]
    return finish_group(parts)

# ====================================================================== DECAY
def build_decay():
    M = mats(); parts = {}
    # --- garras: 4 sulcos paralelos, levemente curvos, com a borda da tinta arrancada
    s = len(K.PARTS)
    for i in range(4):
        off = (i - 1.5) * 0.06
        pts = [(off + 0.02 * math.sin(t * 2.0) + t * 0.10, 0.75 - t * 0.62) for t in [k / 10 for k in range(11)]]
        strip(pts, 0.028, -0.0005, M['gouge_edge'], depth=0.0008)
        strip([(x + 0.002, z) for x, z in pts], 0.012, -0.0016, M['gouge'], depth=0.0006)
    parts["ClawMarks"] = K.PARTS[s:]

    # --- rachadura ramificada (~1 m)
    s = len(K.PARTS)
    def crack(x, z, ang, length, width, depth):
        pts = [(x, z)]
        for k in range(8):
            ang += random.uniform(-0.5, 0.5)
            x += math.cos(ang) * length / 8; z += math.sin(ang) * length / 8; pts.append((x, z))
        strip(pts, width, -0.0006, M['crack'], depth=0.0004)
        if depth > 0:
            for _ in range(2):
                j = random.randint(2, 6); bx, bz = pts[j]
                crack(bx, bz, ang + random.choice((-1, 1)) * random.uniform(0.6, 1.2), length * 0.45, width * 0.6, depth - 1)
    crack(0.0, 0.0, math.radians(70), 1.0, 0.012, 2)
    parts["WallCrack"] = K.PARTS[s:]

    # --- folha de caderno no chão (pautada, margem vermelha, anotações)
    s = len(K.PARTS)
    box(-0.105, 0.105, -0.1485, 0.1485, 0, 0.0004, M['paper'])
    blue_line = K.material("RuleBlue", lin((0.55, 0.70, 0.88)), rough=0.8)
    for i in range(24):
        y = -0.13 + i * 0.0115
        box(-0.105, 0.105, y, y + 0.0007, 0.0004, 0.00045, blue_line)
    box(-0.075, -0.0742, -0.1485, 0.1485, 0.0004, 0.00046, M['red'])
    flat = (0, 0, 0)
    for li, ln in enumerate(["Cap. 7 - Integrais", "f(x) = x², F(x) = x³/3", "revisar exercícios 4 a 9", "prova sexta", "não dormir na cafeteria"]):
        text(ln, -0.06, 0.11 - li * 0.023, 0.0006, 0.0085, M['blue_ink'], rot=flat, align='LEFT')
    parts["PaperSheet"] = K.PARTS[s:]

    # --- folha amassada (bola irregular)
    s = len(K.PARTS)
    bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=3, radius=0.035, location=(0, 0, 0.032))
    o = bpy.context.active_object
    for v in o.data.vertices:
        v.co *= random.uniform(0.75, 1.2)
    o.data.materials.append(M['paper']); K.PARTS.append(o)
    parts["PaperBall"] = K.PARTS[s:]

    # --- caderno aberto (duas páginas, capa, espiral)
    s = len(K.PARTS)
    cover = K.material("NotebookCover", lin((0.10, 0.22, 0.42)), rough=0.55, dirt=0.2)
    box(-0.21, 0.21, -0.14, 0.14, 0, 0.003, cover, 0.002)
    for k in (-1, 1):
        box(k * 0.004 if k > 0 else -0.2, 0.2 if k > 0 else -0.004, -0.135, 0.135, 0.003, 0.009, M['paper'])
        for i in range(18):
            y = -0.115 + i * 0.013
            box(k * 0.012 if k > 0 else -0.19, 0.19 if k > 0 else -0.012, y, y + 0.0007, 0.009, 0.0091, blue_line)
    for i in range(14):
        cyl(0, -0.125 + i * 0.019, 0.012, 0.006, 0.012, M['chain'], 'X', 10)
    for li, ln in enumerate(["acorda", "acorda", "acorda", "ACORDA"]):
        text(ln, 0.03, 0.09 - li * 0.026, 0.0093, 0.012, M['blue_ink'] if li < 3 else M['red'], rot=(0, 0, 0), align='LEFT')
    parts["Notebook"] = K.PARTS[s:]

    # --- pedaços de parede: contorno serrilhado; reboco pintado na frente, tijolo atrás (3 tamanhos)
    for ci, sz in enumerate((0.55, 0.35, 0.22)):
        s = len(K.PARTS)
        prism(blob(0, sz * 0.5, sz * 0.5, 16, 0.4), 'Y', -0.015, 0.0, M['plaster'])
        prism(blob(0.01, sz * 0.48, sz * 0.38, 12, 0.35), 'Y', 0.0, 0.085, M['brick'])
        parts[f"WallChunk_{'ABC'[ci]}"] = K.PARTS[s:]

    # --- a beira: laje de concreto (3,3 m) quebrada no lado do abismo (-Y), 0,25 m de espessura, ferragem para fora
    s = len(K.PARTS)
    W = 3.3; n = 26
    front = [(-W / 2 + i * W / n, -random.uniform(0.05, 0.55)) for i in range(n + 1)]
    pts = [(-W / 2, 0.6)] + front + [(W / 2, 0.6)]
    prism(pts[::-1], 'Z', -0.25, 0.0, M['concrete'])
    for i in range(8):
        x = -W / 2 + 0.25 + i * 0.4 + random.uniform(-0.08, 0.08)
        L = random.uniform(0.35, 0.9)
        r = cyl(x, -L / 2, -0.1, 0.007, L, M['rebar'], 'Y', 8)
        rot_parts([r], math.radians(random.uniform(-35, 5)), 'X', (x, 0, -0.1))
    parts["BrokenEdge"] = K.PARTS[s:]
    return finish_group(parts)

ALL = {"hall": build_hall, "decay": build_decay}[GROUP]()

# ------------------------------------------------------------------ prévias
def preview_lineup():
    sc = bpy.context.scene; sc.render.engine = 'CYCLES'; sc.cycles.samples = 24; sc.cycles.use_denoising = True
    sc.render.resolution_x, sc.render.resolution_y = 1600, 700
    sc.view_settings.view_transform = 'Standard'
    w = bpy.data.worlds.new("w"); sc.world = w; w.use_nodes = True
    w.node_tree.nodes['Background'].inputs['Color'].default_value = (0.32, 0.33, 0.36, 1); w.node_tree.nodes['Background'].inputs['Strength'].default_value = 0.7
    x = 0.0; maxh = 0.0
    for o in ALL:
        d = o.dimensions; bb = [o.matrix_world @ Vector(c) for c in o.bound_box]
        minx = min(v.x for v in bb); minz = min(v.z for v in bb)
        o.location.x += x - minx; o.location.z -= minz
        if GROUP == "decay" and o.name in ("PaperSheet", "PaperBall", "Notebook", "BrokenEdge"):
            o.rotation_euler = (math.radians(60), 0, 0)
        x += d.x + 0.12; maxh = max(maxh, d.z)
    bpy.ops.object.light_add(type='SUN', rotation=(math.radians(55), 0, math.radians(25))); bpy.context.active_object.data.energy = 3.0
    bpy.ops.object.camera_add(); cam = bpy.context.active_object; sc.camera = cam
    cam.data.type = 'ORTHO'; cam.data.ortho_scale = max(x, maxh * 2.3) * 1.02
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
print(f"[build_nightmare] OK {GROUP}:", OUT_FBX, [(o.name, len(o.data.polygons)) for o in ALL])
