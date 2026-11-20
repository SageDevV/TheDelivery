# Grito da Clear no agarrão — tratamento (2026-11-19: o grito "genérico" do banco de sons ganha pânico).
# Uso: blender -b -P treat_scream.py -- <grito> <saída.wav> <início s> <fim s>
import aud, numpy as np, sys
a = sys.argv[sys.argv.index('--')+1:]
src, out, t0, t1 = a[0], a[1], float(a[2]), float(a[3])
SR = 44100
d = aud.Sound(src).resample(SR, False).data(); x = (d.mean(axis=1) if d.ndim > 1 else d).astype(np.float64)
x = x[int(t0*SR):int(t1*SR)]; n = len(x); t = np.arange(n)/SR
x /= np.abs(x).max() + 1e-9
# VOZ TREMENDO: vibrato irregular que cresce (pânico, não canto) — leitura com velocidade variável
cents = (25 + 45*t/t[-1]) * (np.sin(2*np.pi*6.3*t) + 0.5*np.sin(2*np.pi*11.1*t + 2))
speed = 2 ** (cents/1200); pos = np.cumsum(speed) - speed[0]; pos = pos[pos < n-1]
y = np.interp(pos, np.arange(n), x); y = np.pad(y, (0, n-len(y)))
# ROUQUIDÃO: saturação leve só nos picos + um fio de ruído que acompanha a voz
env = np.convolve(np.abs(y), np.ones(400)/400, 'same')
rasp = np.random.default_rng(3).standard_normal(n) * env * 0.18
rasp = np.convolve(rasp, [1, -0.85], 'same')
y = np.tanh(1.8*y)/np.tanh(1.8) + rasp
# PRESENÇA: realce de 2-4 kHz (colado no microfone), sem eco
Y = np.fft.rfft(y); f = np.fft.rfftfreq(n, 1/SR)
Y *= 1 + 0.6*np.exp(-((f-3000)/1200)**2) - 0.3*np.exp(-((f-120)/80)**2)
y = np.fft.irfft(Y, n)
fade = int(0.4*SR); y[-fade:] *= np.linspace(1, 0, fade); y[:200] *= np.linspace(0, 1, 200)
y = 0.92 * y / (np.abs(y).max() + 1e-9)
st = np.stack([y, y], axis=1).astype(np.float32)
aud.Sound.buffer(st, SR).write(out, rate=SR, channels=aud.CHANNELS_STEREO, container=aud.CONTAINER_WAV, codec=aud.CODEC_PCM, format=aud.FORMAT_S16)
print('ok', n/SR)
