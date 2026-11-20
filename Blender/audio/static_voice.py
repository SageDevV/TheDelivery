# Risada da criatura: voz masculina com ESTÁTICA DENTRO DA VOZ (2026-11-20).
# Uso: blender -b -P static_voice.py -- <risada> <saída.wav> <início s> <fim s>
import aud, numpy as np, sys
a = sys.argv[sys.argv.index('--')+1:]
src, out, t0, t1 = a[0], a[1], float(a[2]), float(a[3])
SR = 44100; rng = np.random.default_rng(404)
d = aud.Sound(src).resample(SR, False).data(); x = (d.mean(axis=1) if d.ndim > 1 else d).astype(np.float64)
x = x[int(t0*SR):int(t1*SR)]; x /= np.abs(x).max() + 1e-9; n = len(x); t = np.arange(n)/SR
def norm(y): return y / (np.abs(y).max() + 1e-9)
def fit(y): return y[:n] if len(y) >= n else np.pad(y, (0, n - len(y)))
def pitch(y, s):
    idx = np.arange(0, len(y)-1, 2**(s/12)); return np.interp(idx, np.arange(len(y)), y)
def stft_noise(y, win=1024, hop=256):
    # MESMO ESPECTRO, FASE SORTEADA: a voz vira chiado com o timbre e o ritmo dela
    w = np.hanning(win); outp = np.zeros(len(y) + win); norm_ = np.zeros(len(y) + win)
    for s in range(0, len(y) - win, hop):
        F = np.fft.rfft(y[s:s+win] * w)
        ph = np.exp(1j * rng.uniform(0, 2*np.pi, len(F)))
        outp[s:s+win] += np.fft.irfft(np.abs(F) * ph, win) * w; norm_[s:s+win] += w*w
    return (outp / np.maximum(norm_, 1e-6))[:len(y)]
def bandpass(y, lo, hi):
    Y = np.fft.rfft(y); f = np.fft.rfftfreq(len(y), 1/SR); Y[(f < lo) | (f > hi)] = 0; return np.fft.irfft(Y, len(y))

voice = norm(fit(pitch(x, -2.5)))                        # 1. voz, um pouco mais grave
env = np.convolve(np.abs(voice), np.ones(441)/441, 'same'); env = env / (env.max() + 1e-9)
noisy = norm(stft_noise(voice))                          # 2. a voz feita de chiado
radio = bandpass(voice, 350, 3200)                       # 3. rádio corrompido: banda estreita,
hold = 6; radio = np.repeat(radio[::hold], hold)[:n]     #    taxa baixa (~7 kHz) e 5 bits
radio = norm(np.round(norm(radio) * 16) / 16)
hiss = bandpass(rng.standard_normal(n), 2500, 12000)    # 4. chiado e estalos que SEGUEM a voz
hiss = norm(hiss) * (0.15 + 0.85 * env)
pops = np.zeros(n)
for p in rng.choice(n, 140, replace=False):
    if env[p] > 0.12: pops[p:p+30] += rng.uniform(-1, 1) * np.exp(-np.arange(min(30, n-p))/6)
mix = 0.60*voice + 0.55*noisy + 0.35*radio + 0.22*hiss + 0.45*pops
# 5. FALHAS: trechos curtos em que a voz cai e só o chiado sobra (sinal perdendo)
for c in rng.uniform(0.6, (n/SR) - 0.4, 6):
    s = int(c*SR); g = int(rng.uniform(0.04, 0.11)*SR)
    seg = slice(s, min(n, s+g)); mix[seg] = 0.15*mix[seg] + 0.7*noisy[seg] + 0.3*hiss[seg]
# 6. a estática "respira": tremulação rápida irregular no volume
mix *= 0.82 + 0.18*np.sign(np.sin(2*np.pi*(23 + 9*np.sin(2*np.pi*0.6*t))*t))
# compressão forte: a risada tem que chegar no volume do grito dela (ajustado no vídeo: -10 dB abaixo)
mix = np.tanh(3.4*norm(mix))
fade = int(0.45*SR); mix[-fade:] *= np.linspace(1, 0, fade); mix[:200] *= np.linspace(0, 1, 200)
mix = 0.92*norm(mix)
st = np.stack([mix, np.roll(mix, 23)], axis=1).astype(np.float32)
aud.Sound.buffer(st, SR).write(out, rate=SR, channels=aud.CHANNELS_STEREO, container=aud.CONTAINER_WAV, codec=aud.CODEC_PCM, format=aud.FORMAT_S16)
print('ok', n/SR)
