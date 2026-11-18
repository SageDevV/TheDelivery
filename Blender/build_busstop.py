"""Abrigo de ponto de ônibus REALISTA para o TheDelivery (Blender, background) — geometria por script +
materiais procedurais com desgaste assados em PBR (ver realistic_kit.py).
Z para cima. Abrigo de 3,8 m ao longo do X; FUNDO (vidro + banco) no +Y, lado ABERTO (rua) no -Y.
A placa fica num poste próprio, à esquerda (-X), virada para o trânsito (disco no plano YZ).
Uso: blender --background --python build_busstop.py -- <saida.fbx> <pasta_texturas>
"""
import bpy, sys, math, os
sys.path.append(os.path.dirname(os.path.abspath(__file__)))
import realistic_kit as K

argv = sys.argv[sys.argv.index("--") + 1:]
OUT_FBX, TEX_DIR = argv[0], argv[1]
K.reset_scene()

concrete = K.material("Concrete", (0.50, 0.49, 0.46), 0.88, dirt=0.6, ground_dirt=0.0, color_var=0.14,
                      bump=0.5, bump_scale=28, streaks=0.2, dirt_color=(0.22, 0.20, 0.17))
paint = K.material("FramePaint", (0.09, 0.19, 0.13), 0.45, 0.2, dirt=0.5, ground_dirt=0.9, wear=0.6,
                   wear_color=(0.42, 0.42, 0.42), rust=0.35, color_var=0.05)
roof = K.material("ShelterRoof", (0.30, 0.31, 0.32), 0.55, 0.4, dirt=0.6, streaks=0.45, wear=0.3)
glass = K.material("ShelterGlass", (0.11, 0.14, 0.16), 0.20, dirt=0.35, streaks=0.5, color_var=0.04,
                   dirt_color=(0.26, 0.25, 0.22))
wood = K.material("BenchWood", (0.40, 0.25, 0.13), 0.7, wood=True, dirt=0.4, wear=0.35,
                  wear_color=(0.56, 0.43, 0.29), bump=0.2, bump_scale=40)
steel = K.material("SteelGalv", (0.55, 0.56, 0.57), 0.42, 1.0, dirt=0.5, ground_dirt=0.7, rust=0.25, bump=0.05)
blue = K.material("SignBlue", (0.05, 0.22, 0.58), 0.35, dirt=0.3, wear=0.25, wear_color=(0.7, 0.7, 0.7))
white = K.material("SignWhite", (0.88, 0.88, 0.86), 0.4, dirt=0.35, streaks=0.2)
ink = K.material("Ink", (0.04, 0.04, 0.045), 0.5)
adlight = K.material("AdLight", (0.92, 0.90, 0.82), 0.3, emission=(0.85, 0.80, 0.65), emission_strength=1.0)
poster = K.material("Poster", (0.70, 0.22, 0.08), 0.4, emission=(0.45, 0.13, 0.04), emission_strength=1.0)
postertext = K.material("PosterText", (0.95, 0.92, 0.80), 0.4, emission=(0.9, 0.85, 0.7), emission_strength=1.0)
binmat = K.material("BinPaint", (0.10, 0.18, 0.13), 0.5, 0.2, dirt=0.6, ground_dirt=1.0, wear=0.5,
                    wear_color=(0.4, 0.4, 0.4), rust=0.3)

HL = 1.80
YF, YB = -0.70, 0.70
ROOF_Z = 2.50
P = 0.045

# Laje e meio-fio da laje
K.box(-1.90, 1.90, -0.90, 0.90, 0.0, 0.12, concrete, bevel=0.02)
# Colunas com sapata
for x in (-HL, HL):
    for y in (YF + 0.05, YB - 0.05):
        K.box(x - P, x + P, y - P, y + P, 0.12, ROOF_Z, paint, bevel=0.008)
        K.box(x - 0.08, x + 0.08, y - 0.08, y + 0.08, 0.12, 0.16, steel)
# Cobertura com beiral, calha e frisos
K.box(-HL - 0.12, HL + 0.12, YF - 0.20, YB + 0.10, ROOF_Z, ROOF_Z + 0.07, roof, bevel=0.015)
K.box(-HL - 0.12, HL + 0.12, YF - 0.24, YF - 0.18, ROOF_Z - 0.06, ROOF_Z + 0.09, paint)
K.box(-HL, HL, YB - 0.09, YB - 0.01, ROOF_Z - 0.10, ROOF_Z, paint)
for x in (-HL, HL):
    K.box(x - P, x + P, YF + 0.05, YB - 0.05, ROOF_Z - 0.10, ROOF_Z, paint)

# Vidros com perfil
GZ0, GZ1 = 0.30, 2.30
K.box(-HL + P, HL - P, YB - 0.06, YB - 0.04, GZ0, GZ1, glass)
for z0, z1 in ((GZ0 - 0.05, GZ0), (GZ1, GZ1 + 0.05)):
    K.box(-HL + P, HL - P, YB - 0.08, YB - 0.02, z0, z1, paint)
K.box(-0.01, 0.01, YB - 0.08, YB - 0.02, GZ0, GZ1, paint)   # montante do meio
K.box(-HL - 0.01, -HL + 0.01, -0.05, YB - 0.10, GZ0, GZ1, glass)
K.box(-HL - 0.02, -HL + 0.02, -0.07, -0.03, GZ0, GZ1, paint)

# Painel de anúncio iluminado (lateral direita), cartaz com texto nas duas faces
K.box(HL - 0.06, HL + 0.06, -0.05, YB - 0.10, GZ0 - 0.05, GZ1 + 0.05, paint, bevel=0.01)
for face, rot in ((1, (math.pi / 2, 0, math.pi / 2)), (-1, (math.pi / 2, 0, -math.pi / 2))):
    xf = HL + face * 0.06
    K.box(min(xf, xf + face * 0.01), max(xf, xf + face * 0.01), 0.00, YB - 0.15, GZ0 + 0.05, GZ1 - 0.05, adlight)
    xp = xf + face * 0.01
    K.box(min(xp, xp + face * 0.005), max(xp, xp + face * 0.005), 0.06, YB - 0.21, GZ0 + 0.30, GZ1 - 0.30, poster)
    xt = xp + face * 0.007
    K.text("LANCHES", xt, 0.27 - 0.05, 1.70, 0.13, postertext, rot=rot, extrude=0.002)
    K.text("24H", xt, 0.27 - 0.05, 1.42, 0.22, postertext, rot=rot, extrude=0.002)
    K.text("RUA DO PORTO 413", xt, 0.27 - 0.05, 0.95, 0.045, postertext, rot=rot, extrude=0.001)

# Banco de ripas
BX = 1.20
for i in range(4):
    y0 = 0.22 + i * 0.085
    K.box(-BX, BX, y0, y0 + 0.07, 0.46, 0.50, wood, bevel=0.006)
for i in range(3):
    z0 = 0.62 + i * 0.11
    K.box(-BX, BX, YB - 0.16, YB - 0.12, z0, z0 + 0.08, wood, bevel=0.006)
for x in (-BX + 0.15, 0.0, BX - 0.15):
    K.box(x - 0.025, x + 0.025, 0.24, YB - 0.10, 0.12, 0.46, steel)
    K.box(x - 0.025, x + 0.025, YB - 0.12, YB - 0.08, 0.46, 0.95, steel)

# Poste com a placa (disco ao lado do poste), quadro de horários e lixeira
SX, SY = -HL - 0.55, YF - 0.05
K.cyl(SX, SY, 1.45, 0.04, 2.90, steel, verts=16)
K.cyl(SX, SY, 2.92, 0.045, 0.04, steel, verts=16)
DX = SX + 0.085
K.box(SX, DX, SY - 0.03, SY + 0.03, 2.55, 2.69, steel)
K.cyl(DX, SY, 2.62, 0.32, 0.04, white, axis='X', verts=28)
K.cyl(DX, SY, 2.62, 0.28, 0.06, blue, axis='X', verts=28)
for s in (-1, 1):
    xx = DX + s * 0.032
    rot = (math.pi / 2, 0, math.pi / 2) if s > 0 else (math.pi / 2, 0, -math.pi / 2)
    K.box(xx - 0.004, xx + 0.004, SY - 0.15, SY + 0.15, 2.58, 2.72, white)
    K.box(xx - 0.005, xx + 0.005, SY - 0.12, SY - 0.06, 2.54, 2.58, ink)
    K.box(xx - 0.005, xx + 0.005, SY + 0.06, SY + 0.12, 2.54, 2.58, ink)
    K.box(xx - 0.006, xx + 0.006, SY - 0.12, SY + 0.12, 2.65, 2.70, blue)
    K.text("PONTO", xx + s * 0.004, SY, 2.45, 0.07, white, rot=rot, extrude=0.001)
# Quadro de horários (face +X e -X)
K.box(SX - 0.05, SX + 0.05, SY - 0.20, SY + 0.20, 1.30, 1.75, paint, bevel=0.008)
for s in (-1, 1):
    xb = SX + s * 0.05
    rot = (math.pi / 2, 0, math.pi / 2) if s > 0 else (math.pi / 2, 0, -math.pi / 2)
    K.box(min(xb, xb + s * 0.005), max(xb, xb + s * 0.005), SY - 0.17, SY + 0.17, 1.33, 1.72, white)
    xt = xb + s * 0.007
    K.text("LINHA 413", xt, SY, 1.665, 0.045, ink, rot=rot, extrude=0.001)
    for k in range(7):
        z = 1.58 - k * 0.035
        K.box(min(xt, xt + s * 0.002), max(xt, xt + s * 0.002), SY - 0.13, SY + 0.13 - (k % 3) * 0.04, z, z + 0.012, ink)
# Lixeira com borda e boca
K.cyl(SX + 0.05, SY + 0.45, 0.45, 0.20, 0.70, binmat, verts=16)
K.cyl(SX + 0.05, SY + 0.45, 0.83, 0.215, 0.06, steel, verts=16)
K.cyl(SX + 0.05, SY + 0.45, 0.865, 0.15, 0.02, ink, verts=16)

ob = K.join(list(K.PARTS), "Shelter")
for p in ob.data.polygons:
    p.use_smooth = False
K.unwrap([ob], margin=0.004)
K.bake_all([ob], TEX_DIR, 2048)
K.collapse_materials([ob])
K.export_fbx(OUT_FBX)
print("RESULT busstop tris=%d" % sum(len(p.vertices) - 2 for p in ob.data.polygons))
print("OK", OUT_FBX)
