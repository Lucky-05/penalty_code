/* Motor de cálculo de la plataforma: filtros, envolvente, detección de contracciones,
   métricas de tiempo y de frecuencia, y el simulador. No toca la pantalla, así que
   se puede probar con Node (ver pruebas.js). Amplitudes en µV, tiempos en segundos. */
(function (root) {
  'use strict';
  const D = {};

  // ── Filtros ──────────────────────────────────────────────────────────────────────────
  /** Coeficientes de un bicuadrático de 2.º orden: 'lp', 'hp' o 'notch'. */
  D.biquad = function (type, fc, fs, Q) {
    if (Q === undefined) Q = Math.SQRT1_2;                 // Butterworth
    const w = 2 * Math.PI * fc / fs, c = Math.cos(w), al = Math.sin(w) / (2 * Q);
    let b0, b1, b2;
    if (type === 'lp') { b0 = (1 - c) / 2; b1 = 1 - c; b2 = b0; }
    else if (type === 'hp') { b0 = (1 + c) / 2; b1 = -(1 + c); b2 = b0; }
    else { b0 = 1; b1 = -2 * c; b2 = 1; }
    const a0 = 1 + al;
    return [b0 / a0, b1 / a0, b2 / a0, -2 * c / a0, (1 - al) / a0];
  };

  D.filt = function (x, k) {
    const y = new Float32Array(x.length);
    let z1 = 0, z2 = 0;
    for (let i = 0; i < x.length; i++) {
      const v = x[i], o = k[0] * v + z1;
      z1 = k[1] * v - k[3] * o + z2;
      z2 = k[2] * v - k[4] * o;
      y[i] = o;
    }
    return y;
  };

  /** Filtrado de ida y vuelta: sin desfase, para que los tiempos de activación no se corran. */
  D.filtfilt = function (x, k) {
    const n = x.length;
    if (n < 4) return Float32Array.from(x);
    const pad = Math.min(n - 1, 400), m = n + 2 * pad;
    const e = new Float32Array(m);
    for (let i = 0; i < pad; i++) {
      e[i] = 2 * x[0] - x[pad - i];
      e[m - 1 - i] = 2 * x[n - 1] - x[n - 1 - pad + i];
    }
    e.set(x, pad);
    let y = D.filt(e, k);
    y.reverse();
    y = D.filt(y, k);
    y.reverse();
    return y.slice(pad, pad + n);
  };

  D.mean = function (x, a, b) {
    if (a === undefined) { a = 0; b = x.length; }
    let s = 0;
    for (let i = a; i < b; i++) s += x[i];
    return b > a ? s / (b - a) : 0;
  };

  /**
   * De la señal del electrodo a lo que se analiza.
   * cfg: {type:'raw'|'env', hp, lp, notch, fenv}
   * Devuelve filt (señal limpia, centrada en cero), env (envolvente lineal) y dc (el nivel
   * que se quitó).
   */
  D.preprocess = function (x, fs, cfg) {
    const n = x.length, dc = D.mean(x);
    let f = new Float32Array(n);
    for (let i = 0; i < n; i++) f[i] = x[i] - dc;
    const fenv = Math.min(cfg.fenv, fs * 0.2);
    if (cfg.type === 'env') {
      // El circuito ya entrega la envolvente: solo se suaviza y se apoya en su piso.
      const env = D.filtfilt(x, D.biquad('lp', fenv, fs));
      let lo = Infinity;
      for (let i = 0; i < n; i++) if (env[i] < lo) lo = env[i];
      for (let i = 0; i < n; i++) env[i] = Math.max(0, env[i] - lo);
      return { filt: f, env: env, dc: dc };
    }
    if (cfg.hp > 0) f = D.filtfilt(f, D.biquad('hp', cfg.hp, fs));
    if (cfg.lp > 0 && cfg.lp < fs * 0.45) f = D.filtfilt(f, D.biquad('lp', cfg.lp, fs));
    if (cfg.notch && fs > 150) f = D.filtfilt(f, D.biquad('notch', cfg.mains || 60, fs, 30));
    const r = new Float32Array(n);
    for (let i = 0; i < n; i++) r[i] = Math.abs(f[i]);
    const env = D.filtfilt(r, D.biquad('lp', fenv, fs));
    for (let i = 0; i < n; i++) if (env[i] < 0) env[i] = 0;
    return { filt: f, env: env, dc: dc };
  };

  // ── Métricas en el tiempo ────────────────────────────────────────────────────────────
  D.timeFeatures = function (x, fs, a, b) {
    if (a === undefined) { a = 0; b = x.length; }
    const n = b - a;
    const o = { n: n, rms: 0, mav: 0, p2p: 0, peak: 0, variance: 0, wl: 0, zc: 0, ssc: 0, crest: 0, form: 0, iemg: 0 };
    if (n < 2) return o;
    let s2 = 0, sa = 0, s = 0, mx = -Infinity, mn = Infinity;
    for (let i = a; i < b; i++) {
      const v = x[i];
      s += v; s2 += v * v; sa += Math.abs(v);
      if (v > mx) mx = v;
      if (v < mn) mn = v;
    }
    o.rms = Math.sqrt(s2 / n);
    o.mav = sa / n;
    o.p2p = mx - mn;
    o.peak = Math.max(Math.abs(mx), Math.abs(mn));
    const m = s / n;
    o.variance = s2 / n - m * m;
    o.iemg = sa / fs;                                      // área de la señal rectificada, µV·s
    const dead = o.rms * 0.05;                             // zona muerta: el ruido no cuenta como cruce
    for (let i = a + 1; i < b; i++) {
      const d = x[i] - x[i - 1];
      o.wl += Math.abs(d);
      if (x[i] * x[i - 1] < 0 && Math.abs(d) > dead) o.zc++;
      if (i + 1 < b) {
        const d2 = x[i] - x[i + 1];
        if (d * d2 > 0 && (Math.abs(d) > dead || Math.abs(d2) > dead)) o.ssc++;
      }
    }
    o.crest = o.rms > 0 ? o.peak / o.rms : 0;
    o.form = o.mav > 0 ? o.rms / o.mav : 0;
    return o;
  };

  // ── Frecuencia ───────────────────────────────────────────────────────────────────────
  D.fft = function (re, im) {
    const n = re.length;
    for (let i = 1, j = 0; i < n; i++) {
      let bit = n >> 1;
      for (; j & bit; bit >>= 1) j ^= bit;
      j ^= bit;
      if (i < j) { let t = re[i]; re[i] = re[j]; re[j] = t; t = im[i]; im[i] = im[j]; im[j] = t; }
    }
    for (let len = 2; len <= n; len <<= 1) {
      const ang = -2 * Math.PI / len, wr = Math.cos(ang), wi = Math.sin(ang);
      for (let i = 0; i < n; i += len) {
        let cr = 1, ci = 0;
        for (let k = 0; k < len / 2; k++) {
          const a = i + k, b2 = a + len / 2;
          const xr = re[b2] * cr - im[b2] * ci, xi = re[b2] * ci + im[b2] * cr;
          re[b2] = re[a] - xr; im[b2] = im[a] - xi;
          re[a] += xr; im[a] += xi;
          const t = cr * wr - ci * wi; ci = cr * wi + ci * wr; cr = t;
        }
      }
    }
  };

  /** Densidad espectral de potencia por Welch (Hann, 50 % de traslape). µV²/Hz. */
  D.welch = function (x, fs, a, b) {
    if (a === undefined) { a = 0; b = x.length; }
    const n = b - a;
    if (n < 32) return null;
    let nseg = 64;
    const want = Math.min(n, Math.max(64, fs / 2));
    while (nseg * 2 <= want) nseg *= 2;
    const win = new Float64Array(nseg);
    let w2 = 0;
    for (let i = 0; i < nseg; i++) { win[i] = 0.5 - 0.5 * Math.cos(2 * Math.PI * i / (nseg - 1)); w2 += win[i] * win[i]; }
    const half = nseg / 2, p = new Float64Array(half + 1);
    const re = new Float64Array(nseg), im = new Float64Array(nseg);
    let count = 0;
    for (let s = a; s + nseg <= b; s += half) {
      let m = 0;
      for (let i = 0; i < nseg; i++) m += x[s + i];
      m /= nseg;
      for (let i = 0; i < nseg; i++) { re[i] = (x[s + i] - m) * win[i]; im[i] = 0; }
      D.fft(re, im);
      for (let i = 0; i <= half; i++) p[i] += re[i] * re[i] + im[i] * im[i];
      count++;
    }
    const f = new Float64Array(half + 1), sc = 1 / (fs * w2 * count);
    for (let i = 0; i <= half; i++) { p[i] *= sc * (i > 0 && i < half ? 2 : 1); f[i] = i * fs / nseg; }
    return { f: f, p: p, df: fs / nseg, segments: count };
  };

  D.spectral = function (psd, lo, hi) {
    const o = { mnf: NaN, mdf: NaN, fpk: NaN, ptot: 0 };
    if (!psd) return o;
    let s = 0, sf = 0, pk = -1;
    for (let i = 0; i < psd.f.length; i++) {
      if (psd.f[i] < lo || psd.f[i] > hi) continue;
      s += psd.p[i]; sf += psd.p[i] * psd.f[i];
      if (psd.p[i] > pk) { pk = psd.p[i]; o.fpk = psd.f[i]; }
    }
    if (s <= 0) return o;
    o.mnf = sf / s;
    o.ptot = s * psd.df;
    let c = 0;
    for (let i = 0; i < psd.f.length; i++) {
      if (psd.f[i] < lo || psd.f[i] > hi) continue;
      const prev = c;
      c += psd.p[i];
      if (c >= s / 2) {                                    // se interpola dentro de la casilla
        o.mdf = psd.f[i] - psd.df / 2 + psd.df * (s / 2 - prev) / psd.p[i];
        break;
      }
    }
    return o;
  };

  /** Qué fracción de la potencia cae en la frecuencia de la red eléctrica (±3 Hz). */
  D.humFraction = function (psd, mains) {
    if (!psd) return NaN;
    let s = 0, h = 0;
    for (let i = 0; i < psd.f.length; i++) {
      if (psd.f[i] < 10) continue;
      s += psd.p[i];
      if (Math.abs(psd.f[i] - mains) <= 3) h += psd.p[i];
    }
    return s > 0 ? h / s : NaN;
  };

  // ── Reposo, umbral y contracciones ───────────────────────────────────────────────────
  /** Nivel de reposo estimado sin calibrar: los tramos más quietos de la envolvente. */
  D.baseline = function (env, fs) {
    const blk = Math.max(8, Math.round(fs * 0.1)), means = [];
    for (let i = 0; i + blk <= env.length; i += blk) means.push({ m: D.mean(env, i, i + blk), i: i });
    if (!means.length) return { mu: 0, sd: 0 };
    means.sort(function (p, q) { return p.m - q.m; });
    const take = Math.max(1, Math.floor(means.length * 0.2));
    let s = 0, s2 = 0, c = 0;
    for (let j = 0; j < take; j++)
      for (let i = means[j].i; i < means[j].i + blk; i++) { s += env[i]; s2 += env[i] * env[i]; c++; }
    const mu = s / c;
    return { mu: mu, sd: Math.sqrt(Math.max(0, s2 / c - mu * mu)) };
  };

  /** Tramos donde la envolvente supera el umbral. Une huecos cortos y descarta chispazos. */
  D.detectBursts = function (env, fs, thr, minDur, minGap) {
    const raw = [];
    let on = -1;
    for (let i = 0; i < env.length; i++) {
      if (env[i] > thr) { if (on < 0) on = i; }
      else if (on >= 0) { raw.push([on, i]); on = -1; }
    }
    if (on >= 0) raw.push([on, env.length]);
    const merged = [], g = minGap * fs;
    for (const b of raw) {
      if (merged.length && b[0] - merged[merged.length - 1][1] < g) merged[merged.length - 1][1] = b[1];
      else merged.push([b[0], b[1]]);
    }
    return merged.filter(function (b) { return b[1] - b[0] >= minDur * fs; });
  };

  // ── Entre músculos ───────────────────────────────────────────────────────────────────
  /** Índice de co-contracción de Falconer y Winter sobre dos envolventes normalizadas. */
  D.cci = function (a, b, i0, i1, na, nb) {
    let common = 0, total = 0;
    for (let i = i0; i < i1; i++) {
      const u = a[i] / na, v = b[i] / nb;
      common += Math.min(u, v);
      total += u + v;
    }
    return total > 0 ? 200 * common / total : NaN;
  };

  /** Cuánto se retrasa b respecto de a (s) donde sus envolventes más se parecen. */
  D.xcorrLag = function (a, b, fs, maxLag) {
    const n = Math.min(a.length, b.length), step = Math.max(1, Math.round(fs / 250));
    const ma = D.mean(a, 0, n), mb = D.mean(b, 0, n), L = Math.round(maxLag * fs);
    let best = -Infinity, lag = 0;
    for (let l = -L; l <= L; l += step) {
      let s = 0;
      for (let i = Math.max(0, -l); i < Math.min(n, n - l); i += step) s += (a[i] - ma) * (b[i + l] - mb);
      if (s > best) { best = s; lag = l; }
    }
    return lag / fs;
  };

  D.linreg = function (xs, ys) {
    const n = xs.length;
    if (n < 2) return { slope: NaN, icpt: NaN, r2: NaN };
    let sx = 0, sy = 0, sxx = 0, sxy = 0, syy = 0;
    for (let i = 0; i < n; i++) { sx += xs[i]; sy += ys[i]; sxx += xs[i] * xs[i]; sxy += xs[i] * ys[i]; syy += ys[i] * ys[i]; }
    const den = n * sxx - sx * sx;
    if (den === 0) return { slope: NaN, icpt: NaN, r2: NaN };
    const slope = (n * sxy - sx * sy) / den, icpt = (sy - slope * sx) / n;
    const vy = n * syy - sy * sy;
    return { slope: slope, icpt: icpt, r2: vy > 0 ? Math.pow(n * sxy - sx * sy, 2) / (den * vy) : NaN };
  };

  // ── Simulador ────────────────────────────────────────────────────────────────────────
  /**
   * Señal de práctica: un jugador que patea un penal cada 5 s. No es un registro real;
   * sirve para ensayar la plataforma sin electrodos. Canales: cuádriceps e isquiotibiales
   * de la pierna de golpeo y, si hay cuatro, los de la pierna de apoyo.
   */
  D.makeSim = function (fs, nch, seed) {
    let st = seed || 20261008;
    const rnd = function () { st |= 0; st = st + 0x6D2B79F5 | 0; let t = Math.imul(st ^ st >>> 15, 1 | st); t = t + Math.imul(t ^ t >>> 7, 61 | t) ^ t; return ((t ^ t >>> 14) >>> 0) / 4294967296; };
    const gauss = function () { return Math.sqrt(-2 * Math.log(rnd() + 1e-12)) * Math.cos(2 * Math.PI * rnd()); };
    const PERIOD = 5, CONTACT = 3;
    // [centro respecto del golpeo (s), amplitud relativa, anchura (s)]
    const plan = [
      [[-1.75, 0.22, 0.10], [-1.05, 0.26, 0.10], [-0.09, 1.00, 0.055]],      // cuádriceps de golpeo
      [[-1.40, 0.20, 0.10], [-0.30, 0.55, 0.060], [0.12, 0.85, 0.065]],      // isquiotibiales de golpeo
      [[-1.40, 0.25, 0.10], [-0.20, 0.75, 0.090]],                           // cuádriceps de apoyo
      [[-1.05, 0.22, 0.10], [-0.04, 0.45, 0.090]]                            // isquiotibiales de apoyo
    ];
    const scale = [420, 300, 380, 260];                    // µV eficaces en plena activación
    const ch = [];
    for (let c = 0; c < nch; c++) ch.push({ z: [0, 0, 0, 0], g: 1 });
    let n = 0, rep = -1, repGain = [], mode = null;
    const sim = { events: [], fatigue: true };
    sim.setMode = function (m) { mode = m; };              // null | {kind:'rest'} | {kind:'mvc', ch}
    sim.next = function (count) {
      const out = [];
      for (let c = 0; c < nch; c++) out.push(new Float32Array(count));
      for (let i = 0; i < count; i++, n++) {
        const t = n / fs, r = Math.floor(t / PERIOD), tc = t - r * PERIOD - CONTACT;
        if (r !== rep) {
          rep = r;
          repGain = [];
          for (let c = 0; c < nch; c++) repGain.push(1 + 0.12 * gauss());
        }
        if (!mode && n > 0 && Math.abs(tc) < 0.5 / fs) sim.events.push({ t: t, label: 'Golpeo' });
        const tired = sim.fatigue ? Math.min(1, rep / 40) : 0;
        for (let c = 0; c < nch; c++) {
          let act = 0;
          if (mode && mode.kind === 'mvc') act = mode.ch === c ? 1.25 : 0.04;
          else if (!mode) {
            const p = plan[c % 4];
            for (let j = 0; j < p.length; j++) { const d = (tc - p[j][0]) / p[j][2]; act += p[j][1] * Math.exp(-0.5 * d * d); }
            act *= repGain[c] * (1 + 0.25 * tired);        // con fatiga se recluta más para lo mismo
          }
          // Ruido blanco moldeado como EMG: pasa-altas a 30 Hz y pasa-bajas que baja con la fatiga.
          const s = ch[c], w = gauss();
          const fcut = 130 - 45 * tired;
          if (!s.k || s.kf !== Math.round(fcut)) { s.kf = Math.round(fcut); s.k = D.biquad('lp', s.kf, fs, 0.6); s.h = D.biquad('hp', 30, fs, 0.6); }
          let o = s.k[0] * w + s.z[0];
          s.z[0] = s.k[1] * w - s.k[3] * o + s.z[1];
          s.z[1] = s.k[2] * w - s.k[4] * o;
          const o2 = s.h[0] * o + s.z[2];
          s.z[2] = s.h[1] * o - s.h[3] * o2 + s.z[3];
          s.z[3] = s.h[2] * o - s.h[4] * o2;
          const shape = 1 / Math.sqrt(Math.max(0.05, 2 * (s.kf - 30) / fs));   // deja el ruido moldeado cerca de 1 eficaz
          out[c][i] = o2 * shape * scale[c % 4] * act
            + 6 * gauss()                                                   // ruido del amplificador
            + 9 * Math.sin(2 * Math.PI * 60 * t + c)                        // zumbido de la red
            + 40 * Math.sin(2 * Math.PI * 0.3 * t + c * 2);                 // deriva lenta de la piel
        }
      }
      return out;
    };
    return sim;
  };

  if (typeof module !== 'undefined' && module.exports) module.exports = D;
  else root.DSP = D;
})(typeof self !== 'undefined' ? self : this);
