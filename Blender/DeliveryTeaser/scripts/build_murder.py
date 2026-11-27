# The Delivery - curta 2 "O Banheiro" (PSX, Blender 5.x, rodar em background)
# Uso: blender -b --factory-startup --python build_murder.py -- <raw_frames_dir> <blend_out> [render=1]
# POV da Clear (o jogo é em 1ª pessoa, não existe modelo dela). Violência implícita (pilar "violência mínima").
#  A   1-110  escondida no box: luz sob a porta, sombras de pés, maçaneta chacoalha, porta abre, ele no vão
#  C 111-150  apagões: a cada clarão ele está mais perto; faca erguida; 1 quadro vermelho; preto
#  D 151-200  câmera caída no chão: ele sai de costas, poça escura se espalha, porta fecha, luz apaga
#  E 201-250  cartela com a fala do Andre + THE DELIVERY
import bpy, sys, os, math, random
import numpy as np
from mathutils import Vector, Matrix

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import psx_common as P

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
RAW_DIR = argv[0] if len(argv) > 0 else "//raw_murder"
BLEND_OUT = argv[1] if len(argv) > 1 else None
DO_RENDER = (argv[2] != "0") if len(argv) > 2 else True
END = 250

for o in list(bpy.data.objects):
    bpy.data.objects.remove(o, do_unlink=True)
random.seed(11)
P.random.seed(11)
P.setup_render(RAW_DIR, END)
scene = bpy.context.scene

# ---------------------------------------------------------------- texturas do banheiro
def tex_tiles():
    n = 32
    a = np.zeros((n, n, 3)) + np.array([0.62, 0.68, 0.62])     # azulejo verde-água encardido
    a += P.noise(n, n, 0.05)
    a[::8, :] = np.array([0.30, 0.30, 0.26])                   # rejunte
    a[:, ::8] = np.array([0.30, 0.30, 0.26])
    for _ in range(5):                                          # mofo
        cy, cx = P.rng.integers(0, n), P.rng.integers(0, n)
        yy, xx = np.ogrid[:n, :n]
        m = ((yy - cy) ** 2 + (xx - cx) ** 2) < P.rng.integers(3, 14)
        a[m] *= 0.7
    return a

def tex_bath_floor():
    n = 32
    yy, xx = np.mgrid[:n, :n]
    chk = ((yy // 4 + xx // 4) % 2)[..., None]
    a = np.where(chk, np.array([0.55, 0.55, 0.5]), np.array([0.12, 0.12, 0.13]))
    return a + P.noise(n, n, 0.07)

M_TILE = P.material("BathTiles", image=P.make_image("BathTiles", tex_tiles()))
M_BFLOOR = P.material("BathFloor", image=P.make_image("BathFloor", tex_bath_floor()))
M_WALL = P.material("Wallpaper", image=P.make_image("Wallpaper", P.tex_wallpaper()))
M_WOOD_FLOOR = P.material("WoodFloor", image=P.make_image("WoodFloor", P.tex_wood_floor()))
M_DOOR = P.material("DoorWood", image=P.make_image("DoorWood", P.tex_door()))
M_CEIL = P.material("Ceiling", color=(0.35, 0.34, 0.30))
M_WHITE = P.material("Enamel", color=(0.75, 0.75, 0.72))
M_CURTAIN = P.material("ShowerCurtain", color=(0.55, 0.62, 0.55))
M_TOWEL = P.material("Towel", color=(0.55, 0.35, 0.38))
M_METAL = P.material("Chrome", color=(0.5, 0.5, 0.5))
M_DARK = P.material("Dark", color=(0.03, 0.03, 0.03))
M_FRAME = P.material("Frame", color=(0.25, 0.15, 0.08))
M_BLOOD = P.set_roughness(P.material("Blood", color=(0.10, 0.0, 0.0)), 0.12, 1.0)
M_SHADOW = P.emit_material("FeetShadow", (0, 0, 0), 0.0)
M_STRIP = P.emit_material("DoorGapLight", (1.0, 0.85, 0.55), 1.4)
M_FROST = P.emit_material("FrostedWindow", (0.35, 0.45, 0.9), 0.8)
M_BULB = P.emit_material("Bulb", (1.0, 0.8, 0.5), 8.0)
M_FLASH = P.emit_material("RedFlash", (0.55, 0.0, 0.0), 1.0)
M_TEXT_W = P.emit_material("TextWhite", (0.9, 0.9, 0.85), 1.0)
M_TEXT_R = P.emit_material("TextRed", (0.8, 0.02, 0.02), 1.5)

# ---------------------------------------------------------------- BANHEIRO  X[-1.1,1.1] Y[-1.3,1.3] pé-direito 2.4
H = 2.4
P.box("Bath_Floor", (0, 0, -0.05), (2.2, 2.6, 0.1), [M_BFLOOR], tile=0.5)
P.box("Bath_Ceiling", (0, 0, H + 0.05), (2.2, 2.6, 0.1), [M_CEIL])
P.box("Wall_North", (0, 1.375, H / 2), (2.5, 0.15, H), [M_TILE], tile=0.8)
P.box("Wall_East", (1.175, 0, H / 2), (0.15, 2.6, H), [M_TILE], tile=0.8)
P.box("Wall_West", (-1.175, 0, H / 2), (0.15, 2.6, H), [M_TILE], tile=0.8)
# parede sul com vão de porta x[-0.05, 0.75]
P.box("Wall_South_L", (-0.6, -1.375, H / 2), (1.1, 0.15, H), [M_TILE], tile=0.8)
P.box("Wall_South_R", (0.925, -1.375, H / 2), (0.35, 0.15, H), [M_TILE], tile=0.8)
P.box("Wall_South_Top", (0.35, -1.375, 2.22), (0.8, 0.15, 0.36), [M_TILE], tile=0.8)
for nm, c, s in [("DoorFrame_L", (-0.08, -1.29, 1.02), (0.06, 0.04, 2.08)),
                 ("DoorFrame_R", (0.78, -1.29, 1.02), (0.06, 0.04, 2.08)),
                 ("DoorFrame_Top", (0.35, -1.29, 2.06), (0.92, 0.04, 0.06))]:
    P.bevel(P.box(nm, c, s, [M_DOOR]), 0.01, 2)

# janelinha basculante (luar) no alto da parede norte, atrás da câmera
P.box("FrostedWindow", (-0.4, 1.295, 2.0), (0.5, 0.01, 0.32), [M_FROST])
P.area_light("Light_Moon", (-0.4, 1.15, 2.0), (math.radians(-65), 0, 0), 12, (0.45, 0.55, 1.0), 0.5, 0.32)

# box do chuveiro (onde ela se esconde)
P.bevel(P.box("ShowerTray", (0, 0.9, 0.03), (2.2, 0.8, 0.06), [M_WHITE]), 0.02, 2)
P.lathe("ShowerDrain", (0.0, 0.95, 0.061), [(0, 0), (0.04, 0), (0, 0.002)], M_DARK, segs=8)
P.lathe("CurtainRod", (-1.1, 0.5, 2.1), [(0.01, 0), (0.01, 2.2)], M_METAL, segs=6, rot=(0, math.radians(90), 0))
curtain = P.wavy_curtain("ShowerCurtain", (-0.62, 0.5, 0.12), 0.95, 1.98, 4, 0.05, M_CURTAIN)
curtain.rotation_euler = (0, 0, math.radians(90))
P.lathe("ShowerHead", (0.3, 1.25, 1.9), [(0, 0), (0.05, 0), (0.035, 0.05), (0.012, 0.08), (0, 0.09)], M_METAL, segs=8,
        rot=(math.radians(-120), 0, 0))

# props do projeto Unity
P.import_prop("BathroomSink", (-0.82, -0.3, 0), rot_z_deg=90, scale=0.8, max_tris=1500)
P.import_prop("Mirror", (-1.09, -0.3, 1.2), rot_z_deg=90, scale=0.6, max_tris=600)
toilet = P.import_prop("Toilet", (0.85, -0.35, 0), rot_z_deg=-90, scale=1.8, max_tris=1200)
# o FBX traz o suporte de papel como peça solta ao lado do vaso (pensado para uma parede lateral);
# aqui ele vai para a parede leste, ao lado do vaso: placa (lado +X da peça) encostada em x=1.1
for part in P.split_loose_parts(toilet)[1:]:
    part.rotation_euler = (0, 0, 0)
    part.scale = (1.8, 1.8, 1.8)
    part.location = (1.1 - 1.8 * 0.14, -0.85 + 1.8 * 0.143, 0.62 - 1.8 * 0.3275)
    part.name = "ToiletPaperHolder"
bathmat = P.bevel(P.box("BathMat", (0.0, -0.2, 0.008), (0.5, 0.8, 0.016), [M_TOWEL]), 0.006, 1)
P.key_visible(bathmat, [(1, 150)], END)      # sai do caminho da câmera caída
towel = P.blob("Towel", (1.07, -0.95, 1.25), (0.04, 0.18, 0.3), M_TOWEL, subdiv=2, jit=0.03)
for v in towel.data.vertices:
    v.co.y *= 0.7 + 0.3 * (1 - (v.co.z + 0.3) / 0.6)
P.lathe("TowelHook", (1.1, -0.95, 1.52), [(0.012, 0), (0.012, 0.05)], M_METAL, segs=6, rot=(0, math.radians(-90), 0))
P.lathe("TrashBin", (0.85, 0.25, 0), [(0, 0), (0.1, 0), (0.12, 0.3), (0.11, 0.3), (0, 0.29)], M_WHITE, segs=8)
P.lathe("CeilingFixture", (0, -0.2, 2.34), [(0, 0), (0.16, 0.02), (0.15, 0.06), (0, 0.06)], M_WHITE, segs=10,
        rot=(math.radians(180), 0, 0))

# ---------------------------------------------------------------- CORREDOR DO APÊ (atrás da porta)
P.box("Hall_Floor", (0.0, -2.5, -0.05), (2.6, 2.3, 0.1), [M_WOOD_FLOOR])
P.box("Hall_Ceiling", (0.0, -2.5, 2.45), (2.6, 2.3, 0.1), [M_CEIL])
P.box("Hall_Wall_W", (-1.35, -2.5, 1.2), (0.1, 2.3, 2.4), [M_WALL])
P.box("Hall_Wall_E", (1.35, -2.5, 1.2), (0.1, 2.3, 2.4), [M_WALL])
P.box("Hall_Wall_Back", (0, -3.65, 1.2), (2.6, 0.1, 2.4), [M_WALL])
P.bevel(P.box("PictureFrame", (-0.3, -3.59, 1.5), (0.5, 0.03, 0.38), [M_FRAME]), 0.01, 1)
P.box("Picture", (-0.3, -3.575, 1.5), (0.42, 0.01, 0.3), [P.material("PictureArt", color=(0.25, 0.3, 0.35))])
P.lathe("BulbCord", (0.35, -2.6, 2.1), [(0.005, 0), (0.005, 0.35)], M_DARK, segs=4)
bulb = P.blob("Bulb", (0.35, -2.6, 2.06), (0.05, 0.05, 0.07), M_BULB, subdiv=1)

# ---------------------------------------------------------------- luz do corredor (todas as viradas da noite)
hall_k = {}
for f in range(1, END + 1):
    hall_k[f] = 1.0
for f in range(98, 111):
    hall_k[f] = 1.0 if random.random() > 0.3 else 0.15
for f in list(range(111, 115)) + list(range(124, 128)) + list(range(141, 151)) + list(range(200, END + 1)):
    hall_k[f] = 0.0
for f in range(151, 200):
    hall_k[f] = 1.0 if random.random() > 0.1 else 0.3
hall_light = P.point_light("Light_Hall", (0.35, -2.6, 1.95), 70, (1.0, 0.8, 0.5), 0.05)
bulb_em = M_BULB.node_tree.nodes[M_BULB["em"]]
strip_em = M_STRIP.node_tree.nodes[M_STRIP["em"]]
for f in range(1, END + 1):
    hall_light.data.energy = 70 * hall_k[f]
    hall_light.data.keyframe_insert('energy', frame=f)
    bulb_em.inputs['Strength'].default_value = 8 * hall_k[f] + 0.02
    bulb_em.inputs['Strength'].keyframe_insert('default_value', frame=f)
P.set_constant(hall_light.data)
P.set_constant(M_BULB.node_tree)

# ---------------------------------------------------------------- PORTA (dobradiça em x=0.75, abre para dentro)
hinge = P.empty("DoorHinge", (0.75, -1.3, 0))
door = P.box("BathDoor", (-0.4, 0, 1.02), (0.78, 0.045, 2.02), [M_DOOR], parent=hinge)
P.bevel(door, 0.006, 1)
P.lathe("BathDoorKnob", (-0.7, 0.04, 1.0), [(0, 0), (0.022, 0.0), (0.028, 0.035), (0, 0.055)], M_METAL, segs=6,
        parent=hinge, rot=(math.radians(-90), 0, 0))
door_keys = [(1, 0), (44, 0), (45, -1.8), (47, 0), (49, -1.8), (51, 0), (62, 0), (80, -78), (193, -78), (200, 0)]
for f, deg in door_keys:
    hinge.rotation_euler = (0, 0, math.radians(deg))
    hinge.keyframe_insert('rotation_euler', frame=f)
for fc in P.fcurves_of(hinge):            # chacoalhar em degraus; abrir/fechar suave
    for kp in fc.keyframe_points:
        kp.interpolation = 'CONSTANT' if 44 <= kp.co.x < 62 else 'BEZIER'

# luz vazando sob a porta + sombras de pés (eco do teaser)
strip = P.box("DoorGapStrip", (0.35, -1.25, 0.002), (0.76, 0.07, 0.002), [M_STRIP])
P.key_visible(strip, [(1, 62)], END)
feet = [P.box("FeetShadow_L", (0.24, -1.23, 0.004), (0.12, 0.09, 0.003), [M_SHADOW]),
        P.box("FeetShadow_R", (0.45, -1.23, 0.004), (0.12, 0.09, 0.003), [M_SHADOW])]
for ob in feet:
    P.key_visible(ob, [(28, 62)], END)

# ---------------------------------------------------------------- ENTREGADOR (modelo do projeto, riggado)
arm, body, rig = P.import_deliveryman_rigged()

def over(base, patch):
    """Substitui (não soma) as listas dos ossos em patch."""
    d = dict(base)
    d.update(patch)
    return d

idle = P.pose_idle

# faca presa ao osso da mão direita (armature ainda em repouso aqui)
def make_knife(name, bloody=False):
    k = P.knife(name)
    P.attach_to_bone(k, arm, "Hand.R",
                     Matrix.Translation((-0.775, 0.02, 1.366)) @ Matrix.Rotation(math.radians(-90), 4, 'X'))  # cabo atravessando o punho, lâmina para frente
    if bloody:
        for ch in k.children:
            if ch.name.endswith("_Blade"):
                ch.data.materials[0] = M_BLOOD
    return k

knife_clean = make_knife("Knife")
knife_bloody = make_knife("Knife_Bloody", bloody=True)

# ---- deslocamento do corpo (objeto armature). jump=True: teletransporte (só acontece no escuro)
root_keys = []
def key_root(f, x, y, rz, dz=0.0, jump=False):
    arm.location = (x, y, dz)
    arm.rotation_euler = (0, 0, rz)
    arm.keyframe_insert('location', frame=f)
    arm.keyframe_insert('rotation_euler', frame=f)
    root_keys.append((f, jump))

def key_bob(bobs, x0, y0, x1, y1, rz, f0, f1):
    for f, dz in bobs:
        t = (f - f0) / (f1 - f0)
        key_root(f, x0 + (x1 - x0) * t, y0 + (y1 - y0) * t, rz, dz)

PI = math.pi
# ===== A: no vão da porta (ele empurra a porta, respira, inclina a cabeça devagar)
push = {"UpperArm.L": [('Z', -75), ('X', 10)], "Forearm.L": [('Z', -10)], "Hand.L": [('X', -20)]}
key_root(61, 0.35, -1.85, PI)
rig.key(61, over(idle(head_down=12), push))
rig.key(68, over(idle(head_down=12, breath=1), {"UpperArm.L": [('Z', -82), ('X', 4)], "Forearm.L": [('Z', -4)]}))
rig.key(77, over(idle(head_down=14, breath=-1), {"UpperArm.L": [('Y', 45), ('Z', -30)], "Forearm.L": [('Z', -20)]}))
rig.key(85, idle(head_down=16, breath=1))
rig.key(93, over(idle(head_down=14, breath=-1), {"Head": [('X', 7), ('Y', 8)]}))
rig.key(101, over(idle(head_down=14, breath=1), {"Head": [('X', 8), ('Y', 20), ('Z', 6)]}))
rig.key(104, over(idle(head_down=14, breath=0), {"Head": [('X', 8), ('Y', 22), ('Z', 6)],
                                                  "Hand.R": [('Y', -10), ('X', 28)]}))   # a mão da faca aperta
rig.key(108, over(idle(head_down=14, breath=-1), {"Head": [('X', 9), ('Y', 23), ('Z', 5)]}))
key_root(110, 0.35, -1.85, PI, jump=True)

# ===== C: apagão -> já está no meio do banheiro, dá um passo lento na direção dela
look_down = {"Neck": [('X', 8)], "Head": [('X', 14), ('Y', 8)]}
key_root(115, 0.42, -0.85, PI)
bobs = P.anim_walk(rig, 115, 123, "L", stride=0.7, extra=look_down, bob=0.02)
key_bob(bobs, 0.42, -0.85, 0.45, -0.5, PI, 115, 123)
key_root(127, 0.45, -0.5, PI, jump=True)

# apagão -> em cima dela. Antecipação lenta, segura tremendo, golpe rápido (corte no vermelho)
key_root(128, 0.52, 0.34, PI + 0.15)
loom = over(idle(slouch=14, head_down=22), {"Head": [('X', 14), ('Y', 10)]})
rig.key(128, loom)
rig.key(131, over(loom, {"Shoulder.R": [('Y', 8)], "UpperArm.R": [('Y', -30)], "Forearm.R": [('Z', 45)],
                         "Chest": [('X', 2)]}))
raised = over(loom, {"Shoulder.R": [('Y', 18)], "UpperArm.R": [('Y', 68), ('X', 58)], "Forearm.R": [('Z', 55)],
                     "Hand.R": [('X', -20)], "Chest": [('X', -8)], "Spine": [('X', -3)],
                     "Head": [('X', 22), ('Y', 12)]})
rig.key(136, raised)
rig.key(138, over(raised, {"UpperArm.R": [('Y', 70), ('X', 56)], "Head": [('X', 23), ('Y', 14)]}))
rig.key(139, over(raised, {"UpperArm.R": [('Y', 72), ('X', 53)], "Chest": [('X', -10)]}))
rig.key(140, over(loom, {"UpperArm.R": [('Z', 90), ('X', 50)], "Forearm.R": [('Z', 10)], "Hand.R": [('X', -10)],
                         "Chest": [('X', 18)], "Spine": [('X', 12)], "Head": [('X', 26)]}))
key_root(139, 0.52, 0.34, PI + 0.15)
key_root(140, 0.53, 0.44, PI + 0.15, dz=-0.05)

# ===== D: SEM corte de continuidade. Ele continua onde golpeou, se recompõe olhando para ela,
# vira no lugar e só então vai embora (olhando para trás no meio do caminho)
SX, SY, SRZ = 0.53, 0.44, PI + 0.15
stand_over = over(idle(slouch=16, head_down=26, breath=1), {"Head": [('X', 16), ('Y', 6)]})
rig.key(145, over(stand_over, {"UpperArm.R": [('Y', -60), ('X', -15)], "Forearm.R": [('Z', 35)],
                         "Chest": [('X', 12)], "Spine": [('X', 8)], "Head": [('X', 26)]}))   # braço volta
key_root(146, SX, SY, SRZ, dz=-0.02)
rig.key(151, stand_over)
key_root(151, SX, SY, SRZ)
rig.key(155, over(idle(slouch=16, head_down=26, breath=-1), {"Head": [('X', 17), ('Y', 9)]}))
rig.key(159, over(idle(slouch=14, head_down=24, breath=1), {"Head": [('X', 16), ('Y', 14), ('Z', -5)]}))
# vira no lugar (pés trocam de apoio), ainda com a cabeça baixa
key_root(161, SX, SY, SRZ)
rig.key(161, idle(slouch=12, head_down=14, breath=0))
rig.key(164, P.merge(idle(slouch=12, head_down=12), P.walk_phase("passing", "L", 0.5)))
rig.key(167, P.merge(idle(slouch=12, head_down=10), P.walk_phase("contact", "R", 0.4)))
key_root(167, SX - 0.03, SY - 0.05, 2 * PI)
# caminhada até a porta; no segundo trecho ele olha por cima do ombro para ela
MX, MY = 0.45, -0.75
bobs = P.anim_walk(rig, 168, 182, "R", stride=0.9, bob=0.03)
key_bob(bobs, SX - 0.03, SY - 0.05, MX, MY, 2 * PI, 168, 182)
glance = {"Neck": [('Z', 16)], "Head": [('Z', 30), ('X', 6)], "Chest": [('Z', 6)]}
bobs = P.anim_walk(rig, 182, 196, "R", stride=0.8, extra=glance, bob=0.03)
key_bob(bobs, MX, MY, 0.36, -2.2, 2 * PI, 182, 196)
rig.key(200, idle(slouch=12, head_down=10))

# interpolação: Bezier nos ossos (suave); teletransportes em degrau; caminhada linear no chão
loc_fcs = [fc for fc in P.fcurves_of(arm) if fc.data_path in ('location', 'rotation_euler')]
jumps = {f for f, j in root_keys if j}
for fc in loc_fcs:
    for kp in fc.keyframe_points:
        kp.interpolation = 'CONSTANT' if int(round(kp.co.x)) in jumps else 'LINEAR'
P.add_noise(arm, 'pose.bones["Head"].rotation_quaternion', 0.006, 6)    # micro-movimento vivo
P.add_noise(arm, 'pose.bones["Chest"].rotation_quaternion', 0.004, 10)

P.key_visible(body, [(61, 200)], END)          # nunca some depois que aparece na porta
for k, ranges in [(knife_clean, [(61, 141)]), (knife_bloody, [(142, 200)])]:   # troca no escuro
    for ch in k.children:
        P.key_visible(ch, ranges, END)

# ---------------------------------------------------------------- SANGUE (só a poça e rastros; nada explícito)
def growing_pool(name, loc, radii, f0, f1, step=3):
    pool = P.blob(name, loc, (radii[0], radii[1], 0.004), M_BLOOD, subdiv=2)
    for v in pool.data.vertices:
        d = (random.random() - 0.5) * 0.25
        v.co.x *= 1 + d
        v.co.y *= 1 + d
    n = max(1, (f1 - f0) // step)
    for i, f in enumerate(range(f0, f1 + 1, step)):
        s = 0.12 + 0.88 * (1 - (1 - min(1.0, i / n)) ** 2)
        pool.scale = (s, s, 1)
        pool.keyframe_insert('scale', frame=f)
    P.set_constant(pool)
    P.key_visible(pool, [(f0, END)], END)
    return pool

growing_pool("BloodPool", (0.4, 0.76, 0.064), (0.36, 0.2), 142, 200)       # sobre o piso do box, onde ela está
growing_pool("BloodSpill", (0.38, 0.42, 0.004), (0.14, 0.1), 176, 200)     # escorre do degrau para o chão
for i, (x, y, f) in enumerate([(0.5, 0.05, 172), (0.46, -0.45, 178), (0.42, -0.95, 185), (0.38, -1.45, 191)]):
    st = P.blob(f"BloodStep_{i}", (x, y, 0.003), (0.05, 0.11, 0.002), M_BLOOD, subdiv=1, jit=0.02)
    P.key_visible(st, [(f, END)], END)

# ---------------------------------------------------------------- CÂMERAS
def track(cam, target):
    c = cam.constraints.new('TRACK_TO')
    c.target = target
    c.track_axis = 'TRACK_NEGATIVE_Z'
    c.up_axis = 'UP_Y'

# A: agachada no box, espiando pela fresta da cortina
camA = P.camera("Cam_A_Hiding", (0.58, 1.0, 0.62), 20)
tgtA = P.empty("Cam_A_Target")
track(camA, tgtA)
for f, loc in [(1, (0.35, -1.3, 0.6)), (22, (0.35, -1.3, 0.2)), (40, (0.35, -1.3, 0.25)), (46, (0.0, -1.25, 0.95)),
               (60, (0.05, -1.25, 0.95)), (72, (0.35, -1.6, 1.1)), (92, (0.35, -1.7, 1.5)), (110, (0.35, -1.7, 1.55))]:
    tgtA.location = loc
    tgtA.keyframe_insert('location', frame=f)
camA.keyframe_insert('location', frame=1)
P.add_noise(camA, 'location', 0.012, 10)          # respiração curta
P.add_noise(tgtA, 'location', 0.03, 7, 5)

# C: pânico; mira salta a cada clarão
camC = P.camera("Cam_C_Panic", (0.58, 1.02, 0.6), 16)
tgtC = P.empty("Cam_C_Target")
track(camC, tgtC)
for f, loc in [(111, (0.42, -0.8, 1.3)), (123, (0.45, -0.45, 1.3)), (127, (0.45, -0.45, 1.3)), (128, (0.55, 0.36, 1.7)),
               (134, (0.6, 0.36, 1.95)), (138, (0.6, 0.36, 2.0)), (140, (0.5, 0.4, 1.45))]:
    tgtC.location = loc
    tgtC.keyframe_insert('location', frame=f)
camC.keyframe_insert('location', frame=111)
P.add_noise(camC, 'location', 0.03, 3)
P.add_noise(tgtC, 'location', 0.06, 4, 2)
flash = P.box("RedFlash", (0, 0, -0.08), (0.5, 0.5, 0.001), [M_FLASH], parent=camC)
P.key_visible(flash, [(141, 141)], END)

# D: caída de lado no chão, olhando para a porta
camD = P.camera("Cam_D_Floor", (0.4, 1.05, 0.15), 18, 0.01)   # caída no piso do box
fwd = Vector((0.04, -1.0, 0.02)).normalized()
up0 = (Vector((0, 0, 1)) - fwd * fwd.dot(Vector((0, 0, 1)))).normalized()
side_v = fwd.cross(up0)
up = (up0 * math.cos(math.radians(65)) + side_v * math.sin(math.radians(65))).normalized()
x_axis = up.cross(-fwd)
camD.rotation_euler = Matrix((x_axis, up, -fwd)).transposed().to_euler()
camD.keyframe_insert('location', frame=151)
P.add_noise(camD, 'location', 0.003, 20)          # respiração sumindo

# E: cartela
camE = P.camera("Cam_E_Title", (50, 4, 1.0), 50)
camE.rotation_euler = (math.pi / 2, 0, math.pi)
P.text("Text_Line1", "Sempre começa igual.", (50, 0, 1.3), 0.15, M_TEXT_W, 205)
P.text("Text_Line2", "Um dia comum. Alguém indo pra casa.", (50, 0, 1.08), 0.11, M_TEXT_W, 214)
P.text("Text_Title", "THE DELIVERY", (50, 0, 0.75), 0.3, M_TEXT_R, 228)

scene.camera = camA
for name, f, cam in [("A", 1, camA), ("C", 111, camC), ("D", 151, camD), ("E", 201, camE)]:
    scene.timeline_markers.new(name, frame=f).camera = cam

if BLEND_OUT:
    bpy.ops.wm.save_as_mainfile(filepath=BLEND_OUT)
    print("[Murder] blend salvo:", BLEND_OUT)
if DO_RENDER:
    bpy.ops.render.render(animation=True)
    print("[Murder] render ok")
