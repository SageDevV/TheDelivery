# Risada da criatura do pesadelo — distorção pesada (v2, 2026-11-19: "mais distorcido e estranho").
# Uso: blender -b -P demonize_laugh.py -- <risada base> <textura molhada> <saída.wav>
import aud, numpy as np, sys
a = sys.argv[sys.argv.index('--')+1:]
base_p, tex_p, out_p = a
SR = 44100
rng = np.random.default_rng(666)
def load(p):
    d = aud.Sound(p).resample(SR, False).data()
    return (d.mean(axis=1) if d.ndim > 1 else d).astype(np.float64)
def norm(x): return x / (np.abs(x).max() + 1e-9)
def fit(x, n): return x[:n] if len(x) >= n else np.pad(x, (0, n - len(x)))
def pitch(x, semis):
    idx = np.arange(0, len(x) - 1, 2 ** (semis / 12.0))
    return np.interp(idx, np.arange(len(x)), x)
def warp(x, curve):
    # tom variando no tempo: lê o sinal com velocidade variável (o "pitch bend" doente)
    pos = np.cumsum(curve); pos = pos[pos < len(x) - 1]
    return np.interp(pos, np.arange(len(x)), x)

base = norm(load(base_p)); n = len(base); t = np.arange(n) / SR
# 1. TOM QUE ESCORREGA: a voz desliza para baixo e volta em ondas lentas e irregulares
bend = 2 ** ((-3 + 3.5 * np.sin(2*np.pi*0.55*t) + 2 * np.sin(2*np.pi*1.7*t + 1)) / 12)
slid = fit(warp(base, bend), n)
# 2. CORO DE COISAS: -12 (oco, enorme), -5, +7 atrasado, +12 sussurrado e invertido
sub = fit(pitch(base, -12), n)
low = fit(pitch(base, -5), n)
high = fit(np.pad(pitch(base, 7), (int(0.06*SR), 0)), n)
ghost = fit(pitch(base, 12)[::-1], n) * np.linspace(0.2, 1, n)
# 3. GRÃOS INVERTIDOS: pedaços de 70-160 ms tocados ao contrário, aleatórios
grains = slid.copy(); p = int(0.5*SR)
while p < n - int(0.2*SR):
    g = int(rng.uniform(0.07, 0.16) * SR)
    if rng.random() < 0.45: grains[p:p+g] = grains[p:p+g][::-1] * np.hanning(g) + grains[p:p+g] * (1 - np.hanning(g))
    p += g + int(rng.uniform(0.05, 0.25) * SR)
# 4. ANEL MAIS FORTE + DECIMAÇÃO (voz "rachada" digital)
ring = grains * np.sin(2*np.pi*(35 + 40*np.abs(np.sin(2*np.pi*0.9*t)))*t)
hold = int(SR / 5500); crushed = np.repeat(grains[::hold], hold)[:n]; crushed = np.round(crushed * 24) / 24
tex = load(tex_p); tex = norm(fit(tex[int(0.4*SR):], n))
mix = (0.55*grains + 0.9*sub + 0.55*low + 0.35*high + 0.30*ghost + 0.60*ring + 0.45*crushed + 0.55*tex)
# 5. GAGUEIRA: trechos que travam e repetem como disco arranhado
for c in (0.95, 1.6, 2.2, 2.9, 3.5):
    s = int(c*SR); g = int(rng.uniform(0.035, 0.08)*SR); grain = mix[s:s+g].copy() * np.hanning(g)
    for k in range(1, int(rng.integers(3, 7))):
        e = s + k*g
        if e + g < n: mix[e:e+g] = mix[e:e+g]*0.2 + grain
# 6. SUMIÇOS: a voz corta para o silêncio por instantes (a mais incômoda das falhas)
for c in (1.35, 2.6, 3.75):
    s = int(c*SR); g = int(0.09*SR); mix[s:s+g] *= np.concatenate([np.linspace(1, 0.02, g//4), np.full(g - g//2, 0.02), np.linspace(0.02, 1, g - g//4 - (g - g//2))])
# 7. ECO QUE VEM ANTES: a risada invertida com cauda, entrando antes de cada ataque
tail = int(1.0*SR); ir = rng.standard_normal(tail) * np.exp(-np.arange(tail) / (0.3*SR)); ir = np.convolve(ir, np.ones(60)/60, 'same')
rev = np.fft.irfft(np.fft.rfft(mix[::-1], n+tail) * np.fft.rfft(ir, n+tail))[:n][::-1]
wet = np.fft.irfft(np.fft.rfft(mix, n+tail) * np.fft.rfft(ir, n+tail))[:n]
mix = norm(mix) + 0.35*norm(rev) + 0.28*norm(wet)
# 8. SATURAÇÃO PESADA assimétrica + fade
mix = norm(mix); mix = np.tanh(3.2*mix + 0.15) - np.tanh(0.15)
fade = int(0.5*SR); mix[-fade:] *= np.linspace(1, 0, fade); mix = 0.9 * norm(mix)
L = mix; R = 0.85*mix + 0.15*fit(np.pad(mix, (int(0.017*SR), 0)), n)
st = np.stack([L, R], axis=1).astype(np.float32)
aud.Sound.buffer(st, SR).write(out_p, rate=SR, channels=aud.CHANNELS_STEREO, container=aud.CONTAINER_WAV, codec=aud.CODEC_PCM, format=aud.FORMAT_S16)
print('ok', n/SR)
