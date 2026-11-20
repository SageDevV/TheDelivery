# A ESTÁTICA AO ENCARAR A CRIATURA (estilo Slender), 2026-11-20.
# Uso: blender -b -P stare_static.py -- <chiado> <saída.wav> <duração s> <subida s>
import aud, numpy as np, sys
a = sys.argv[sys.argv.index('--')+1:]
src, out, dur, rise = a[0], a[1], float(a[2]), float(a[3])
SR = 44100; rng = np.random.default_rng(1999)
d = aud.Sound(src).resample(SR, False).data(); x = (d.mean(axis=1) if d.ndim > 1 else d).astype(np.float64)
n = int(dur*SR); x = x[int(0.3*SR):int(0.3*SR)+n]; x /= np.abs(x).max() + 1e-9; t = np.arange(n)/SR
# SUBIDA: o chiado cresce com a câmera chegando na cara (curva exponencial)
env = np.where(t < rise, 0.15 + 0.85*(t/rise)**2, 1.0)
# ESTOUROS E CORTES: blocos de 40-120 ms que estouram (x1.8, saturados) ou somem
gate = np.ones(n); p = int(rise*SR*0.6)
while p < n:
    g = int(rng.uniform(0.04, 0.12)*SR); r = rng.random()
    if r < 0.30: gate[p:p+g] = 1.8
    elif r < 0.45: gate[p:p+g] = 0.05
    p += g + int(rng.uniform(0.08, 0.3)*SR)
gate = np.convolve(gate, np.ones(80)/80, 'same')
y = np.tanh(2.0 * x * env * gate)
# GRAVE PULSANTE: 38 Hz + 41 Hz batendo (pulsa ~3 vezes por segundo), cresce junto
sub = (np.sin(2*np.pi*38*t) + np.sin(2*np.pi*41*t)) * 0.5 * env
y = 0.8*y/np.abs(y).max() + 0.45*sub
fade = int(0.35*SR); y[-fade:] *= np.linspace(1, 0, fade); y[:300] *= np.linspace(0, 1, 300)
y = 0.9*y/np.abs(y).max()
st = np.stack([y, np.roll(y, 37)], axis=1).astype(np.float32)
aud.Sound.buffer(st, SR).write(out, rate=SR, channels=aud.CHANNELS_STEREO, container=aud.CONTAINER_WAV, codec=aud.CODEC_PCM, format=aud.FORMAT_S16)
print('ok', n/SR)
