/* La pantalla del técnico: recibe la señal real (juego de penales, Arduino o CSV), la manda al motor de
   cálculo (dsp.js) y dibuja osciloscopio, tablas y gráficas. */
(function () {
  'use strict';
  const D = window.DSP, $ = function (id) { return document.getElementById(id); };

  const NAMES = {
    1: ['Cuádriceps'],
    2: ['Cuádriceps', 'Isquiotibiales'],
    4: ['Cuádriceps · golpeo', 'Isquiotibiales · golpeo', 'Cuádriceps · apoyo', 'Isquiotibiales · apoyo']
  };
  const COLORS = ['#2563eb', '#ea580c', '#7c3aed', '#db2777'];
  const MAX_SECONDS = 15 * 60, CAL_SECONDS = 3, PRELOAD_KICKS = 36;

  const S = {
    fs: 1000, nch: 2, data: [], n: 0, events: [], running: false, src: 'none', sim: null,
    rest: [], mvc: [], clip: [], live: [], an: null, span: 10, pos: 0, ymax: [], capture: null, rate: NaN,
    cfg: { nch: 2, fs: 1000, type: 'raw', hp: 20, lp: 450, notch: true, mains: 60, fenv: 6, k: 3, minDur: 30, minGap: 60, floor: 8,
           units: 'adc', gain: 1000, vref: 5, bits: 10, baud: 250000 }
  };

  // ── Utilidades ───────────────────────────────────────────────────────────────────────
  function fmt(v, d) { return v === undefined || v === null || !isFinite(v) ? '—' : v.toFixed(d === undefined ? 1 : d); }
  function ms(v) { return isFinite(v) ? (v >= 0 ? '+' : '−') + Math.abs(v * 1000).toFixed(0) : '—'; }
  function el(tag, attrs, text) {
    const e = document.createElement(tag);
    if (attrs) for (const k in attrs) e.setAttribute(k, attrs[k]);
    if (text !== undefined) e.textContent = text;
    return e;
  }
  function notice(text) { const n = $('notice'); n.textContent = text || ''; n.style.display = text ? 'block' : 'none'; }
  function names() { return NAMES[S.nch]; }
  function fillTable(id, head, rows, tips) {
    const t = $(id);
    t.textContent = '';
    const hr = el('tr');
    head.forEach(function (h, i) { const th = el('th', tips && tips[i] ? { title: tips[i] } : null, h); hr.appendChild(th); });
    t.appendChild(hr);
    rows.forEach(function (r) {
      const tr = el('tr', r.cls ? { class: r.cls } : null);
      r.cells.forEach(function (c, i) {
        const td = el('td', r.txt && r.txt.indexOf(i) >= 0 ? { class: 'txt' } : null);
        if (i === 0 && r.color) td.appendChild(el('span', { class: 'dot', style: 'background:' + r.color }));
        td.appendChild(document.createTextNode(c));
        tr.appendChild(td);
      });
      t.appendChild(tr);
    });
  }

  // ── Almacén de la señal ──────────────────────────────────────────────────────────────
  function reset() {
    S.nch = S.cfg.nch; S.fs = S.cfg.fs;
    S.data = []; S.rest = []; S.mvc = []; S.clip = []; S.ymax = []; S.live = [];
    for (let c = 0; c < S.nch; c++) { S.data.push(new Float32Array(S.fs * 60)); S.rest.push(null); S.mvc.push(null); S.clip.push(0); S.ymax.push(20); }
    S.n = 0; S.events = []; S.an = null; S.capture = null; S.rate = NaN; S.pos = 0;
    buildChannels();
  }

  /** Agrega muestras ya convertidas a µV: un arreglo por canal. */
  function push(chunks) {
    const add = chunks[0].length;
    if (!add) return;
    if (S.src !== 'nube' && S.n + add > MAX_SECONDS * S.fs) { stop(); notice('Se alcanzó el máximo de 15 minutos de grabación. Exporta la señal e inicia otra captura.'); return; }
    if (S.n + add > S.data[0].length) {
      for (let c = 0; c < S.nch; c++) { const g = new Float32Array(Math.max(S.data[c].length * 2, S.n + add)); g.set(S.data[c].subarray(0, S.n)); S.data[c] = g; }
    }
    for (let c = 0; c < S.nch; c++) S.data[c].set(chunks[c], S.n);
    S.n += add;
  }

  function toMicrovolts(v) {
    const g = S.cfg;
    if (g.units === 'uv') return v;
    if (g.units === 'v') return v / g.gain * 1e6;
    return v / (Math.pow(2, g.bits) - 1) * g.vref / g.gain * 1e6;
  }

  /** Filas crudas tal como llegan del Arduino o del archivo. */
  function pushRows(rows) {
    if (!rows.length) return;
    const top = Math.pow(2, S.cfg.bits) - 1, chunks = [];
    for (let c = 0; c < S.nch; c++) {
      const a = new Float32Array(rows.length);
      for (let i = 0; i < rows.length; i++) {
        const v = rows[i][c];
        if (S.cfg.units === 'adc' && (v <= 0 || v >= top)) S.clip[c]++;
        a[i] = toMicrovolts(v);
      }
      chunks.push(a);
    }
    push(chunks);
  }

  // ── Fuentes ──────────────────────────────────────────────────────────────────────────
  let simTimer = null, lastTick = 0, reader = null, game = null;
  const BRIDGE = 'http://127.0.0.1:5010', GAME_URL = BRIDGE + '/flujo';   // lo sirve emg_puente.py del juego de penales

  function setRunning(on, src) {
    S.running = on;
    if (src) S.src = src;
    const b = $('srcBadge');
    b.className = 'badge' + (S.src === 'sim' ? ' sim' : S.src === 'serial' || S.src === 'game' ? ' live' : S.src === 'nube' ? ' rec' : '');
    $('replayBar').classList.toggle('on', S.src === 'nube');
    if (S.src === 'nube' && S.cloud) $('replayName').textContent = S.cloud.paciente + ' · ' + fmtFecha(S.cloud.inicio);
    b.textContent = S.src === 'sim' ? 'DATOS SIMULADOS' : S.src === 'serial' ? (on ? 'ARDUINO EN VIVO' : 'ARDUINO · DETENIDO')
      : S.src === 'game' ? (on ? 'JUEGO DE PENALES EN VIVO' : 'JUEGO · DETENIDO') : S.src === 'csv' ? 'ARCHIVO CSV'
      : S.src === 'nube' && S.cloud ? 'SESIÓN GRABADA · ' + S.cloud.paciente : 'SIN SEÑAL';
    $('bStop').disabled = !on;
    $('bRest').disabled = $('bMark').disabled = !on;
    document.querySelectorAll('#mvcBtns button').forEach(function (x) { x.disabled = !on; });
    $('bExpSig').disabled = $('bExpBursts').disabled = S.n === 0;
    $('pos').disabled = on || S.n === 0;
    if (!on && S.n) {
      $('pos').min = Math.min(S.n, S.span * S.fs); $('pos').max = S.n; $('pos').value = S.n; S.pos = S.n;
      analyze(); renderAll();
    }
  }

  function stop() {
    if (simTimer) { clearInterval(simTimer); simTimer = null; }
    if (reader) { reader.cancel().catch(function () {}); }
    if (game) { game.close(); game = null; }
    if (S.running) setRunning(false);
  }

  function startSim() {
    stop(); notice('');
    reset();
    S.sim = D.makeSim(S.fs, S.nch);
    S.events = S.sim.events;
    // La sesión de práctica arranca ya jugada: reposo, contracción máxima de cada músculo
    // y una tanda de golpeos, para que todas las pantallas tengan datos desde el inicio.
    const cal = function (kind, ch) {
      const from = S.n;
      S.sim.setMode({ kind: kind, ch: ch });
      push(S.sim.next(CAL_SECONDS * S.fs));
      S.sim.setMode(null);
      calibrate({ kind: kind, ch: ch, from: from, until: S.n });
    };
    cal('rest', -1);
    for (let c = 0; c < S.nch; c++) cal('mvc', c);
    push(S.sim.next(PRELOAD_KICKS * 5 * S.fs + Math.round(1.4 * S.fs)));
    lastTick = performance.now();
    simTimer = setInterval(function () {
      const now = performance.now(), count = Math.min(S.fs, Math.round((now - lastTick) * S.fs / 1000));
      if (count < 1) return;
      lastTick = now;
      push(S.sim.next(count));
    }, 40);
    setRunning(true, 'sim');
    analyze(); renderAll();
  }

  async function startSerial() {
    if (!('serial' in navigator)) {
      notice('Este navegador no puede leer el puerto USB. Abre esta misma página en Google Chrome o Edge para conectar el Arduino; los archivos CSV sí funcionan aquí.');
      return;
    }
    stop(); notice('');
    let port;
    try { port = await navigator.serial.requestPort(); await port.open({ baudRate: S.cfg.baud }); }
    catch (e) { notice('No se pudo abrir el puerto: ' + e.message); return; }
    reset();
    setRunning(true, 'serial');
    const dec = new TextDecoder();
    let buf = '', seen = 0, t0 = performance.now(), bad = 0;
    reader = port.readable.getReader();
    try {
      while (S.running) {
        const r = await reader.read();
        if (r.done) break;
        buf += dec.decode(r.value, { stream: true });
        const rows = [];
        let i;
        while ((i = buf.indexOf('\n')) >= 0) {
          const parts = buf.slice(0, i).trim().split(/[,;\t ]+/).map(Number);
          buf = buf.slice(i + 1);
          if (parts.length >= S.nch && parts.every(isFinite)) rows.push(parts.slice(parts.length - S.nch));
          else bad++;
        }
        pushRows(rows);
        seen += rows.length;
        const dt = (performance.now() - t0) / 1000;
        if (dt >= 2) {
          S.rate = seen / dt; seen = 0; t0 = performance.now();
          if (Math.abs(S.rate - S.fs) / S.fs > 0.05)
            notice('Llegan ' + S.rate.toFixed(0) + ' muestras por segundo y la plataforma está configurada a ' + S.fs + ' Hz. Los tiempos y las frecuencias saldrán mal hasta que coincidan (pestaña «Señal y conexión»).');
        }
      }
    } catch (e) { notice('Se perdió la conexión con el Arduino: ' + e.message); }
    try { reader.releaseLock(); await port.close(); } catch (e) { /* ya estaba cerrado */ }
    reader = null;
    if (S.running) setRunning(false);
  }

  // Juego de penales: el puente del juego tiene el puerto del Arduino y nos reparte la señal
  // cruda de A2 (cuádriceps) y A3 (isquiotibiales) a 1 kHz, más los tiros que detectó.
  // Al abrir, el panel se engancha solo al juego; si el juego todavía no está abierto,
  // lo sigue buscando cada 3 s hasta que el técnico elija otra fuente. Sin datos simulados.
  let autoGame = true;
  function probeGame() {
    if (!autoGame || game || S.running) return;
    const p = new EventSource(GAME_URL);
    p.onopen = function () { p.close(); if (autoGame && !game) startGame(); };
    p.onerror = function () { p.close(); };
  }
  setInterval(probeGame, 3000);

  function startGame() {
    stop(); notice('');
    S.cfg.nch = 2; S.cfg.fs = 1000; S.cfg.units = 'adc'; S.cfg.bits = 10; S.cfg.vref = 5;
    reset(); buildForms();
    let seen = 0, t0 = performance.now(), gotData = false, live = false;
    const es = new EventSource(GAME_URL);
    game = es;
    setRunning(true, 'game');
    const waitMsg = function () {
      if (game === es && !gotData) notice('Conectado al juego, pero todavía no llega señal del Arduino. Revisa el cable USB; si el juego acaba de abrir, espera unos segundos.');
    };
    setTimeout(waitMsg, 3000);
    es.onopen = function () { live = true; };
    es.onmessage = function (m) {
      const rows = [];
      m.data.split(';').forEach(function (r) {
        const p = r.split(',').map(Number);
        if (p.length === 2 && isFinite(p[0]) && isFinite(p[1])) rows.push(p);
      });
      if (!gotData && rows.length) { gotData = true; notice(''); }
      pushRows(rows);
      seen += rows.length;
      const dt = (performance.now() - t0) / 1000;
      if (dt >= 2) { S.rate = seen / dt; seen = 0; t0 = performance.now(); }
    };
    es.addEventListener('evento', function (m) {
      const d = m.data, t = S.n / S.fs;
      if (d === 'KICK,1') S.events.push({ t: t, label: 'Golpeo' });                        // tiro del cuádriceps en el juego
      else if (d === 'KICK,2') S.events.push({ t: t, label: 'Tiro A3', info: true });
      else if (d === 'SIN_ARDUINO') notice('El juego está abierto pero el Arduino está desconectado. Conecta el USB; la señal regresa sola.');
      else if (d === 'LISTO' && gotData) notice('');
    });
    es.onerror = function () {
      if (game !== es) return;
      if (!live) {
        stop();
        notice('No encuentro el juego de penales. Ábrelo con «iniciar_todo.command»; el panel se conecta solo en cuanto esté abierto.');
      } else notice('Se perdió la conexión con el juego; reintentando…');
    };
  }

  // ── Sesiones grabadas (Supabase, las sube el puente del juego) ───────────────────────
  let sesList = [];
  function fmtFecha(iso) { return new Date(iso).toLocaleString('es-MX', { dateStyle: 'medium', timeStyle: 'short' }); }
  function fmtDur(x) {
    if (!x.fin) return 'en curso';
    const sec = (new Date(x.fin) - new Date(x.inicio)) / 1000;
    return sec < 90 ? Math.round(sec) + ' s' : (sec / 60).toFixed(1) + ' min';
  }
  function pair(a, d) { return a && (a[0] || a[1]) ? (a[0] == null ? '—' : fmt(a[0], d)) + ' / ' + (a[1] == null ? '—' : fmt(a[1], d)) : '—'; }

  async function refreshSessions() {
    if (!window.nube) { $('sesNote').textContent = 'Las sesiones grabadas solo se ven en la app de escritorio.'; return; }
    $('sesNote').textContent = 'Cargando sesiones…';
    try { sesList = await window.nube.sesiones(); renderSessions(); }
    catch (e) { $('sesNote').textContent = 'No se pudo leer Supabase: ' + e.message; }
  }

  function renderSessions() {
    const f = $('fPac').value.trim().toLowerCase(), t = $('tSes');
    t.textContent = '';
    const hr = el('tr');
    ['Fecha', 'Paciente', 'Duración', 'Tiros A2 / A3', 'MVC A2 / A3', 'Umbral 70 % A2 / A3', ''].forEach(function (h) { hr.appendChild(el('th', null, h)); });
    t.appendChild(hr);
    const rows = sesList.filter(function (x) { return !f || x.paciente.toLowerCase().indexOf(f) >= 0; });
    rows.forEach(function (x) {
      const tr = el('tr', S.cloud && S.cloud.id === x.id ? { class: 'strong' } : null);
      [fmtFecha(x.inicio), x.paciente, fmtDur(x), x.tiros ? x.tiros[0] + ' / ' + x.tiros[1] : '—', pair(x.mvc, 1), pair(x.umbral, 1)].forEach(function (c, i) {
        tr.appendChild(el('td', i < 3 ? { class: 'txt' } : null, c));
      });
      const td = el('td'), b = el('button', null, 'Abrir');
      b.onclick = function () { openSession(x.id); };
      td.appendChild(b); tr.appendChild(td); t.appendChild(tr);
    });
    $('sesNote').textContent = rows.length
      ? rows.length + ' sesión(es). MVC y umbrales en cuentas de la envolvente del Arduino (umbral = 70 % de la MVC medida en el juego). Una sesión «en curso» se abre con lo que lleva grabado.'
      : (sesList.length ? 'Ninguna sesión coincide con el filtro.' : 'Todavía no hay sesiones grabadas. Abre el sistema con «iniciar_todo.command» y escribe el código del paciente.');
  }

  async function openSession(id) {
    stop(); autoGame = false;
    notice('Descargando la sesión…');
    let d;
    try { d = await window.nube.sesion(id); }
    catch (e) { notice('No se pudo descargar la sesión: ' + e.message); return; }
    S.cfg.nch = 2; S.cfg.fs = d.sesion.fs || 1000; S.cfg.units = 'adc'; S.cfg.bits = 10; S.cfg.vref = 5;
    S.src = 'nube'; S.cloud = d.sesion;
    reset(); buildForms();
    let next = 0, gaps = 0;
    d.bloques.forEach(function (b) {
      if (b.n0 > next && S.n) {                           // se perdió un tramo: se rellena plano para no mover los tiempos
        const k = Math.min(b.n0 - next, 60 * S.fs), last = [S.data[0][S.n - 1], S.data[1][S.n - 1]];
        push([new Float32Array(k).fill(last[0]), new Float32Array(k).fill(last[1])]);
        gaps += k;
      }
      const rows = new Array(b.a2.length);
      for (let i = 0; i < b.a2.length; i++) rows[i] = [b.a2[i] / 10, b.a3[i] / 10];
      pushRows(rows);
      next = b.n0 + b.a2.length;
    });
    d.eventos.forEach(function (e) {
      const t = e.muestra / S.fs;
      if (e.tipo === 'KICK' && e.valor === '1') S.events.push({ t: t, label: 'Golpeo' });
      else if (e.tipo === 'KICK' && e.valor === '2') S.events.push({ t: t, label: 'Tiro A3', info: true });
    });
    setRunning(false, 'nube');
    if (!S.n) { notice('La sesión no tiene señal grabada.'); return; }
    notice('Sesión de ' + d.sesion.paciente + ' · ' + fmtFecha(d.sesion.inicio) + ' · ' + (S.n / S.fs / 60).toFixed(1) + ' min · '
      + S.events.filter(function (e) { return e.label === 'Golpeo'; }).length + ' tiros de A2 marcados por el juego'
      + (gaps ? ' · faltaron ' + (gaps / S.fs).toFixed(1) + ' s de señal (se rellenaron planos)' : '') + '.');
    renderSessions();
    document.querySelector('#nav button[data-t=resumen]').click();
  }

  function loadCsv(text) {
    stop(); notice('');
    const rows = [];
    text.split(/\r?\n/).forEach(function (line) {
      const p = line.trim().split(/[,;\t]+/).map(Number);
      if (p.length && p.every(isFinite) && line.trim() !== '') rows.push(p);
    });
    if (rows.length < 50) { notice('El archivo no tiene suficientes filas numéricas (se encontraron ' + rows.length + ').'); return; }
    let cols = rows[0].length, first = 0, fs = S.cfg.fs, timed = false;
    if (cols > 1) {
      let mono = true;
      for (let i = 1; i < Math.min(rows.length, 500); i++) if (!(rows[i][0] > rows[i - 1][0])) { mono = false; break; }
      if (mono) {                                          // la primera columna es el tiempo
        const d = [];
        for (let i = 1; i < Math.min(rows.length, 2000); i++) d.push(rows[i][0] - rows[i - 1][0]);
        d.sort(function (a, b) { return a - b; });
        const med = d[d.length >> 1];
        fs = med >= 50 ? 1e6 / med : med >= 0.05 ? 1e3 / med : 1 / med;     // µs, ms o s
        first = 1; cols--; timed = true;
      }
    }
    S.cfg.nch = cols >= 4 ? 4 : cols >= 2 ? 2 : 1;
    S.cfg.fs = Math.round(fs);
    reset();
    pushRows(rows.map(function (r) { return r.slice(first, first + S.nch); }));
    buildForms();
    S.src = 'csv';
    setRunning(false, 'csv');
    notice('Archivo cargado: ' + rows.length + ' muestras, ' + S.nch + ' canal(es), ' + S.fs + ' Hz '
      + (timed ? '(calculados de la columna de tiempo)' : '(tomados de la configuración; el archivo no trae columna de tiempo)')
      + '. Las amplitudes se interpretan como «' + $('unitsSel').selectedOptions[0].textContent + '».');
  }

  // ── Calibración y marcas ─────────────────────────────────────────────────────────────
  function startCapture(kind, ch) {
    if (!S.running || S.capture) return;
    S.capture = { kind: kind, ch: ch, from: S.n, until: S.n + CAL_SECONDS * S.fs };
    if (S.sim && S.src === 'sim') S.sim.setMode({ kind: kind, ch: ch });
  }

  function finishCapture() {
    const c = S.capture;
    S.capture = null;
    if (S.sim) S.sim.setMode(null);
    calibrate(c);
  }

  function calibrate(c) {
    const skip = Math.round(0.3 * S.fs);
    for (let ch = 0; ch < S.nch; ch++) {
      if (c.kind === 'mvc' && ch !== c.ch) continue;
      const P = D.preprocess(S.data[ch].subarray(c.from, c.until), S.fs, S.cfg), e = P.env, a = skip, b = e.length - skip;
      if (c.kind === 'rest') {
        const mu = D.mean(e, a, b);
        let v = 0;
        for (let i = a; i < b; i++) v += (e[i] - mu) * (e[i] - mu);
        S.rest[ch] = { mu: mu, sd: Math.sqrt(v / (b - a)), rms: D.timeFeatures(P.filt, S.fs, a, b).rms };
      } else {
        const w = Math.round(0.5 * S.fs);                  // mejor medio segundo sostenido
        let best = 0, sum = 0;
        for (let i = a; i < b; i++) { sum += e[i]; if (i - a >= w) sum -= e[i - w]; if (i - a >= w - 1) best = Math.max(best, sum / w); }
        S.mvc[ch] = best;
      }
    }
    S.events.push({ t: c.from / S.fs, label: c.kind === 'rest' ? 'Reposo' : 'MVC', cal: true });
  }

  // ── Análisis de la sesión ────────────────────────────────────────────────────────────
  function threshold(ch, env) {
    let base, src;
    if (S.rest[ch]) { base = S.rest[ch]; src = 'calibrado'; }
    else { base = D.baseline(env, S.fs); src = 'estimado'; }
    let mx = 0;
    for (let i = 0; i < env.length; i++) if (env[i] > mx) mx = env[i];
    return { thr: Math.max(base.mu + S.cfg.k * base.sd, S.cfg.floor / 100 * mx), base: base, src: src, max: mx };
  }

  function inCal(t) {                                      // ¿cae dentro de una calibración?
    return S.events.some(function (e) { return e.cal && t >= e.t - 0.1 && t <= e.t + CAL_SECONDS + 0.3; });
  }

  function analyze() {
    const n = S.n, fs = S.fs, cfg = S.cfg;
    if (n < fs) { S.an = null; return; }
    const A = { ch: [], pairs: [], kicks: [], ref: 'marcado' };
    for (let c = 0; c < S.nch; c++) {
      const P = D.preprocess(S.data[c].subarray(0, n), fs, cfg), T = threshold(c, P.env);
      const o = { P: P, thr: T.thr, base: T.base, baseSrc: T.src, maxEnv: T.max, bursts: [] };
      const raw = D.detectBursts(P.env, fs, T.thr, cfg.minDur / 1000, cfg.minGap / 1000);
      let actN = 0, act2 = 0, actPeak = 0;
      raw.forEach(function (b) {
        const i0 = b[0], i1 = b[1], f = D.timeFeatures(P.filt, fs, i0, i1);
        let pk = 0, ip = i0;
        for (let i = i0; i < i1; i++) if (P.env[i] > pk) { pk = P.env[i]; ip = i; }
        const sp = cfg.type === 'raw' && i1 - i0 >= 96 ? D.spectral(D.welch(P.filt, fs, i0, i1), cfg.hp, Math.min(cfg.lp, fs / 2)) : { mnf: NaN, mdf: NaN };
        const ttp = (ip - i0) / fs;
        o.bursts.push({ i0: i0, i1: i1, t0: i0 / fs, t1: i1 / fs, dur: (i1 - i0) / fs, peak: pk, tPeak: ip / fs, ttp: ttp,
          rise: ttp > 0 ? (pk - P.env[i0]) / ttp : NaN, rms: f.rms, mav: f.mav, p2p: f.p2p, iemg: f.iemg, mnf: sp.mnf, mdf: sp.mdf,
          pct: S.mvc[c] ? 100 * pk / S.mvc[c] : NaN, cal: inCal(i0 / fs) });
        actN += i1 - i0; act2 += f.rms * f.rms * (i1 - i0); actPeak = Math.max(actPeak, f.peak);
      });
      let pkMax = 0;
      o.bursts.forEach(function (b) { if (!b.cal && b.peak > pkMax) pkMax = b.peak; });
      o.bursts.forEach(function (b) { b.strong = !b.cal && b.peak >= 0.5 * pkMax; });
      o.tf = D.timeFeatures(P.filt, fs);
      o.actRms = actN ? Math.sqrt(act2 / actN) : NaN;
      o.crest = actN ? actPeak / o.actRms : NaN;
      // Reposo: lo que queda fuera de las contracciones, con 50 ms de margen a cada lado.
      const mask = new Uint8Array(n), guard = Math.round(0.05 * fs);
      raw.forEach(function (b) { mask.fill(1, Math.max(0, b[0] - guard), Math.min(n, b[1] + guard)); });
      let r2 = 0, rn = 0;
      for (let i = 0; i < n; i++) if (!mask[i]) { r2 += P.filt[i] * P.filt[i]; rn++; }
      o.restRms = S.rest[c] ? S.rest[c].rms : rn > fs * 0.2 ? Math.sqrt(r2 / rn) : NaN;
      o.snr = 20 * Math.log10(o.actRms / o.restRms);
      o.activePct = 100 * actN / n;
      // Espectro: solo los tramos de contracción, pegados uno tras otro.
      if (cfg.type === 'raw') {
        let act = P.filt;
        if (actN >= 256) { act = new Float32Array(actN); let k = 0; raw.forEach(function (b) { act.set(P.filt.subarray(b[0], b[1]), k); k += b[1] - b[0]; }); }
        o.psd = D.welch(act, fs);
        o.spec = D.spectral(o.psd, cfg.hp, Math.min(cfg.lp, fs / 2));
        const tail = S.data[c].subarray(Math.max(0, n - 20 * fs), n), m = D.mean(tail), z = new Float32Array(tail.length);
        for (let i = 0; i < z.length; i++) z[i] = tail[i] - m;
        o.hum = D.humFraction(D.welch(z, fs), cfg.mains);
      } else { o.psd = null; o.spec = { mnf: NaN, mdf: NaN, fpk: NaN, ptot: NaN }; o.hum = NaN; }
      // Fatiga: tendencia de las contracciones fuertes.
      const st = o.bursts.filter(function (b) { return b.strong; });
      const idx = st.map(function (b, i) { return i + 1; });
      o.strong = st;
      o.fatMdf = D.linreg(idx.filter(function (x, i) { return isFinite(st[i].mdf); }), st.filter(function (b) { return isFinite(b.mdf); }).map(function (b) { return b.mdf; }));
      o.fatRms = D.linreg(idx, st.map(function (b) { return b.rms; }));
      o.meanPeak = st.length ? st.reduce(function (s, b) { return s + b.peak; }, 0) / st.length : NaN;
      o.meanDur = st.length ? st.reduce(function (s, b) { return s + b.dur; }, 0) / st.length : NaN;
      o.clipPct = S.cfg.units === 'adc' ? 100 * S.clip[c] / n : NaN;
      A.ch.push(o);
    }
    // Entre músculos del mismo muslo: canales (0,1) y, si hay cuatro, (2,3).
    for (let p = 0; p + 1 < S.nch; p += 2) {
      const q = A.ch[p], h = A.ch[p + 1], norm = S.mvc[p] && S.mvc[p + 1];
      const nq = norm ? S.mvc[p] : q.maxEnv || 1, nh = norm ? S.mvc[p + 1] : h.maxEnv || 1;
      const from = Math.max(0, n - 20 * fs);
      A.pairs.push({ q: p, h: p + 1, norm: !!norm,
        ratio: norm ? (h.meanPeak / S.mvc[p + 1]) / (q.meanPeak / S.mvc[p]) : h.actRms / q.actRms,
        cci: D.cci(q.P.env, h.P.env, 0, n, nq, nh),
        lag: D.xcorrLag(q.P.env.subarray(from, n), h.P.env.subarray(from, n), fs, 0.5) });
    }
    // Golpeos: referidos a la marca; si no hay marcas, al pico del cuádriceps de golpeo.
    let refs = S.events.filter(function (e) { return !e.cal && !e.info; }).map(function (e) { return e.t; });
    if (!refs.length) { A.ref = 'pico del cuádriceps'; refs = A.ch[0].strong.map(function (b) { return b.tPeak; }); }
    const pick = function (bs, lo, hi, key) {
      let best = null;
      bs.forEach(function (b) { if (!b.cal && b[key] >= lo && b[key] < hi && (!best || b.peak > best.peak)) best = b; });
      return best;
    };
    refs.forEach(function (t0) {
      const k = { t: t0 }, q = pick(A.ch[0].bursts, t0 - 0.45, t0 + 0.2, 'tPeak');
      k.q = q;
      if (S.nch >= 2) {
        const hb = A.ch[1].bursts;
        k.back = pick(hb, t0 - 0.7, t0 - 0.05, 'tPeak');
        k.brake = pick(hb, t0 - 0.05, t0 + 0.5, 'tPeak');
        const a = Math.max(0, Math.round((t0 - 0.4) * fs)), b = Math.min(n, Math.round((t0 + 0.3) * fs)), pr = A.pairs[0];
        if (b - a > 10) {
          const nq = pr.norm ? S.mvc[0] : A.ch[0].maxEnv || 1, nh = pr.norm ? S.mvc[1] : A.ch[1].maxEnv || 1;
          k.cci = D.cci(A.ch[0].P.env, A.ch[1].P.env, a, b, nq, nh);
          k.ratio = pr.norm ? (D.mean(A.ch[1].P.env, a, b) / nh) / (D.mean(A.ch[0].P.env, a, b) / nq)
                            : D.timeFeatures(A.ch[1].P.filt, fs, a, b).rms / D.timeFeatures(A.ch[0].P.filt, fs, a, b).rms;
        }
      }
      if (t0 * fs < n - 0.5 * fs) A.kicks.push(k);
    });
    // Golpeo promedio: envolventes alineadas al contacto, de 600 ms antes a 400 ms después.
    const w0 = Math.round(0.6 * fs), w1 = Math.round(0.4 * fs), step = Math.max(1, Math.round(fs / 200));
    const good = A.kicks.filter(function (k) { return k.t * fs - w0 >= 0 && k.t * fs + w1 < n && !inCal(k.t); });
    A.ens = null;
    if (good.length >= 2) {
      const m = Math.floor((w0 + w1) / step), E = { t: new Float32Array(m), ch: [], n: good.length, norm: true };
      for (let j = 0; j < m; j++) E.t[j] = (j * step - w0) / fs * 1000;
      for (let c = 0; c < S.nch; c++) {
        const nrm = S.mvc[c] ? S.mvc[c] / 100 : 1, mean = new Float32Array(m), sd = new Float32Array(m), env = A.ch[c].P.env;
        if (!S.mvc[c]) E.norm = false;
        for (let j = 0; j < m; j++) {
          let s1 = 0, s2 = 0;
          good.forEach(function (k) { const u = env[Math.round(k.t * fs) - w0 + j * step] / nrm; s1 += u; s2 += u * u; });
          mean[j] = s1 / good.length; sd[j] = Math.sqrt(Math.max(0, s2 / good.length - mean[j] * mean[j]));
        }
        E.ch.push({ mean: mean, sd: sd });
      }
      A.ens = E;
    }
    const stat = function (xs) {
      xs = xs.filter(isFinite);
      if (!xs.length) return { m: NaN, sd: NaN, n: 0 };
      const mu = xs.reduce(function (p, q) { return p + q; }, 0) / xs.length;
      return { m: mu, sd: Math.sqrt(xs.reduce(function (p, q) { return p + (q - mu) * (q - mu); }, 0) / xs.length), n: xs.length };
    };
    A.kstat = {
      qOn: stat(good.map(function (k) { return k.q ? k.q.t0 - k.t : NaN; })),
      brOn: stat(good.map(function (k) { return k.brake ? k.brake.t0 - k.t : NaN; })),
      lag: stat(good.map(function (k) { return k.q && k.brake ? k.brake.t0 - k.q.t0 : NaN; })),
      cci: stat(good.map(function (k) { return k.cci; })),
      ratio: stat(good.map(function (k) { return k.ratio; }))
    };
    S.an = A;
  }

  function fatRead(o) {
    const n = o.strong.length, ch = function (f) { return 100 * f.slope * (n - 1) / (f.icpt + f.slope); };
    const dm = ch(o.fatMdf), dr = ch(o.fatRms);
    const r = { dm: dm, dr: dr, n: n };
    if (n < 8) { r.text = 'Pocas contracciones para concluir'; r.lvl = 'info'; }
    else if (dm < -8 && dr > 5) { r.text = 'Patrón compatible con fatiga'; r.lvl = 'warn'; }
    else if (dm < -8) { r.text = 'Baja la frecuencia sin subir el RMS'; r.lvl = 'warn'; }
    else { r.text = 'Sin señal de fatiga'; r.lvl = 'ok'; }
    return r;
  }

  // ── Dibujo ───────────────────────────────────────────────────────────────────────────
  function fit(cv) {
    const r = window.devicePixelRatio || 1, w = cv.clientWidth, h = cv.clientHeight;
    if (cv.width !== Math.round(w * r) || cv.height !== Math.round(h * r)) { cv.width = Math.round(w * r); cv.height = Math.round(h * r); }
    const g = cv.getContext('2d');
    g.setTransform(r, 0, 0, r, 0, 0);
    g.clearRect(0, 0, w, h);
    g.font = '11px -apple-system,Segoe UI,sans-serif';
    return { g: g, w: w, h: h };
  }

  function view() {
    const end = S.running ? S.n : Math.max(Math.min(S.n, S.span * S.fs), S.pos || S.n);
    return { a: Math.max(0, end - S.span * S.fs), b: end };
  }

  function drawScopes() {
    const v = view(), fs = S.fs, len = S.span * fs;
    const m = Math.min(v.a, Math.round(0.5 * fs));         // margen para que el filtro no ensucie la orilla
    for (let c = 0; c < S.nch; c++) {
      const cv = $('sc' + c), F = fit(cv), g = F.g, W = F.w - 54, H = F.h, mid = H / 2 + 6, x0 = 44;
      if (v.b - v.a < 8) { g.fillStyle = '#667085'; g.fillText('Sin señal. Esperando al Arduino a través del juego de penales…', x0, mid); continue; }
      const P = D.preprocess(S.data[c].subarray(v.a - m, v.b), fs, S.cfg), n = v.b - v.a;
      const thr = S.an && S.an.ch[c] ? S.an.ch[c].thr : threshold(c, P.env).thr;
      let mx = 20;
      for (let i = m; i < m + n; i++) { const q = Math.abs(P.filt[i]); if (q > mx) mx = q; }
      S.ymax[c] = Math.max(mx * 1.08, S.ymax[c] * 0.985);
      const ym = S.ymax[c], half = (H - 30) / 2, X = function (i) { return x0 + (i / len) * W; }, Y = function (u) { return mid - (u / ym) * half; };
      // contracciones sombreadas
      g.fillStyle = COLORS[c] + '16';
      D.detectBursts(P.env.subarray(m, m + n), fs, thr, S.cfg.minDur / 1000, S.cfg.minGap / 1000).forEach(function (b) { g.fillRect(X(b[0]), 14, X(b[1]) - X(b[0]), H - 28); });
      // rejilla y ejes
      g.strokeStyle = '#e6e8ee'; g.lineWidth = 1; g.beginPath();
      g.moveTo(x0, mid); g.lineTo(x0 + W, mid);
      const tA = v.a / fs, step = S.span <= 10 ? 1 : S.span <= 20 ? 2 : 10;
      g.fillStyle = '#667085';
      for (let t = Math.ceil(tA / step) * step; t <= tA + S.span; t += step) {
        const x = X((t - tA) * fs);
        g.moveTo(x, 14); g.lineTo(x, H - 14);
        g.fillText(t.toFixed(0) + ' s', x + 3, H - 3);
      }
      g.stroke();
      g.fillText('+' + ym.toFixed(0), 4, 24); g.fillText('−' + ym.toFixed(0), 4, H - 16); g.fillText('µV', 4, mid + 4);
      // señal: mínimo y máximo por columna de pantalla
      g.strokeStyle = COLORS[c] + 'b0'; g.beginPath();
      const per = Math.max(1, len / W);
      for (let px = 0; px < W; px++) {
        const i0 = Math.floor(px * per), i1 = Math.min(n, Math.floor((px + 1) * per) + 1);
        if (i0 >= n) break;
        let lo = Infinity, hi = -Infinity;
        for (let i = i0; i < i1; i++) { const q = P.filt[m + i]; if (q < lo) lo = q; if (q > hi) hi = q; }
        g.moveTo(x0 + px + 0.5, Y(hi)); g.lineTo(x0 + px + 0.5, Y(lo) + 0.5);
      }
      g.stroke();
      // envolvente y umbral
      g.strokeStyle = '#0f172a'; g.lineWidth = 1.6; g.beginPath();
      const stepI = Math.max(1, Math.floor(per));
      for (let i = 0; i < n; i += stepI) { const x = X(i), y = Y(P.env[m + i]); if (i) g.lineTo(x, y); else g.moveTo(x, y); }
      g.stroke();
      g.strokeStyle = '#d97706'; g.lineWidth = 1; g.setLineDash([5, 4]); g.beginPath(); g.moveTo(x0, Y(thr)); g.lineTo(x0 + W, Y(thr)); g.stroke(); g.setLineDash([]);
      // marcas
      S.events.forEach(function (e) {
        const i = e.t * fs - v.a;
        if (i < 0 || i > n) return;
        g.strokeStyle = e.cal ? '#059669' : '#dc2626'; g.beginPath(); g.moveTo(X(i), 14); g.lineTo(X(i), H - 14); g.stroke();
        g.fillStyle = g.strokeStyle; g.fillText(e.label, X(i) + 3, 24);
      });
      // nivel instantáneo: RMS de los últimos 200 ms
      const w200 = Math.min(n, Math.round(0.2 * fs)), lf = D.timeFeatures(P.filt, fs, m + n - w200, m + n), live = lf.rms;
      S.live[c] = { rms: lf.rms, pct: S.mvc[c] ? 100 * lf.mav / S.mvc[c] : NaN, rel: Math.min(1, lf.mav / ((S.mvc[c] || (S.an && S.an.ch[c] ? S.an.ch[c].maxEnv : ym)) || 1)) };
      const ref = S.mvc[c] || (S.an && S.an.ch[c] ? S.an.ch[c].maxEnv : ym), lvl = Math.min(1, live / (ref * 1.3));
      g.fillStyle = '#eef0f4'; g.fillRect(F.w - 8, 14, 5, H - 28);
      g.fillStyle = COLORS[c]; g.fillRect(F.w - 8, 14 + (H - 28) * (1 - lvl), 5, (H - 28) * lvl);
      g.fillStyle = COLORS[c]; g.font = '600 12px -apple-system,Segoe UI,sans-serif';
      g.fillText(names()[c], x0 + 4, 11);
      g.fillStyle = '#0f172a'; g.font = '11px ui-monospace,Menlo,monospace';
      const txt = 'RMS ' + live.toFixed(0) + ' µV' + (S.mvc[c] ? ' · ' + S.live[c].pct.toFixed(0) + ' % MVC' : '') + ' · umbral ' + thr.toFixed(0) + ' µV';
      g.fillText(txt, x0 + W - g.measureText(txt).width - 4, 11);
    }
  }

  function drawTimeline() {
    const F = fit($('timeline')), g = F.g, v = view(), fs = S.fs, x0 = 44, W = F.w - 54, row = (F.h - 18) / S.nch;
    g.fillStyle = '#667085'; g.fillText('Orden de activación', x0 + 4, 11);
    if (!S.an) return;
    const X = function (t) { return x0 + (t * fs - v.a) / (S.span * fs) * W; };
    for (let c = 0; c < S.nch; c++) {
      const y = 16 + c * row;
      g.fillStyle = '#eef0f4'; g.fillRect(x0, y + 2, W, row - 5);
      S.an.ch[c].bursts.forEach(function (b) {
        if (b.t1 * fs < v.a || b.t0 * fs > v.b) return;
        const xa = Math.max(x0, X(b.t0)), xb = Math.min(x0 + W, X(b.t1));
        g.fillStyle = COLORS[c] + (b.strong ? 'ff' : '66'); g.fillRect(xa, y + 2, Math.max(1.5, xb - xa), row - 5);
      });
      g.fillStyle = COLORS[c]; g.fillText(S.nch === 4 ? 'C' + (c + 1) : names()[c].slice(0, 5) + '.', 4, y + row / 2 + 3);
    }
    S.events.forEach(function (e) {
      if (e.cal || e.t * fs < v.a || e.t * fs > v.b) return;
      g.strokeStyle = '#dc2626'; g.beginPath(); g.moveTo(X(e.t), 14); g.lineTo(X(e.t), F.h - 2); g.stroke();
    });
  }

  /** Gráfica sencilla: series de líneas o puntos con ejes automáticos. */
  function plot(id, series, opt) {
    const F = fit($(id)), g = F.g, L = 54, R = 12, T = 24, B = 30, W = F.w - L - R, H = F.h - T - B;
    let x0 = Infinity, x1 = -Infinity, y1 = -Infinity, y0 = opt.y0 === undefined ? Infinity : opt.y0;
    series.forEach(function (s) { for (let i = 0; i < s.x.length; i++) { if (!isFinite(s.y[i])) continue; x0 = Math.min(x0, s.x[i]); x1 = Math.max(x1, s.x[i]); y1 = Math.max(y1, s.y[i]); if (opt.y0 === undefined) y0 = Math.min(y0, s.y[i]); } });
    if (opt.x1 !== undefined) { x0 = opt.x0; x1 = opt.x1; }
    if (!isFinite(x0) || !isFinite(y1)) { g.fillStyle = '#667085'; g.fillText(opt.empty || 'Aún no hay datos suficientes.', L, T + 20); return; }
    if (x1 === x0) x1 = x0 + 1;
    const pad = (y1 - y0) * 0.08 || 1;
    y1 += pad; if (opt.y0 === undefined) y0 -= pad;
    const X = function (x) { return L + (x - x0) / (x1 - x0) * W; }, Y = function (y) { return T + H - (y - y0) / (y1 - y0) * H; };
    g.strokeStyle = '#e6e8ee'; g.fillStyle = '#667085'; g.beginPath();
    for (let i = 0; i <= 4; i++) {
      const y = y0 + (y1 - y0) * i / 4, x = x0 + (x1 - x0) * i / 4;
      g.moveTo(L, Y(y)); g.lineTo(L + W, Y(y));
      g.fillText(Math.abs(y) >= 10000 ? (y / 1000).toFixed(0) + ' k' : y.toFixed(y1 - y0 < 5 ? 2 : 0), 4, Y(y) + 4);
      g.fillText(x.toFixed(0), X(x) - 8, T + H + 14);
    }
    g.stroke();
    g.fillText(opt.xl, L + W / 2 - 40, F.h - 3);
    g.fillText(opt.yl, 4, 12);
    series.forEach(function (s) {
      g.strokeStyle = g.fillStyle = s.color; g.lineWidth = s.w || 1.6; g.setLineDash(s.dash || []);
      if (s.points) { for (let i = 0; i < s.x.length; i++) if (isFinite(s.y[i])) { g.beginPath(); g.arc(X(s.x[i]), Y(s.y[i]), 3.2, 0, 7); g.fill(); } }
      else { g.beginPath(); let pen = false; for (let i = 0; i < s.x.length; i++) { if (!isFinite(s.y[i]) || s.x[i] < x0 || s.x[i] > x1) continue; if (pen) g.lineTo(X(s.x[i]), Y(s.y[i])); else { g.moveTo(X(s.x[i]), Y(s.y[i])); pen = true; } } g.stroke(); }
      g.setLineDash([]);
    });
    (opt.vlines || []).forEach(function (l) { if (!isFinite(l.x)) return; g.strokeStyle = l.color; g.setLineDash(l.dash || []); g.beginPath(); g.moveTo(X(l.x), T); g.lineTo(X(l.x), T + H); g.stroke(); g.setLineDash([]); });
  }

  // ── Portada ──────────────────────────────────────────────────────────────────────────
  function drawLeg() {
    const box = $('legNums');
    if (box.childElementCount !== Math.min(2, S.nch)) {
      box.textContent = '';
      for (let c = 0; c < Math.min(2, S.nch); c++) {
        const d = el('div', { class: 'lvl' }), top = el('div', { class: 'top' }), b = el('b', { id: 'lv' + c }, '—');
        top.appendChild(el('span', { class: 'name', style: 'color:' + COLORS[c] }, names()[c].split(' · ')[0])); top.appendChild(b);
        const mt = el('div', { class: 'meter' }); mt.appendChild(el('i', { id: 'lm' + c, style: 'background:' + COLORS[c] }));
        d.appendChild(top); d.appendChild(mt); box.appendChild(d);
      }
    }
    for (let c = 0; c < Math.min(2, S.nch); c++) {
      const L = S.live[c];
      if (!L) continue;
      $(c ? 'legH' : 'legQ').setAttribute('fill-opacity', (0.12 + 0.88 * Math.sqrt(L.rel)).toFixed(2));
      const b = $('lv' + c);
      b.textContent = isFinite(L.pct) ? L.pct.toFixed(0) : L.rms.toFixed(0);
      b.appendChild(el('small', null, isFinite(L.pct) ? '% del máximo' : 'µV'));
      $('lm' + c).style.width = (100 * L.rel).toFixed(0) + '%';
    }
    $('legH').style.display = S.nch > 1 ? '' : 'none';
  }

  function drawEnsemble() {
    const F = fit($('pEns')), g = F.g, L = 46, R = 26, T = 22, B = 30, W = F.w - L - R, H = F.h - T - B, E = S.an && S.an.ens;
    if (!E) { g.fillStyle = '#667085'; g.fillText('Se necesitan al menos dos golpeos.', L, T + 20); $('ensNote').textContent = ''; return; }
    let ymax = 1;
    E.ch.forEach(function (c) { for (let j = 0; j < c.mean.length; j++) ymax = Math.max(ymax, c.mean[j] + c.sd[j]); });
    ymax *= 1.08;
    const X = function (t) { return L + (t + 600) / 1000 * W; }, Y = function (v) { return T + H - v / ymax * H; };
    g.strokeStyle = '#e6e8ee'; g.fillStyle = '#667085'; g.beginPath();
    for (let i = 0; i <= 4; i++) { const y = ymax * i / 4; g.moveTo(L, Y(y)); g.lineTo(L + W, Y(y)); g.fillText(y.toFixed(0), 6, Y(y) + 4); }
    for (let t = -600; t <= 400; t += 200) { g.moveTo(X(t), T); g.lineTo(X(t), T + H); g.fillText((t > 0 ? '+' : '') + t, X(t) - 12, T + H + 14); }
    g.stroke();
    g.fillText(E.norm ? '% del máximo' : 'µV', 6, 12);
    g.fillText('milisegundos respecto del contacto', L + W / 2 - 90, F.h - 3);
    for (let c = Math.min(E.ch.length, S.nch) - 1; c >= 0; c--) {
      const q = E.ch[c], dim = c > 1;
      g.fillStyle = COLORS[c] + (dim ? '10' : '22'); g.beginPath();
      for (let j = 0; j < q.mean.length; j++) { const x = X(E.t[j]), y = Y(q.mean[j] + q.sd[j]); if (j) g.lineTo(x, y); else g.moveTo(x, y); }
      for (let j = q.mean.length - 1; j >= 0; j--) g.lineTo(X(E.t[j]), Y(Math.max(0, q.mean[j] - q.sd[j])));
      g.fill();
      g.strokeStyle = COLORS[c]; g.lineWidth = dim ? 1.2 : 2.4; g.beginPath();
      for (let j = 0; j < q.mean.length; j++) { const x = X(E.t[j]), y = Y(q.mean[j]); if (j) g.lineTo(x, y); else g.moveTo(x, y); }
      g.stroke();
    }
    g.lineWidth = 1; g.strokeStyle = '#dc2626'; g.beginPath(); g.moveTo(X(0), T - 6); g.lineTo(X(0), T + H); g.stroke();
    g.fillStyle = '#dc2626'; g.fillText('contacto', X(0) + 4, T + 2);
    const K = S.an.kstat, mark = function (t, c, label) {
      if (!isFinite(t)) return;
      g.strokeStyle = g.fillStyle = COLORS[c]; g.setLineDash([3, 3]); g.beginPath(); g.moveTo(X(t * 1000), T + 10); g.lineTo(X(t * 1000), T + H); g.stroke(); g.setLineDash([]);
      g.fillText(label, X(t * 1000) - g.measureText(label).width - 4, T + 20 + c * 13);
    };
    mark(K.qOn.m, 0, 'se enciende');
    if (S.nch > 1) mark(K.brOn.m, 1, 'frena');
    $('ensNote').textContent = 'Promedio de ' + E.n + ' golpeos; la franja es la variación de uno a otro (±1 desviación estándar). '
      + names().slice(0, 2).map(function (x, c) { return (c ? 'Naranja: ' : 'Azul: ') + x.toLowerCase(); }).join('. ') + '.';
  }

  function renderReads() {
    const ul = $('reads'), A = S.an, K = A.kstat, col = { ok: '#059669', warn: '#d97706', bad: '#dc2626', info: '#2563eb' };
    ul.textContent = '';
    const add = function (lvl, title, text) {
      const li = el('li'), d = el('div');
      li.appendChild(el('i', { style: 'background:' + col[lvl] }));
      d.appendChild(el('b', null, title)); d.appendChild(el('span', null, text));
      li.appendChild(d); ul.appendChild(li);
    };
    const snr = Math.min.apply(null, A.ch.map(function (o) { return o.snr; })), ratio = Math.pow(10, snr / 20), hum = Math.max.apply(null, A.ch.map(function (o) { return o.hum || 0; }));
    if (!isFinite(snr)) add('info', 'Calidad de la señal', 'Todavía no hay contracciones para comparar contra el reposo.');
    else add(snr >= 20 ? 'ok' : snr >= 10 ? 'warn' : 'bad', 'Calidad de la señal',
      (snr >= 20 ? 'Limpia: ' : snr >= 10 ? 'Aceptable: ' : 'Pobre: ') + 'la contracción tiene ' + ratio.toFixed(0) + ' veces la amplitud del reposo (' + snr.toFixed(0) + ' dB).'
      + (hum > 0.25 ? ' Entra mucho zumbido de la red eléctrica (' + (100 * hum).toFixed(0) + ' %).' : ''));
    if (S.mvc.slice(0, S.nch).every(Boolean))
      add('info', 'Intensidad', names().map(function (nm, c) { return nm + ': ' + (100 * A.ch[c].meanPeak / S.mvc[c]).toFixed(0) + ' % de su máximo'; }).join(' · ') + ' en el pico de las contracciones fuertes.');
    else add('warn', 'Intensidad', 'Falta calibrar la contracción máxima (botones MVC): sin ella la intensidad queda en microvoltios y no se puede comparar entre músculos.');
    if (K.qOn.n >= 2) {
      const self = A.ref !== 'marcado', steady = K.qOn.sd < 0.03;
      add(steady ? 'ok' : 'warn', 'Secuencia del golpeo', 'El cuádriceps se enciende ' + Math.abs(K.qOn.m * 1000).toFixed(0) + ' ms ' + (K.qOn.m < 0 ? 'antes' : 'después') + (self ? ' de su propio pico' : ' del contacto')
        + (S.nch > 1 && K.lag.n ? ' y el isquiotibial empieza a frenar ' + (K.lag.m * 1000).toFixed(0) + ' ms después que él' : '') + '. '
        + (steady ? 'Gesto repetible' : 'Gesto variable') + ': ±' + (K.qOn.sd * 1000).toFixed(0) + ' ms entre ' + K.qOn.n + ' golpeos.');
    } else add('info', 'Secuencia del golpeo', 'Se necesitan al menos dos golpeos para medir la secuencia.');
    if (A.pairs.length && !isFinite(A.pairs[0].ratio)) add('info', 'Equilibrio del muslo', 'Todavía no hay contracciones de los dos músculos para compararlos.');
    else if (A.pairs.length) {
      const p = A.pairs[0], c = isFinite(K.cci.m) ? K.cci.m : p.cci, r = p.ratio;
      add(p.norm ? 'info' : 'warn', 'Equilibrio del muslo', 'El isquiotibial se activa ' + r.toFixed(2) + ' veces lo que el cuádriceps (razón I:C) y durante el golpeo coinciden ' + c.toFixed(0) + ' % del tiempo: '
        + (c < 25 ? 'se alternan con claridad.' : c < 50 ? 'se traslapan en parte.' : 'trabajan casi a la vez.') + (p.norm ? '' : ' Sin MVC, la razón solo sirve para seguir al mismo jugador.'));
    }
    A.ch.forEach(function (o, c) {
      const f = fatRead(o);
      add(f.lvl, 'Fatiga · ' + names()[c], f.text + (f.n >= 8 ? ': la frecuencia mediana cambió ' + f.dm.toFixed(0) + ' % y el RMS ' + (f.dr >= 0 ? '+' : '') + f.dr.toFixed(0) + ' % en ' + f.n + ' contracciones.' : '.'));
    });
    const kicks = A.kicks.length, bursts = A.ch.reduce(function (p, o) { return p + o.bursts.length; }, 0), line = $('sessionLine');
    line.textContent = '';
    [['Sesión', (S.n / S.fs / 60).toFixed(1) + ' min'], ['Golpeos', String(kicks)], ['Contracciones', String(bursts)], ['Muestras', (S.n * S.nch).toLocaleString('es-MX')], ['Muestreo', S.fs + ' Hz']].forEach(function (q, i) {
      const sp = el('span', null, q[0]); sp.appendChild(el('b', null, q[1])); line.appendChild(sp);
    });
  }

  // ── Tablas y tarjetas ────────────────────────────────────────────────────────────────
  const GLOSS = [
    ['RMS', 'Valor eficaz', '√( Σx² / N )', 'Cuánta señal eléctrica produce el músculo: la medida estándar de intensidad de activación. Sube con el esfuerzo y con la fatiga.'],
    ['MAV', 'Valor absoluto medio', 'Σ|x| / N', 'Otra medida de intensidad, menos sensible a picos sueltos que el RMS.'],
    ['Pico a pico', 'Amplitud pico a pico', 'máx(x) − mín(x)', 'Distancia entre el punto más alto y el más bajo de la señal. Muy sensible a artefactos de movimiento: si se dispara sola, revisa los electrodos.'],
    ['Pico', 'Pico de la envolvente', 'máx(envolvente)', 'El momento de mayor activación dentro de la contracción.'],
    ['% MVC', 'Porcentaje de la contracción máxima voluntaria', '100 · pico / MVC', 'Qué tan cerca de su máximo trabajó el músculo. Es la única forma válida de comparar dos músculos o dos días distintos.'],
    ['Inicio', 'Tiempo de activación', 'envolvente > reposo + k·DE', 'Instante en que el músculo se enciende: la envolvente supera el nivel de reposo más k desviaciones estándar (k = 3 por defecto) durante el tiempo mínimo.'],
    ['Duración', 'Duración de la contracción', 't_fin − t_inicio', 'Cuánto tiempo estuvo encendido el músculo.'],
    ['T. al pico', 'Tiempo al pico', 't_pico − t_inicio', 'Qué tan rápido llega a su máximo. En un golpeo explosivo es corto.'],
    ['Subida', 'Tasa de subida de la activación', '(pico − nivel al inicio) / t. al pico', 'Velocidad con la que crece la activación, en µV por segundo. Refleja la explosividad del reclutamiento.'],
    ['iEMG', 'EMG integrado', 'Σ|x| / fs', 'Área bajo la señal rectificada: el trabajo eléctrico total de la contracción, en µV·s.'],
    ['MNF', 'Frecuencia media', 'Σ f·P(f) / Σ P(f)', 'Centro de gravedad del espectro. Baja cuando el músculo se fatiga.'],
    ['MDF', 'Frecuencia mediana', 'f que parte la potencia en dos mitades', 'El indicador de fatiga más usado: más estable que la frecuencia media frente al ruido.'],
    ['F. pico', 'Frecuencia pico', 'f donde P(f) es máxima', 'Dónde se concentra la energía. Si cae en 60 Hz, lo que se ve es la red eléctrica, no el músculo.'],
    ['Potencia', 'Potencia total', 'Σ P(f) · Δf', 'Energía de la señal en la banda de análisis, en µV². Equivale al RMS al cuadrado.'],
    ['SNR', 'Relación señal a ruido', '20 · log₁₀( RMS activo / RMS reposo )', 'Qué tan clara es la diferencia entre contracción y reposo. Por debajo de unos 10 dB la detección de inicios deja de ser confiable.'],
    ['Contr./reposo', 'Razón contracción a reposo', 'RMS activo / RMS reposo', 'La misma idea que el SNR, como proporción: «la contracción es N veces el reposo».'],
    ['Cresta', 'Factor de cresta', 'pico / RMS, en contracción', 'Qué tan «picuda» es la señal mientras el músculo trabaja. Un EMG limpio ronda 3 a 6; valores mucho mayores apuntan a artefactos.'],
    ['Cruces', 'Cruces por cero', 'cambios de signo por segundo', 'Medida de frecuencia calculada en el tiempo; baja con la fatiga igual que la frecuencia mediana.'],
    ['Long. de onda', 'Longitud de la forma de onda', 'Σ|xᵢ − xᵢ₋₁|', 'Combina amplitud y frecuencia en un solo número: la «complejidad» de la señal.'],
    ['60 Hz', 'Interferencia de la red', 'potencia en 57–63 Hz / potencia total', 'Cuánta de la señal que llega es zumbido eléctrico. Se mide antes del filtro de la plataforma, así que describe la calidad del circuito y del montaje.'],
    ['Saturación', 'Muestras saturadas', 'muestras en 0 o en el máximo del convertidor', 'Si no es cero, la ganancia es demasiada o el nivel de continua está mal centrado, y las amplitudes medidas se quedan cortas.'],
    ['I:C', 'Razón isquiotibiales a cuádriceps', 'activación isquio / activación cuádriceps', 'Equilibrio entre el músculo que frena y el que golpea. Es una razón de activación eléctrica, no de fuerza, y solo es comparable entre jugadores si se calibró el MVC.'],
    ['Co-contracción', 'Índice de co-contracción (Falconer y Winter)', '2 · área común / área total · 100', 'Cuánto trabajan los dos músculos al mismo tiempo. 0 % es alternancia perfecta; 100 % es activación idéntica.'],
    ['Desfase', 'Desfase entre músculos', 'retraso que maximiza la correlación de envolventes', 'Cuánto después del cuádriceps se activa el isquiotibial. En el golpeo, el isquiotibial frena la pierna después del contacto.'],
    ['Tendencia', 'Pendiente de fatiga', 'regresión lineal sobre las contracciones', 'Cuánto cambia la frecuencia mediana o el RMS de una contracción a la siguiente. R² indica qué tan creíble es la tendencia.']
  ];

  function renderCards() {
    const box = $('cards');
    box.textContent = '';
    if (!S.an) { box.appendChild(el('p', { class: 'note' }, 'Las métricas aparecen después del primer segundo de señal.')); return; }
    const kpi = function (parent, value, unit, label) {
      const d = el('div', { class: 'kpi' }), b = el('b', null, value);
      if (unit) b.appendChild(el('small', null, unit));
      d.appendChild(b); d.appendChild(el('span', null, label)); parent.appendChild(d);
    };
    S.an.ch.forEach(function (o, c) {
      const card = el('div', { class: 'card' }), h = el('h3');
      h.appendChild(el('i', { style: 'background:' + COLORS[c] })); h.appendChild(document.createTextNode(names()[c]));
      card.appendChild(h);
      const k = el('div', { class: 'kpis' });
      kpi(k, fmt(o.actRms, 0), 'µV', 'RMS en contracción');
      kpi(k, fmt(o.tf.p2p, 0), 'µV', 'Pico a pico');
      kpi(k, S.mvc[c] ? fmt(100 * o.meanPeak / S.mvc[c], 0) : '—', '% MVC', 'Pico medio');
      kpi(k, fmt(o.spec.mdf, 0), 'Hz', 'Frecuencia mediana');
      kpi(k, fmt(o.spec.mnf, 0), 'Hz', 'Frecuencia media');
      kpi(k, fmt(o.snr, 1), 'dB', 'Señal a ruido');
      kpi(k, String(o.strong.length), '', 'Contracciones fuertes');
      kpi(k, fmt(o.meanDur * 1000, 0), 'ms', 'Duración media');
      kpi(k, fmt(o.activePct, 0), '%', 'Tiempo activo');
      card.appendChild(k);
      card.appendChild(el('p', { class: 'note' }, 'Reposo ' + o.baseSrc + ' · umbral ' + fmt(o.thr, 0) + ' µV · MVC ' + (S.mvc[c] ? fmt(S.mvc[c], 0) + ' µV' : 'sin calibrar')));
      const warn = [];
      if (o.snr < 10) warn.push('La contracción apenas se distingue del reposo (menos de 10 dB).');
      if (o.hum > 0.25) warn.push('Mucho zumbido de ' + S.cfg.mains + ' Hz en la señal que llega (' + (100 * o.hum).toFixed(0) + ' %).');
      if (o.clipPct > 0.1) warn.push('El convertidor se satura en ' + o.clipPct.toFixed(1) + ' % de las muestras.');
      if (warn.length) card.appendChild(el('p', { class: 'note warn' }, warn.join(' ')));
      box.appendChild(card);
    });
    S.an.pairs.forEach(function (p) {
      const card = el('div', { class: 'card' });
      card.appendChild(el('h3', null, 'Equilibrio del muslo' + (S.nch === 4 ? (p.q === 0 ? ' · golpeo' : ' · apoyo') : '')));
      const k = el('div', { class: 'kpis' });
      kpi(k, fmt(p.ratio, 2), '', 'Razón I:C');
      kpi(k, fmt(p.cci, 0), '%', 'Co-contracción');
      kpi(k, ms(p.lag), 'ms', 'Desfase isquio − cuádriceps');
      card.appendChild(k);
      card.appendChild(el('p', { class: 'note' + (p.norm ? '' : ' warn') }, p.norm
        ? 'Normalizado al MVC de cada músculo.'
        : 'Sin MVC calibrado: la razón compara microvoltios de dos músculos distintos y solo sirve para seguir al mismo jugador con los mismos electrodos. Calibra el MVC para poder comparar.'));
      box.appendChild(card);
    });
    if (S.nch === 4) {
      const card = el('div', { class: 'card' }), k = el('div', { class: 'kpis' }), A = S.an.ch;
      card.appendChild(el('h3', null, 'Golpeo frente a apoyo'));
      [[0, 2, 'Cuádriceps'], [1, 3, 'Isquiotibiales']].forEach(function (q) {
        const a = S.mvc[q[0]] && S.mvc[q[1]] ? A[q[0]].meanPeak / S.mvc[q[0]] : A[q[0]].meanPeak, b = S.mvc[q[0]] && S.mvc[q[1]] ? A[q[1]].meanPeak / S.mvc[q[1]] : A[q[1]].meanPeak;
        kpi(k, fmt(200 * (a - b) / (a + b), 0), '%', q[2] + ': diferencia');
      });
      card.appendChild(k);
      card.appendChild(el('p', { class: 'note' }, 'Positivo: trabaja más la pierna de golpeo. En el golpeo las dos piernas hacen tareas distintas, así que no se espera simetría.'));
      box.appendChild(card);
    }
  }

  function renderAll() {
    renderCards();
    const A = S.an, N = names();
    if (!A) return;
    renderReads(); drawEnsemble();
    fillTable('tAll',
      ['Músculo', 'RMS activo', 'RMS reposo', 'Contr./reposo', 'SNR dB', 'MAV', 'Pico a pico', 'Pico', 'Cresta', 'Cruces /s', 'Long. de onda', 'iEMG µV·s', 'MNF Hz', 'MDF Hz', 'F. pico Hz', 'Potencia µV²', '60 Hz %', 'Saturación %', 'Umbral µV', 'MVC µV'],
      A.ch.map(function (o, c) { return { color: COLORS[c], cells: [N[c], fmt(o.actRms), fmt(o.restRms), fmt(o.actRms / o.restRms), fmt(o.snr), fmt(o.tf.mav), fmt(o.tf.p2p, 0), fmt(o.maxEnv, 0), fmt(o.crest, 2),
        fmt(o.tf.zc / (S.n / S.fs), 0), fmt(o.tf.wl, 0), fmt(o.tf.iemg, 1), fmt(o.spec.mnf), fmt(o.spec.mdf), fmt(o.spec.fpk, 0), fmt(o.spec.ptot, 0), fmt(100 * o.hum, 0), fmt(o.clipPct, 2), fmt(o.thr), fmt(S.mvc[c] || NaN)] }; }));

    const rows = [];
    A.ch.forEach(function (o, c) { o.bursts.forEach(function (b, i) { rows.push({ c: c, b: b, i: i + 1 }); }); });
    rows.sort(function (p, q) { return q.b.t0 - p.b.t0; });
    fillTable('tBursts', ['Músculo', 'N.º', 'Inicio s', 'Fin s', 'Duración ms', 'T. al pico ms', 'Pico µV', '% MVC', 'Subida µV/s', 'RMS µV', 'MAV µV', 'Pico a pico µV', 'iEMG µV·s', 'MNF Hz', 'MDF Hz'],
      rows.slice(0, 400).map(function (r) { const b = r.b; return { color: COLORS[r.c], cls: b.strong ? 'strong' : '', cells: [N[r.c] + (b.cal ? ' (calibración)' : ''), String(r.i), fmt(b.t0, 3), fmt(b.t1, 3), fmt(b.dur * 1000, 0), fmt(b.ttp * 1000, 0), fmt(b.peak, 0), fmt(b.pct, 0), fmt(b.rise, 0), fmt(b.rms, 0), fmt(b.mav, 0), fmt(b.p2p, 0), fmt(b.iemg, 2), fmt(b.mnf, 0), fmt(b.mdf, 0)] }; }));

    const pk = function (b) { return b ? (isFinite(b.pct) ? fmt(b.pct, 0) + ' %' : fmt(b.peak, 0) + ' µV') : '—'; };
    fillTable('tKicks', ['N.º', 'Instante s', 'Cuád. inicio ms', 'Cuád. pico ms', 'Cuád. pico', 'Isq. balanceo inicio ms', 'Isq. frenado inicio ms', 'Isq. frenado pico', 'Frenado − cuád. ms', 'Co-contracción %', 'I:C'],
      A.kicks.map(function (k, i) { return { cells: [String(i + 1), fmt(k.t, 2), k.q ? ms(k.q.t0 - k.t) : '—', k.q ? ms(k.q.tPeak - k.t) : '—', pk(k.q), k.back ? ms(k.back.t0 - k.t) : '—', k.brake ? ms(k.brake.t0 - k.t) : '—', pk(k.brake),
        k.q && k.brake ? ms(k.brake.t0 - k.q.t0) : '—', fmt(k.cci, 0), fmt(k.ratio, 2)] }; }).reverse());
    $('kickNote').textContent = 'Tiempos respecto del golpeo (negativo = antes). Referencia: ' + (A.ref === 'marcado'
      ? (S.src === 'game' ? 'el tiro que detectó el juego de penales (canal A2). El Arduino lo dispara cuando la envolvente lleva 60 ms sobre el umbral del 70 % MVC, así que la marca cae unos 100 a 150 ms después de que empieza la contracción.'
        : 'la marca de golpeo. Con el Arduino la marca se pone a mano con la barra espaciadora, así que arrastra el tiempo de reacción de quien la presiona (unos 150 a 250 ms).')
      : 'el pico del cuádriceps de golpeo, porque no hay marcas; los tiempos del cuádriceps quedan referidos a sí mismo.')
      + ' Co-contracción e I:C se calculan de 400 ms antes a 300 ms después.';

    plot('pSpec', A.ch.filter(function (o) { return o.psd; }).map(function (o) { return { x: o.psd.f, y: o.psd.p, color: COLORS[A.ch.indexOf(o)] }; }),
      { xl: 'Frecuencia (Hz)', yl: 'µV²/Hz', y0: 0, x0: 0, x1: Math.min(500, S.fs / 2), empty: 'El espectro necesita la señal sin rectificar (tipo «cruda»).',
        vlines: [].concat.apply([], A.ch.map(function (o, c) { return [{ x: o.spec.mnf, color: COLORS[c] }, { x: o.spec.mdf, color: COLORS[c], dash: [4, 4] }]; })) });
    fillTable('tSpec', ['Músculo', 'MNF Hz', 'MDF Hz', 'F. pico Hz', 'Potencia µV²', 'Resolución Hz', 'Segmentos', '60 Hz %'],
      A.ch.map(function (o, c) { return { color: COLORS[c], cells: [N[c], fmt(o.spec.mnf), fmt(o.spec.mdf), fmt(o.spec.fpk, 0), fmt(o.spec.ptot, 0), o.psd ? fmt(o.psd.df, 2) : '—', o.psd ? String(o.psd.segments) : '—', fmt(100 * o.hum, 0)] }; }));
    $('specNote').textContent = 'Banda de análisis: ' + S.cfg.hp + ' a ' + Math.min(S.cfg.lp, S.fs / 2) + ' Hz. La columna «60 Hz» se mide sobre la señal que llega, antes del filtro de la plataforma.';

    const ser = function (key) {
      const out = [];
      A.ch.forEach(function (o, c) {
        const x = o.strong.map(function (b, i) { return i + 1; }), fitv = key === 'mdf' ? o.fatMdf : o.fatRms;
        out.push({ x: x, y: o.strong.map(function (b) { return b[key]; }), color: COLORS[c], points: true });
        if (isFinite(fitv.slope) && x.length >= 3) out.push({ x: [1, x.length], y: [fitv.icpt + fitv.slope, fitv.icpt + fitv.slope * x.length], color: COLORS[c], dash: [5, 4] });
      });
      return out;
    };
    plot('pMdf', ser('mdf'), { xl: 'N.º de contracción fuerte', yl: 'Hz' });
    plot('pRms', ser('rms'), { xl: 'N.º de contracción fuerte', yl: 'µV' });
    fillTable('tFat', ['Músculo', 'Contracciones', 'MDF: pendiente Hz/contr.', 'MDF: cambio %', 'MDF: R²', 'RMS: pendiente µV/contr.', 'RMS: cambio %', 'RMS: R²', 'Lectura'],
      A.ch.map(function (o, c) {
        const fr = fatRead(o), n = fr.n, dm = fr.dm, dr = fr.dr, read = fr.text;
        return { color: COLORS[c], txt: [8], cells: [N[c], String(n), fmt(o.fatMdf.slope, 2), fmt(dm, 1), fmt(o.fatMdf.r2, 2), fmt(o.fatRms.slope, 2), fmt(dr, 1), fmt(o.fatRms.r2, 2), read] };
      }));
  }

  // ── Formularios, canales y textos fijos ──────────────────────────────────────────────
  function buildChannels() {
    const sc = $('scopes'), mv = $('mvcBtns');
    sc.textContent = ''; mv.textContent = '';
    names().forEach(function (nm, c) {
      sc.appendChild(el('canvas', { id: 'sc' + c }));
      const b = el('button', { title: 'Contracción máxima de ' + nm + ' durante 3 s' }, 'MVC ' + (S.nch === 4 ? 'C' + (c + 1) : nm.slice(0, 5) + '.'));
      b.disabled = !S.running;
      b.onclick = function () { startCapture('mvc', c); };
      mv.appendChild(b);
    });
  }

  function field(parent, label, key, kind, options) {
    const l = el('label', null, label);
    let i;
    if (kind === 'select') {
      i = el('select');
      options.forEach(function (o) { const op = el('option', { value: o[0] }, o[1]); if (String(S.cfg[key]) === String(o[0])) op.selected = true; i.appendChild(op); });
    } else if (kind === 'check') { i = el('input', { type: 'checkbox' }); i.checked = S.cfg[key]; }
    else { i = el('input', { type: 'number', step: options || 'any' }); i.value = S.cfg[key]; }
    i.onchange = function () {
      const num = kind === 'check' ? i.checked : kind === 'select' && isNaN(Number(i.value)) ? i.value : Number(i.value);
      if (kind !== 'check' && typeof num === 'number' && !isFinite(num)) return;
      S.cfg[key] = num;
      if (!S.running && S.n) { analyze(); renderAll(); draw(); }
      renderChain();
    };
    if (key === 'units') i.id = 'unitsSel';
    l.appendChild(i); parent.appendChild(l);
  }

  function buildForms() {
    const a = $('fAcq'), d = $('fDsp');
    a.textContent = ''; d.textContent = '';
    field(a, 'Canales', 'nch', 'select', [[1, '1 · cuádriceps'], [2, '2 · cuádriceps e isquiotibiales'], [4, '4 · las dos piernas']]);
    field(a, 'Frecuencia de muestreo (Hz)', 'fs', 'num', 1);
    field(a, 'Qué entrega el circuito', 'type', 'select', [['raw', 'Señal cruda amplificada'], ['env', 'Envolvente ya rectificada']]);
    field(a, 'Unidades de los datos', 'units', 'select', [['adc', 'Cuentas del convertidor'], ['v', 'Voltios en el convertidor'], ['uv', 'Microvoltios en el electrodo']]);
    field(a, 'Ganancia total del circuito', 'gain', 'num');
    field(a, 'Voltaje de referencia (V)', 'vref', 'num');
    field(a, 'Bits del convertidor', 'bits', 'num', 1);
    field(a, 'Velocidad del puerto (baudios)', 'baud', 'num', 1);
    field(d, 'Pasa-altas (Hz)', 'hp', 'num');
    field(d, 'Pasa-bajas (Hz)', 'lp', 'num');
    field(d, 'Quitar la red eléctrica', 'notch', 'check');
    field(d, 'Frecuencia de la red (Hz)', 'mains', 'select', [[60, '60'], [50, '50']]);
    field(d, 'Suavizado de la envolvente (Hz)', 'fenv', 'num');
    field(d, 'Umbral: k desviaciones sobre el reposo', 'k', 'num');
    field(d, 'Umbral mínimo (% del pico)', 'floor', 'num');
    field(d, 'Duración mínima (ms)', 'minDur', 'num');
    field(d, 'Unir pausas menores a (ms)', 'minGap', 'num');
    renderChain();
  }

  function renderChain() {
    const g = S.cfg, raw = g.type === 'raw', lp = Math.min(g.lp, g.fs * 0.45);
    fillTable('tChain', ['Bloque', 'Valor de diseño', 'Para qué'], [
      { txt: [1, 2], cells: ['Adquisición', g.nch + ' canal(es) · ' + g.fs + ' Hz · ' + g.bits + ' bits · 0 a ' + g.vref + ' V · resolución ' + (g.vref / (Math.pow(2, g.bits) - 1) / g.gain * 1e6).toFixed(1) + ' µV en el electrodo', 'Señal de EMG digitalizada'] },
      { txt: [1, 2], cells: ['Quitar nivel de continua', 'Resta de la media del tramo', 'Centra la señal en cero'] },
      { txt: [1, 2], cells: ['Pasa-banda', raw ? g.hp + ' a ' + lp.toFixed(0) + ' Hz · Butterworth de 2.º orden aplicado de ida y vuelta (4.º orden efectivo, fase cero)' : 'No se aplica: la envolvente ya viene del circuito', 'Quita artefactos de movimiento y ruido de alta frecuencia'] },
      { txt: [1, 2], cells: ['Rechaza-banda', raw && g.notch ? g.mains + ' Hz · Q = 30 · ida y vuelta' : 'Apagado', 'Quita el zumbido de la red eléctrica'] },
      { txt: [1, 2], cells: ['Rectificación', raw ? 'Onda completa (valor absoluto)' : 'Hecha en el circuito', 'Señal no negativa'] },
      { txt: [1, 2], cells: ['Envolvente lineal', 'Pasa-bajas de ' + g.fenv + ' Hz · Butterworth de 2.º orden de ida y vuelta', 'Tendencia e intensidad de la contracción'] },
      { txt: [1, 2], cells: ['Detección de activación', 'Reposo + ' + g.k + ' DE, mínimo ' + g.floor + ' % del pico · ' + g.minDur + ' ms mínimos · une pausas de ' + g.minGap + ' ms', 'Inicio, fin y duración'] },
      { txt: [1, 2], cells: ['Salida de intensidad', 'RMS, MAV, pico a pico e iEMG sobre la señal filtrada sin rectificar; pico y % MVC sobre la envolvente', 'Activación e intensidad'] },
      { txt: [1, 2], cells: ['Salida de frecuencia', raw ? 'Welch · ventana de Hann · 50 % de traslape · sobre la señal filtrada sin rectificar' : 'No disponible con envolvente', 'Espectro, MNF y MDF'] }
    ]);
    const n = S.cfg.nch;
    $('sketch').textContent =
      '// EMG de muslo: ' + n + ' canal(es) a ' + S.cfg.fs + ' Hz por el puerto USB.\n' +
      '// A0 = cuádriceps' + (n > 1 ? ', A1 = isquiotibiales' : '') + (n > 2 ? ', A2 y A3 = pierna de apoyo' : '') + '\n' +
      'const uint8_t CANALES = ' + n + ';\n' +
      'const unsigned long PERIODO_US = ' + Math.round(1e6 / S.cfg.fs) + ';\n' +
      'unsigned long siguiente;\n\n' +
      'void setup() {\n' +
      '  Serial.begin(' + S.cfg.baud + ');\n' +
      '  ADCSRA = (ADCSRA & 0xF8) | 0x05;   // conversión rápida (unos 26 µs por lectura)\n' +
      '  siguiente = micros();\n' +
      '}\n\n' +
      'void loop() {\n' +
      '  if ((long)(micros() - siguiente) < 0) return;\n' +
      '  siguiente += PERIODO_US;\n' +
      '  for (uint8_t c = 0; c < CANALES; c++) {\n' +
      '    Serial.print(analogRead(A0 + c));\n' +
      '    Serial.print(c + 1 < CANALES ? \',\' : \'\\n\');\n' +
      '  }\n' +
      '}';
  }

  function exportCsv(name, lines) {
    const a = el('a', { href: URL.createObjectURL(new Blob([lines.join('\n')], { type: 'text/csv' })), download: name });
    document.body.appendChild(a); a.click(); a.remove();
  }

  // ── Ciclo principal ──────────────────────────────────────────────────────────────────
  let lastAn = 0;
  function draw() { drawScopes(); drawTimeline(); drawLeg(); }
  function loop() {
    if (S.running) {
      if (S.capture && S.n >= S.capture.until) finishCapture();
      const now = performance.now();
      if (now - lastAn > 1000) { lastAn = now; analyze(); renderAll(); }
      draw();
      const cap = S.capture ? ' · ' + (S.capture.kind === 'rest' ? 'REPOSO' : 'MVC ' + names()[S.capture.ch].toUpperCase()) + ' ' + Math.max(0, (S.capture.until - S.n) / S.fs).toFixed(1) + ' s' : '';
      $('status').textContent = (S.n / S.fs).toFixed(1) + ' s · ' + S.fs + ' Hz' + (isFinite(S.rate) ? ' · fs medida ' + S.rate.toFixed(0) + ' Hz' : '') + cap;
    }
    setTimeout(loop, 66);
  }

  $('bSerial').onclick = function () { autoGame = false; startSerial(); };
  $('bGame').onclick = function () { autoGame = true; S.cloud = null; startGame(); };
  $('bRefresh').onclick = refreshSessions;
  $('fPac').oninput = renderSessions;
  function backToLive() { autoGame = true; S.cloud = null; $('replayBar').classList.remove('on'); startGame(); document.querySelector('#nav button[data-t=resumen]').click(); }
  $('bLive').onclick = backToLive;
  $('bBack').onclick = backToLive;

  // ── Grabación (la hace el puente; el panel solo la enciende y la apaga) ──────────────
  let rec = { grabando: false, sinPuente: true };
  function mmss(t) { return Math.floor(t / 60) + ':' + String(t % 60).padStart(2, '0'); }
  function renderRec() {
    const b = $('bRec');
    b.classList.toggle('on', !!rec.grabando);
    b.textContent = rec.grabando ? '■ Detener grabación' : '● Grabar';
    b.disabled = !!rec.sinPuente;
    $('pac').disabled = !!rec.grabando;
    if (rec.grabando && !$('pac').value) $('pac').value = rec.paciente;
    $('recState').textContent = rec.grabando
      ? '● ' + rec.paciente + ' · ' + mmss(rec.segundos) + (rec.fallas ? ' · sin internet, se sube al volver' : '')
      : (rec.sinPuente ? 'Juego cerrado' : '');
  }
  async function recPoll() {
    try { rec = await (await fetch(BRIDGE + '/estado')).json(); }
    catch (e) { rec = { grabando: false, sinPuente: true }; }
    renderRec();
  }
  $('bRec').onclick = async function () {
    try {
      if (rec.grabando) { await fetch(BRIDGE + '/detener'); setTimeout(function () { if ($('t-sesiones').classList.contains('on')) refreshSessions(); }, 3000); }
      else {
        const p = $('pac').value.trim();
        if (!/^[A-Za-z0-9_-]{1,32}$/.test(p)) { notice('Escribe el código del paciente (letras, números, - o _), por ejemplo P001. Nunca el nombre.'); $('pac').focus(); return; }
        notice('');
        await fetch(BRIDGE + '/grabar?paciente=' + encodeURIComponent(p));
      }
    } catch (e) { notice('No se pudo hablar con el juego: ' + e.message); }
    recPoll();
  };
  setInterval(recPoll, 1000);
  recPoll();
  if (window.nube) window.nube.alProgreso(function (n) { notice('Descargando la sesión… ' + (n / 60).toFixed(1) + ' min'); });
  $('bStop').onclick = stop;
  $('bCsv').onclick = function () { autoGame = false; $('fCsv').click(); };
  $('fCsv').onchange = function () { const f = this.files[0]; if (!f) return; f.text().then(loadCsv); this.value = ''; };
  $('bRest').onclick = function () { startCapture('rest', -1); };
  $('bMark').onclick = function () { if (S.running) S.events.push({ t: S.n / S.fs, label: 'Golpeo' }); };
  document.addEventListener('keydown', function (e) { if (e.code === 'Space' && S.running && !/INPUT|SELECT|BUTTON/.test(e.target.tagName)) { e.preventDefault(); $('bMark').onclick(); } });
  $('span').onchange = function () { S.span = Number(this.value); if (!S.running && S.n) { $('pos').min = Math.min(S.n, S.span * S.fs); draw(); } };
  $('pos').oninput = function () { S.pos = Number(this.value); draw(); };
  $('nav').onclick = function (e) {
    const t = e.target.getAttribute('data-t');
    if (!t) return;
    document.querySelectorAll('#nav button').forEach(function (b) { b.classList.toggle('on', b === e.target); });
    document.querySelectorAll('.tab').forEach(function (s) { s.classList.toggle('on', s.id === 't-' + t); });
    $('viewTitle').textContent = e.target.textContent;
    if (t === 'sesiones') refreshSessions();
    if (S.an) renderAll();
  };
  $('bExpSig').onclick = function () {
    const lines = ['t_s,' + names().map(function (x) { return '"' + x + ' (uV)"'; }).join(',') + ',evento'], ev = {};
    S.events.forEach(function (e) { ev[Math.round(e.t * S.fs)] = e.label; });
    for (let i = 0; i < S.n; i++) { let r = (i / S.fs).toFixed(4); for (let c = 0; c < S.nch; c++) r += ',' + S.data[c][i].toFixed(2); lines.push(r + ',' + (ev[i] || '')); }
    exportCsv('emg_senal.csv', lines);
  };
  $('bExpBursts').onclick = function () {
    if (!S.an) return;
    const lines = ['musculo,n,inicio_s,fin_s,duracion_ms,t_al_pico_ms,pico_uV,pct_MVC,subida_uV_s,RMS_uV,MAV_uV,pico_a_pico_uV,iEMG_uVs,MNF_Hz,MDF_Hz,fuerte'];
    S.an.ch.forEach(function (o, c) { o.bursts.forEach(function (b, i) { if (!b.cal) lines.push(['"' + names()[c] + '"', i + 1, b.t0.toFixed(4), b.t1.toFixed(4), (b.dur * 1000).toFixed(0), (b.ttp * 1000).toFixed(0), b.peak.toFixed(1), isFinite(b.pct) ? b.pct.toFixed(1) : '', isFinite(b.rise) ? b.rise.toFixed(0) : '', b.rms.toFixed(1), b.mav.toFixed(1), b.p2p.toFixed(1), b.iemg.toFixed(3), isFinite(b.mnf) ? b.mnf.toFixed(1) : '', isFinite(b.mdf) ? b.mdf.toFixed(1) : '', b.strong ? 1 : 0].join(',')); }); });
    exportCsv('emg_contracciones.csv', lines);
  };
  window.addEventListener('resize', function () { if (!S.running) { draw(); if (S.an) renderAll(); } });

  fillTable('gloss', ['Abreviatura', 'Nombre', 'Cómo se calcula', 'Cómo leerlo'], GLOSS.map(function (r) { return { cells: r }; }));
  reset();
  buildForms();
  setRunning(false, 'none');
  draw();
  loop();
  window.__emg = { S: S, startSim: startSim, startGame: startGame, openSession: openSession, refreshSessions: refreshSessions, stop: stop, startCapture: startCapture, loadCsv: loadCsv };    // para pruebas
  notice('Buscando el juego de penales… Abre todo con «iniciar_todo.command»; el panel se conecta solo.');
  probeGame();
})();
