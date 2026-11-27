# Pós-processamento PSX + encode MP4 (roda dentro do Blender, usa o numpy embutido)
# Uso: blender -b --factory-startup --python post_psx.py -- <raw_dir> <post_dir> <out.mp4> [peep "76-165"|none] [overlay_dir|none]
# - Plano B (frames 76-165): olho mágico (barril/fisheye + máscara circular)
# - Todos: grão, quantização 15-bit (5 bits/canal) com dithering Bayer 4x4, upscale nearest x3
import bpy, sys, os, glob
import numpy as np

_args = sys.argv[sys.argv.index("--") + 1:]
raw_dir, post_dir, out_mp4 = _args[:3]
# 4º argumento opcional: faixa do olho mágico "ini-fim" ou "none" (padrão = teaser 76-165)
_peep = _args[3] if len(_args) > 3 else "76-165"
# 5º argumento opcional: pasta de overlay RGBA (legendas/pálpebras, mesmos nomes f_####.png) ou "none"
OVERLAY_DIR = _args[4] if len(_args) > 4 and _args[4] != "none" else None
os.makedirs(post_dir, exist_ok=True)
SCALE = 3
PEEP = range(0) if _peep == "none" else range(int(_peep.split("-")[0]), int(_peep.split("-")[1]) + 1)
rng = np.random.default_rng(1)

BAYER = np.array([[0, 8, 2, 10], [12, 4, 14, 6], [3, 11, 1, 9], [15, 7, 13, 5]], dtype=np.float32) / 16.0 - 0.5

def peephole(a):
    h, w = a.shape[:2]
    cy, cx = (h - 1) / 2, (w - 1) / 2
    R = h * 0.49
    yy, xx = np.mgrid[:h, :w].astype(np.float32)
    dx, dy = (xx - cx) / R, (yy - cy) / R
    r = np.sqrt(dx * dx + dy * dy) + 1e-6
    rs = r * (0.40 + 0.60 * r * r)          # centro ampliado, borda comprimida
    src_R = w * 0.5
    sx = np.clip(np.rint(cx + dx / r * rs * src_R), 0, w - 1).astype(int)
    sy = np.clip(np.rint(cy + dy / r * rs * src_R), 0, h - 1).astype(int)
    out = a[sy, sx]
    vig = np.clip(1.0 - (r ** 4) * 0.85, 0, 1)[..., None]
    out[..., :3] *= vig
    out[..., :3] *= (0.85 + 0.15 * (1 - np.clip(r, 0, 1)))[..., None]  # leve escurecimento geral (vidro sujo)
    out[r > 1.0, :3] = 0.0
    return out

def psx(a):
    h, w = a.shape[:2]
    rgb = a[..., :3] + rng.normal(0, 0.012, (h, w, 1)).astype(np.float32)
    th = np.tile(BAYER, (h // 4 + 1, w // 4 + 1))[:h, :w, None]
    levels = 31.0
    rgb = np.floor(np.clip(rgb, 0, 1) * levels + 0.5 + th) / levels
    a[..., :3] = np.clip(rgb, 0, 1)
    a[..., 3] = 1.0
    return a

files = sorted(glob.glob(os.path.join(raw_dir, "f_*.png")))
out_img = None
for path in files:
    frame = int(os.path.basename(path)[2:6])
    img = bpy.data.images.load(path)
    w, h = img.size
    a = np.empty(w * h * 4, dtype=np.float32)
    img.pixels.foreach_get(a)
    a = a.reshape(h, w, 4)
    bpy.data.images.remove(img)
    if frame in PEEP:
        a = peephole(a)
    if OVERLAY_DIR:
        op = os.path.join(OVERLAY_DIR, os.path.basename(path))
        if os.path.exists(op):
            oi = bpy.data.images.load(op)
            o = np.empty(w * h * 4, dtype=np.float32)
            oi.pixels.foreach_get(o)
            o = o.reshape(h, w, 4)
            bpy.data.images.remove(oi)
            al = o[..., 3:4]
            a[..., :3] = a[..., :3] * (1 - al) + o[..., :3] * al   # antes do dither: texto também fica PS1
    a = psx(a)
    a = np.repeat(np.repeat(a, SCALE, axis=0), SCALE, axis=1)
    if out_img is None:
        out_img = bpy.data.images.new("psx_out", w * SCALE, h * SCALE, alpha=False)
    out_img.pixels.foreach_set(a.ravel())
    out_img.filepath_raw = os.path.join(post_dir, f"p_{frame:04d}.png")
    out_img.file_format = 'PNG'
    out_img.save()
print("[Teaser] pós ok:", len(files), "frames")

# ---------------------------------------------------------------- encode
post = sorted(glob.glob(os.path.join(post_dir, "p_*.png")))
sc = bpy.data.scenes.new("Edit")
sc.render.fps = 15
sc.render.resolution_x, sc.render.resolution_y = 320 * SCALE, 240 * SCALE
sc.render.resolution_percentage = 100
sc.frame_start, sc.frame_end = 1, len(post)
sc.view_settings.view_transform = 'Standard'
sed = sc.sequence_editor_create()
coll = sed.strips if hasattr(sed, "strips") else sed.sequences
st = coll.new_image(name="frames", filepath=post[0], channel=1, frame_start=1)
for p in post[1:]:
    st.elements.append(os.path.basename(p))
r = sc.render
try:
    r.image_settings.media_type = 'VIDEO'
except Exception:
    pass
r.image_settings.file_format = 'FFMPEG'
r.ffmpeg.format = 'MPEG4'
r.ffmpeg.codec = 'H264'
r.ffmpeg.constant_rate_factor = 'HIGH'
r.filepath = out_mp4
bpy.ops.render.render(animation=True, scene=sc.name)
print("[Teaser] mp4:", out_mp4)
