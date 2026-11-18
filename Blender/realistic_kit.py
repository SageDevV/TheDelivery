"""KIT DE MODELAGEM REALISTA (Blender, background) — usado por build_bus.py e build_busstop.py.

Geometria limpa por script + MATERIAIS PROCEDURAIS com desgaste (sujeira nas cavidades e embaixo,
pintura lascada nas quinas, variação de cor/rugosidade, relevo) que são ASSADOS (Cycles) em
texturas PBR 2048: base_color, normal, metallic, roughness, emission. O Unity lê essas texturas
pelo RoadCarModelsSetup.BuildMaterial.

Tudo em metros, Z para cima. Cada peça recebe um material deste kit; no fim, todas as peças são
desembrulhadas juntas (UV smart project, ilhas sem sobreposição) e assadas na mesma textura.
"""
import bpy, bmesh, math, os

# ----------------------------------------------------------------------------------- cena
def reset_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    sc = bpy.context.scene
    sc.render.engine = 'CYCLES'
    sc.cycles.device = 'CPU'
    sc.cycles.samples = 24
    sc.render.bake.margin = 8

# ----------------------------------------------------------------------------------- nós
def _n(nt, t, x=0, y=0):
    n = nt.nodes.new(t)
    n.location = (x, y)
    return n

def _in(node, ident):
    for s in node.inputs:
        if s.identifier == ident or s.name == ident:
            return s
    raise KeyError(ident)

def _out(node, ident):
    for s in node.outputs:
        if s.identifier == ident or s.name == ident:
            return s
    raise KeyError(ident)

def _mix_color(nt, fac_socket, a, b):
    m = _n(nt, 'ShaderNodeMix')
    m.data_type = 'RGBA'
    nt.links.new(fac_socket, _in(m, 'Factor_Float'))
    if isinstance(a, tuple): _in(m, 'A_Color').default_value = (*a, 1.0)
    else: nt.links.new(a, _in(m, 'A_Color'))
    if isinstance(b, tuple): _in(m, 'B_Color').default_value = (*b, 1.0)
    else: nt.links.new(b, _in(m, 'B_Color'))
    return _out(m, 'Result_Color')

def _math(nt, op, a, b=None, clamp=False):
    m = _n(nt, 'ShaderNodeMath')
    m.operation = op
    m.use_clamp = clamp
    if isinstance(a, (int, float)): m.inputs[0].default_value = a
    else: nt.links.new(a, m.inputs[0])
    if b is not None:
        if isinstance(b, (int, float)): m.inputs[1].default_value = b
        else: nt.links.new(b, m.inputs[1])
    return m.outputs[0]

def _noise(nt, scale, detail=6.0, coord=None):
    n = _n(nt, 'ShaderNodeTexNoise')
    _in(n, 'Scale').default_value = scale
    _in(n, 'Detail').default_value = detail
    if coord is not None: nt.links.new(coord, _in(n, 'Vector'))
    return n

MATS = {}

def material(name, base, rough=0.5, metal=0.0, *, dirt=0.0, dirt_color=(0.16, 0.14, 0.11), ground_dirt=0.0,
             wear=0.0, wear_color=(0.45, 0.45, 0.45), rust=0.0, color_var=0.06, rough_var=0.12,
             bump=0.0, bump_scale=40.0, seams=None, wood=False, emission=None, emission_strength=0.0,
             streaks=0.0):
    """Material procedural. dirt = cavidades (AO); ground_dirt = sujeira que sobe do chão;
    wear = pintura lascada nas quinas (mostra wear_color); rust = manchas de ferrugem; seams =
    (eixo 'X'/'Y'/'Z', espaçamento m) emendas de chapa; streaks = escorridos verticais."""
    if name in MATS:
        return MATS[name]
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    nt.nodes.clear()
    out = _n(nt, 'ShaderNodeOutputMaterial', 900, 0)
    bsdf = _n(nt, 'ShaderNodeBsdfPrincipled', 600, 0)
    nt.links.new(bsdf.outputs[0], out.inputs['Surface'])
    tc = _n(nt, 'ShaderNodeTexCoord', -1200, 0)
    obj = tc.outputs['Object']
    sep = _n(nt, 'ShaderNodeSeparateXYZ', -1000, -300)
    nt.links.new(obj, sep.inputs[0])

    # Cor base com variação (manchas largas)
    col = (base[0], base[1], base[2])
    var = _noise(nt, 3.0, 4.0, obj)
    fac = _math(nt, 'MULTIPLY', var.outputs['Fac'], color_var * 2.0)
    darker = (base[0] * 0.75, base[1] * 0.75, base[2] * 0.75)
    color = _mix_color(nt, fac, col, darker)

    # Madeira: veio
    if wood:
        w = _n(nt, 'ShaderNodeTexWave')
        w.wave_type = 'BANDS'
        w.bands_direction = 'X'
        _in(w, 'Scale').default_value = 6.0
        _in(w, 'Distortion').default_value = 9.0
        _in(w, 'Detail').default_value = 4.0
        nt.links.new(obj, _in(w, 'Vector'))
        color = _mix_color(nt, _math(nt, 'MULTIPLY', w.outputs['Fac'], 0.55), color, (base[0] * 0.55, base[1] * 0.5, base[2] * 0.45))

    rough_s = None
    dirt_mask = None
    # Sujeira nas cavidades (AO) + subindo do chão + ruído
    if dirt > 0 or ground_dirt > 0:
        ao = _n(nt, 'ShaderNodeAmbientOcclusion', -600, 300)
        ao.samples = 16
        _in(ao, 'Distance').default_value = 0.35
        cav = _math(nt, 'SUBTRACT', 1.0, ao.outputs['AO'])
        cav = _math(nt, 'MULTIPLY', cav, 1.8 * dirt)
        z = sep.outputs['Z']
        h = _math(nt, 'SUBTRACT', 1.0, _math(nt, 'DIVIDE', z, 0.9))
        h = _math(nt, 'MAXIMUM', h, 0.0)
        h = _math(nt, 'MULTIPLY', h, ground_dirt)
        nz = _noise(nt, 14.0, 8.0, obj)
        n2 = _math(nt, 'MULTIPLY', nz.outputs['Fac'], 1.4)
        mask = _math(nt, 'ADD', cav, h)
        mask = _math(nt, 'MULTIPLY', mask, n2, clamp=True)
        dirt_mask = mask
        color = _mix_color(nt, mask, color, dirt_color)

    # Escorridos verticais (chuva/fuligem)
    if streaks > 0:
        st = _n(nt, 'ShaderNodeTexNoise')
        _in(st, 'Scale').default_value = 1.0
        _in(st, 'Detail').default_value = 2.0
        mp = _n(nt, 'ShaderNodeMapping')
        _in(mp, 'Scale').default_value = (18.0, 18.0, 0.6)
        nt.links.new(obj, _in(mp, 'Vector'))
        nt.links.new(mp.outputs[0], _in(st, 'Vector'))
        sm = _math(nt, 'SUBTRACT', st.outputs['Fac'], 0.55)
        sm = _math(nt, 'MULTIPLY', sm, 3.0 * streaks, clamp=True)
        color = _mix_color(nt, sm, color, dirt_color)

    # Pintura lascada nas quinas (Bevel x normal)
    if wear > 0:
        bv = _n(nt, 'ShaderNodeBevel', -600, -500)
        bv.samples = 8
        _in(bv, 'Radius').default_value = 0.025
        geo = _n(nt, 'ShaderNodeNewGeometry', -600, -700)
        dot = _n(nt, 'ShaderNodeVectorMath', -400, -600)
        dot.operation = 'DOT_PRODUCT'
        nt.links.new(bv.outputs['Normal'], dot.inputs[0])
        nt.links.new(geo.outputs['Normal'], dot.inputs[1])
        edge = _math(nt, 'SUBTRACT', 1.0, dot.outputs['Value'])
        edge = _math(nt, 'MULTIPLY', edge, 25.0)
        en = _noise(nt, 22.0, 10.0, obj)
        edge = _math(nt, 'MULTIPLY', edge, _math(nt, 'MULTIPLY', en.outputs['Fac'], 1.6))
        edge = _math(nt, 'MULTIPLY', edge, wear, clamp=True)
        edge = _math(nt, 'GREATER_THAN', edge, 0.45)
        color = _mix_color(nt, edge, color, wear_color)

    # Ferrugem em manchas, mais perto do chão
    if rust > 0:
        rn = _noise(nt, 6.0, 10.0, obj)
        rm = _math(nt, 'SUBTRACT', rn.outputs['Fac'], 0.58)
        rm = _math(nt, 'MULTIPLY', rm, 6.0 * rust, clamp=True)
        low = _math(nt, 'SUBTRACT', 1.0, _math(nt, 'DIVIDE', sep.outputs['Z'], 1.5))
        rm = _math(nt, 'MULTIPLY', rm, _math(nt, 'MAXIMUM', low, 0.25), clamp=True)
        color = _mix_color(nt, rm, color, (0.30, 0.13, 0.05))
        dirt_mask = rm if dirt_mask is None else _math(nt, 'MAXIMUM', dirt_mask, rm)

    # Emendas de chapa: linhas finas escuras + sulco no relevo
    seam_mask = None
    if seams:
        axis, spacing = seams
        comp = {'X': sep.outputs['X'], 'Y': sep.outputs['Y'], 'Z': sep.outputs['Z']}[axis]
        f = _math(nt, 'DIVIDE', comp, spacing)
        f = _math(nt, 'FRACT', f)
        d = _math(nt, 'ABSOLUTE', _math(nt, 'SUBTRACT', f, 0.5))
        seam_mask = _math(nt, 'GREATER_THAN', d, 0.5 - 0.006 / spacing * 1.0)
        color = _mix_color(nt, _math(nt, 'MULTIPLY', seam_mask, 0.55), color, (0.05, 0.05, 0.05))

    nt.links.new(color, bsdf.inputs['Base Color'])

    # Rugosidade com variação (+ sujeira deixa mais fosco)
    rv = _noise(nt, 9.0, 5.0, obj)
    rs = _math(nt, 'ADD', rough - rough_var * 0.5, _math(nt, 'MULTIPLY', rv.outputs['Fac'], rough_var))
    if dirt_mask is not None:
        rs = _math(nt, 'ADD', rs, _math(nt, 'MULTIPLY', dirt_mask, 0.35))
    rs = _math(nt, 'MINIMUM', rs, 1.0)
    nt.links.new(rs, bsdf.inputs['Roughness'])
    bsdf.inputs['Metallic'].default_value = metal

    # Relevo
    height = None
    if bump > 0:
        bn = _noise(nt, bump_scale, 12.0, obj)
        height = _math(nt, 'MULTIPLY', bn.outputs['Fac'], bump)
    if seam_mask is not None:
        sh = _math(nt, 'MULTIPLY', seam_mask, -0.6)
        height = sh if height is None else _math(nt, 'ADD', height, sh)
    if height is not None:
        b = _n(nt, 'ShaderNodeBump', 400, -400)
        _in(b, 'Strength').default_value = 0.5
        _in(b, 'Distance').default_value = 0.004
        nt.links.new(height, _in(b, 'Height'))
        nt.links.new(b.outputs['Normal'], bsdf.inputs['Normal'])

    if emission is not None:
        bsdf.inputs['Emission Color'].default_value = (*emission, 1.0)
        bsdf.inputs['Emission Strength'].default_value = emission_strength
    MATS[name] = m
    return m

# ----------------------------------------------------------------------------------- geometria
PARTS = []

def _finish(ob, mat):
    ob.data.materials.clear()
    ob.data.materials.append(mat)
    PARTS.append(ob)
    return ob

def box(x0, x1, y0, y1, z0, z1, mat, bevel=0.0, segments=2):
    me = bpy.data.meshes.new("p")
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    for v in bm.verts:
        v.co.x = (x0 + x1) / 2 + v.co.x * (x1 - x0)
        v.co.y = (y0 + y1) / 2 + v.co.y * (y1 - y0)
        v.co.z = (z0 + z1) / 2 + v.co.z * (z1 - z0)
    if bevel > 0:
        bmesh.ops.bevel(bm, geom=list(bm.edges), offset=bevel, segments=segments, affect='EDGES')
    bm.to_mesh(me)
    bm.free()
    ob = bpy.data.objects.new("p", me)
    bpy.context.collection.objects.link(ob)
    return _finish(ob, mat)

def cyl(x, y, z, r, depth, mat, axis='Z', verts=16, bevel=0.0):
    rot = {'Z': (0, 0, 0), 'X': (0, math.pi / 2, 0), 'Y': (math.pi / 2, 0, 0)}[axis]
    bpy.ops.mesh.primitive_cylinder_add(vertices=verts, radius=r, depth=depth, location=(x, y, z), rotation=rot)
    ob = bpy.context.active_object
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    if bevel > 0:
        mod = ob.modifiers.new("b", 'BEVEL')
        mod.width = bevel
        mod.segments = 2
        bpy.ops.object.modifier_apply(modifier=mod.name)
    return _finish(ob, mat)

def text(s, x, y, z, size, mat, rot=(math.pi / 2, 0, -math.pi / 2), extrude=0.004, align='CENTER'):
    """Texto em malha. rot padrão: de pé, lendo para quem olha do -X para o +X... ajuste por chamada."""
    bpy.ops.object.text_add(location=(x, y, z), rotation=rot)
    ob = bpy.context.active_object
    ob.data.body = s
    ob.data.size = size
    ob.data.extrude = extrude
    ob.data.align_x = align
    ob.data.align_y = 'CENTER'
    bpy.ops.object.convert(target='MESH')
    ob = bpy.context.active_object
    return _finish(ob, mat)

def join(objs, name):
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs: o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    if len(objs) > 1: bpy.ops.object.join()
    ob = bpy.context.active_object
    ob.name = name
    ob.data.name = name
    return ob

# ----------------------------------------------------------------------------------- bake
def _principled(m):
    for n in m.node_tree.nodes:
        if n.type == 'BSDF_PRINCIPLED': return n
    return None

def _source(nt, sock):
    """Saída ligada a um input do Principled, ou um nó de valor/cor com o default."""
    if sock.is_linked:
        return sock.links[0].from_socket
    if sock.type == 'RGBA':
        n = nt.nodes.new('ShaderNodeRGB'); n.outputs[0].default_value = sock.default_value
    else:
        n = nt.nodes.new('ShaderNodeValue'); n.outputs[0].default_value = sock.default_value
    return n.outputs[0]

def unwrap(objs, margin=0.006):
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs: o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.smart_project(angle_limit=math.radians(66), island_margin=margin, area_weight=0.0, scale_to_bounds=True)
    bpy.ops.object.mode_set(mode='OBJECT')

def bake_all(objs, out_dir, size=2048):
    os.makedirs(out_dir, exist_ok=True)
    mats = {m for o in objs for m in o.data.materials if m}
    imgs = {}
    def target(name, non_color):
        im = bpy.data.images.new(name, size, size, alpha=False, float_buffer=False)
        if non_color: im.colorspace_settings.name = 'Non-Color'
        imgs[name] = im
        for m in mats:
            nt = m.node_tree
            for n in [n for n in nt.nodes if n.name == "__bake"]: nt.nodes.remove(n)
            tn = nt.nodes.new('ShaderNodeTexImage'); tn.name = "__bake"; tn.image = im
            nt.nodes.active = tn
        return im
    def select():
        bpy.ops.object.select_all(action='DESELECT')
        for o in objs: o.select_set(True)
        bpy.context.view_layer.objects.active = objs[0]

    # Canais escalares/cor: liga a fonte do input do Principled numa Emissão e assa EMIT.
    def emit_pass(name, input_name, non_color, strength_from=None):
        im = target(name, non_color)
        saved = []
        for m in mats:
            nt = m.node_tree
            p = _principled(m)
            out = [n for n in nt.nodes if n.type == 'OUTPUT_MATERIAL'][0]
            em = nt.nodes.new('ShaderNodeEmission'); em.name = "__em"
            src = _source(nt, p.inputs[input_name])
            nt.links.new(src, em.inputs['Color'])
            if strength_from:
                em.inputs['Strength'].default_value = min(1.0, p.inputs[strength_from].default_value)
            nt.links.new(em.outputs[0], out.inputs['Surface'])
            saved.append((nt, p, out, em))
        select()
        bpy.ops.object.bake(type='EMIT')
        for nt, p, out, em in saved:
            nt.links.new(p.outputs[0], out.inputs['Surface'])
            nt.nodes.remove(em)
        im.filepath_raw = os.path.join(out_dir, name + ".png"); im.file_format = 'PNG'; im.save()

    emit_pass("base_color", 'Base Color', False)
    emit_pass("roughness", 'Roughness', True)
    emit_pass("metallic", 'Metallic', True)
    emit_pass("emission", 'Emission Color', False, strength_from='Emission Strength')
    # Normal (tangente) com o relevo do Principled
    im = target("normal", True)
    select()
    bpy.ops.object.bake(type='NORMAL', normal_space='TANGENT')
    im.filepath_raw = os.path.join(out_dir, "normal.png"); im.file_format = 'PNG'; im.save()

def collapse_materials(objs, name="Baked"):
    """Depois do bake: um ÚNICO material em todas as faces. Sem isso o FBX sai com uma submalha por
    material do Blender e o Unity só aplica a textura assada no primeiro slot (o resto fica cinza)."""
    m = bpy.data.materials.new(name)
    for o in objs:
        o.data.materials.clear()
        o.data.materials.append(m)
        for p in o.data.polygons:
            p.material_index = 0

def export_fbx(path):
    bpy.ops.object.select_all(action='SELECT')
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, apply_scale_options='FBX_SCALE_UNITS',
                             axis_forward='-Z', axis_up='Y', mesh_smooth_type='FACE', path_mode='STRIP',
                             embed_textures=False, bake_space_transform=True)
