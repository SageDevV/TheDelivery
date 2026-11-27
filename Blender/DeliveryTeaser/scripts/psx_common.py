# Utilitários compartilhados dos curtas PSX de The Delivery (Blender 5.x).
# Extraído do build_scene.py: texturas pixeladas, materiais com fog, geometria low poly
# orgânica, luzes, câmeras, texto, render PSX e import do entregador (Antagonist.fbx).
import bpy, bmesh, math, random, os, glob
import numpy as np

random.seed(7)
rng = np.random.default_rng(7)

class _SceneProxy:
    # link() usa sempre a cena ativa
    def __getattr__(self, k):
        return getattr(bpy.context.scene, k)
scene = _SceneProxy()

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
FOG_NEAR, FOG_FAR = 5.0, 17.0     # cada curta pode mudar antes de criar os materiais

def add_fog(nt, shader_out, near=None, far=None):
    near = FOG_NEAR if near is None else near
    far = FOG_FAR if far is None else far
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


def camera(name, loc, lens, clip_start=0.05):
    cd = bpy.data.cameras.new(name)
    cd.lens = lens
    cd.clip_start = clip_start
    cd.clip_end = 60
    ob = bpy.data.objects.new(name, cd)
    ob.location = loc
    return link(ob)


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

def set_roughness(mat, value, specular=0.5):
    bsdf = mat.node_tree.nodes[mat["bsdf"]]
    bsdf.inputs['Roughness'].default_value = value
    try:
        bsdf.inputs['Specular IOR Level'].default_value = specular
    except KeyError:
        pass
    return mat

def key_visible(ob, visible_ranges, last_frame):
    """Renderiza só dentro de [(ini, fim), ...]; chaves em hide_render (booleano = sempre constante)."""
    vis = set()
    for a, b in visible_ranges:
        vis.update(range(a, b + 1))
    prev = None
    for f in range(1, last_frame + 1):
        h = f not in vis
        if h != prev:
            ob.hide_render = h
            ob.keyframe_insert('hide_render', frame=f)
            prev = h

# ---------------------------------------------------------------- render PSX (cena ativa)
def setup_render(raw_dir, frame_end, fps=15):
    sc = bpy.context.scene
    sc.frame_start, sc.frame_end = 1, frame_end
    sc.render.fps = fps
    world = bpy.data.worlds.new("Black")
    try:
        world.use_nodes = True
    except Exception:
        pass
    bg = world.node_tree.nodes.get('Background')
    if bg:
        bg.inputs['Color'].default_value = (0, 0, 0, 1)
        bg.inputs['Strength'].default_value = 0
    sc.world = world
    r = sc.render
    r.engine = 'BLENDER_EEVEE'
    r.resolution_x, r.resolution_y = 320, 240
    r.resolution_percentage = 100
    try:
        r.filter_size = 0.0          # sem antialiasing
    except Exception:
        pass
    try:
        sc.eevee.taa_render_samples = 16
    except Exception:
        pass
    sc.view_settings.view_transform = 'Standard'
    sc.view_settings.look = 'None'
    r.image_settings.file_format = 'PNG'
    r.filepath = raw_dir.rstrip("/\\") + "/f_####"

# ---------------------------------------------------------------- assets do projeto Unity
UNITY_PROJECT = r"C:\Users\sageo\Documents\TheDelivery\Assets\_Project"

def _import_single_mesh(fbx_path):
    """Importa um FBX estático; vários meshes viram um só (materiais preservados)."""
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=fbx_path)
    new = [o for o in bpy.data.objects if o not in before]
    meshes = [o for o in new if o.type == 'MESH']
    for o in meshes:
        o.data = o.data.copy() if o.data.users > 1 else o.data
        o.data.transform(o.matrix_world.copy())
        o.parent = None
        o.matrix_world.identity()
    ob = max(meshes, key=lambda o: len(o.data.polygons))
    leftovers = [o for o in new if o.type != 'MESH']
    if len(meshes) > 1:
        with bpy.context.temp_override(active_object=ob, selected_editable_objects=meshes,
                                       selected_objects=meshes, object=ob):
            bpy.ops.object.join()          # os outros meshes deixam de existir aqui
    for o in leftovers:
        bpy.data.objects.remove(o, do_unlink=True)
    return ob

def world_bbox(obs):
    """(min, max) de mundo considerando deformações (avaliado)."""
    dg = bpy.context.evaluated_depsgraph_get()
    pts = []
    for o in obs:
        ev = o.evaluated_get(dg)
        me = ev.to_mesh()
        mw = ev.matrix_world
        pts += [mw @ v.co for v in me.vertices]
        ev.to_mesh_clear()
    mn = Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts)))
    mx = Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
    return mn, mx

def _decimate(ob, ratio):
    if ratio >= 1.0:
        return
    dec = ob.modifiers.new("Decimate", 'DECIMATE')
    dec.ratio = ratio
    dg = bpy.context.evaluated_depsgraph_get()
    me = bpy.data.meshes.new_from_object(ob.evaluated_get(dg))
    ob.modifiers.clear()
    old = ob.data
    ob.data = me
    bpy.data.meshes.remove(old)

def _psx_texture(path, size):
    img = bpy.data.images.load(path)
    img.scale(size, size)
    img.pack()
    return img

def import_prop(name, loc, rot_z_deg=0.0, scale=1.0, max_tris=1500, tex_size=128, height=None, tex_name=None,
                obj_name=None):
    """Prop do Unity (Models/<name>.fbx + Textures/<tex>/<tex>_BaseColor.*), decimado e com textura PS1.
    height: escala uniformemente até essa altura (ignora `scale`). Sem textura: mantém materiais do FBX."""
    ob = _import_single_mesh(os.path.join(UNITY_PROJECT, "Models", name + ".fbx"))
    _decimate(ob, min(1.0, max_tris / max(1, len(ob.data.polygons))))
    tex_name = tex_name or name
    tex = glob.glob(os.path.join(UNITY_PROJECT, "Textures", tex_name, tex_name + "_BaseColor.*"))
    if tex:
        ob.data.materials.clear()
        ob.data.materials.append(material(name, image=_psx_texture(tex[0], tex_size)))
    else:
        for m in ob.data.materials:                 # texturas embutidas: sem filtro, como o resto
            if m and m.node_tree:
                for nd in m.node_tree.nodes:
                    if nd.type == 'TEX_IMAGE':
                        nd.interpolation = 'Closest'
    smooth(ob)
    ob.name = obj_name or name
    if height:
        zs = [v.co.z for v in ob.data.vertices]
        scale = height / max(1e-6, max(zs) - min(zs))
    ob.location = loc
    ob.rotation_euler = (0, 0, math.radians(rot_z_deg))
    ob.scale = (scale, scale, scale)
    return ob

# ---------------------------------------------------------------- personagens Mixamo do projeto (riggados + animados)
def import_character(name, height, tex_name=None, max_tris=3500, tex_size=256, scale=None):
    """Importa Models/<name>.fbx (Mixamo): corrige a escala (cm), decima mantendo os pesos, aplica a
    textura PS1 e devolve (armature, mesh, action). A ação original (30 fps) fica desligada do objeto."""
    before = set(bpy.data.objects)
    acts_before = set(bpy.data.actions)
    bpy.ops.import_scene.fbx(filepath=os.path.join(UNITY_PROJECT, "Models", name + ".fbx"))
    new = [o for o in bpy.data.objects if o not in before]
    arm = [o for o in new if o.type == 'ARMATURE'][0]
    meshes = [o for o in new if o.type == 'MESH']
    for o in new:
        if o not in meshes and o is not arm:
            bpy.data.objects.remove(o, do_unlink=True)
    action = ([a for a in bpy.data.actions if a not in acts_before] or [None])[0]
    if arm.animation_data:
        arm.animation_data.action = None
    # decima com o Armature desligado (pesos sobrevivem ao collapse)
    for ob in meshes:
        for m in ob.modifiers:
            if m.type == 'ARMATURE':
                m.show_viewport = False
                m.show_render = False
        ratio = min(1.0, max_tris / max(1, len(ob.data.polygons)))
        if ratio < 1.0:
            dec = ob.modifiers.new("Decimate", 'DECIMATE')
            dec.ratio = ratio
            dg = bpy.context.evaluated_depsgraph_get()
            me = bpy.data.meshes.new_from_object(ob.evaluated_get(dg), preserve_all_data_layers=True,
                                                 depsgraph=dg)
            ob.modifiers.remove(dec)
            old = ob.data
            ob.data = me
            bpy.data.meshes.remove(old)
        for m in ob.modifiers:
            if m.type == 'ARMATURE':
                m.show_viewport = True
                m.show_render = True
        tex_name = tex_name or name
        tex = glob.glob(os.path.join(UNITY_PROJECT, "Textures", tex_name, tex_name + "_BaseColor.*"))
        if tex:
            ob.data.materials.clear()
            ob.data.materials.append(material(name + "_Mat", image=_psx_texture(tex[0], tex_size)))
        smooth(ob)
    # altura real (pose de repouso)
    mn, mx = world_bbox(meshes)
    k = height / max(1e-6, mx.z - mn.z)
    if scale is not None:                       # ex.: clipe sentado mede errado; reaproveita a escala do em pé
        k = scale / arm.scale.x
    arm.scale = arm.scale * k
    arm.name = name
    return arm, meshes, action

def play_action(arm, action, start, end, src_fps=30, dst_fps=15, src_range=None, name="clip"):
    """Toca `action` (em loop) no armature entre start e end, convertendo 30->15 fps via NLA."""
    ad = arm.animation_data or arm.animation_data_create()
    track = ad.nla_tracks.new()
    track.name = name
    strip = track.strips.new(name, int(start), action)
    if src_range:
        strip.action_frame_start, strip.action_frame_end = src_range
    length = strip.action_frame_end - strip.action_frame_start
    strip.scale = dst_fps / src_fps
    strip.repeat = max(1.0, (end - start) / (length * strip.scale))
    try:
        strip.extrapolation = 'HOLD'
    except Exception:
        pass
    try:
        if hasattr(strip, "action_slot") and strip.action_slot is None and len(action.slots):
            strip.action_slot = action.slots[0]
    except Exception:
        pass
    return strip

def split_loose_parts(ob):
    """Separa as peças soltas (ilhas) de um mesh em objetos próprios, maior primeiro.
    O maior continua sendo `ob`; os outros herdam transformação e materiais."""
    bm = bmesh.new()
    bm.from_mesh(ob.data)
    bm.verts.ensure_lookup_table()
    seen, islands = set(), []
    for v in bm.verts:
        if v.index in seen:
            continue
        stack, comp = [v], []
        seen.add(v.index)
        while stack:
            a = stack.pop()
            comp.append(a.index)
            for e in a.link_edges:
                b = e.other_vert(a)
                if b.index not in seen:
                    seen.add(b.index)
                    stack.append(b)
        islands.append(comp)
    bm.free()
    islands.sort(key=len, reverse=True)
    if len(islands) == 1:
        return [ob]
    parts = []
    for i, keep in enumerate(islands):
        keep = set(keep)
        b2 = bmesh.new()
        b2.from_mesh(ob.data)
        b2.verts.ensure_lookup_table()
        bmesh.ops.delete(b2, geom=[v for v in b2.verts if v.index not in keep], context='VERTS')
        me = bpy.data.meshes.new(f"{ob.data.name}_part{i}")
        b2.to_mesh(me)
        b2.free()
        for m in ob.data.materials:
            me.materials.append(m)
        if i == 0:
            parts.append(me)
        else:
            o = bpy.data.objects.new(f"{ob.name}_Part{i:02d}", me)
            link(o)
            o.matrix_world = ob.matrix_world.copy()
            parts.append(smooth(o))
    old = ob.data
    ob.data = parts[0]
    bpy.data.meshes.remove(old)
    smooth(ob)
    return [ob] + parts[1:]

# ---------------------------------------------------------------- entregador (Antagonist.fbx)
# Pose T, sem rig. Poses feitas por skinning linear à mão + shape keys.
HEAD_TILT_MAX = 24.0
HAND_T = 0.83                       # |x| do centro da mão na pose T

def _arm_weights(co):
    ax = np.abs(co[:, 0])
    arm_z = smoothstep(1.30, 1.37, co[:, 2])
    return smoothstep(0.19, 0.27, ax) * arm_z, smoothstep(0.47, 0.53, ax) * arm_z

def _pose_arm(co, w_upper, w_fore, side, fore_deg, up_deg, fwd_deg=0.0):
    """side: +1/-1 por vértice. fore_deg dobra o antebraço para frente; up_deg gira o braço no ombro
    (negativo = desce); fwd_deg leva o braço para frente (eixo X no ombro)."""
    n = len(co)
    elbow = np.stack([side * 0.5, np.full(n, 0.07), np.full(n, 1.43)], axis=1)
    bent = rot_about(co - elbow, np.zeros(3), 'Z', -side * math.radians(fore_deg)) + elbow
    co = co + (bent - co) * w_fore[:, None]
    shoulder = np.stack([side * 0.21, np.full(n, 0.07), np.full(n, 1.44)], axis=1)
    rot = rot_about(co - shoulder, np.zeros(3), 'Y', -side * math.radians(up_deg)) + shoulder
    if fwd_deg:
        rot = rot_about(rot - shoulder, np.zeros(3), 'X', math.radians(fwd_deg)) + shoulder
    return co + (rot - co) * w_upper[:, None]

POSE_DOWN = dict(fore_deg=18, up_deg=-80)
POSE_RAISED = dict(fore_deg=60, up_deg=80, fwd_deg=45)      # faca erguida acima da cabeça

def deliveryman_hand(side, pose):
    """Posição (local do modelo) do centro da mão na pose dada."""
    p = np.array([[side * HAND_T, 0.06, 1.40]])
    return _pose_arm(p, np.ones(1), np.ones(1), np.array([float(side)]), **pose)[0]

def import_deliveryman(ratio=0.012, raise_side=-1):
    """Retorna (obj, {'HeadTilt': key, 'ArmRaise': key}). raise_side = braço da faca (-1 = mão direita dele)."""
    ob = _import_single_mesh(os.path.join(UNITY_PROJECT, "Models", "Antagonist.fbx"))
    _decimate(ob, ratio)
    me = ob.data
    me.name = "DeliverymanMesh"
    n = len(me.vertices)
    co0 = np.empty(n * 3)
    me.vertices.foreach_get('co', co0)
    co0 = co0.reshape(-1, 3)
    side = np.sign(co0[:, 0])
    wu, wf = _arm_weights(co0)
    down = _pose_arm(co0, wu, wf, side, **POSE_DOWN)
    me.vertices.foreach_set('co', down.ravel())
    me.update()
    ob.shape_key_add(name="Basis")
    keys = {}
    sk = ob.shape_key_add(name="HeadTilt")          # cabeça inclinada + queixo baixo
    w_head = smoothstep(1.49, 1.57, down[:, 2]) * (1 - smoothstep(0.16, 0.22, np.abs(down[:, 0])))
    piv = np.array([0.0, 0.0, 1.52])
    tilted = rot_about(rot_about(down, piv, 'Y', math.radians(HEAD_TILT_MAX)), piv, 'X', math.radians(-10))
    sk.data.foreach_set('co', (down + (tilted - down) * w_head[:, None]).ravel())
    keys["HeadTilt"] = sk
    sk = ob.shape_key_add(name="ArmRaise")          # braço da faca erguido
    raised = _pose_arm(co0, wu, wf, side, **POSE_RAISED)
    sk.data.foreach_set('co', np.where((side == raise_side)[:, None], raised, down).ravel())
    keys["ArmRaise"] = sk
    me.materials.clear()
    me.materials.append(material("Deliveryman", image=_psx_texture(
        os.path.join(UNITY_PROJECT, "Textures", "Antagonist", "Antagonist_BaseColor.jpg"), 256)))
    smooth(ob)
    ob.name = "DeliverymanBody"
    return ob, keys

def knife(name, parent=None):
    """Faca de cozinha low poly; origem no punho, lâmina apontando para -Z."""
    root = empty(name)
    if parent:
        root.parent = parent
    m_handle = material(name + "_Handle", color=(0.05, 0.03, 0.02))
    m_blade = set_roughness(material(name + "_Blade", color=(0.6, 0.62, 0.65),
                                     emission=(0.5, 0.5, 0.55), emit_strength=0.15), 0.25, 1.0)
    bevel(box(name + "_Grip", (0, 0, 0.03), (0.024, 0.03, 0.11), [m_handle], parent=root), 0.008, 1)
    blade = box(name + "_Blade", (0, 0, -0.13), (0.004, 0.038, 0.22), [m_blade], parent=root)
    for v in blade.data.vertices:
        if v.co.z < 0:
            v.co.y = v.co.y * 0.1 - 0.012     # ponta
    return root

# ---------------------------------------------------------------- entregador RIGGADO (animação orgânica)
# Esqueleto humanoide encaixado na pose T do Antagonist.fbx (medido na malha). Frente = -Y.
# .L = lado +X (esquerda dele), .R = lado -X (direita dele, mão da faca).
from mathutils import Matrix, Quaternion, Vector

RIG_BONES = [  # nome, cabeça, cauda, pai
    ("Hips", (0, 0.02, 0.92), (0, 0.02, 1.08), None),
    ("Spine", (0, 0.02, 1.08), (0, 0.02, 1.28), "Hips"),
    ("Chest", (0, 0.02, 1.28), (0, 0.02, 1.46), "Spine"),
    ("Neck", (0, 0.0, 1.46), (0, 0.0, 1.56), "Chest"),
    ("Head", (0, 0.0, 1.56), (0, 0.0, 1.86), "Neck"),
]
for _s, _n in ((1, "L"), (-1, "R")):
    RIG_BONES += [
        (f"Shoulder.{_n}", (_s * 0.04, 0.05, 1.43), (_s * 0.21, 0.07, 1.44), "Chest"),
        (f"UpperArm.{_n}", (_s * 0.21, 0.07, 1.44), (_s * 0.50, 0.07, 1.43), f"Shoulder.{_n}"),
        (f"Forearm.{_n}", (_s * 0.50, 0.07, 1.43), (_s * 0.69, 0.07, 1.42), f"UpperArm.{_n}"),
        (f"Hand.{_n}", (_s * 0.69, 0.07, 1.42), (_s * 0.79, 0.07, 1.40), f"Forearm.{_n}"),   # pulso -> base dos dedos (medido)
        (f"Fingers.{_n}", (_s * 0.79, 0.07, 1.40), (_s * 0.875, 0.07, 1.40), f"Hand.{_n}"),   # fecha o punho
        (f"Thigh.{_n}", (_s * 0.09, 0.04, 0.90), (_s * 0.12, 0.04, 0.50), "Hips"),
        (f"Shin.{_n}", (_s * 0.12, 0.04, 0.50), (_s * 0.14, 0.07, 0.10), f"Thigh.{_n}"),
        (f"Foot.{_n}", (_s * 0.14, 0.07, 0.10), (_s * 0.14, -0.13, 0.03), f"Shin.{_n}"),
    ]

def _bone_allowed(name, co):
    """Máscara por região: evita braço puxar tronco, perna esquerda puxar a direita etc."""
    x, z = co[:, 0], co[:, 2]
    if "." not in name:
        return np.ones(len(co), bool)
    part, side = name.split(".")
    s = 1 if side == "L" else -1
    if part in ("UpperArm", "Forearm", "Hand", "Fingers"):
        return (x * s > 0.15) & (z > 1.25)
    if part == "Shoulder":
        return x * s > 0.0
    if part == "Thigh":
        return (x * s > -0.01) & (z < 1.02)
    return (x * s > -0.01) & (z < 0.62)          # Shin / Foot

def _auto_weights(co, power=4, top=2):
    names = [b[0] for b in RIG_BONES]
    d = np.empty((len(co), len(names)))
    for j, (nm, h, t, _) in enumerate(RIG_BONES):
        a, b = np.array(h, float), np.array(t, float)
        ab = b - a
        tt = np.clip(((co - a) @ ab) / (ab @ ab), 0, 1)
        dist = np.linalg.norm(co - (a + tt[:, None] * ab), axis=1)
        dist[~_bone_allowed(nm, co)] = 1e3
        d[:, j] = dist
    w = 1.0 / (d + 0.02) ** power
    keep = np.argsort(-w, axis=1)[:, :top]
    out = np.zeros_like(w)
    rows = np.arange(len(co))[:, None]
    out[rows, keep] = w[rows, keep]
    out /= out.sum(axis=1, keepdims=True)
    return names, out

class Rig:
    """Poses em ESPAÇO DE REPOUSO do armature: cada osso recebe uma lista [(eixo, graus), ...]
    aplicada em ordem, em torno da própria cabeça. Ossos ausentes no dicionário = repouso."""
    def __init__(self, arm):
        self.arm = arm
        self.rest = {b.name: b.matrix_local.to_3x3() for b in arm.data.bones}
        for pb in arm.pose.bones:
            pb.rotation_mode = 'QUATERNION'

    def apply(self, pose):
        for pb in self.arm.pose.bones:
            R = Matrix.Identity(3)
            for axis, deg in pose.get(pb.name, []):
                R = Matrix.Rotation(math.radians(deg), 3, axis) @ R
            M = self.rest[pb.name]
            pb.rotation_quaternion = (M.inverted() @ R @ M).to_quaternion()

    def key(self, frame, pose):
        self.apply(pose)
        for pb in self.arm.pose.bones:
            pb.keyframe_insert('rotation_quaternion', frame=frame)

def import_deliveryman_rigged(ratio=0.012):
    """Malha decimada + armature com pesos automáticos. Retorna (armature_obj, mesh_obj, Rig)."""
    ob = _import_single_mesh(os.path.join(UNITY_PROJECT, "Models", "Antagonist.fbx"))
    _decimate(ob, ratio)
    me = ob.data
    me.name = "DeliverymanMesh"
    ad = bpy.data.armatures.new("DeliverymanRig")
    arm = bpy.data.objects.new("Deliveryman", ad)
    link(arm)
    bpy.context.view_layer.objects.active = arm
    arm.select_set(True)
    bpy.ops.object.mode_set(mode='EDIT')
    eb = {}
    for nm, h, t, parent in RIG_BONES:
        b = ad.edit_bones.new(nm)
        b.head, b.tail = Vector(h), Vector(t)
        b.roll = 0.0
        if parent:
            b.parent = eb[parent]
            b.use_connect = (Vector(h) - eb[parent].tail).length < 1e-4
        eb[nm] = b
    bpy.ops.object.mode_set(mode='OBJECT')

    n = len(me.vertices)
    co = np.empty(n * 3)
    me.vertices.foreach_get('co', co)
    co = co.reshape(-1, 3)
    names, W = _auto_weights(co)
    for j, nm in enumerate(names):
        vg = ob.vertex_groups.new(name=nm)
        idx = np.nonzero(W[:, j] > 1e-3)[0]
        for i in idx:
            vg.add([int(i)], float(W[i, j]), 'REPLACE')
    ob.parent = arm
    mod = ob.modifiers.new("Armature", 'ARMATURE')
    mod.object = arm
    me.materials.clear()
    me.materials.append(material("Deliveryman", image=_psx_texture(
        os.path.join(UNITY_PROJECT, "Textures", "Antagonist", "Antagonist_BaseColor.jpg"), 256)))
    smooth(ob)
    ob.name = "DeliverymanBody"
    return arm, ob, Rig(arm)

def attach_to_bone(child, arm, bone, world_matrix):
    """Prende child ao osso mantendo a transformação de mundo dada (armature em repouso)."""
    child.parent = arm
    child.parent_type = 'BONE'
    child.parent_bone = bone
    bpy.context.view_layer.update()
    child.matrix_world = world_matrix

# ---------------------------------------------------------------- biblioteca de poses do entregador
def merge(*poses):
    out = {}
    for p in poses:
        for k, v in p.items():
            out[k] = out.get(k, []) + list(v)
    return out

def pose_idle(slouch=6, head_down=6, breath=0.0):
    """Parado, braços caídos, ombros um pouco curvados. breath -1..1 enche o peito."""
    return {
        "Spine": [('X', slouch * 0.5)],
        "Chest": [('X', slouch * 0.5 - 2.5 * breath)],
        "Neck": [('X', head_down * 0.5)],
        "Head": [('X', head_down * 0.5)],
        "Shoulder.L": [('Y', 4 + 2 * breath)], "Shoulder.R": [('Y', -4 - 2 * breath)],
        "UpperArm.L": [('Y', 76)], "UpperArm.R": [('Y', -74)],
        "Forearm.L": [('Z', -12)], "Forearm.R": [('Z', 22)],     # mão da faca um pouco mais dobrada
        "Hand.L": [('Y', 8)], "Hand.R": [('Y', -10)],
        "Fingers.L": [('Y', 25)], "Fingers.R": [('Y', -100)],   # direita fechada no cabo da faca
    }

def walk_phase(phase, lead, stride=1.0):
    """phase 'contact' | 'passing'; lead 'L'/'R' = perna que vai à frente. Passo pesado, arrastado."""
    o = "R" if lead == "L" else "L"
    if phase == "contact":
        legs = {f"Thigh.{lead}": [('X', -22 * stride)], f"Shin.{lead}": [('X', 6)], f"Foot.{lead}": [('X', -8)],
                f"Thigh.{o}": [('X', 16 * stride)], f"Shin.{o}": [('X', 18)], f"Foot.{o}": [('X', 12)]}
        swing = 14 * stride
    else:
        legs = {f"Thigh.{lead}": [('X', -4)], f"Shin.{lead}": [('X', 4)],
                f"Thigh.{o}": [('X', -14 * stride)], f"Shin.{o}": [('X', 38)], f"Foot.{o}": [('X', 10)]}
        swing = 4 * stride
    s = 1 if lead == "L" else -1
    arms = {"UpperArm.L": [('X', s * swing * 0.9)],               # braço oposto à perna vai à frente
            "UpperArm.R": [('X', -s * swing * 0.4)],              # o da faca balança pouco (rígido)
            "Spine": [('Z', s * 4)], "Chest": [('Z', -s * 3)]}
    return merge(legs, arms)

def anim_walk(rig, f0, f1, first_lead="L", stride=1.0, extra=None, bob=0.03):
    """Ciclo de caminhada entre f0 e f1 (ossos). Retorna lista [(frame, dz)] para o balanço do corpo."""
    n_half = max(1, round((f1 - f0) / 8))
    step = (f1 - f0) / n_half
    lead = first_lead
    bobs = []
    for i in range(n_half + 1):
        fc = round(f0 + i * step)
        base = merge(pose_idle(slouch=10, head_down=10), extra or {})
        rig.key(fc, merge(base, walk_phase("contact", lead, stride)))
        bobs.append((fc, -bob))
        if i < n_half:
            fp = round(f0 + (i + 0.5) * step)
            rig.key(fp, merge(base, walk_phase("passing", lead, stride)))
            bobs.append((fp, bob * 0.4))
        lead = "R" if lead == "L" else "L"
    return bobs


def fcurves_of_action(action):
    """F-curves de uma Action (API antiga ou slotted actions do Blender 4.4+/5)."""
    try:
        return list(action.fcurves)
    except AttributeError:
        from bpy_extras import anim_utils
        return list(anim_utils.action_get_channelbag_for_slot(action, action.slots[0]).fcurves)

def make_in_place(action, bone="mixamorig:Hips"):
    """Remove o deslocamento horizontal do quadril (root motion) para o ciclo poder repetir.
    Retorna o deslocamento removido por eixo (unidades da action)."""
    removed = {}
    fcs = [fc for fc in fcurves_of_action(action) if fc.data_path == f'pose.bones["{bone}"].location']
    ranges = {fc.array_index: (max(k.co.y for k in fc.keyframe_points) - min(k.co.y for k in fc.keyframe_points))
              for fc in fcs}
    vertical = min(ranges, key=ranges.get)          # o eixo que menos varia é o vertical (balanço)
    for fc in fcs:
        if fc.array_index == vertical:
            continue
        kps = fc.keyframe_points
        removed[fc.array_index] = kps[-1].co.y - kps[0].co.y
        v0 = kps[0].co.y
        for kp in kps:
            kp.co.y = v0
            kp.handle_left.y = v0
            kp.handle_right.y = v0
        fc.update()
    return removed
