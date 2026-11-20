# Risadinha da criatura no estilo "criança fantasma" (inspiração: risada debochada com eco de salão vazio),
# versão masculina. 2026-11-20. Uso: blender -b -P ghost_giggle.py -- <risadinha> <saída.wav> <fim s>
import aud, numpy as np, sys
a = sys.argv[sys.argv.index('--')+1:]
src, out, t1 = a[0], a[1], float(a[2])
SR = 44100
d = aud.Sound(src).resample(SR, False).data(); x = (d.mean(axis=1) if d.ndim > 1 else d).astype(np.float64)
x = x[:int(t1*SR)]; x /= np.abs(x).max() + 1e-9
tail = int(2.2*SR); n = len(x) + tail; t = np.arange(n)/SR
def fit(y): return y[:n] if len(y) >= n else np.pad(y, (0, n-len(y)))
def pitch(y, s):
    idx = np.arange(0, len(y)-1, 2**(s/12)); return np.interp(idx, np.arange(len(y)), y)
dry = fit(x)
# FITA ANTIGA: balanço lento do tom (±18 cents)
speed = 2 ** (18*np.sin(2*np.pi*0.8*t)/1200); pos = np.cumsum(speed) - 1; pos = pos[pos < len(dry)-1]
dry = fit(np.interp(pos, np.arange(len(dry)), dry))
# A VOZ ERRADA POR BAIXO: a mesma risadinha uma oitava abaixo, baixinha
under = fit(pitch(x, -12)) * 0.32
# ECOS DE SALÃO VAZIO: repetições que vão escurecendo, alternando lados
L = dry + under; R = dry*0.95 + under
for k, (dt, g) in enumerate([(0.19, 0.45), (0.38, 0.30), (0.61, 0.20), (0.86, 0.12)]):
    o = int(dt*SR); e = np.pad(dry, (o, 0))[:n]
    e = np.convolve(e, np.ones(6+6*k)/(6+6*k), 'same')
    if k % 2 == 0: L += g*e; R += g*0.5*e
    else: R += g*e; L += g*0.5*e
# CAUDA LONGA (reverb difuso) 
rng = np.random.default_rng(7); ir_n = int(2.0*SR)
ir = rng.standard_normal(ir_n) * np.exp(-np.arange(ir_n)/(0.55*SR)); ir = np.convolve(ir, np.ones(25)/25, 'same')
wet = np.fft.irfft(np.fft.rfft(dry, n+ir_n) * np.fft.rfft(ir, n+ir_n))[:n]; wet /= np.abs(wet).max() + 1e-9
L += 0.30*wet; R += 0.30*np.roll(wet, int(0.013*SR))
m = max(np.abs(L).max(), np.abs(R).max()); L *= 0.9/m; R *= 0.9/m
st = np.stack([L, R], axis=1).astype(np.float32)
aud.Sound.buffer(st, SR).write(out, rate=SR, channels=aud.CHANNELS_STEREO, container=aud.CONTAINER_WAV, codec=aud.CODEC_PCM, format=aud.FORMAT_S16)
print('ok', n/SR)
