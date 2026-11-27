# The Delivery - teaser PSX (Blender 5.x, rodar em background)
# Uso: blender -b --factory-startup --python build_scene.py -- <raw_frames_dir> <blend_out> [render=1]
# Plano A (1-75): POV da Clear indo até a porta; sombras de pés sob a porta.
# Plano B (76-165): olho mágico; entregador se aproxima a cada apagão.
# Plano C (166-210): cartela "Eu sei onde você mora agora." / THE DELIVERY.
import bpy, bmesh, sys, math, random
import numpy as np

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
RAW_DIR = argv[0] if len(argv) > 0 else "//raw"
BLEND_OUT = argv[1] if len(argv) > 1 else None
DO_RENDER = (argv[2] != "0") if len(argv) > 2 else True

random.seed(7)
rng = np.random.default_rng(7)

# ---------------------------------------------------------------- limpeza
for o in list(bpy.data.objects):
    bpy.data.objects.remove(o, do_unlink=True)
scene = bpy.context.scene
scene.frame_start, scene.frame_end = 1, 210
scene.render.fps = 15

# ---------------------------------------------------------------- utilitários de animação
def fcurves_of(idb):
    ad = idb.animation_data
    act = ad.action
    try:
        return list(act.fcurves)
    except AttributeError:
        from bpy_extras import anim_utils
        cb = anim_utils.action_get_channelbag_for_slot(act, ad.action_slot)
        return list(cb.fcurves)

def set_constant(idb):
    for fc in fcurves_of(idb):
        for kp in fc.keyframe_points:
            kp.interpolation = 'CONSTANT'

def add_noise(idb, data_path, strength, scale, phase=0.0):
    for fc in fcurves_of(idb):
        if fc.data_path == data_path:
            m = fc.modifiers.new('NOISE')
            m.strength = strength
            m.scale = scale
            m.phase = phase + fc.array_index * 13.7

def link(obj):
    scene.collection.objects.link(obj)
    return obj

# ---------------------------------------------------------------- texturas pixeladas (numpy)
def make_image(name, arr):
    h, w = arr.shape[:2]
    img = bpy.data.images.new(name, w, h, alpha=False)
    rgba = np.ones((h, w, 4), dtype=np.float32)
    rgba[..., :3] = np.clip(arr, 0, 1)
    img.pixels.foreach_set(rgba.ravel())
    img.pack()
    return img

def noise(h, w, amp):
    return (rng.random((h, w, 1)) - 0.5) * amp

def tex_wallpaper():
    h = w = 32
    a = np.zeros((h, w, 3)) + np.array([0.42, 0.40, 0.30])
    a[:, ::4] *= 0.85                      # listras verticais
    a += noise(h, w, 0.06)
    for _ in range(4):                     # manchas de umidade
        cy, cx = rng.integers(0, h), rng.integers(0, w)
        yy, xx = np.ogrid[:h, :w]
        m = ((yy - cy) ** 2 + (xx - cx) ** 2) < rng.integers(4, 30)
        a[m] *= 0.75
    return a

def tex_wood_floor():
    h = w = 32
    a = np.zeros((h, w, 3)) + np.array([0.30, 0.18, 0.10])
    for r in range(0, h, 8):
        a[r] *= 0.5
        a[r:r + 8] *= 0.85 + rng.random() * 0.3
    a += noise(h, w, 0.05)
    return a

def tex_door():
    h, w = 64, 32
    a = np.zeros((h, w, 3)) + np.array([0.28, 0.15, 0.08])
    a += noise(h, w, 0.04)
    for (y0, y1) in [(6, 28), (36, 58)]:   # almofadas da porta
        a[y0:y1, 5:27] *= 0.8
        a[y0, 5:27] *= 0.6
        a[y1, 5:27] *= 1.3
    return a

def tex_corridor_floor():
    h = w = 32
    yy, xx = np.mgrid[:h, :w]
    chk = ((yy // 8 + xx // 8) % 2)[..., None]
    a = np.where(chk, np.array([0.45, 0.42, 0.33]), np.array([0.22, 0.16, 0.12]))
    a = a + noise(h, w, 0.08)
    return a

def tex_corridor_wall():
    h = w = 32
    a = np.zeros((h, w, 3)) + np.array([0.55, 0.52, 0.38])
    a[:12] = np.array([0.25, 0.30, 0.22])  # rodapé/lambri verde sujo (metade de baixo)
    a[12] *= 0.5
    a += noise(h, w, 0.07)
    return a

def tex_face():
    # rosto 32x32 (linha 0 = queixo): pele pálida e cansada, olheiras, órbitas fundas,
    # sorriso fino e largo demais. Projetado de frente sobre a cabeça arredondada.
    n = 32
    yy, xx = np.mgrid[:n, :n].astype(np.float32)
    a = np.zeros((n, n, 3)) + np.array([0.78, 0.66, 0.58])
    side = np.abs(xx - 15.5) / 15.5
    a *= (1 - 0.35 * side ** 2)[..., None]                       # volume nas laterais
    a += noise(n, n, 0.035)
    a[26:] *= 0.45                                               # sombra da aba do boné
    a[23:26] *= 0.8
    for cx in (10.5, 21.5):
        d = ((xx - cx) / 3.6) ** 2 + ((yy - 18) / 2.3) ** 2
        a *= np.where(d < 1, 0.25 + 0.6 * d, 1.0)[..., None]      # órbitas fundas com gradiente
        a[18, int(cx) - 1:int(cx) + 2] = np.array([0.75, 0.72, 0.6])  # branco do olho
        a[18, int(cx)] = np.array([0.02, 0.0, 0.0])              # pupila minúscula
        a[15:17, int(cx) - 3:int(cx) + 4] *= 0.78                # olheiras
        a[21, int(cx) - 3:int(cx) + 3] *= 0.6                    # sobrancelha caída
    a[12:17, 15:17] *= 0.82                                      # sombra do nariz
    a[13, 14] = a[13, 17] = np.array([0.2, 0.1, 0.08])          # narinas
    lip = np.array([0.22, 0.05, 0.05])
    for x in range(6, 26):                                       # sorriso em parábola, largo demais
        y = int(round(8.5 + 0.035 * (x - 15.5) ** 2))
        a[y, x] = lip
        if 9 <= x <= 22 and x % 2 == 0:
            a[y + 1, x] = np.array([0.82, 0.78, 0.62])          # dentes
    a[3:7] *= 0.88                                               # barba por fazer
    a[3:7] += noise(4, n, 0.06)
    return a

# ---------------------------------------------------------------- materiais com fog PSX
FOG = (0.004, 0.006, 0.005, 1)

def add_fog(nt, shader_out, near=5.0, far=17.0):
    n = nt.nodes
    cam = n.new('ShaderNodeCameraData')
    mr = n.new('ShaderNodeMapRange')
    mr.clamp = True
    mr.inputs['From Min'].default_value = near
    mr.inputs['From Max'].default_value = far
    nt.links.new(cam.outputs[1], mr.inputs['Value'])
    fog = n.new('ShaderNodeEmission')
    fog.inputs['Color'].default_value = FOG
    fog.inputs['Strength'].default_value = 1.0
    mix = n.new('ShaderNodeMixShader')
    nt.links.new(mr.outputs[0], mix.inputs[0])
    nt.links.new(shader_out, mix.inputs[1])
    nt.links.new(fog.outputs[0], mix.inputs[2])
    return mix.outputs[0]

def material(name, color=(0.5, 0.5, 0.5), image=None, emission=None, emit_strength=0.0, fog=True):
    m = bpy.data.materials.new(name)
    try:
        m.use_nodes = True
    except Exception:
        pass
    nt = m.node_tree
    for nd in list(nt.nodes):
        nt.nodes.remove(nd)
    out = nt.nodes.new('ShaderNodeOutputMaterial')
    bsdf = nt.nodes.new('ShaderNodeBsdfPrincipled')
    bsdf.inputs['Roughness'].default_value = 1.0
    try:
        bsdf.inputs['Specular IOR Level'].default_value = 0.0
    except KeyError:
        pass
    bsdf.inputs['Base Color'].default_value = (*color, 1)
    if image is not None:
        tx = nt.nodes.new('ShaderNodeTexImage')
        tx.image = image
        tx.interpolation = 'Closest'       # sem filtragem = cara de PS1
        nt.links.new(tx.outputs['Color'], bsdf.inputs['Base Color'])
    if emission is not None:
        bsdf.inputs['Emission Color'].default_value = (*emission, 1)
        bsdf.inputs['Emission Strength'].default_value = emit_strength
    sh = bsdf.outputs[0]
    if fog:
        sh = add_fog(nt, sh)
    nt.links.new(sh, out.inputs['Surface'])
    m["bsdf"] = bsdf.name
    return m

def emit_material(name, color, strength, fog=False):
    m = bpy.data.materials.new(name)
    try:
        m.use_nodes = True
    except Exception:
        pass
    nt = m.node_tree
    for nd in list(nt.nodes):
        nt.nodes.remove(nd)
    out = nt.nodes.new('ShaderNodeOutputMaterial')
    em = nt.nodes.new('ShaderNodeEmission')
    em.inputs['Color'].default_value = (*color, 1)
    em.inputs['Strength'].default_value = strength
    sh = em.outputs[0]
    if fog:
        sh = add_fog(nt, sh)
    nt.links.new(sh, out.inputs['Surface'])
    m["em"] = em.name
    return m

# ---------------------------------------------------------------- geometria
FACES = [  # (índices, eixo-normal) ; ordem: -X +X -Y +Y -Z +Z
    ((0, 3, 7, 4), 0), ((1, 5, 6, 2), 0),
    ((0, 4, 5, 1), 1), ((3, 2, 6, 7), 1),
    ((0, 1, 2, 3), 2), ((4, 7, 6, 5), 2),
]

def box(name, center, size, mats, tile=1.0, special=None, parent=None):
    """Caixa com UV projetada em escala de mundo. special = {face_idx: mat_idx} usa UV 0..1 inteira."""
    sx, sy, sz = (s / 2 for s in size)
    v = [(-sx, -sy, -sz), (sx, -sy, -sz), (sx, sy, -sz), (-sx, sy, -sz),
         (-sx, -sy, sz), (sx, -sy, sz), (sx, sy, sz), (-sx, sy, sz)]
    faces = [f for f, _ in FACES]
    me = bpy.data.meshes.new(name)
    me.from_pydata(v, [], faces)
    me.update()
    # garante normais para fora
    bm = bmesh.new()
    bm.from_mesh(me)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(me)
    bm.free()
    uv = me.uv_layers.new(name="UV")
    for poly in me.polygons:
        ax = max(range(3), key=lambda i: abs(poly.normal[i]))
        fi = None
        n = poly.normal
        fi = [0, 1][n[0] > 0] if ax == 0 else ([2, 3][n[1] > 0] if ax == 1 else [4, 5][n[2] > 0])
        if special and fi in special:
            poly.material_index = special[fi]
            corners = [me.vertices[me.loops[li].vertex_index].co for li in poly.loop_indices]
            others = [i for i in range(3) if i != ax]
            for li, co in zip(poly.loop_indices, corners):
                u = (co[others[0]] + size[others[0]] / 2) / size[others[0]]
                w = (co[others[1]] + size[others[1]] / 2) / size[others[1]]
                uv.data[li].uv = (u, w)
        else:
            others = [i for i in range(3) if i != ax]
            for li in poly.loop_indices:
                co = me.vertices[me.loops[li].vertex_index].co
                p = [co[i] + center[i] for i in range(3)]
                uv.data[li].uv = (p[others[0]] / tile, p[others[1]] / tile)
    for m in mats:
        me.materials.append(m)
    ob = bpy.data.objects.new(name, me)
    ob.location = center
    link(ob)
    if parent:
        ob.parent = parent
        ob.location = center
    return ob

def cyl(name, center, r, h, mat, verts=8, parent=None):
    me = bpy.data.meshes.new(name)
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=True, segments=verts, radius1=r, radius2=r, depth=h)
    bm.to_mesh(me)
    bm.free()
    me.materials.append(mat)
    ob = bpy.data.objects.new(name, me)
    link(ob)
    ob.location = center
    if parent:
        ob.parent = parent
        ob.location = center
    return ob

def empty(name, loc=(0, 0, 0)):
    e = bpy.data.objects.new(name, None)
    e.location = loc
    return link(e)

# ---------------------------------------------------------------- helpers orgânicos (ainda low poly / PS1)
def smooth(ob):
    # Gouraud: o PS1 já sombreava suave por vértice
    for p in ob.data.polygons:
        p.use_smooth = True
    return ob

def bevel(ob, width, segs=2, smooth_shade=False):
    m = ob.modifiers.new("Bevel", 'BEVEL')
    m.width = width
    m.segments = segs
    m.limit_method = 'NONE'
    if smooth_shade:
        smooth(ob)
    return ob

def jitter(ob, amt, only=None):
    # imprecisão "feita à mão" nos vértices
    for v in ob.data.vertices:
        if only is None or only(v.co):
            v.co.x += (random.random() - 0.5) * amt
            v.co.y += (random.random() - 0.5) * amt
            v.co.z += (random.random() - 0.5) * amt
    return ob

def _finish(name, me, mat, loc, parent, rot=None):
    if isinstance(mat, (list, tuple)):
        for m in mat:
            me.materials.append(m)
    else:
        me.materials.append(mat)
    ob = bpy.data.objects.new(name, me)
    link(ob)
    if parent:
        ob.parent = parent
    ob.location = loc
    if rot:
        ob.rotation_euler = rot
    return ob

def blob(name, loc, scale, mat, subdiv=2, jit=0.0, parent=None, rot=None, flat=False):
    me = bpy.data.meshes.new(name)
    bm = bmesh.new()
    bmesh.ops.create_icosphere(bm, subdivisions=subdiv, radius=1.0)
    for v in bm.verts:
        v.co.x *= scale[0]
        v.co.y *= scale[1]
        v.co.z *= scale[2]
        if jit:
            v.co.x += (random.random() - 0.5) * jit
            v.co.y += (random.random() - 0.5) * jit
            v.co.z += (random.random() - 0.5) * jit
    bm.to_mesh(me)
    bm.free()
    ob = _finish(name, me, mat, loc, parent, rot)
    return ob if flat else smooth(ob)

def lathe(name, loc, profile, mat, segs=10, parent=None, rot=None):
    """Sólido de revolução a partir de [(raio, z), ...] (de baixo para cima)."""
    verts, faces, rings = [], [], []
    for r, z in profile:
        if r <= 1e-5:
            rings.append([len(verts)])
            verts.append((0, 0, z))
        else:
            ring = []
            for s in range(segs):
                a = 2 * math.pi * s / segs
                ring.append(len(verts))
                verts.append((math.cos(a) * r, math.sin(a) * r, z))
            rings.append(ring)
    for a, b in zip(rings[:-1], rings[1:]):
        if len(a) == 1 and len(b) == 1:
            continue
        if len(a) == 1:
            for s in range(segs):
                faces.append((a[0], b[s], b[(s + 1) % segs]))
        elif len(b) == 1:
            for s in range(segs):
                faces.append((a[s], a[(s + 1) % segs], b[0]))
        else:
            for s in range(segs):
                faces.append((a[s], a[(s + 1) % segs], b[(s + 1) % segs], b[s]))
    me = bpy.data.meshes.new(name)
    me.from_pydata(verts, [], faces)
    me.update()
    bm = bmesh.new()
    bm.from_mesh(me)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(me)
    bm.free()
    return smooth(_finish(name, me, mat, loc, parent, rot))

def skin(name, verts, edges, radii, mat, loc=(0, 0, 0), parent=None, subdiv=1):
    """Corpo orgânico: esqueleto de vértices + Skin modifier + 1 nível de subdivisão."""
    me = bpy.data.meshes.new(name)
    me.from_pydata(verts, edges, [])
    me.update()
    ob = _finish(name, me, mat, loc, parent)
    mod = ob.modifiers.new("Skin", 'SKIN')
    mod.use_smooth_shade = True
    if len(me.skin_vertices) == 0:
        me.skin_vertices.new()
    sv = me.skin_vertices[0].data
    for i, r in enumerate(radii):
        sv[i].radius = r
        sv[i].use_root = (i == 0)
    sub = ob.modifiers.new("Subsurf", 'SUBSURF')
    sub.levels = subdiv
    sub.render_levels = subdiv
    return ob

def wavy_curtain(name, loc, width, height, folds, depth, mat):
    nx, nz = folds * 2 + 1, 6
    verts, faces = [], []
    for k in range(nz):
        z = height * k / (nz - 1)
        amp = depth * (0.6 + 0.4 * (1 - k / (nz - 1)))   # pano abre um pouco embaixo
        for i in range(nx):
            y = width * i / (nx - 1) - width / 2
            x = math.sin(i / (nx - 1) * folds * 2 * math.pi) * amp
            verts.append((x, y, z))
    for k in range(nz - 1):
        for i in range(nx - 1):
            a = k * nx + i
            faces.append((a, a + 1, a + nx + 1, a + nx))
    me = bpy.data.meshes.new(name)
    me.from_pydata(verts, [], faces)
    me.update()
    ob = smooth(_finish(name, me, mat, loc, None))
    jitter(ob, 0.015)
    sol = ob.modifiers.new("Solid", 'SOLIDIFY')
    sol.thickness = 0.01
    return ob

# ---------------------------------------------------------------- materiais
M_WALL = material("Wallpaper", image=make_image("Wallpaper", tex_wallpaper()))
M_FLOOR = material("WoodFloor", image=make_image("WoodFloor", tex_wood_floor()))
M_DOOR = material("DoorWood", image=make_image("DoorWood", tex_door()))
M_CFLOOR = material("CorridorFloor", image=make_image("CorridorFloor", tex_corridor_floor()))
M_CWALL = material("CorridorWall", image=make_image("CorridorWall", tex_corridor_wall()))
M_CEIL = material("Ceiling", color=(0.35, 0.34, 0.30))
M_DARK = material("Dark", color=(0.03, 0.03, 0.03))
M_METAL = material("Brass", color=(0.55, 0.45, 0.2))
M_SOFA = material("Sofa", color=(0.18, 0.22, 0.30))
M_COUNTER = material("Counter", color=(0.45, 0.43, 0.40))
M_FRIDGE = material("Fridge", color=(0.70, 0.70, 0.66))
M_BAG = material("PaperBag", color=(0.55, 0.40, 0.22))
M_RUG = material("Rug", color=(0.35, 0.08, 0.07))
M_SKIN = material("Skin", color=(0.80, 0.70, 0.62))
M_FACE = material("Face", image=make_image("Face", tex_face()))
M_PANTS = material("Pants", color=(0.07, 0.08, 0.12))
M_JACKET = material("Jacket", color=(0.35, 0.05, 0.05))
M_PACK = material("DeliveryPack", color=(0.75, 0.55, 0.08))
M_SHOE = material("Shoe", color=(0.02, 0.02, 0.02))
M_SHADOW = emit_material("FeetShadow", (0, 0, 0), 0.0)
M_STRIP = emit_material("DoorGapLight", (1.0, 0.95, 0.7), 1.6)
M_MOON = emit_material("MoonWindow", (0.35, 0.45, 0.9), 1.5)
M_TUBE = emit_material("Tube", (0.85, 1.0, 0.85), 6.0)
M_EXIT = emit_material("ExitSign", (0.1, 1.0, 0.2), 3.0)
M_TEXT_W = emit_material("TextWhite", (0.9, 0.9, 0.85), 1.0)
M_TEXT_R = emit_material("TextRed", (0.8, 0.02, 0.02), 1.5)

# TV com estática animada (noise 4D)
M_TV = bpy.data.materials.new("TVStatic")
try:
    M_TV.use_nodes = True
except Exception:
    pass
nt = M_TV.node_tree
for nd in list(nt.nodes):
    nt.nodes.remove(nd)
tv_out = nt.nodes.new('ShaderNodeOutputMaterial')
tv_noise = nt.nodes.new('ShaderNodeTexNoise')
tv_noise.noise_dimensions = '4D'
tv_noise.inputs['Scale'].default_value = 60.0
tv_em = nt.nodes.new('ShaderNodeEmission')
tv_em.inputs['Strength'].default_value = 2.5
nt.links.new(tv_noise.outputs['Fac'], tv_em.inputs['Color'])
nt.links.new(tv_em.outputs[0], tv_out.inputs['Surface'])
for f in range(1, 211):
    tv_noise.inputs['W'].default_value = f * 1.7
    tv_noise.inputs['W'].keyframe_insert('default_value', frame=f)

# ---------------------------------------------------------------- APARTAMENTO (origem = centro do piso, 5x6m, pé-direito 2.6)
H = 2.6
box("Floor", (0, 0, -0.05), (5, 6, 0.1), [M_FLOOR])
box("Ceiling", (0, 0, H + 0.05), (5, 6, 0.1), [M_CEIL])
box("Wall_North", (0, 3.075, H / 2), (5.3, 0.15, H), [M_WALL])
box("Wall_East", (2.575, 0, H / 2), (0.15, 6, H), [M_WALL])
# parede oeste com janela (vão 1.2 x 1.0)
box("Wall_West_A", (-2.575, -1.65, H / 2), (0.15, 2.7, H), [M_WALL])
box("Wall_West_B", (-2.575, 2.1, H / 2), (0.15, 1.8, H), [M_WALL])
box("Wall_West_Low", (-2.575, 0.6, 0.5), (0.15, 1.2, 1.0), [M_WALL])
box("Wall_West_High", (-2.575, 0.6, 2.3), (0.15, 1.2, 0.6), [M_WALL])
box("MoonWindow", (-2.62, 0.6, 1.5), (0.02, 1.2, 1.0), [M_MOON])
# parede sul com porta (vão 0.9 x 2.1)
box("Wall_South_L", (-1.475, -3.075, H / 2), (2.05, 0.15, H), [M_WALL])
box("Wall_South_R", (1.475, -3.075, H / 2), (2.05, 0.15, H), [M_WALL])
box("Wall_South_Top", (0, -3.075, 2.35), (0.9, 0.15, 0.5), [M_WALL])
door = box("FrontDoor", (0, -3.075, 1.06), (0.88, 0.05, 2.08), [M_DOOR], tile=1.0)
cyl("DoorKnob", (0.33, -3.03, 1.0), 0.03, 0.06, M_METAL).rotation_euler = (math.pi / 2, 0, 0)
cyl("Peephole", (0, -3.05, 1.6), 0.012, 0.03, M_METAL).rotation_euler = (math.pi / 2, 0, 0)
box("DoorChain", (0.25, -3.045, 1.45), (0.18, 0.01, 0.015), [M_METAL])
# luz do corredor vazando por baixo da porta
box("DoorGapStrip", (0, -2.97, 0.002), (0.86, 0.09, 0.002), [M_STRIP])
box("DoorGapSpill", (0, -2.80, 0.001), (0.86, 0.25, 0.001), [emit_material("Spill", (1.0, 0.95, 0.7), 0.25)])
feet = [box("FeetShadow_L", (-0.13, -2.92, 0.004), (0.13, 0.2, 0.003), [M_SHADOW]),
        box("FeetShadow_R", (0.09, -2.92, 0.004), (0.13, 0.2, 0.003), [M_SHADOW])]
for ob in feet:
    for f, hide in [(1, True), (47, True), (48, False)]:
        ob.hide_render = hide
        ob.keyframe_insert('hide_render', frame=f)
    # passo lateral: as sombras deslizam um pouco e param
    ob.keyframe_insert('location', frame=48)
    ob.location.x += 0.06
    ob.keyframe_insert('location', frame=58)

# batente da porta
for nm, c, s in [("DoorFrame_L", (-0.49, -2.99, 1.08), (0.08, 0.04, 2.16)),
                 ("DoorFrame_R", (0.49, -2.99, 1.08), (0.08, 0.04, 2.16)),
                 ("DoorFrame_Top", (0, -2.99, 2.14), (1.06, 0.04, 0.08))]:
    bevel(box(nm, c, s, [M_DOOR]), 0.012, 2)
bevel(door, 0.008, 1)

# móveis: low poly, mas com cantos gastos e estofado "fofo"
M_SOFA2 = material("SofaCushion", color=(0.20, 0.24, 0.33))
M_WOOD = material("Wood", color=(0.25, 0.15, 0.08))
M_PLANT = material("Plant", color=(0.10, 0.22, 0.08))
M_POT = material("Terracotta", color=(0.45, 0.20, 0.10))
M_CLOTH = material("Curtain", color=(0.30, 0.22, 0.30))
M_MUG = material("Mug", color=(0.75, 0.72, 0.62))

bevel(box("Rug", (0, 0.2, 0.006), (2.0, 1.4, 0.012), [M_RUG]), 0.004, 1)
bevel(box("Sofa_Base", (-1.6, 1.0, 0.2), (0.85, 1.9, 0.32), [M_SOFA]), 0.05, 2, True)
for i, y in enumerate((0.55, 1.45)):
    c = box(f"Sofa_Seat_{i:02d}", (-1.55, y, 0.43), (0.72, 0.86, 0.16), [M_SOFA2])
    jitter(c, 0.03, only=lambda co: co.z > 0)
    bevel(c, 0.065, 3, True)
    b = box(f"Sofa_BackCushion_{i:02d}", (-1.86, y, 0.74), (0.2, 0.86, 0.46), [M_SOFA2])
    b.rotation_euler = (0, math.radians(-12), 0)
    jitter(b, 0.03)
    bevel(b, 0.07, 3, True)
bevel(box("Sofa_Back", (-2.02, 1.0, 0.62), (0.16, 1.95, 0.6), [M_SOFA]), 0.06, 3, True)
for i, y in enumerate((0.0, 2.0)):
    bevel(box(f"Sofa_Arm_{i:02d}", (-1.6, y, 0.48), (0.85, 0.18, 0.34), [M_SOFA]), 0.08, 3, True)
blob("Sofa_Pillow", (-1.75, 1.85, 0.62), (0.08, 0.2, 0.17), M_RUG, subdiv=2, jit=0.03,
     rot=(0, math.radians(-25), math.radians(10)))

bevel(box("CoffeeTable_Top", (-0.6, 1.0, 0.42), (0.6, 1.0, 0.04), [M_WOOD]), 0.015, 2)
for dx in (-0.25, 0.25):
    for dy in (-0.45, 0.45):
        lathe(f"CoffeeTable_Leg_{dx}_{dy}", (-0.6 + dx, 1.0 + dy, 0), [(0.02, 0), (0.028, 0.2), (0.022, 0.4)], M_WOOD, segs=6)
bag = box("FoodBag", (-0.55, 0.8, 0.56), (0.22, 0.15, 0.24), [M_BAG])
jitter(bag, 0.04, only=lambda co: co.z > 0)       # boca do saco amassada
bevel(bag, 0.01, 1)
lathe("Mug", (-0.75, 1.25, 0.44), [(0, 0), (0.035, 0), (0.04, 0.09), (0.036, 0.09), (0.03, 0.01)], M_MUG, segs=8)
blob("FoodContainer", (-0.45, 1.2, 0.47), (0.09, 0.07, 0.03), M_MUG, subdiv=1, flat=True)

bevel(box("TVStand", (0.6, 2.75, 0.3), (1.0, 0.4, 0.6), [M_WOOD]), 0.02, 2)
tv = box("TV", (0.6, 2.72, 0.85), (0.6, 0.45, 0.5), [M_DARK, M_TV], special={2: 1})
for v in tv.data.vertices:                        # traseira afunilada de CRT
    if v.co.y > 0:
        v.co.x *= 0.6
        v.co.z *= 0.65
bevel(tv, 0.035, 2)
for sgn in (-1, 1):
    lathe(f"TVAntenna_{sgn}", (0.6, 2.8, 1.08), [(0.006, 0), (0.003, 0.4)], M_METAL, segs=4,
          rot=(0, math.radians(25 * sgn), 0))

bevel(box("Counter", (2.2, 1.0, 0.45), (0.6, 2.5, 0.9), [M_COUNTER]), 0.02, 2)
lathe("Kettle", (2.2, 1.6, 0.9), [(0, 0), (0.09, 0), (0.1, 0.08), (0.085, 0.16), (0.04, 0.2), (0.015, 0.23), (0, 0.24)], M_FRIDGE, segs=10)
blob("FruitBowl", (2.15, 0.6, 0.93), (0.13, 0.13, 0.04), M_POT, subdiv=2)
for i in range(3):
    blob(f"Fruit_{i}", (2.1 + i * 0.05, 0.57 + (i % 2) * 0.06, 0.98), (0.04, 0.04, 0.04), M_RUG, subdiv=1)
fridge = box("Fridge", (2.15, -1.4, 0.9), (0.7, 0.7, 1.8), [M_FRIDGE])
bevel(fridge, 0.08, 3, True)                      # geladeira antiga, arredondada
bevel(box("FridgeHandle", (1.79, -1.15, 1.1), (0.03, 0.04, 0.4), [M_METAL]), 0.01, 1)

lathe("CoatRack", (-1.0, -2.75, 0), [(0.14, 0), (0.12, 0.03), (0.02, 0.05), (0.018, 1.7), (0.03, 1.72), (0, 1.75)], M_WOOD, segs=6)
coat = blob("Coat", (-1.0, -2.68, 1.3), (0.17, 0.09, 0.34), M_JACKET, subdiv=2, jit=0.05)
for v in coat.data.vertices:                      # casaco pendurado: largo embaixo, estreito no gancho
    v.co.x *= 0.6 + 0.4 * (1 - (v.co.z + 0.34) / 0.68)

lathe("PlantPot", (-2.2, -0.3, 0), [(0, 0), (0.12, 0), (0.16, 0.3), (0.17, 0.32), (0.15, 0.3), (0, 0.28)], M_POT, segs=8)
for i in range(7):
    a = i / 7 * 2 * math.pi
    blob(f"PlantLeaf_{i}", (-2.2 + math.cos(a) * 0.12, -0.3 + math.sin(a) * 0.12, 0.5 + (i % 3) * 0.09),
         (0.05, 0.16, 0.015), M_PLANT, subdiv=1, rot=(math.radians(50), 0, a + math.pi / 2))
for sgn in (-1, 1):
    wavy_curtain(f"Curtain_{sgn}", (-2.45, 0.6 + sgn * 0.5, 0.15), 0.42, 2.25, 3, 0.04, M_CLOTH)
lathe("CurtainRod", (-2.45, -0.3, 2.42), [(0.012, 0), (0.012, 1.8)], M_METAL, segs=6, rot=(math.radians(-90), 0, 0))

def area_light(name, loc, rot, energy, color, size=1.0, size_y=None):
    ld = bpy.data.lights.new(name, 'AREA')
    ld.energy = energy
    ld.color = color
    ld.shape = 'RECTANGLE'
    ld.size = size
    ld.size_y = size_y or size
    ob = bpy.data.objects.new(name, ld)
    ob.location = loc
    ob.rotation_euler = rot
    return link(ob)

def point_light(name, loc, energy, color, radius=0.1):
    ld = bpy.data.lights.new(name, 'POINT')
    ld.energy = energy
    ld.color = color
    ld.shadow_soft_size = radius
    ob = bpy.data.objects.new(name, ld)
    ob.location = loc
    return link(ob)

area_light("Light_Moon", (-2.3, 0.6, 1.6), (0, math.radians(-90), 0), 60, (0.45, 0.55, 1.0), 1.2, 1.0)
tv_light = point_light("Light_TV", (0.6, 2.2, 0.9), 15, (0.7, 0.8, 1.0))
for f in range(1, 211, 2):
    tv_light.data.energy = 8 + random.random() * 14
    tv_light.data.keyframe_insert('energy', frame=f)
set_constant(tv_light.data)
point_light("Light_DoorGap", (0, -2.85, 0.03), 2.5, (1.0, 0.9, 0.65), 0.05)

# ---------------------------------------------------------------- CORREDOR (sul da porta, Y de -3.15 a -16)
CL = 12.85
cy0 = -3.15 - CL / 2
box("Corridor_Floor", (0, cy0, -0.05), (2.0, CL, 0.1), [M_CFLOOR], tile=0.6)
box("Corridor_Ceiling", (0, cy0, 2.55), (2.0, CL, 0.1), [M_CEIL])
box("Corridor_Wall_W", (-1.05, cy0, 1.25), (0.1, CL, 2.5), [M_CWALL], tile=2.5)
box("Corridor_Wall_E", (1.05, cy0, 1.25), (0.1, CL, 2.5), [M_CWALL], tile=2.5)
box("Corridor_Wall_End", (0, -16.05, 1.25), (2.0, 0.1, 2.5), [M_CWALL], tile=2.5)
box("ExitSign", (0, -15.98, 2.25), (0.4, 0.03, 0.15), [M_EXIT])
for i, y in enumerate([-6.0, -10.0, -14.0]):
    for side in (-1, 1):
        box(f"CorridorDoor_{i:02d}_{'W' if side < 0 else 'E'}", (side * 0.99, y, 1.05), (0.04, 0.9, 2.1), [M_DOOR])
        box(f"DoorNumber_{i:02d}_{side}", (side * 0.965, y, 1.7), (0.01, 0.12, 0.08), [M_METAL])
        for nm, dy, dz, sz in [("L", -0.49, 1.08, (0.06, 0.08, 2.16)), ("R", 0.49, 1.08, (0.06, 0.08, 2.16)),
                               ("T", 0, 2.14, (0.06, 1.06, 0.08))]:
            bevel(box(f"CorridorDoorFrame_{i:02d}_{side}_{nm}", (side * 0.985, y + dy, dz), sz, [M_DOOR]), 0.012, 2)
        lathe(f"CorridorDoorKnob_{i:02d}_{side}", (side * 0.96, y + 0.33, 1.0),
              [(0, 0), (0.025, 0.01), (0.03, 0.04), (0, 0.06)], M_METAL, segs=6,
              rot=(0, math.radians(90 * -side), 0))
for side in (-1, 1):
    bevel(box(f"Baseboard_{side}", (side * 0.99, cy0, 0.06), (0.03, CL, 0.12), [M_WOOD]), 0.01, 1)
# cano velho no teto + extintor + sacos de lixo (formas orgânicas)
lathe("CeilingPipe", (0.7, -3.2, 2.36), [(0.045, 0), (0.045, 12.7)], M_METAL, segs=8, rot=(math.radians(90), 0, 0))
for y in (-5.5, -9.5, -13.5):
    bevel(box(f"PipeBracket_{y}", (0.7, y, 2.43), (0.03, 0.04, 0.14), [M_METAL]), 0.008, 1)
M_RED = material("ExtinguisherRed", color=(0.6, 0.04, 0.03))
lathe("Extinguisher", (-0.9, -11.6, 0.8), [(0, 0), (0.08, 0), (0.085, 0.05), (0.085, 0.42), (0.06, 0.5), (0.025, 0.54), (0, 0.56)], M_RED, segs=10)
for i, (x, y, s) in enumerate([(0.55, -15.4, 1.0), (0.2, -15.6, 0.8), (0.7, -14.9, 0.65)]):
    tb = blob(f"TrashBag_{i:02d}", (x, y, 0.24 * s), (0.27 * s, 0.24 * s, 0.26 * s), M_DARK, subdiv=2, jit=0.07 * s)
    for v in tb.data.vertices:                     # base achatada no chão
        v.co.z = max(v.co.z, -0.2 * s)
    blob(f"TrashKnot_{i:02d}", (x, y, 0.5 * s), (0.05, 0.05, 0.07), M_DARK, subdiv=1, jit=0.02)

tube_lights = []
for i, y in enumerate([-4.6, -8.8, -13.0]):
    bevel(box(f"TubeHousing_{i:02d}", (0, y, 2.485), (0.22, 1.2, 0.04), [M_FRIDGE]), 0.012, 2)
    lathe(f"Tube_{i:02d}", (0, y - 0.55, 2.45), [(0.022, 0), (0.022, 1.1)], M_TUBE, segs=6, rot=(math.radians(-90), 0, 0))
    tube_lights.append(area_light(f"Light_{i}", (0, y, 2.42), (0, 0, 0), 38, (0.85, 1.0, 0.85), 0.2, 1.1))

# ---------------------------------------------------------------- ENTREGADOR: modelo do projeto Unity (Antagonist.fbx)
# Fonte em pose T, sem rig, ~270k tris. Aqui ele vira PS1: decimado, textura 256px sem filtro,
# braços abaixados por skinning linear feito à mão e inclinação da cabeça como shape key.
UNITY_MODELS = r"C:\Users\sageo\Documents\TheDelivery\Assets\_Project"
DELIVERYMAN_FBX = UNITY_MODELS + r"\Models\Antagonist.fbx"
DELIVERYMAN_TEX = UNITY_MODELS + r"\Textures\Antagonist\Antagonist_BaseColor.jpg"
DELIVERYMAN_RATIO = 0.012            # 270k -> ~3.2k triângulos
HEAD_TILT_MAX = 24.0                 # graus da shape key "HeadTilt"

def smoothstep(e0, e1, x):
    t = np.clip((x - e0) / (e1 - e0), 0.0, 1.0)
    return t * t * (3 - 2 * t)

def rot_about(p, pivot, axis, ang):
    """Rotaciona pontos p (N,3) em torno de pivot; ang pode ser escalar ou (N,)."""
    q = p - pivot
    c, s = np.cos(ang), np.sin(ang)
    x, y, z = q[:, 0], q[:, 1], q[:, 2]
    if axis == 'Y':
        q = np.stack([x * c + z * s, y, -x * s + z * c], axis=1)
    elif axis == 'Z':
        q = np.stack([x * c - y * s, x * s + y * c, z], axis=1)
    else:
        q = np.stack([x, y * c - z * s, y * s + z * c], axis=1)
    return q + pivot

def import_deliveryman():
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=DELIVERYMAN_FBX)
    new = [o for o in bpy.data.objects if o not in before]
    ob = [o for o in new if o.type == 'MESH'][0]
    for o in new:
        if o is not ob:
            bpy.data.objects.remove(o, do_unlink=True)
    # aplica transformações e decima (low poly real, estilo PS1)
    mw = ob.matrix_world.copy()
    ob.data.transform(mw)
    ob.matrix_world.identity()
    dec = ob.modifiers.new("Decimate", 'DECIMATE')
    dec.ratio = DELIVERYMAN_RATIO
    dg = bpy.context.evaluated_depsgraph_get()
    me = bpy.data.meshes.new_from_object(ob.evaluated_get(dg))
    ob.modifiers.clear()
    old = ob.data
    ob.data = me
    bpy.data.meshes.remove(old)
    me.name = "DeliverymanMesh"

    n = len(me.vertices)
    co = np.empty(n * 3)
    me.vertices.foreach_get('co', co)
    co = co.reshape(-1, 3)
    ax = np.abs(co[:, 0])
    side = np.sign(co[:, 0])
    arm_z = smoothstep(1.30, 1.37, co[:, 2])
    w_upper = smoothstep(0.19, 0.27, ax) * arm_z
    w_fore = smoothstep(0.47, 0.53, ax) * arm_z
    # antebraço levemente dobrado para frente (cotovelo ~|x|=0.5)
    elbow = np.stack([side * 0.5, np.full(n, 0.07), np.full(n, 1.43)], axis=1)
    bent = rot_about(co - elbow, np.zeros(3), 'Z', -side * math.radians(18)) + elbow
    co = co + (bent - co) * w_fore[:, None]
    # braço inteiro cai do ombro (~|x|=0.21); 80° deixa ele um pouco afastado do corpo
    shoulder = np.stack([side * 0.21, np.full(n, 0.07), np.full(n, 1.44)], axis=1)
    down = rot_about(co - shoulder, np.zeros(3), 'Y', side * math.radians(80)) + shoulder
    co = co + (down - co) * w_upper[:, None]
    me.vertices.foreach_set('co', co.ravel())
    me.update()

    # shape key: cabeça inclinada (pivô no pescoço)
    ob.shape_key_add(name="Basis")
    sk = ob.shape_key_add(name="HeadTilt")
    w_head = smoothstep(1.49, 1.57, co[:, 2]) * (1 - smoothstep(0.16, 0.22, np.abs(co[:, 0])))
    tilted = rot_about(co, np.array([0.0, 0.0, 1.52]), 'Y', math.radians(HEAD_TILT_MAX))
    tilted = rot_about(tilted, np.array([0.0, 0.0, 1.52]), 'X', math.radians(-10))  # e abaixa o queixo
    sk.data.foreach_set('co', (co + (tilted - co) * w_head[:, None]).ravel())

    # textura reduzida para 256px, sem filtro, com o fog PSX dos demais materiais
    img = bpy.data.images.load(DELIVERYMAN_TEX)
    img.scale(256, 256)
    img.pack()
    me.materials.clear()
    me.materials.append(material("Deliveryman", image=img))
    smooth(ob)
    ob.name = "DeliverymanBody"
    return ob, sk

root = empty("Deliveryman", (0, -13.2, 0))
root.rotation_euler = (0, 0, math.pi)  # modelo olha para -Y; vira para a porta (+Y)
body, head_tilt = import_deliveryman()
body.parent = root
body.location = (0, 0, 0)

# ---------------------------------------------------------------- PLANO B: apagões + aproximação
# (frame, y do entregador, luzes acesas?, inclinação da cabeça em graus)
beats = [
    (76, -13.2, True, 0), (98, -13.2, False, 0),
    (106, -8.8, True, 0), (124, -8.8, False, 0),
    (131, -5.0, True, 8), (148, -5.0, False, 8),
    (154, -3.8, True, 24),
]
light_seq = {}
for i, (f, y, on, tilt) in enumerate(beats):
    root.location.y = y
    # no último beat ele se curva um pouco: o rosto fica na altura do olho mágico
    root.location.z = -0.08 if f >= 154 else 0.0
    root.keyframe_insert('location', frame=f)
    head_tilt.value = tilt / HEAD_TILT_MAX
    head_tilt.keyframe_insert('value', frame=f)
    nxt = beats[i + 1][0] if i + 1 < len(beats) else 166
    for ff in range(f, nxt):
        # flicker fino enquanto aceso
        light_seq[ff] = (1.0 if random.random() > 0.12 else 0.25) if on else 0.0
for ff in range(1, 76):
    light_seq[ff] = 1.0
set_constant(root)
set_constant(body.data.shape_keys)

tube_em = M_TUBE.node_tree.nodes[M_TUBE["em"]]
for ff in sorted(light_seq):
    k = light_seq[ff]
    for L in tube_lights:
        L.data.energy = 38 * k
        L.data.keyframe_insert('energy', frame=ff)
    tube_em.inputs['Strength'].default_value = 6.0 * k + 0.05
    tube_em.inputs['Strength'].keyframe_insert('default_value', frame=ff)
for L in tube_lights:
    set_constant(L.data)
set_constant(M_TUBE.node_tree)
# luz fraca de porta no final (rosto colado no olho mágico)
# luz fraca de bocal perto da porta: acompanha os apagões e ilumina o rosto quando ele chega perto
face_fill = point_light("Light_FaceFill", (0.2, -3.35, 1.15), 0, (1.0, 0.85, 0.6), 0.05)
for ff in sorted(light_seq):
    face_fill.data.energy = 0 if ff < 131 else 22 * light_seq[ff]
    face_fill.data.keyframe_insert('energy', frame=ff)
set_constant(face_fill.data)

# ---------------------------------------------------------------- CÂMERAS
def camera(name, loc, lens, clip_start=0.05):
    cd = bpy.data.cameras.new(name)
    cd.lens = lens
    cd.clip_start = clip_start
    cd.clip_end = 60
    ob = bpy.data.objects.new(name, cd)
    ob.location = loc
    return link(ob)

# Plano A: POV andando até a porta
camA = camera("Cam_A_POV", (0.9, 2.3, 1.55), 24)
targetA = empty("Cam_A_Target", (0, -3.0, 1.35))
tr = camA.constraints.new('TRACK_TO')
tr.target = targetA
tr.track_axis = 'TRACK_NEGATIVE_Z'
tr.up_axis = 'UP_Y'
for f, loc in [(1, (0.9, 2.3, 1.55)), (30, (0.45, 0.4, 1.52)), (60, (0.08, -1.2, 1.5)), (75, (0.05, -1.35, 1.48))]:
    camA.location = loc
    camA.keyframe_insert('location', frame=f)
# começa olhando a sala (sofá/janela), vira para a porta, depois desce para a fresta
for f, loc in [(1, (-2.0, 0.9, 0.8)), (8, (-2.0, 0.4, 0.9)), (24, (0, -3.0, 1.35)), (44, (0, -3.0, 1.2)),
               (56, (0, -2.95, 0.05)), (75, (0, -2.95, 0.02))]:
    targetA.location = loc
    targetA.keyframe_insert('location', frame=f)
add_noise(camA, 'location', 0.035, 6)       # passo/respiração
add_noise(targetA, 'location', 0.05, 9, 3)

# Plano B: olho mágico (grande angular; o fisheye é feito no pós)
camB = camera("Cam_B_Peephole", (0, -3.11, 1.6), 9, 0.01)
camB.rotation_euler = (math.pi / 2, 0, math.pi)
camB.keyframe_insert('location', frame=76)
add_noise(camB, 'location', 0.006, 12)

# Plano C: cartela
camC = camera("Cam_C_Title", (50, 4, 1.0), 50)
camC.rotation_euler = (math.pi / 2, 0, math.pi)

def text(name, body, loc, size, mat, show_from):
    cu = bpy.data.curves.new(name, 'FONT')
    cu.body = body
    cu.align_x = 'CENTER'
    cu.align_y = 'CENTER'
    cu.size = size
    cu.materials.append(mat)
    ob = bpy.data.objects.new(name, cu)
    ob.location = loc
    ob.rotation_euler = (math.pi / 2, 0, math.pi)
    link(ob)
    for f, hide in [(1, True), (show_from - 1, True), (show_from, False)]:
        ob.hide_render = hide
        ob.keyframe_insert('hide_render', frame=f)
    return ob

text("Text_Line", "Eu sei onde você mora agora.", (50, 0, 1.15), 0.16, M_TEXT_W, 172)
text("Text_Title", "THE DELIVERY", (50, 0, 0.8), 0.3, M_TEXT_R, 192)

scene.camera = camA
for name, f, cam in [("A", 1, camA), ("B", 76, camB), ("C", 166, camC)]:
    mk = scene.timeline_markers.new(name, frame=f)
    mk.camera = cam

# ---------------------------------------------------------------- render PSX
world = bpy.data.worlds.new("Black")
try:
    world.use_nodes = True
except Exception:
    pass
bg = world.node_tree.nodes.get('Background')
if bg:
    bg.inputs['Color'].default_value = (0, 0, 0, 1)
    bg.inputs['Strength'].default_value = 0
scene.world = world

r = scene.render
r.engine = 'BLENDER_EEVEE'
r.resolution_x, r.resolution_y = 320, 240
r.resolution_percentage = 100
try:
    r.filter_size = 0.0  # sem antialiasing
except Exception:
    pass
try:
    scene.eevee.taa_render_samples = 16  # sombras limpas; bordas seguem duras (filter_size 0)
except Exception:
    pass
scene.view_settings.view_transform = 'Standard'
scene.view_settings.look = 'None'
r.image_settings.file_format = 'PNG'
r.filepath = RAW_DIR.rstrip("/\\") + "/f_####"

if BLEND_OUT:
    bpy.ops.wm.save_as_mainfile(filepath=BLEND_OUT)
    print("[Teaser] blend salvo:", BLEND_OUT)
if DO_RENDER:
    bpy.ops.render.render(animation=True)
    print("[Teaser] render ok")
