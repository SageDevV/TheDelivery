"""Folha dupla de jornal REALISTA para o TheDelivery (Blender, background).

1) Diagrama as páginas (texto real em malha, fotos com grão) e RENDERIZA cada lado da folha numa
   imagem (câmera ortográfica, só emissão -> impressão fiel).
2) Monta a folha (0,64 x 0,56 m, dobrada ao meio no X=0), DUAS faces (fora/dentro) com UVs próprias:
   fora = metade de cima do atlas, dentro = metade de baixo.
3) Material: papel jornal (amarelado, manchas de umidade, amassados, sujeira) x tinta da impressão,
   assado em PBR 2048 pelo realistic_kit.
4) Shape keys para o vento: Curl (bordas enrolam), Flap (meia folha levanta), Wave (ondula).
Uso: blender --background --python build_newspaper.py -- <saida.fbx> <pasta_texturas>
"""
import bpy, bmesh, sys, math, os, random
sys.path.append(os.path.dirname(os.path.abspath(__file__)))
import realistic_kit as K

argv = sys.argv[sys.argv.index("--") + 1:]
OUT_FBX, TEX_DIR = argv[0], argv[1]
os.makedirs(TEX_DIR, exist_ok=True)
random.seed(413)

SW, SH = 0.64, 0.56          # folha aberta (duas páginas de 0,32 x 0,56)
PX_PER_M = 2000

# ============================================================== 1) DIAGRAMAÇÃO
LOREM = ("A prefeitura informou nesta quarta que a iluminação da Rua do Porto será revista após "
         "novas queixas de moradores. Segundo a vizinhança, os postes falham durante a madrugada e "
         "o movimento de pedestres diminuiu desde o mês passado. Comerciantes relatam prejuízo e "
         "pedem mais rondas na região do cais. A empresa de entregas não comentou o caso até o "
         "fechamento desta edição. Testemunhas dizem ter visto um homem parado sob a marquise do "
         "antigo armazém, sempre no mesmo horário. A polícia diz que acompanha a situação. ")

def em_mat(name, color):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    nt.nodes.clear()
    o = nt.nodes.new('ShaderNodeOutputMaterial')
    e = nt.nodes.new('ShaderNodeEmission')
    e.inputs['Color'].default_value = (*color, 1.0)
    nt.links.new(e.outputs[0], o.inputs['Surface'])
    return m

def photo_mat(name, seed):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    nt.nodes.clear()
    o = nt.nodes.new('ShaderNodeOutputMaterial')
    e = nt.nodes.new('ShaderNodeEmission')
    n = nt.nodes.new('ShaderNodeTexNoise')
    n.inputs['Scale'].default_value = 3.0 + seed
    n.inputs['Detail'].default_value = 12.0
    r = nt.nodes.new('ShaderNodeValToRGB')
    r.color_ramp.elements[0].position = 0.35
    r.color_ramp.elements[0].color = (0.05, 0.05, 0.05, 1)
    r.color_ramp.elements[1].position = 0.75
    r.color_ramp.elements[1].color = (0.75, 0.75, 0.75, 1)
    nt.links.new(n.outputs['Fac'], r.inputs['Fac'])
    nt.links.new(r.outputs['Color'], e.inputs['Color'])
    nt.links.new(e.outputs[0], o.inputs['Surface'])
    return m

INK = None
PAPER = None

def rect(x0, x1, y0, y1, mat, z=0.0002):
    bpy.ops.mesh.primitive_plane_add(size=1, location=((x0 + x1) / 2, (y0 + y1) / 2, z))
    ob = bpy.context.active_object
    ob.scale = (x1 - x0, y1 - y0, 1)
    ob.data.materials.append(mat)
    return ob

def txt(s, x, y, size, width=0.0, align='LEFT', z=0.0003):
    bpy.ops.object.text_add(location=(x, y, z))
    ob = bpy.context.active_object
    ob.data.body = s
    ob.data.size = size
    ob.data.align_x = align
    ob.data.align_y = 'TOP'
    if width > 0:
        ob.data.text_boxes[0].width = width
        ob.data.space_line = 1.05
    ob.data.materials.append(INK)
    return ob

def columns(x0, y_top, y_bot, ncols, col_w, gap, size=0.0040):
    for c in range(ncols):
        x = x0 + c * (col_w + gap)
        body = (LOREM * 6)[random.randint(0, 200):]
        ob = txt(body, x, y_top, size, width=col_w)
        ob.data.text_boxes[0].height = y_top - y_bot
        ob.data.overflow = 'TRUNCATE'

def render_spread(path, build):
    for o in list(bpy.data.objects):
        bpy.data.objects.remove(o, do_unlink=True)
    rect(-SW / 2, SW / 2, -SH / 2, SH / 2, PAPER, z=0.0)
    build()
    cam_data = bpy.data.cameras.new("cam")
    cam_data.type = 'ORTHO'
    cam_data.ortho_scale = SW
    cam = bpy.data.objects.new("cam", cam_data)
    bpy.context.collection.objects.link(cam)
    cam.location = (0, 0, 1)
    sc = bpy.context.scene
    sc.camera = cam
    sc.render.resolution_x = int(SW * PX_PER_M)
    sc.render.resolution_y = int(SH * PX_PER_M)
    sc.render.filepath = path
    bpy.ops.render.render(write_still=True)

def outer():
    # Página direita = CAPA
    x0 = 0.012
    txt("DIÁRIO DO PORTO", 0.16, 0.268, 0.034, align='CENTER')
    rect(x0, 0.308, 0.222, 0.2235, INK)
    txt("QUINTA-FEIRA, 14 DE NOVEMBRO DE 1996   ·   ANO LXII   ·   Nº 21.413   ·   R$ 1,00", 0.16, 0.219, 0.0052, align='CENTER')
    rect(x0, 0.308, 0.2105, 0.2115, INK)
    txt("ENTREGADOR SOME NA\nZONA DO PORTO", x0, 0.204, 0.024, width=0.296)
    txt("Moto foi achada ligada na Rua do Porto; pedido nº 0413 não chegou ao destino", x0, 0.143, 0.0075, width=0.296)
    rect(x0, 0.19, 0.012, 0.125, photo_mat("ph1", 1))
    txt("A moto do entregador, encontrada às 23h de terça.", x0, 0.007, 0.0042, width=0.18)
    columns(0.198, 0.125, -0.26, 1, 0.11, 0.0)
    columns(x0, -0.008, -0.26, 2, 0.087, 0.006)
    # Página esquerda = CLASSIFICADOS (contracapa)
    txt("CLASSIFICADOS", -0.16, 0.262, 0.022, align='CENTER')
    rect(-0.308, -0.012, 0.236, 0.2375, INK)
    rect(-0.300, -0.170, 0.225, 0.110, INK)
    rect(-0.297, -0.173, 0.222, 0.113, PAPER, z=0.00025)
    txt("LANCHES\n24H", -0.235, 0.214, 0.026, align='CENTER')
    txt("RUA DO PORTO, 413  ·  ENTREGAMOS", -0.235, 0.126, 0.0048, align='CENTER')
    ads = ["ALUGA-SE QUARTO perto do cais, mobiliado, tratar c/ D. Rosa", "VENDE-SE moto 125 cc, revisada, aceito troca",
           "PRECISA-SE de entregador noturno, ter moto própria", "DESAPARECIDO: gato cinza, atende por Fumaça. Gratifica",
           "CONSERTO de rádio e TV, orçamento sem compromisso", "ALUGA-SE box no Mercado do Porto",
           "VENDE-SE geladeira usada, 2 portas", "AULAS de violão, fins de semana"]
    y = 0.105
    for i, a in enumerate(ads * 3):
        col = i % 2
        x = -0.300 + col * 0.150
        txt(a, x, y, 0.0046, width=0.138)
        rect(x, x + 0.138, y - 0.034, y - 0.0335, INK)
        if col == 1:
            y -= 0.040
        if y < -0.25:
            break
    rect(-0.155, -0.012, 0.225, 0.110, photo_mat("ph2", 3))

def inner():
    txt("CIDADE", -0.30, 0.262, 0.016)
    rect(-0.308, -0.012, 0.240, 0.2412, INK)
    txt("MORADORES RELATAM\nVULTOS NA RUA DO PORTO", -0.300, 0.232, 0.017, width=0.29)
    rect(-0.300, -0.130, 0.170, 0.060, photo_mat("ph3", 5))
    columns(-0.122, 0.170, -0.26, 1, 0.11, 0.0)
    columns(-0.300, 0.050, -0.26, 2, 0.083, 0.008)
    txt("POLÍCIA", 0.012, 0.262, 0.016)
    rect(0.012, 0.308, 0.240, 0.2412, INK)
    txt("CASO DO PEDIDO 0413\nSEGUE SEM PISTAS", 0.012, 0.232, 0.017, width=0.29)
    columns(0.012, 0.170, -0.26, 3, 0.090, 0.008)

sc = bpy.context.scene
bpy.ops.wm.read_factory_settings(use_empty=True)
sc = bpy.context.scene
sc.render.engine = 'CYCLES'
sc.cycles.samples = 4
sc.view_settings.view_transform = 'Standard'
INK = em_mat("ink", (0.035, 0.035, 0.04))
PAPER = em_mat("paper", (1.0, 1.0, 1.0))
outer_png = os.path.join(TEX_DIR, "_layout_outer.png")
inner_png = os.path.join(TEX_DIR, "_layout_inner.png")
render_spread(outer_png, outer)
render_spread(inner_png, inner)

# ============================================================== 2) FOLHA
K.reset_scene()
bpy.context.scene.view_settings.view_transform = 'Standard'
NX, NY = 24, 20

def grid(name, z, flip):
    me = bpy.data.meshes.new(name)
    bm = bmesh.new()
    uv = bm.loops.layers.uv.new("UVMap")
    verts = [[None] * (NY + 1) for _ in range(NX + 1)]
    for i in range(NX + 1):
        for j in range(NY + 1):
            x = -SW / 2 + SW * i / NX
            y = -SH / 2 + SH * j / NY
            crease = -0.004 * max(0.0, 1.0 - abs(x) / 0.02)   # vinco da dobra no meio
            verts[i][j] = bm.verts.new((x, y, z + crease))
    for i in range(NX):
        for j in range(NY):
            q = [verts[i][j], verts[i + 1][j], verts[i + 1][j + 1], verts[i][j + 1]]
            if flip: q.reverse()
            f = bm.faces.new(q)
            for loop in f.loops:
                vx, vy = loop.vert.co.x, loop.vert.co.y
                u = (vx + SW / 2) / SW
                v = (vy + SH / 2) / SH
                if not flip:
                    loop[uv].uv = (u, 0.5 + 0.5 * v)          # fora: metade de cima
                else:
                    loop[uv].uv = (1.0 - u, 0.5 * v)          # dentro: metade de baixo (espelhado)
    bm.to_mesh(me)
    bm.free()
    ob = bpy.data.objects.new(name, me)
    bpy.context.collection.objects.link(ob)
    return ob

top = grid("Out", 0.0002, False)
bot = grid("In", -0.0002, True)
sheet = K.join([top, bot], "Newspaper")
bpy.ops.object.select_all(action='DESELECT')
sheet.select_set(True)
bpy.context.view_layer.objects.active = sheet
bpy.ops.object.mode_set(mode='EDIT')
bpy.ops.mesh.select_all(action='SELECT')
bpy.ops.object.mode_set(mode='OBJECT')

# ============================================================== 3) MATERIAL (papel x impressão)
img_out = bpy.data.images.load(outer_png)
img_in = bpy.data.images.load(inner_png)
m = bpy.data.materials.new("NewsPaper")
m.use_nodes = True
nt = m.node_tree
nt.nodes.clear()
out = K._n(nt, 'ShaderNodeOutputMaterial')
bsdf = K._n(nt, 'ShaderNodeBsdfPrincipled')
nt.links.new(bsdf.outputs[0], out.inputs['Surface'])
uvn = K._n(nt, 'ShaderNodeUVMap')
sep = K._n(nt, 'ShaderNodeSeparateXYZ')
nt.links.new(uvn.outputs['UV'], sep.inputs[0])
# UV da página: fora (v 0.5..1) e dentro (v 0..0.5, u espelhado) -> 0..1 de cada imagem
def page_uv(is_outer):
    comb = K._n(nt, 'ShaderNodeCombineXYZ')
    if is_outer:
        nt.links.new(sep.outputs['X'], comb.inputs['X'])
        nt.links.new(K._math(nt, 'MULTIPLY', K._math(nt, 'SUBTRACT', sep.outputs['Y'], 0.5), 2.0), comb.inputs['Y'])
    else:
        nt.links.new(K._math(nt, 'SUBTRACT', 1.0, sep.outputs['X']), comb.inputs['X'])
        nt.links.new(K._math(nt, 'MULTIPLY', sep.outputs['Y'], 2.0), comb.inputs['Y'])
    return comb.outputs[0]
t_out = K._n(nt, 'ShaderNodeTexImage'); t_out.image = img_out; t_out.extension = 'CLIP'
t_in = K._n(nt, 'ShaderNodeTexImage'); t_in.image = img_in; t_in.extension = 'CLIP'
nt.links.new(page_uv(True), t_out.inputs['Vector'])
nt.links.new(page_uv(False), t_in.inputs['Vector'])
is_out = K._math(nt, 'GREATER_THAN', sep.outputs['Y'], 0.5)
printed = K._mix_color(nt, is_out, t_in.outputs['Color'], t_out.outputs['Color'])
# 'ink' = quanto da impressão é tinta (1 - luminância)
bw = K._n(nt, 'ShaderNodeRGBToBW')
nt.links.new(printed, bw.inputs[0])
ink = K._math(nt, 'SUBTRACT', 1.0, bw.outputs[0])
# Papel: amarelado irregular + bordas mais amareladas
obj = K._n(nt, 'ShaderNodeTexCoord').outputs['Object']
yel = K._noise(nt, 4.0, 6.0, obj)
paper = K._mix_color(nt, K._math(nt, 'MULTIPLY', yel.outputs['Fac'], 0.8), (0.80, 0.78, 0.70), (0.70, 0.64, 0.50))
# Manchas de umidade (anéis) e sujeira de rua
stain = K._noise(nt, 2.2, 3.0, obj)
st = K._math(nt, 'GREATER_THAN', stain.outputs['Fac'], 0.62)
paper = K._mix_color(nt, K._math(nt, 'MULTIPLY', st, 0.35), paper, (0.55, 0.47, 0.33))
dirt = K._noise(nt, 18.0, 10.0, obj)
dm = K._math(nt, 'MULTIPLY', K._math(nt, 'SUBTRACT', dirt.outputs['Fac'], 0.52), 3.0, clamp=True)
paper = K._mix_color(nt, K._math(nt, 'MULTIPLY', dm, 0.5), paper, (0.30, 0.27, 0.22))
color = K._mix_color(nt, K._math(nt, 'MULTIPLY', ink, 0.92), paper, (0.06, 0.06, 0.065))
nt.links.new(color, bsdf.inputs['Base Color'])
nt.links.new(K._math(nt, 'ADD', 0.78, K._math(nt, 'MULTIPLY', st, 0.1)), bsdf.inputs['Roughness'])
bsdf.inputs['Metallic'].default_value = 0.0
# Amassados: arestas de Voronoi viram vincos no relevo
vor = K._n(nt, 'ShaderNodeTexVoronoi')
vor.feature = 'DISTANCE_TO_EDGE'
vor.inputs['Scale'].default_value = 9.0
nt.links.new(obj, vor.inputs['Vector'])
crease = K._math(nt, 'SUBTRACT', 1.0, K._math(nt, 'MULTIPLY', vor.outputs['Distance'], 14.0, clamp=True))
fine = K._noise(nt, 90.0, 8.0, obj)
height = K._math(nt, 'ADD', K._math(nt, 'MULTIPLY', crease, 0.6), K._math(nt, 'MULTIPLY', fine.outputs['Fac'], 0.25))
bump = K._n(nt, 'ShaderNodeBump')
bump.inputs['Strength'].default_value = 0.35
bump.inputs['Distance'].default_value = 0.002
nt.links.new(height, bump.inputs['Height'])
nt.links.new(bump.outputs['Normal'], bsdf.inputs['Normal'])
sheet.data.materials.clear()
sheet.data.materials.append(m)

K.bake_all([sheet], TEX_DIR, 2048)
K.collapse_materials([sheet])

# ============================================================== 4) SHAPE KEYS (vento)
sheet.shape_key_add(name="Basis")
def key(name, fn):
    k = sheet.shape_key_add(name=name)
    for i, v in enumerate(sheet.data.vertices):
        x, y, z = v.co
        k.data[i].co = fn(x, y, z)
key("Curl", lambda x, y, z: (x * 0.96, y * 0.97, z + 0.07 * (abs(x) / (SW / 2)) ** 2 + 0.03 * (abs(y) / (SH / 2)) ** 2))
def flap(x, y, z):
    if x <= 0: return (x, y, z)
    a = math.radians(70)
    return (x * math.cos(a) - z * math.sin(a), y, x * math.sin(a) + z * math.cos(a))
key("Flap", flap)
key("Wave", lambda x, y, z: (x, y, z + 0.025 * math.sin(x * 22.0) * (0.5 + abs(y) / SH)))

os.remove(outer_png); os.remove(inner_png)
K.export_fbx(OUT_FBX)
print("RESULT newspaper tris=%d verts=%d" % (sum(len(p.vertices) - 2 for p in sheet.data.polygons), len(sheet.data.vertices)))
print("OK", OUT_FBX)
