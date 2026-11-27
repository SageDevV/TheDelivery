# The Delivery - curta 3 "A Cafeteria" (Ato 1) - PSX, Blender 5.x, rodar em background
# Uso: blender -b --factory-startup --python build_cafeteria.py -- <raw_dir> <overlay_dir> <blend_out> [render=1]
# POV da Clear (jogo em 1ª pessoa). Assets do Ato 1: cafeteria, Marina (Mixamo: andando/sentada),
# atendente, velho do café, estudante no fliperama. Falas/pensamentos dos assets Thought_Act1 / Dialogue_Act1-1.
#    1-110  acorda com a cabeça na mesa (desfocado, de lado, piscando) e levanta a cabeça
#  110-170  olha a cafeteria no fim de tarde
#  170-290  Marina entra pela porta e vem até a mesa (câmera acompanha); olhar para o laptop = elipse
#  290-505  conversa (legendas). Na fala da prova o entregador está na rua, atrás do ombro dela; depois some
#  506-560  cartela
# Legendas e pálpebras são uma cena "Overlay" (RGBA) composta no post_psx.py (sem pegar o desfoque).
import bpy, sys, os, math, random
from mathutils import Vector, Matrix

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import psx_common as P

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
RAW_DIR = argv[0] if len(argv) > 0 else "//raw_cafe"
OVL_DIR = argv[1] if len(argv) > 1 else "//ovl_cafe"
BLEND_OUT = argv[2] if len(argv) > 2 else None
DO_RENDER = (argv[3] != "0") if len(argv) > 3 else True
END = 560
TITLE = 506

for o in list(bpy.data.objects):
    bpy.data.objects.remove(o, do_unlink=True)
random.seed(3)
P.random.seed(3)
# névoa de fim de tarde: só pega o que está lá fora
P.FOG = (0.42, 0.24, 0.14, 1)
P.FOG_NEAR, P.FOG_FAR = 12.0, 45.0
P.setup_render(RAW_DIR, END)
scene = bpy.context.scene
T = P.UNITY_PROJECT + r"\Textures"

def tex(name, size=128):
    return P._psx_texture(rf"{T}\{name}\{name}_BaseColor.jpg", size)

M_WALL = P.material("CafeteriaWall", image=tex("CafeteriaWall"))
M_FLOOR = P.material("CafeteriaFloor", image=tex("CafeteriaFloor"))
M_CEIL = P.material("CafeteriaCeiling", image=tex("CafeteriaCeiling"))
M_FACADE = P.material("BuildingFacade", image=tex("BuildingFacade", 256))
M_SIDEWALK = P.material("Sidewalk", image=tex("Sidewalk"))
M_STREET = P.material("Street", image=tex("Street"))
M_FRAME = P.material("WindowFrame", color=(0.08, 0.07, 0.06))
M_NOTE = P.material("Notebook", color=(0.85, 0.82, 0.72))
M_PEN = P.material("Pen", color=(0.1, 0.15, 0.5))
M_SKY = P.emit_material("DuskSky", (1.0, 0.48, 0.22), 1.6)
M_TEXT_W = P.emit_material("TextWhite", (0.9, 0.9, 0.85), 1.0)
M_TEXT_R = P.emit_material("TextRed", (0.8, 0.02, 0.02), 1.5)

# ---------------------------------------------------------------- SALÃO  X[-4,4] Y[-3,3] pé-direito 3
H = 3.0
P.box("Floor", (0, 0, -0.05), (8, 6, 0.1), [M_FLOOR], tile=1.5)
P.box("Ceiling", (0, 0, H + 0.05), (8, 6, 0.1), [M_CEIL], tile=1.5)
P.box("Wall_North", (0, 3.075, H / 2), (8.3, 0.15, H), [M_WALL], tile=2.0)
P.box("Wall_South", (0, -3.075, H / 2), (8.3, 0.15, H), [M_WALL], tile=2.0)
P.box("Wall_West", (-4.075, 0, H / 2), (0.15, 6, H), [M_WALL], tile=2.0)
# vitrine + porta na parede leste (rua)
P.box("Storefront_Low_S", (4.075, -1.95, 0.4), (0.15, 2.1, 0.8), [M_WALL], tile=2.0)
P.box("Storefront_Low_N", (4.075, 1.95, 0.4), (0.15, 2.1, 0.8), [M_WALL], tile=2.0)
P.box("Storefront_Top", (4.075, 0, 2.75), (0.15, 6, 0.5), [M_WALL], tile=2.0)
P.box("Door_Top", (4.075, 0, 2.4), (0.15, 1.8, 0.2), [M_FRAME])
for y in (-3.0, -1.95, -0.9, 0.9, 1.95, 3.0):
    P.bevel(P.box(f"Mullion_{y}", (4.06, y, 1.4), (0.08, 0.07, 2.2), [M_FRAME]), 0.01, 1)
for nm, z, ys in [("Sill", 0.82, None), ("Transom", 2.48, None)]:
    for yc in (-1.95, 1.95):
        P.box(f"{nm}_{yc}", (4.06, yc, z), (0.08, 2.1, 0.05), [M_FRAME])
# porta de vidro aberta (folha encostada para dentro)
leaf = P.box("DoorLeaf", (3.6, 0.95, 1.12), (0.9, 0.05, 2.2), [M_FRAME])
leaf.rotation_euler = (0, 0, math.radians(-8))
P.box("DoorLeafGlass", (3.6, 0.95, 1.2), (0.75, 0.052, 1.9), [P.emit_material("Glass", (0.35, 0.3, 0.28), 0.25)])

# rua lá fora
P.box("Sidewalk", (5.6, 0, -0.04), (3.0, 40, 0.1), [M_SIDEWALK], tile=1.5)
P.box("Street", (10.0, 0, -0.1), (6.0, 40, 0.1), [M_STREET], tile=3.0)
P.box("FacadeAcross", (13.2, 0, 6), (0.3, 40, 12), [M_FACADE], tile=6.0)
P.box("Sky", (45, 0, 10), (0.2, 200, 60), [M_SKY])
for y in (-6, 4):
    P.lathe(f"StreetLamp_{y}", (6.8, y, 0), [(0.06, 0), (0.05, 3.6), (0.0, 3.62)], M_FRAME, segs=6)

# ---------------------------------------------------------------- MOBÍLIA (assets do Ato 1, escala por altura real)
def prop(name, loc, rot, height, tris=1500, obj_name=None, tex_name=None):
    return P.import_prop(name, loc, rot_z_deg=rot, height=height, max_tris=tris, obj_name=obj_name, tex_name=tex_name)

CLEAR_TABLE = (-2.0, -1.0)
prop("CafeteriaTable", (*CLEAR_TABLE, 0), 0, 0.78, obj_name="ClearTable")
prop("CafeteriaChair", (-2.65, -1.0, 0), 90, 0.88, 800, obj_name="ClearChair")
prop("CafeteriaChair", (-1.35, -1.0, 0), -90, 0.88, 800, obj_name="MarinaChair")
for i, (x, y) in enumerate([(0.9, -2.1), (2.6, 1.1), (-0.6, 1.6)]):
    prop("CafeteriaTable", (x, y, 0), 0, 0.78, obj_name=f"Table_{i:02d}")
    prop("CafeteriaChair", (x - 0.6, y, 0), 90, 0.88, 800, obj_name=f"Chair_{i:02d}_A")
    prop("CafeteriaChair", (x + 0.6, y, 0), -90, 0.88, 800, obj_name=f"Chair_{i:02d}_B")
    prop("CoffeeCup", (x + 0.08, y + 0.05, 0.78), 30, 0.1, 300, obj_name=f"Cup_{i:02d}")
prop("CafeteriaCounter", (0.6, 2.0, 0), 0, 1.05, 2500)
prop("FoodDisplay", (2.75, 2.45, 0), 0, 1.8, 1500)
prop("CafeteriaShelf_Vertical", (0.6, 2.84, 0), 0, 2.0, 1500)
prop("CafeteriaShelf_VerticalSmall", (-2.2, 2.76, 0), 0, 1.7, 1200)
prop("CafeteriaShelf_Horizontal", (-3.92, 0.6, 1.5), 90, 0.3, 800)
arcade = prop("ArcadeCabinet", (1.8, -2.62, 0), 180, 1.95, 1200)
# o FBX referencia texturas que não estão no projeto (sairia branco): gabinete escuro + tela acesa
M_ARCADE = P.material("ArcadeBody", color=(0.06, 0.05, 0.12))
for i in range(len(arcade.data.materials)):
    arcade.data.materials[i] = M_ARCADE
P.box("ArcadeScreen", (1.8, -2.36, 1.42), (0.5, 0.02, 0.38), [P.emit_material("ArcadeScreen", (0.3, 0.6, 1.0), 1.2)])
paint = prop("Paint", (-1.4, -2.98, 1.35), 180, 0.55, 100)
for i, (x, y) in enumerate([(-2.0, -1.0), (0.9, -2.1), (2.6, 1.1), (-0.6, 1.6)]):
    lamp = prop("CafeteriaLamp", (x, y, 2.35), 0, 0.42, 600, obj_name=f"Lamp_{i:02d}")
    P.lathe(f"LampCord_{i}", (x, y, 2.75), [(0.006, 0), (0.006, 0.25)], M_FRAME, segs=4)
    P.point_light(f"Light_Lamp_{i}", (x, y, 2.3), 35, (1.0, 0.72, 0.42), 0.08)
# mesa da Clear: laptop virado para ela, café, caderno, celular
prop("Laptop", (-2.2, -1.0, 0.78), -90, 0.26, 5000)
prop("CoffeeCup", (-2.05, -0.75, 0.78), 0, 0.1, 300, obj_name="ClearCup")
P.bevel(P.box("Notebook", (-2.1, -1.27, 0.785), (0.21, 0.15, 0.012), [M_NOTE]), 0.003, 1).rotation_euler = (0, 0, 0.3)
P.lathe("Pen", (-2.0, -1.3, 0.795), [(0.004, 0), (0.004, 0.13)], M_PEN, segs=4, rot=(0, math.radians(90), 0.6))
prop("Cellphone", (-1.82, -0.78, 0.78), 25, 0.012, 400)

# ---------------------------------------------------------------- LUZ: pôr do sol entrando pela vitrine
sun_d = bpy.data.lights.new("Sun", 'SUN')
sun_d.energy = 4.5
sun_d.color = (1.0, 0.58, 0.3)
sun = bpy.data.objects.new("Sun", sun_d)
P.link(sun)
sun.rotation_euler = (math.radians(-6), math.radians(78), 0)
P.area_light("Light_Fill", (0, 0, 2.9), (0, 0, 0), 120, (1.0, 0.8, 0.6), 4.0, 3.0)
P.point_light("Light_TableFill", (-2.35, -1.0, 1.25), 14, (1.0, 0.82, 0.62), 0.2)   # rosto da Marina contra a vitrine
# luz rebatida do céu na fachada da frente (o sol está atrás dela)
P.area_light("Light_FacadeBounce", (9.0, 0, 5.0), (0, math.radians(-90), 0), 9000, (1.0, 0.6, 0.38), 20.0, 10.0)

# ---------------------------------------------------------------- PERSONAGENS (Mixamo do projeto)
def face(dx, dy):
    """Rotação Z para o modelo (frente -Y) olhar na direção (dx, dy)."""
    return math.atan2(dx, -dy)

def place(arm, x, y, rz):
    arm.location = (x, y, 0)
    arm.rotation_euler[2] = rz          # mantém a rotação X que o FBX do Mixamo traz

att, att_m, att_a = P.import_character("OldLadyAttendant", 1.6)
place(att, 0.6, 2.62, face(0, -1))
P.play_action(att, att_a, 1, END, name="attendant")
old, old_m, old_a = P.import_character("OldManDrinkingCoffee", 1.72)
place(old, 1.0, 0.55, face(-1, -0.4))
P.play_action(old, old_a, 1, END, name="oldman")
stu, stu_m, stu_a = P.import_character("StudentPlaying", 1.72)
place(stu, 1.8, -2.0, face(0, -1))
P.play_action(stu, stu_a, 1, END, name="student")

# Marina andando (root motion removido; o deslocamento é do objeto, na velocidade do ciclo)
mw, mw_m, mw_a = P.import_character("Marina_Walking", 1.65, tex_name="Marina", max_tris=9000)
P.make_in_place(mw_a)
P.play_action(mw, mw_a, 150, 300, name="walk")
SPEED = 0.052                                   # m por frame a 15 fps (medido no ciclo)
path = [(4.9, 0.05), (0.1, -0.4), (-0.85, -1.0)]
f = 170
mw.location = (*path[0], 0)
for (x0, y0), (x1, y1) in zip(path[:-1], path[1:]):
    rz = face(x1 - x0, y1 - y0)
    mw.rotation_euler[2] = rz
    mw.location = (x0, y0, 0)
    mw.keyframe_insert('location', frame=f)
    mw.keyframe_insert('rotation_euler', frame=f)
    f += round(math.hypot(x1 - x0, y1 - y0) / SPEED)
    mw.location = (x1, y1, 0)
    mw.keyframe_insert('location', frame=f - 3)
    mw.keyframe_insert('rotation_euler', frame=f - 3)
mw.keyframe_insert('location', frame=f)
for fc in P.fcurves_of(mw):
    for kp in fc.keyframe_points:
        kp.interpolation = 'LINEAR'
MARINA_ARRIVES = f
for m in mw_m:
    P.key_visible(m, [(170, 289)], END)

# Marina sentada na frente da Clear
ms, ms_m, ms_a = P.import_character("Marina_Sitting", 1.65, tex_name="Marina", max_tris=9000, scale=mw.scale.x)
place(ms, -1.33, -1.0, face(-1, 0))
P.play_action(ms, ms_a, 250, END, name="sit")
for m in ms_m:
    P.key_visible(m, [(290, TITLE - 1)], END)

# o entregador, parado na rua, olhando para dentro (só durante a fala da prova)
dm, dm_body, dm_rig = P.import_deliveryman_rigged()
place(dm, 6.6, -0.55, face(-1, 0))
dm_rig.key(1, P.merge(P.pose_idle(slouch=6, head_down=4), {"Head": [('Y', 10)]}))
P.key_visible(dm_body, [(388, 432)], END)

# ---------------------------------------------------------------- CÂMERA = olhos da Clear
look = P.empty("Look")
headobj = P.empty("ClearHead")
c_look = headobj.constraints.new('TRACK_TO')
c_look.target = look
c_walk = headobj.constraints.new('TRACK_TO')
c_walk.target, c_walk.subtarget = mw, "mixamorig:Head"
c_sit = headobj.constraints.new('TRACK_TO')
c_sit.target, c_sit.subtarget = ms, "mixamorig:Head"
for c in (c_look, c_walk, c_sit):
    c.track_axis, c.up_axis = 'TRACK_NEGATIVE_Z', 'UP_Y'

cam = P.camera("Cam_ClearPOV", (0, 0, 0), 22, 0.02)
cam.parent = headobj
cam.location = (0, 0, 0)

# cabeça: deitada na mesa -> levanta
for fr, loc in [(1, (-2.5, -1.05, 0.86)), (82, (-2.5, -1.05, 0.86)), (100, (-2.62, -1.0, 1.1)), (108, (-2.68, -1.0, 1.18))]:
    headobj.location = loc
    headobj.keyframe_insert('location', frame=fr)
P.add_noise(headobj, 'location', 0.006, 14)
# rolagem (cabeça de lado na mesa)
for fr, deg in [(1, 78), (82, 76), (100, 8), (108, 0)]:
    cam.rotation_euler = (0, 0, math.radians(deg))
    cam.keyframe_insert('rotation_euler', frame=fr)

looks = [(1, (-1.4, -1.15, 0.8)), (82, (-1.4, -1.1, 0.82)), (98, (-2.25, -1.0, 0.82)), (106, (-2.25, -1.0, 0.82)),
         (118, (1.0, 0.1, 1.3)), (132, (0.6, 2.6, 1.5)), (146, (1.0, 0.55, 1.55)), (160, (1.8, -2.0, 1.4)),
         (172, (4.0, 0.05, 1.45)), (272, (-2.25, -1.0, 0.82)), (290, (-2.25, -1.0, 0.82)),
         (400, (-1.33, -1.0, 1.3)), (410, (6.6, -0.55, 1.6)), (424, (6.6, -0.55, 1.6)),
         (438, (6.6, -0.55, 1.5)), (452, (6.6, -0.55, 1.5))]
for fr, loc in looks:
    look.location = loc
    look.keyframe_insert('location', frame=fr)
# quem a câmera segue: (frame, look, marina andando, marina sentada)
infl = [(1, 1, 0, 0), (172, 1, 0, 0), (178, 1, 1, 0), (270, 1, 1, 0), (278, 1, 0, 0), (290, 1, 0, 0),
        (298, 1, 0, 1), (404, 1, 0, 1), (416, 1, 0, 0.75), (430, 1, 0, 1), (440, 1, 0, 1), (446, 1, 0, 0),
        (454, 1, 0, 0), (462, 1, 0, 1), (END, 1, 0, 1)]
for fr, a, b, c in infl:
    for con, v in ((c_look, a), (c_walk, b), (c_sit, c)):
        con.influence = v
        con.keyframe_insert('influence', frame=fr)

# foco: acorda desfocada e vai focando
cam.data.dof.use_dof = True
for fr, fstop, dist in [(1, 0.25, 0.4), (60, 0.35, 0.6), (100, 0.9, 1.5), (125, 4.0, 3.0), (170, 5.6, 4.0),
                        (300, 5.6, 0.9), (410, 4.0, 3.0), (430, 5.6, 0.9)]:
    cam.data.dof.aperture_fstop = fstop
    cam.data.dof.focus_distance = dist
    cam.data.dof.keyframe_insert('aperture_fstop', frame=fr)
    cam.data.dof.keyframe_insert('focus_distance', frame=fr)

# cartela
camE = P.camera("Cam_Title", (100, 4, 1.0), 50)
camE.rotation_euler = (math.pi / 2, 0, math.pi)
P.text("Text_Line", "Um dia comum.", (100, 0, 1.15), 0.16, M_TEXT_W, TITLE + 6)
P.text("Text_Title", "THE DELIVERY", (100, 0, 0.8), 0.3, M_TEXT_R, TITLE + 22)
scene.camera = cam
scene.timeline_markers.new("POV", frame=1).camera = cam
scene.timeline_markers.new("Title", frame=TITLE).camera = camE

# ---------------------------------------------------------------- OVERLAY: pálpebras + legendas (tela 4 x 3 unidades)
ovl = bpy.data.scenes.new("Overlay")
ovl.frame_start, ovl.frame_end = 1, END
ovl.render.fps = 15
ovl.render.engine = 'BLENDER_EEVEE'
ovl.render.resolution_x, ovl.render.resolution_y = 320, 240
ovl.render.film_transparent = True
ovl.view_settings.view_transform = 'Standard'
ovl.render.image_settings.file_format = 'PNG'
ovl.render.image_settings.color_mode = 'RGBA'
ovl.render.filepath = OVL_DIR.rstrip("/\\") + "/f_####"
try:
    ovl.render.filter_size = 0.0
except Exception:
    pass

def to_ovl(ob):
    for c in list(ob.users_collection):
        c.objects.unlink(ob)
    ovl.collection.objects.link(ob)
    return ob

oc = bpy.data.cameras.new("OverlayCam")
oc.type = 'ORTHO'
oc.ortho_scale = 4.0
ocam = bpy.data.objects.new("OverlayCam", oc)
ovl.collection.objects.link(ocam)
ocam.location = (0, 0, 10)
ovl.camera = ocam

M_LID = P.emit_material("Eyelid", (0, 0, 0), 0.0)
lids = []
for sgn in (1, -1):
    lid = to_ovl(P.blob(f"Eyelid_{'Top' if sgn > 0 else 'Bottom'}", (0, 0, 0), (3.2, 1.6, 0.01), M_LID, subdiv=3))
    lids.append((lid, sgn))
blinks = [(1, 0), (24, 0), (28, 0.35), (31, 0.05), (38, 0.5), (41, 0.0), (50, 0.7), (54, 0.4), (64, 1.0),
          (199, 1.0), (200, 0.0), (202, 1.0), (359, 1.0), (360, 0.0), (362, 1.0), (471, 1.0), (472, 0.1),
          (474, 1.0), (END, 1.0)]
for fr, o in blinks:
    for lid, sgn in lids:
        lid.location = (0, sgn * (1.6 + 1.55 * o), 0)
        lid.keyframe_insert('location', frame=fr)

def subtitle(text, f0, f1, color):
    mat = P.emit_material(f"Sub_{f0}", color, 1.0)
    for dx, dy, m, nm in ((0.022, -0.022, M_LID, "Shadow"), (0, 0, mat, "Text")):
        cu = bpy.data.curves.new(f"Sub_{f0}_{nm}", 'FONT')
        cu.body = text
        cu.align_x, cu.align_y = 'CENTER', 'CENTER'
        cu.size = 0.15
        cu.space_line = 1.05
        cu.materials.append(m)
        ob = bpy.data.objects.new(f"Sub_{f0}_{nm}", cu)
        ovl.collection.objects.link(ob)
        ob.location = (dx, -1.08 + dy, 0.5 if nm == "Text" else 0.4)
        P.key_visible(ob, [(f0, f1)], END)

THOUGHT, MARINA, CLEAR = (0.72, 0.78, 0.9), (1.0, 0.82, 0.48), (0.95, 0.95, 0.92)
subtitle("Que sonho foi esse...", 6, 40, THOUGHT)
subtitle("Foi só um sonho... só um sonho.", 46, 88, THOUGHT)
subtitle("Já é fim de tarde...\ndormi estudando de novo.", 118, 166, THOUGHT)
subtitle("Marina: Oi! Desculpa o atraso,\no ônibus demorou uma eternidade.", 294, 336, MARINA)
subtitle("Clear: Tranquilo. Tava só revisando pra\nprova de algoritmos. Acabei cochilando.", 338, 386, CLEAR)
subtitle("Marina: Aquela prova vai ser um massacre.\nVocê entendeu recursão?", 388, 432, MARINA)
subtitle("Clear: Mais ou menos.\nAs árvores binárias ainda me confundem.", 434, 476, CLEAR)
subtitle("Minha cabeça está explodindo...", 480, 504, THOUGHT)

if BLEND_OUT:
    bpy.ops.wm.save_as_mainfile(filepath=BLEND_OUT)
    print("[Cafe] blend salvo:", BLEND_OUT)
if DO_RENDER:
    bpy.ops.render.render(animation=True, scene=ovl.name)
    print("[Cafe] overlay ok")
    bpy.ops.render.render(animation=True, scene=scene.name)
    print("[Cafe] render ok")
