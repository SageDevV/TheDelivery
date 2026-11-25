"""Acabamento da Recepção (Ato 2, 2026-11-25) — por script, 0 créditos. Três grupos, cada um com seu FBX e atlas:

  lobby   : MailboxBank (caixas de correio 101–603), NoticeBoard (quadro de avisos), KeyCabinet (quadro de chaves),
            BuildingName (letras "EDIFÍCIO PORTO SERENO"), SlatPanel (ripado atrás do balcão), FilmedPlaque
            ("sorria, você está sendo filmado"), ExitSign (SAÍDA, acesa), EmergencyLight, ElevatorPanel (botão),
            ElevatorIndicator (andar "T"), CctvCamera, PlantPot (vaso com palmeira), EntranceMat, WetFloorSign
            (piso molhado), Magazines
  desk    : Monitor (câmeras de segurança na tela), Keyboard, DeskPhone (interfone), Radio, Logbook (livro de visitas
            aberto + caneta), Mug (café pela metade)
  storage : MopBucket (balde espremedor com água suja), Mop (encostado), CleaningBottles, UtilityDoor (folha nova da
            porta da sala de produtos)

Convenção (igual a build_interior.py): frente para -Y do Blender (= +Z no Unity).
  chão   — origem no centro da BASE;   parede — origem no centro da face de TRÁS, embaixo;
  mesa   — origem no centro da base (apoiado no tampo).
Uso: blender --background --python build_reception.py -- <grupo> <saida.fbx> <pasta_texturas> [<pasta_previas>]
"""
import bpy, bmesh, sys, math, os, random
from mathutils import Vector, noise, Matrix
sys.path.append(os.path.dirname(os.path.abspath(__file__)))
import realistic_kit as K

argv = sys.argv[sys.argv.index("--") + 1:]
GROUP, OUT_FBX, TEX_DIR = argv[0], argv[1], argv[2]
PREV_DIR = argv[3] if len(argv) > 3 else None
random.seed(7)
K.reset_scene()

def lin(c): return tuple(x ** 2.2 for x in c)
def box(x0, x1, y0, y1, z0, z1, mat, bevel=0.0, seg=2):
    return K.box(min(x0, x1), max(x0, x1), min(y0, y1), max(y0, y1), min(z0, z1), max(z0, z1), mat, bevel=bevel, segments=seg)
def cyl(x, y, z, r, d, mat, axis='Z', verts=24, bevel=0.0): return K.cyl(x, y, z, r, d, mat, axis=axis, verts=verts, bevel=bevel)
FRONT = (math.pi / 2, 0, 0)        # texto de pé, lido de quem olha de -Y
def text(s, x, y, z, size, mat, extrude=0.0008, align='CENTER', rot=FRONT): return K.text(s, x, y, z, size, mat, rot=rot, extrude=extrude, align=align)

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

def noise_fac(nt, vec, scale, detail=6.0, sx=1.0, sy=1.0, sz=1.0):
    mp = nt.nodes.new('ShaderNodeMapping'); mp.inputs['Scale'].default_value = (sx, sy, sz)
    nt.links.new(vec, mp.inputs['Vector'])
    n = nt.nodes.new('ShaderNodeTexNoise'); n.inputs['Scale'].default_value = scale; n.inputs['Detail'].default_value = detail
    nt.links.new(mp.outputs[0], n.inputs['Vector']); return n.outputs['Fac']

def rename(o, n): o.name = n; o.data.name = n; return o
def finish_group(d):
    """Junta cada grupo e APLICA a transformação: o Unity usa só a malha, então a origem precisa ser a do mundo
    (texto e primitivas criados com location/rotação deixavam o objeto juntado girado/deslocado)."""
    out = []
    for k, v in d.items():
        o = K.join(v, k) if len(v) > 1 else rename(v[0], k)
        bpy.ops.object.select_all(action='DESELECT'); o.select_set(True); bpy.context.view_layer.objects.active = o
        bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
        out.append(o)
    return out


def tri(x, y, z, size, mat, depth=0.0006, outline=False):
    """Triângulo (seta para cima / aviso) de pé no plano XZ, em y (frente -Y). Centro em (x, z)."""
    h = size * 0.866
    pts = [(x - size / 2, z - h / 3), (x + size / 2, z - h / 3), (x, z + 2 * h / 3)]
    bm = bmesh.new()
    def face(p):
        vs = [bm.verts.new((px, y, pz)) for px, pz in p]
        bm.faces.new(vs[::-1])
    if outline:
        k = 0.72; cx, cz = x, z
        inner = [(cx + (px - cx) * k, cz + (pz - cz) * k) for px, pz in pts]
        for i in range(3):
            a, b = pts[i], pts[(i + 1) % 3]; ia, ib = inner[i], inner[(i + 1) % 3]
            face([a, b, ib, ia])
    else:
        face(pts)
    me = bpy.data.meshes.new("tri"); bm.to_mesh(me); bm.free()
    o = bpy.data.objects.new("tri", me); bpy.context.collection.objects.link(o)
    so = o.modifiers.new("so", 'SOLIDIFY'); so.thickness = depth
    bpy.context.view_layer.objects.active = o; bpy.ops.object.modifier_apply(modifier="so")
    return K._finish(o, mat)

def open_top(o, zmin):
    """Apaga as faces de cima (normal +Z acima de zmin): deixa o recipiente aberto ANTES do solidify."""
    me = o.data; bm = bmesh.new(); bm.from_mesh(me)
    tops = [fc for fc in bm.faces if fc.normal.z > 0.9 and fc.calc_center_median().z > zmin]
    bmesh.ops.delete(bm, geom=tops, context='FACES'); bm.to_mesh(me); bm.free()

def solidify(o, t):
    so = o.modifiers.new("so", 'SOLIDIFY'); so.thickness = t
    bpy.context.view_layer.objects.active = o; bpy.ops.object.modifier_apply(modifier="so")

def rot_parts(parts, angle, axis='X', pivot=(0, 0, 0)):
    """Gira peças já criadas em torno de um pivô (aplica na malha)."""
    M = Matrix.Translation(pivot) @ Matrix.Rotation(angle, 4, axis) @ Matrix.Translation(Vector(pivot) * -1)
    for o in parts:
        o.data.transform(o.matrix_world.inverted() @ M @ o.matrix_world)

# ------------------------------------------------------------------ materiais comuns
def mats():
    M = {}
    M['steel'] = K.material("BrushedSteel", lin((0.72, 0.72, 0.73)), rough=0.32, metal=1.0, dirt=0.18, color_var=0.04, bump=0.05, bump_scale=600.0)
    M['bronze'] = K.material("MailBronze", lin((0.20, 0.17, 0.14)), rough=0.4, metal=0.8, dirt=0.25, wear=0.12, wear_color=lin((0.55, 0.45, 0.32)))
    M['brass'] = K.material("Brass", lin((0.78, 0.62, 0.30)), rough=0.3, metal=1.0, dirt=0.2)
    M['black'] = K.material("BlackPlastic", lin((0.04, 0.04, 0.045)), rough=0.45, dirt=0.1)
    M['white'] = K.material("WhitePlastic", lin((0.88, 0.88, 0.86)), rough=0.45, dirt=0.2, color_var=0.03)
    M['paper'] = K.material("Paper", lin((0.93, 0.92, 0.88)), rough=0.85, color_var=0.03)
    M['ink'] = K.material("Ink", lin((0.05, 0.05, 0.06)), rough=0.6)
    M['red'] = K.material("RedPaint", lin((0.70, 0.06, 0.05)), rough=0.35)
    M['wood'] = K.material("DarkWood", lin((0.28, 0.17, 0.10)), rough=0.45, wood=True, dirt=0.12, color_var=0.08)
    M['alu'] = K.material("AluFrame", lin((0.75, 0.75, 0.76)), rough=0.35, metal=1.0, dirt=0.2)
    return M

# ====================================================================== LOBBY
def build_lobby():
    M = mats()
    parts = {}
    cork = simple_mat("Cork", lambda nt, p, tc: (nt.links.new(ramp(nt, noise_fac(nt, tc.outputs['Object'], 260.0, 12.0), [(0.3, (0.45, 0.30, 0.17)), (0.7, (0.66, 0.48, 0.30))]), p.inputs['Base Color']), setattr(p.inputs['Roughness'], 'default_value', 0.95)))
    slat = K.material("SlatWood", lin((0.36, 0.22, 0.12)), rough=0.5, wood=True, color_var=0.1)
    backboard = K.material("SlatBack", lin((0.06, 0.05, 0.045)), rough=0.8)
    green_em = K.material("ExitGreen", lin((0.05, 0.55, 0.20)), rough=0.3, emission=lin((0.1, 0.9, 0.35)), emission_strength=1.0)
    white_em = K.material("ExitWhite", lin((0.95, 0.98, 0.95)), rough=0.3, emission=(1.0, 1.0, 1.0), emission_strength=1.0)
    red_em = K.material("IndicatorRed", lin((0.9, 0.1, 0.05)), rough=0.3, emission=lin((1.0, 0.12, 0.05)), emission_strength=1.0)
    amber_em = K.material("ButtonAmber", lin((1.0, 0.6, 0.2)), rough=0.3, emission=lin((1.0, 0.55, 0.15)), emission_strength=1.0)
    led_em = K.material("LedRed", lin((1.0, 0.05, 0.05)), rough=0.3, emission=(1.0, 0.05, 0.05), emission_strength=1.0)
    yellow = K.material("WarnYellow", lin((0.95, 0.74, 0.05)), rough=0.4, dirt=0.15)
    pot = K.material("PotCeramic", lin((0.12, 0.11, 0.10)), rough=0.35, dirt=0.15, color_var=0.05)
    soil = K.material("Soil", lin((0.13, 0.09, 0.06)), rough=0.95, bump=0.6, bump_scale=200.0)
    rubber = K.material("MatRubber", lin((0.06, 0.06, 0.06)), rough=0.85)
    coir = simple_mat("MatFiber", lambda nt, p, tc: (nt.links.new(ramp(nt, noise_fac(nt, tc.outputs['Object'], 500.0, 12.0, 1, 5, 1), [(0.3, (0.10, 0.10, 0.11)), (0.7, (0.22, 0.21, 0.20))]), p.inputs['Base Color']), setattr(p.inputs['Roughness'], 'default_value', 0.95)))

    # --- caixas de correio: 3 colunas x 6 andares (101..603), frente -Y, origem atrás/embaixo
    s = len(K.PARTS)
    bw, bh, bd, fr = 0.28, 0.13, 0.30, 0.02
    W, H = 3 * bw + 2 * fr, 6 * bh + 2 * fr
    box(-W / 2, W / 2, -bd, 0, 0, H, M['bronze'], 0.004)
    for row in range(6):
        for col in range(3):
            x0 = -W / 2 + fr + col * bw; z0 = fr + row * bh
            box(x0 + 0.006, x0 + bw - 0.006, -bd - 0.006, -bd, z0 + 0.006, z0 + bh - 0.006, M['bronze'], 0.003)      # portinhola
            box(x0 + 0.05, x0 + bw - 0.05, -bd - 0.0075, -bd - 0.006, z0 + bh - 0.035, z0 + bh - 0.028, M['black'])   # fenda
            box(x0 + 0.02, x0 + 0.09, -bd - 0.008, -bd - 0.006, z0 + 0.025, z0 + 0.05, M['brass'])                    # plaquinha
            text(f"{row + 1}0{col + 1}", x0 + 0.055, -bd - 0.0085, z0 + 0.0375, 0.018, M['ink'], extrude=0.0003)
            cyl(x0 + bw - 0.035, -bd - 0.009, z0 + bh / 2 - 0.01, 0.007, 0.006, M['steel'], 'Y', 12)                  # fechadura
            if (row, col) == (2, 1):   # 302: carta presa na fenda
                e = box(x0 + 0.06, x0 + 0.20, -bd - 0.05, -bd + 0.01, z0 + bh - 0.034, z0 + bh - 0.031, M['paper'])
                rot_parts([e], math.radians(-12), 'X', (0, -bd, z0 + bh - 0.032))
            if (row, col) == (0, 0):   # 101: panfleto amassado
                e = box(x0 + 0.04, x0 + 0.22, -bd - 0.04, -bd + 0.01, z0 + bh - 0.034, z0 + bh - 0.030, M['red'])
                rot_parts([e], math.radians(-20), 'X', (0, -bd, z0 + bh - 0.032))
    parts["MailboxBank"] = K.PARTS[s:]

    # --- quadro de avisos 1,10 x 0,70 (com o cartaz de DESAPARECIDA, pedido do usuário 2026-11-25)
    s = len(K.PARTS)
    W, H = 1.10, 0.70
    box(-W / 2, W / 2, -0.012, 0, 0, H, cork)
    for (x0, x1, z0, z1) in ((-W / 2, W / 2, H - 0.02, H), (-W / 2, W / 2, 0, 0.02), (-W / 2, -W / 2 + 0.02, 0, H), (W / 2 - 0.02, W / 2, 0, H)):
        box(x0, x1, -0.02, 0, z0, z1, M['alu'], 0.002)
    notes = [
        (-0.38, 0.50, 0.21, 0.28, -3, "AVISO\n\nReunião de condomínio\nquinta-feira, às 20h\nno salão de festas.\n\nA administração"),
        (-0.13, 0.50, 0.21, 0.24, 2, "COLETA DE LIXO\n\nSegunda, quarta e sexta\na partir das 19h.\nSepare o reciclável."),
        (0.41, 0.52, 0.18, 0.22, -2, "É PROIBIDO\ndeixar bicicletas\ne carrinhos no hall."),
        (-0.30, 0.16, 0.20, 0.14, 4, "VENDO BICICLETA\naro 29, pouco uso\nTratar no 504"),
        (0.42, 0.20, 0.18, 0.18, -1, "INTERFONE\n\nPortaria: 9\nZeladoria: 8\nEmergência: 190"),
    ]
    pin_cols = [M['red'], yellow, K.material("PinBlue", lin((0.1, 0.25, 0.7)), rough=0.4)]
    for i, (cx, cz, w, h, ang, msg) in enumerate(notes):
        ps = len(K.PARTS)
        box(cx - w / 2, cx + w / 2, -0.0135, -0.012, cz - h / 2, cz + h / 2, M['paper'])
        lines = msg.split("\n")
        for li, ln in enumerate(lines):
            if not ln: continue
            sz = 0.017 if li == 0 else 0.0105
            text(ln, cx, -0.0140, cz + h / 2 - 0.03 - li * 0.018, sz, M['ink'], extrude=0.0002)
        if i == 3:   # tirinhas para destacar
            for k in range(6):
                box(cx - w / 2 + 0.012 + k * 0.031, cx - w / 2 + 0.04 + k * 0.031, -0.0137, -0.0135, cz - h / 2 - 0.045, cz - h / 2, M['paper'])
        rot_parts(K.PARTS[ps:], math.radians(ang), 'Y', (cx, -0.013, cz))
        cyl(cx, -0.018, cz + h / 2 - 0.012, 0.006, 0.008, pin_cols[i % 3], 'Y', 12)

    # cartaz A4 (0,21 x 0,297), papel já amarelado, preso com fita nos cantos, por cima do canto do "coleta"
    ps = len(K.PARTS)
    cx, cz, w, h = 0.16, 0.38, 0.21, 0.297
    old = K.material("OldPaper", lin((0.90, 0.86, 0.74)), rough=0.85, color_var=0.08, dirt=0.25, dirt_color=lin((0.6, 0.52, 0.38)))
    photo = K.material("PhotoBack", lin((0.42, 0.45, 0.50)), rough=0.6, color_var=0.1)
    silh = K.material("PhotoSilhouette", lin((0.16, 0.15, 0.15)), rough=0.6)
    tape = K.material("Tape", lin((0.86, 0.84, 0.72)), rough=0.4)
    y0 = -0.0145
    box(cx - w / 2, cx + w / 2, y0, y0 + 0.001, cz - h / 2, cz + h / 2, old)
    text("DESAPARECIDA", cx, y0 - 0.0004, cz + h / 2 - 0.032, 0.026, M['red'], extrude=0.0003)
    px0, px1, pz0, pz1 = cx - 0.055, cx + 0.055, cz + 0.005, cz + 0.105                      # foto 3x4 ampliada
    box(px0, px1, y0 - 0.0008, y0, pz0, pz1, photo)
    cyl(cx, y0 - 0.0011, pz0 + 0.064, 0.018, 0.0005, silh, 'Y', 24)                            # cabeça
    box(cx - 0.009, cx + 0.009, y0 - 0.0011, y0 - 0.0008, pz0 + 0.036, pz0 + 0.052, silh)       # pescoço
    bm = bmesh.new(); pts = []                                                                   # ombros: meia-elipse
    for k in range(25):
        a = math.pi * k / 24
        pts.append(bm.verts.new((cx + math.cos(a) * 0.050, y0 - 0.0011, pz0 + math.sin(a) * 0.040)))
    bm.faces.new(pts); me = bpy.data.meshes.new("sh"); bm.to_mesh(me); bm.free()
    sh = bpy.data.objects.new("sh", me); bpy.context.collection.objects.link(sh); solidify(sh, 0.0003); K._finish(sh, silh)
    info = ["Helena M., 31 anos", "moradora do 405", "", "Vista pela última vez em 09/11,", "voltando do trabalho à noite.",
            "", "Qualquer informação:", "portaria ou 190"]
    for li, ln in enumerate(info):
        if ln: text(ln, cx, y0 - 0.0004, cz - 0.015 - li * 0.0145, 0.0105 if li > 1 else 0.012, M['ink'], extrude=0.0002)
    rot_parts(K.PARTS[ps:], math.radians(2.5), 'Y', (cx, -0.014, cz))
    for (tx, tz, ta) in ((cx - w / 2, cz + h / 2, 40), (cx + w / 2, cz + h / 2, -40)):        # fita nos cantos de cima
        t = box(tx - 0.03, tx + 0.03, y0 - 0.0016, y0 - 0.0010, tz - 0.009, tz + 0.009, tape)
        rot_parts([t], math.radians(ta), 'Y', (tx, -0.014, tz))
    parts["NoticeBoard"] = K.PARTS[s:]

    # --- quadro de chaves 0,45 x 0,55 (madeira, 4 x 5 ganchos, um vazio)
    s = len(K.PARTS)
    W, H = 0.45, 0.55
    box(-W / 2, W / 2, -0.018, 0, 0, H, M['wood'], 0.004)
    text("CHAVES", 0, -0.0185, H - 0.045, 0.03, M['brass'], extrude=0.001)
    tag_cols = [M['red'], yellow, pin_cols[2], K.material("TagGreen", lin((0.1, 0.5, 0.2)), rough=0.4)]
    for r in range(4):
        for c in range(5):
            x = -W / 2 + 0.06 + c * 0.0825; z = H - 0.12 - r * 0.11
            cyl(x, -0.03, z, 0.0035, 0.025, M['brass'], 'Y', 8)
            if (r, c) == (1, 3): continue                     # gancho vazio
            box(x - 0.008, x + 0.008, -0.036, -0.03, z - 0.065, z - 0.005, M['steel'], 0.002)     # chave
            cyl(x, -0.038, z - 0.08, 0.014, 0.003, tag_cols[(r + c) % 4], 'Y', 16)               # etiqueta
    parts["KeyCabinet"] = K.PARTS[s:]

    # --- letras do prédio (aço escovado, 1,5 cm de relevo)
    s = len(K.PARTS)
    text("EDIFÍCIO", 0, -0.0075, 0.25, 0.075, M["steel"], extrude=0.0075)
    text("PORTO SERENO", 0, -0.0075, 0.05, 0.15, M["steel"], extrude=0.0075)
    parts["BuildingName"] = K.PARTS[s:]

    # --- ripado: 2,58 x 2,78; ripas 4 cm com 2 cm de vão sobre fundo escuro (origem: atrás, embaixo, centro)
    s = len(K.PARTS)
    W, H = 2.58, 2.78
    box(-W / 2, W / 2, -0.012, 0, 0, H, backboard)
    n = int(W / 0.06)
    x = -W / 2 + (W - (n * 0.06 - 0.02)) / 2
    for k in range(n):
        box(x, x + 0.04, -0.032, -0.012, 0, H, slat, 0.002)
        x += 0.06
    parts["SlatPanel"] = K.PARTS[s:]

    # --- placa "sorria" 0,30 x 0,20
    s = len(K.PARTS)
    box(-0.15, 0.15, -0.003, 0, 0, 0.20, yellow, 0.002)
    text("SORRIA!", 0, -0.0035, 0.155, 0.032, M['ink'], extrude=0.0004)
    text("VOCÊ ESTÁ SENDO", 0, -0.0035, 0.105, 0.019, M['ink'], extrude=0.0004)
    text("FILMADO", 0, -0.0035, 0.07, 0.03, M['ink'], extrude=0.0004)
    box(-0.03, 0.02, -0.006, -0.003, 0.018, 0.045, M['ink']); cyl(0.032, -0.0045, 0.031, 0.01, 0.003, M['ink'], 'Y', 16)   # câmera
    parts["FilmedPlaque"] = K.PARTS[s:]

    # --- SAÍDA (caixa 0,34 x 0,16, acesa)
    s = len(K.PARTS)
    box(-0.17, 0.17, -0.05, 0, 0, 0.16, M['white'], 0.006)
    box(-0.155, 0.155, -0.052, -0.05, 0.015, 0.145, green_em)
    text("SAÍDA", -0.02, -0.0535, 0.08, 0.06, white_em, extrude=0.0005)
    box(0.085, 0.13, -0.0535, -0.052, 0.074, 0.086, white_em);
    for k in (1, -1):
        e = box(0.115, 0.145, -0.0535, -0.052, 0.074, 0.086, white_em)
        rot_parts([e], math.radians(35 * k), 'Y', (0.145, -0.053, 0.08))
    parts["ExitSign"] = K.PARTS[s:]

    # --- luminária de emergência
    s = len(K.PARTS)
    box(-0.15, 0.15, -0.06, 0, 0, 0.07, M['white'], 0.008)
    for k in (-1, 1):
        cyl(k * 0.09, -0.065, 0.035, 0.022, 0.03, M['white'], 'Y', 20, 0.003)
        cyl(k * 0.09, -0.081, 0.035, 0.016, 0.004, K.material("LensClear", lin((0.85, 0.85, 0.8)), rough=0.15), 'Y', 20)
    cyl(0.0, -0.061, 0.055, 0.004, 0.003, K.material("LedGreen", lin((0.1, 0.9, 0.2)), rough=0.3, emission=(0.1, 1.0, 0.2), emission_strength=1.0), 'Y', 10)
    parts["EmergencyLight"] = K.PARTS[s:]

    # --- botoeira do elevador
    s = len(K.PARTS)
    box(-0.055, 0.055, -0.006, 0, 0, 0.22, M['steel'], 0.003)
    cyl(0, -0.007, 0.13, 0.024, 0.004, M['steel'], 'Y', 32, 0.001)
    cyl(0, -0.009, 0.13, 0.018, 0.003, amber_em, 'Y', 32)
    tri(0, -0.0112, 0.13, 0.016, M["ink"])
    for k in range(3): cyl(-0.012 + k * 0.006, -0.0075, 0.085, 0.0016, 0.002, M['steel'], 'Y', 8)   # braille
    parts["ElevatorPanel"] = K.PARTS[s:]

    # --- indicador de andar
    s = len(K.PARTS)
    box(-0.12, 0.12, -0.03, 0, 0, 0.10, M['steel'], 0.004)
    box(-0.10, 0.10, -0.032, -0.03, 0.012, 0.088, M['black'])
    text("T", -0.03, -0.0335, 0.05, 0.055, red_em, extrude=0.0005)
    tri(0.05, -0.0335, 0.05, 0.032, red_em)
    parts["ElevatorIndicator"] = K.PARTS[s:]

    # --- câmera de segurança (suporte de parede + corpo bala com quebra-sol + LED)
    s = len(K.PARTS)
    box(-0.04, 0.04, -0.012, 0, -0.05, 0.03, M['white'], 0.004)
    cyl(0, -0.07, -0.01, 0.012, 0.12, M['white'], 'Y', 16)
    bs = len(K.PARTS)
    cyl(0, -0.20, -0.03, 0.035, 0.16, M['white'], 'Y', 24, 0.004)
    box(-0.042, 0.042, -0.30, -0.12, 0.0, 0.006, M['white'], 0.002)
    cyl(0, -0.281, -0.03, 0.026, 0.004, M['black'], 'Y', 24)
    cyl(0.022, -0.283, -0.012, 0.003, 0.002, led_em, 'Y', 8)
    rot_parts(K.PARTS[bs:], math.radians(-22), 'X', (0, -0.12, -0.01))    # olhando para baixo
    parts["CctvCamera"] = K.PARTS[s:]

    # --- vaso com espada-de-são-jorge (Sansevieria): vaso 0,36 x 0,50; folhas de 0,45 a 0,95 m
    s = len(K.PARTS)
    bpy.ops.mesh.primitive_cylinder_add(vertices=40, radius=0.18, depth=0.50, location=(0, 0, 0.25))
    v = bpy.context.active_object
    for vt in v.data.vertices:
        if vt.co.z < 0: vt.co.x *= 0.78; vt.co.y *= 0.78
    open_top(v, 0.45); solidify(v, 0.012)
    m_ = v.modifiers.new("b", 'BEVEL'); m_.width = 0.004; m_.segments = 2
    bpy.ops.object.modifier_apply(modifier="b"); K._finish(v, pot)
    cyl(0, 0, 0.47, 0.165, 0.02, soil, 'Z', 40)
    def sansev_mat(nt, p, tc):
        # faixas transversais claras/escuras ao longo da altura (onda distorcida)
        w = nt.nodes.new('ShaderNodeTexWave'); w.wave_type = 'BANDS'; w.bands_direction = 'Z'
        w.inputs['Scale'].default_value = 9.0; w.inputs['Distortion'].default_value = 9.0; w.inputs['Detail'].default_value = 3.0
        nt.links.new(tc.outputs['Object'], w.inputs['Vector'])
        c = ramp(nt, w.outputs['Fac'], [(0.0, (0.06, 0.16, 0.07)), (0.45, (0.10, 0.24, 0.10)), (0.6, (0.30, 0.42, 0.24)), (1.0, (0.12, 0.26, 0.11))])
        nt.links.new(c, p.inputs['Base Color']); p.inputs['Roughness'].default_value = 0.45
    sleaf = simple_mat("SansevieriaLeaf", sansev_mat)
    for k in range(16):
        a = k * 2.39996 + random.uniform(-0.2, 0.2); r = random.uniform(0.0, 0.10)
        L = random.uniform(0.45, 0.95); wid = random.uniform(0.045, 0.07)
        tilt = random.uniform(0.05, 0.28) * (0.6 + r * 4)
        twist = random.uniform(-0.6, 0.6); face_a = a + random.uniform(-0.8, 0.8)
        base = Vector((math.cos(a) * r, math.sin(a) * r, 0.47))
        out = Vector((math.cos(a), math.sin(a), 0))
        bm = bmesh.new(); prev = None; segs = 10
        for t_ in range(segs + 1):
            u = t_ / segs
            wu = max(0.002, wid * (1.0 - u ** 3) * (0.75 + 0.25 * math.sin(math.pi * min(1.0, u + 0.2))))
            ang = face_a + twist * u
            side = Vector((-math.sin(ang), math.cos(ang), 0)) * (wu / 2)
            c = base + out * (tilt * L * u * u) + Vector((0, 0, L * u))
            fold = Vector((math.cos(ang), math.sin(ang), 0)) * (0.006 * (1 - u))    # folha em calha
            v1 = bm.verts.new(c - side + fold); v2 = bm.verts.new(c + side + fold); v3 = bm.verts.new(c)
            if prev:
                bm.faces.new((prev[0], v1, v3, prev[2])); bm.faces.new((prev[2], v3, v2, prev[1]))
            prev = (v1, v2, v3)
        me = bpy.data.meshes.new("sleaf"); bm.to_mesh(me); bm.free()
        lo = bpy.data.objects.new("sleaf", me); bpy.context.collection.objects.link(lo)
        solidify(lo, 0.004)
        for pg in lo.data.polygons: pg.use_smooth = True
        K._finish(lo, sleaf)
    parts["PlantPot"] = K.PARTS[s:]

    # --- tapete da entrada 1,60 x 0,90
    s = len(K.PARTS)
    box(-0.80, 0.80, -0.45, 0.45, 0, 0.008, rubber, 0.004)
    box(-0.74, 0.74, -0.39, 0.39, 0.008, 0.016, coir, 0.003)
    parts["EntranceMat"] = K.PARTS[s:]

    # --- placa "piso molhado" (cavalete aberto; 0,30 x 0,62)
    s = len(K.PARTS)
    for k in (-1, 1):
        ps = len(K.PARTS)
        box(-0.15, 0.15, -0.008, 0.008, 0.0, 0.62, yellow, 0.01)
        if k == -1:
            text("CUIDADO", 0, -0.0085, 0.44, 0.045, M['ink'], extrude=0.0006)
            text("PISO", 0, -0.0085, 0.37, 0.04, M['ink'], extrude=0.0006)
            text("MOLHADO", 0, -0.0085, 0.32, 0.04, M['ink'], extrude=0.0006)
            tri(0, -0.0085, 0.17, 0.13, M["ink"], outline=True); text("!", 0, -0.0085, 0.16, 0.06, M["ink"], extrude=0.0006)
        rot_parts(K.PARTS[ps:], math.radians(14 * k), 'X', (0, 0, 0.62))
    box(-0.12, 0.12, -0.02, 0.02, 0.6, 0.64, yellow, 0.01)     # alça/topo
    parts["WetFloorSign"] = K.PARTS[s:]

    # --- revistas na mesinha
    s = len(K.PARTS)
    covers = [K.material(f"Cover{i}", lin(c), rough=0.35) for i, c in enumerate(((0.75, 0.15, 0.12), (0.12, 0.30, 0.55), (0.85, 0.80, 0.70)))]
    for k in range(3):
        ps = len(K.PARTS)
        box(-0.105, 0.105, -0.14, 0.14, k * 0.006, k * 0.006 + 0.005, covers[k], 0.001)
        rot_parts(K.PARTS[ps:], math.radians((-8, 6, -3)[k]), 'Z', (0, 0, 0))
    parts["Magazines"] = K.PARTS[s:]
    return finish_group(parts)

# ====================================================================== DESK
def screen_mat():
    """Tela do monitor de segurança: 4 câmeras (2 x 2). Coordenadas pela POSIÇÃO da tela no objeto (x -0,24..0,24,
    z 0,11..0,38) — Generated muda quando as peças são juntadas."""
    def fn(nt, p, tc):
        mp = nt.nodes.new('ShaderNodeMapping'); mp.vector_type = 'POINT'
        mp.inputs['Scale'].default_value = (1 / 0.48, 1.0, 1 / 0.27)
        mp.inputs['Location'].default_value = (0.5, 0.0, -0.11 / 0.27)
        nt.links.new(tc.outputs['Object'], mp.inputs['Vector'])
        sep = nt.nodes.new('ShaderNodeSeparateXYZ'); nt.links.new(mp.outputs[0], sep.inputs[0])
        def mathn(op, a, b=None):
            n = nt.nodes.new('ShaderNodeMath'); n.operation = op
            if isinstance(a, float): n.inputs[0].default_value = a
            else: nt.links.new(a, n.inputs[0])
            if b is not None:
                if isinstance(b, float): n.inputs[1].default_value = b
                else: nt.links.new(b, n.inputs[1])
            return n.outputs[0]
        fx = mathn('FRACT', mathn('MULTIPLY', sep.outputs['X'], 2.0))
        fz = mathn('FRACT', mathn('MULTIPLY', sep.outputs['Z'], 2.0))
        # cada câmera: parede (cinza médio, manchas) em cima, piso mais claro embaixo, uma porta escura
        nz = noise_fac(nt, mp.outputs[0], 14.0, 6.0)
        wall = ramp(nt, nz, [(0.3, (0.26, 0.27, 0.26)), (0.7, (0.40, 0.41, 0.40))])
        floor_c = ramp(nt, nz, [(0.3, (0.46, 0.47, 0.45)), (0.7, (0.60, 0.61, 0.58))])
        is_floor = mathn('LESS_THAN', fz, 0.38)
        m1 = nt.nodes.new('ShaderNodeMix'); m1.data_type = 'RGBA'
        nt.links.new(is_floor, m1.inputs['Factor']); nt.links.new(wall, m1.inputs['A']); nt.links.new(floor_c, m1.inputs['B'])
        door = mathn('MULTIPLY', mathn('MULTIPLY', mathn('GREATER_THAN', fx, 0.62), mathn('LESS_THAN', fx, 0.78)),
                     mathn('MULTIPLY', mathn('GREATER_THAN', fz, 0.38), mathn('LESS_THAN', fz, 0.8)))
        m2 = nt.nodes.new('ShaderNodeMix'); m2.data_type = 'RGBA'; m2.inputs['B'].default_value = (0.05, 0.05, 0.05, 1)
        nt.links.new(door, m2.inputs['Factor']); nt.links.new(m1.outputs['Result'], m2.inputs['A'])
        e = mathn('MAXIMUM', mathn('GREATER_THAN', mathn('ABSOLUTE', mathn('SUBTRACT', fx, 0.5)), 0.485),
                  mathn('GREATER_THAN', mathn('ABSOLUTE', mathn('SUBTRACT', fz, 0.5)), 0.485))
        m3 = nt.nodes.new('ShaderNodeMix'); m3.data_type = 'RGBA'; m3.inputs['B'].default_value = (0.0, 0.0, 0.0, 1)
        nt.links.new(e, m3.inputs['Factor']); nt.links.new(m2.outputs['Result'], m3.inputs['A'])
        tint = nt.nodes.new('ShaderNodeMix'); tint.data_type = 'RGBA'; tint.blend_type = 'MULTIPLY'; tint.inputs['Factor'].default_value = 1.0
        tint.inputs['B'].default_value = (0.8, 0.95, 0.85, 1)
        nt.links.new(m3.outputs['Result'], tint.inputs['A'])
        nt.links.new(tint.outputs['Result'], p.inputs['Base Color'])
        nt.links.new(tint.outputs['Result'], p.inputs['Emission Color'])
        p.inputs['Emission Strength'].default_value = 1.0
        p.inputs['Roughness'].default_value = 0.2
    return simple_mat("CctvScreen", fn)

def build_desk():
    M = mats()
    parts = {}
    scr_text = K.material("ScreenText", (0.9, 0.9, 0.9), rough=0.3, emission=(0.9, 0.95, 0.9), emission_strength=1.0)
    # --- monitor 22" (tela 0,48 x 0,27), base no tampo
    s = len(K.PARTS)
    box(-0.25, 0.25, -0.015, 0.02, 0.10, 0.39, M['black'], 0.006)                      # carcaça
    scr = box(-0.24, 0.24, -0.0165, -0.015, 0.11, 0.38, screen_mat())                   # tela
    for i, (x, z) in enumerate(((-0.235, 0.37), (0.005, 0.37), (-0.235, 0.235), (0.005, 0.235))):
        text(f"CAM 0{i + 1}  23:41", x + 0.005, -0.0168, z - 0.008, 0.008, scr_text, extrude=0.0001, align='LEFT')
    cyl(0, 0.03, 0.16, 0.02, 0.18, M['black'], 'Z', 16)                                   # haste
    box(-0.11, 0.11, -0.06, 0.10, 0, 0.012, M['black'], 0.004)                            # pé
    cyl(0.215, -0.0165, 0.103, 0.003, 0.002, K.material("PowerLed", lin((0.2, 0.6, 1.0)), rough=0.3, emission=(0.2, 0.6, 1.0), emission_strength=1.0), 'Y', 8)
    parts["Monitor"] = K.PARTS[s:]

    # --- teclado 0,44 x 0,14
    s = len(K.PARTS)
    box(-0.22, 0.22, -0.07, 0.07, 0, 0.018, M['black'], 0.004)
    keycap = K.material("KeyCap", lin((0.10, 0.10, 0.11)), rough=0.5, dirt=0.15)
    for r in range(5):
        for c in range(14):
            x = -0.205 + c * 0.0293; y = -0.055 + r * 0.025
            if r == 0 and 3 < c < 10:
                if c == 4: box(x, x + 0.0293 * 6 - 0.003, y, y + 0.021, 0.018, 0.026, keycap, 0.002)
                continue
            box(x, x + 0.026, y, y + 0.021, 0.018, 0.026, keycap, 0.002)
    parts["Keyboard"] = K.PARTS[s:]

    # --- interfone de mesa: corpo em cunha (frente baixa), fone deitado na lateral esquerda, teclado à direita
    s = len(K.PARTS)
    bpy.ops.mesh.primitive_cube_add(size=1, location=(0, 0, 0.03))
    b = bpy.context.active_object; b.scale = (0.19, 0.21, 0.06); bpy.ops.object.transform_apply(scale=True)
    for vt in b.data.vertices:
        if vt.co.z > 0.04 and vt.co.y < 0: vt.co.z = 0.035      # topo inclinado para a frente (-Y)
    mod = b.modifiers.new("b", 'BEVEL'); mod.width = 0.008; mod.segments = 3; bpy.ops.object.modifier_apply(modifier="b")
    K._finish(b, M['white'])
    gray = K.material("PhoneGrey", lin((0.55, 0.56, 0.56)), rough=0.4)
    box(-0.09, -0.02, -0.10, 0.10, 0.045, 0.062, gray, 0.004)                                     # berço
    box(-0.085, -0.025, -0.115, 0.115, 0.062, 0.082, M['white'], 0.009)                           # fone
    box(-0.09, -0.02, -0.12, -0.07, 0.056, 0.09, M['white'], 0.012)                               # bocal
    box(-0.09, -0.02, 0.07, 0.12, 0.056, 0.09, M['white'], 0.012)                                 # auricular
    keym = K.material("PhoneKey", lin((0.75, 0.75, 0.73)), rough=0.4)
    for r in range(4):
        for c in range(3):
            y = -0.08 + r * 0.024; z = 0.035 + (y + 0.105) / 0.21 * 0.025 + 0.002
            box(0.005 + c * 0.024, 0.023 + c * 0.024, y, y + 0.016, z, z + 0.006, keym, 0.002)
    box(0.005, 0.071, 0.02, 0.05, 0.051, 0.058, K.material("PhoneLcd", lin((0.35, 0.45, 0.30)), rough=0.2, emission=lin((0.35, 0.5, 0.3)), emission_strength=0.5))
    parts["DeskPhone"] = K.PARTS[s:]

    # --- rádio portátil 0,26 x 0,09 x 0,15
    s = len(K.PARTS)
    radio_body = K.material("RadioBody", lin((0.16, 0.16, 0.17)), rough=0.45, dirt=0.2, wear=0.15, wear_color=lin((0.4, 0.4, 0.4)))
    box(-0.13, 0.13, -0.045, 0.045, 0, 0.15, radio_body, 0.012)
    cyl(-0.05, -0.046, 0.075, 0.055, 0.004, M['steel'], 'Y', 32)                          # grade do alto-falante
    for r in range(-4, 5):
        for c in range(-4, 5):
            if r * r + c * c <= 18: cyl(-0.05 + c * 0.011, -0.0485, 0.075 + r * 0.011, 0.0035, 0.002, M['black'], 'Y', 8)
    box(0.03, 0.115, -0.047, -0.045, 0.085, 0.125, K.material("DialGlass", lin((0.85, 0.75, 0.45)), rough=0.2, emission=lin((0.9, 0.7, 0.35)), emission_strength=0.6))
    for k in range(9): box(0.035 + k * 0.009, 0.0365 + k * 0.009, -0.0475, -0.047, 0.095, 0.115, M['ink'])
    cyl(0.05, -0.05, 0.05, 0.014, 0.012, M['black'], 'Y', 20, 0.002); cyl(0.095, -0.05, 0.05, 0.014, 0.012, M['black'], 'Y', 20, 0.002)
    ant = cyl(0.11, 0.03, 0.30, 0.003, 0.30, M['steel'], 'Z', 8)
    rot_parts([ant], math.radians(-25), 'Y', (0.11, 0.03, 0.15))
    box(-0.10, 0.10, -0.008, 0.008, 0.15, 0.17, M['black'], 0.004)                        # alça
    parts["Radio"] = K.PARTS[s:]

    # --- livro de visitas aberto (0,42 x 0,30) + caneta
    s = len(K.PARTS)
    coverb = K.material("BookCover", lin((0.10, 0.14, 0.30)), rough=0.5, wear=0.15, wear_color=lin((0.3, 0.3, 0.35)))
    box(-0.215, 0.215, -0.15, 0.15, 0, 0.004, coverb, 0.002)
    rows = ["Data      Nome               Apto   Hora",
            "24/11   Carlos M.           104    18:12",
            "24/11   entrega iFood       503    19:40",
            "24/11   Marta (diarista)    201    20:05",
            "24/11   ---                         ---",
            "24/11   J.                          23:"]
    for side in (-1, 1):
        bm = bmesh.new(); segs = 12; prev = None
        for i in range(segs + 1):
            u = i / segs; x = side * (0.005 + 0.2 * u)
            z = 0.004 + 0.022 * math.sin(math.pi * (1 - u) * 0.5) * (1 - u) + 0.006
            a = bm.verts.new((x, -0.14, z)); b = bm.verts.new((x, 0.14, z))
            if prev: bm.faces.new((prev[0], a, b, prev[1]) if side > 0 else (prev[1], b, a, prev[0]))
            prev = (a, b)
        me = bpy.data.meshes.new("page"); bm.to_mesh(me); bm.free()
        po = bpy.data.objects.new("page", me); bpy.context.collection.objects.link(po)
        so = po.modifiers.new("so", 'SOLIDIFY'); so.thickness = 0.008; so.offset = -1
        bpy.context.view_layer.objects.active = po; bpy.ops.object.modifier_apply(modifier="so")
        K._finish(po, M['paper'])
    for i, ln in enumerate(rows):
        text(ln, 0.015, 0.11 - i * 0.03, 0.0125, 0.0085, M['ink'], extrude=0.0001, align='LEFT', rot=(0, 0, 0))
    for i in range(9): box(0.015, 0.195, 0.118 - i * 0.03, 0.1185 - i * 0.03, 0.0122, 0.0124, K.material("Ruling", lin((0.55, 0.65, 0.85)), rough=0.6))
    pen = cyl(0.10, -0.05, 0.016, 0.0045, 0.14, K.material("PenBlue", lin((0.08, 0.15, 0.55)), rough=0.3), 'X', 12)
    rot_parts([pen], math.radians(25), 'Z', (0.10, -0.05, 0.016))
    parts["Logbook"] = K.PARTS[s:]

    # --- caneca de café (pela metade)
    s = len(K.PARTS)
    mugm = K.material("MugCeramic", lin((0.86, 0.84, 0.80)), rough=0.25, dirt=0.25, dirt_color=lin((0.45, 0.32, 0.2)))
    bpy.ops.mesh.primitive_cylinder_add(vertices=32, radius=0.042, depth=0.10, location=(0, 0, 0.05))
    mug = bpy.context.active_object
    open_top(mug, 0.09); solidify(mug, 0.005)          # boca aberta, parede de 5 mm
    K._finish(mug, mugm)
    cyl(0, 0, 0.055, 0.036, 0.004, K.material("Coffee", lin((0.10, 0.05, 0.02)), rough=0.08), 'Z', 32)
    bpy.ops.mesh.primitive_torus_add(major_radius=0.028, minor_radius=0.007, location=(0.046, 0, 0.05), rotation=(math.pi / 2, 0, 0))
    K._finish(bpy.context.active_object, mugm)
    parts["Mug"] = K.PARTS[s:]
    return finish_group(parts)

# ====================================================================== STORAGE
def build_storage():
    M = mats()
    parts = {}
    yellow = K.material("BucketYellow", lin((0.92, 0.70, 0.05)), rough=0.35, dirt=0.35, dirt_color=lin((0.35, 0.30, 0.2)), color_var=0.05)
    grey = K.material("WringerGrey", lin((0.30, 0.31, 0.32)), rough=0.5, dirt=0.3)
    water = K.material("DirtyWater", lin((0.20, 0.18, 0.13)), rough=0.05)
    door_paint = K.material("DoorPaint", lin((0.42, 0.47, 0.46)), rough=0.45, dirt=0.3, wear=0.2, wear_color=lin((0.6, 0.6, 0.58)), ground_dirt=0.4, color_var=0.04)
    # --- balde espremedor (0,40 x 0,30 x 0,35 + espremedor)
    s = len(K.PARTS)
    bpy.ops.mesh.primitive_cube_add(size=1, location=(0, 0, 0.2))
    b = bpy.context.active_object; b.scale = (0.40, 0.30, 0.30); bpy.ops.object.transform_apply(scale=True)
    for v in b.data.vertices:
        if v.co.z < 0.1: v.co.x *= 0.85; v.co.y *= 0.85
    open_top(b, 0.30)
    mod = b.modifiers.new("b", 'BEVEL'); mod.width = 0.03; mod.segments = 3; bpy.ops.object.modifier_apply(modifier="b")
    solidify(b, 0.006)
    K._finish(b, yellow)
    box(-0.185, 0.185, -0.135, 0.135, 0.26, 0.262, water)
    for (x, y) in ((-0.15, -0.11), (0.15, -0.11), (-0.15, 0.11), (0.15, 0.11)):
        cyl(x, y, 0.025, 0.025, 0.02, M['black'], 'X', 16)
    box(0.05, 0.20, -0.13, 0.13, 0.35, 0.47, grey, 0.01)                                     # espremedor
    lev = box(0.10, 0.13, -0.02, 0.02, 0.47, 0.85, grey, 0.006)
    rot_parts([lev], math.radians(-18), 'Y', (0.115, 0, 0.47))
    bpy.ops.mesh.primitive_torus_add(major_radius=0.12, minor_radius=0.006, location=(-0.05, 0, 0.36), rotation=(math.pi / 2, 0, 0))
    K._finish(bpy.context.active_object, M['steel'])                                         # alça
    parts["MopBucket"] = K.PARTS[s:]

    # --- esfregão encostado (cabo 1,3 m inclinado para +Y, a parede)
    s = len(K.PARTS)
    cyl(0, 0, 0.75, 0.012, 1.30, K.material("MopStick", lin((0.15, 0.32, 0.55)), rough=0.4, dirt=0.2), 'Z', 12)
    cyl(0, 0, 0.12, 0.03, 0.05, grey, 'Z', 16)
    strand = K.material("MopStrand", lin((0.70, 0.66, 0.58)), rough=0.9, dirt=0.5, dirt_color=lin((0.35, 0.30, 0.22)))
    for k in range(26):
        a = k * 2 * math.pi / 26 + random.uniform(-0.1, 0.1); r = random.uniform(0.04, 0.12)
        top = Vector((math.cos(a) * 0.02, math.sin(a) * 0.02, 0.11)); bot = Vector((math.cos(a) * r, math.sin(a) * r, 0.0))
        d = top - bot
        bpy.ops.mesh.primitive_cylinder_add(vertices=6, radius=0.008, depth=d.length, location=(top + bot) / 2)
        st = bpy.context.active_object; st.rotation_euler = d.to_track_quat('Z', 'Y').to_euler(); bpy.ops.object.transform_apply(rotation=True)
        K._finish(st, strand)
    rot_parts(K.PARTS[s:], math.radians(-13), 'X', (0, 0, 0))
    parts["Mop"] = K.PARTS[s:]

    # --- galões de produtos de limpeza (4)
    s = len(K.PARTS)
    jugs = [("ÁGUA\nSANITÁRIA", (0.92, 0.92, 0.90), (0.1, 0.3, 0.7)), ("DESINFETANTE", (0.25, 0.55, 0.85), (0.95, 0.95, 0.95)),
            ("DETERGENTE", (0.95, 0.75, 0.15), (0.1, 0.4, 0.15)), ("CERA\nLÍQUIDA", (0.75, 0.15, 0.10), (0.95, 0.9, 0.8))]
    for i, (lbl, body, band) in enumerate(jugs):
        x = -0.27 + i * 0.18
        bm_ = K.material(f"Jug{i}", lin(body), rough=0.35, dirt=0.2)
        lb = K.material(f"JugLabel{i}", lin(band), rough=0.5)
        box(x - 0.07, x + 0.07, -0.05, 0.05, 0, 0.26, bm_, 0.02, 3)
        cyl(x + 0.03, 0, 0.275, 0.02, 0.03, bm_, 'Z', 16, 0.003)
        box(x - 0.055, x - 0.005, -0.012, 0.012, 0.20, 0.27, bm_, 0.008)                       # alça
        box(x - 0.066, x + 0.066, -0.052, -0.05, 0.06, 0.17, lb)
        for li, ln in enumerate(lbl.split("\n")):
            text(ln, x, -0.0525, 0.125 - li * 0.025, 0.016 if len(ln) < 10 else 0.0115, M['ink'] if band[0] > 0.5 else M['paper'], extrude=0.0002)
    parts["CleaningBottles"] = K.PARTS[s:]

    # --- folha da porta da sala de produtos: 0,87 x 2,00 x 0,045, frente (-Y) = corredor/saguão.
    #     Maçaneta no lado do Unity +X (Blender -X); chapa de chute inox; placa "DEPÓSITO".
    s = len(K.PARTS)
    W, H, T = 0.87, 2.0, 0.045
    box(-W / 2, W / 2, -T / 2, T / 2, 0, H, door_paint, 0.004)
    for sgn in (-1, 1):
        box(-W / 2 + 0.03, W / 2 - 0.03, sgn * T / 2, sgn * (T / 2 + 0.0015), 0.02, 0.25, M['steel'], 0.002)     # chute
    hx = -(W / 2 - 0.075)
    for sgn in (-1, 1):
        y = sgn * T / 2
        box(hx - 0.022, hx + 0.022, y, y + sgn * 0.008, 0.88, 1.12, M['steel'], 0.003)                         # espelho
        cyl(hx, y + sgn * 0.03, 1.02, 0.009, 0.05, M['steel'], 'Y', 12)
        lv = cyl(hx + 0.06, y + sgn * 0.052, 1.02, 0.009, 0.13, M['steel'], 'X', 12, 0.002)
        cyl(hx, y + sgn * 0.010, 0.94, 0.006, 0.004, M['black'], 'Y', 12)                                   # cilindro da chave
    sign = K.material("SignPlate", lin((0.15, 0.15, 0.16)), rough=0.4)
    box(-0.16, 0.16, -T / 2 - 0.003, -T / 2, 1.50, 1.62, sign, 0.003)
    text("DEPÓSITO", 0, -T / 2 - 0.0035, 1.575, 0.035, M['paper'], extrude=0.0004)
    text("ACESSO RESTRITO", 0, -T / 2 - 0.0035, 1.525, 0.017, M['paper'], extrude=0.0004)
    for z in (0.25, 1.0, 1.75):                                                                              # dobradiças (lado +X)
        box(W / 2 - 0.004, W / 2 + 0.004, -0.012, 0.012, z, z + 0.10, M['steel'])
    parts["UtilityDoor"] = K.PARTS[s:]
    return finish_group(parts)

ALL = {"lobby": build_lobby, "desk": build_desk, "storage": build_storage}[GROUP]()

# ------------------------------------------------------------------ prévia (tudo enfileirado de frente)
if PREV_DIR:
    os.makedirs(PREV_DIR, exist_ok=True)
    sc = bpy.context.scene; sc.render.engine = 'CYCLES'; sc.cycles.samples = 32
    sc.render.resolution_x, sc.render.resolution_y = 1800, 900
    sc.view_settings.view_transform = 'Standard'
    w = bpy.data.worlds.new("w"); sc.world = w; w.use_nodes = True
    w.node_tree.nodes['Background'].inputs['Color'].default_value = (0.35, 0.35, 0.37, 1); w.node_tree.nodes['Background'].inputs['Strength'].default_value = 0.8
    x = 0.0; maxh = 0.0
    big = [o for o in ALL if o.name == "SlatPanel"]
    for o in ALL:
        if o in big: continue
        d = o.dimensions; o.location.x = x + d.x / 2 - (o.bound_box[0][0] + o.bound_box[6][0]) / 2 * 0 ; x += d.x + 0.12; maxh = max(maxh, d.z)
    for o in big: o.location = (x / 2, 2.0, 0)
    bpy.ops.object.light_add(type='SUN', rotation=(math.radians(55), 0, math.radians(25))); bpy.context.active_object.data.energy = 3.0
    bpy.ops.object.camera_add(); cam = bpy.context.active_object; sc.camera = cam
    cam.data.type = 'ORTHO'; cam.data.ortho_scale = max(x, maxh * 2.1) * 1.02
    cam.location = (x / 2, -8, maxh * 0.5 + 0.05); cam.rotation_euler = (math.radians(90), 0, 0)
    sc.render.filepath = os.path.join(PREV_DIR, GROUP + ".png"); bpy.ops.render.render(write_still=True)
    for o in ALL: o.location = (0, 0, 0)
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
print(f"[build_reception] OK {GROUP}:", OUT_FBX, [(o.name, len(o.data.polygons)) for o in ALL])
