// Pruebas del motor de cálculo. Correr con:  node pruebas.js
const D = require('./dsp.js');
let fails = 0;
function near(name, got, want, tol) {
  const ok = Math.abs(got - want) <= tol;
  if (!ok) fails++;
  console.log((ok ? 'ok   ' : 'FALLA') + ' ' + name + ': ' + got.toFixed(3) + ' (esperado ' + want + ' ± ' + tol + ')');
}
const fs = 1000, cfg = { type: 'raw', hp: 20, lp: 450, notch: true, fenv: 6, mains: 60 };

// 1. Un seno puro de 100 µV de amplitud: valores que se saben de memoria. Se usa 97.3 Hz
//    y no 100 para que el muestreo no caiga siempre en los mismos puntos del ciclo.
const F0 = 97.3, sine = new Float32Array(4000);
for (let i = 0; i < sine.length; i++) sine[i] = 100 * Math.sin(2 * Math.PI * F0 * i / fs);
const tf = D.timeFeatures(sine, fs);
near('RMS de seno', tf.rms, 70.711, 0.05);
near('MAV de seno', tf.mav, 63.662, 0.1);
near('pico a pico de seno', tf.p2p, 200, 1.5);
near('factor de cresta de seno', tf.crest, 1.4142, 0.01);
near('cruces por cero (2 por ciclo)', tf.zc, 2 * F0 * 4, 3);
const sp = D.spectral(D.welch(sine, fs), 20, 450);
near('MNF de seno', sp.mnf, F0, 1.5);
near('MDF de seno', sp.mdf, F0, 2);
near('frecuencia pico de seno', sp.fpk, F0, 2);
near('potencia total = RMS²', sp.ptot, 5000, 100);

// 2. El preprocesado quita el nivel de continua y el zumbido de 60 Hz, y respeta la señal.
const dirty = new Float32Array(4000);
for (let i = 0; i < dirty.length; i++) dirty[i] = 2.5e6 + sine[i] + 80 * Math.sin(2 * Math.PI * 60 * i / fs);
const P = D.preprocess(dirty, fs, cfg);
near('RMS tras limpiar offset y 60 Hz', D.timeFeatures(P.filt, fs, 500, 3500).rms, 70.711, 2.5);
near('envolvente de seno = MAV', D.mean(P.env, 500, 3500), 63.66, 2.5);
near('zumbido medido antes de filtrar', D.humFraction(D.welch(dirty, fs), 60), 0.39, 0.04);

// 3. Detección de contracciones: tres ráfagas con inicio y duración conocidos.
const n = 8000, burst = new Float32Array(n);
let seed = 7;
const rnd = () => { seed = (seed * 16807) % 2147483647; return seed / 2147483647; };
const g = () => Math.sqrt(-2 * Math.log(rnd())) * Math.cos(2 * Math.PI * rnd());
const truth = [[1.0, 1.4], [3.0, 3.25], [5.5, 6.3]];
for (let i = 0; i < n; i++) {
  const t = i / fs, on = truth.some(b => t >= b[0] && t < b[1]);
  burst[i] = g() * (on ? 200 : 5);
}
const B = D.preprocess(burst, fs, cfg), base = D.baseline(B.env, fs);
const found = D.detectBursts(B.env, fs, Math.max(base.mu + 3 * base.sd, 0.08 * Math.max(...B.env)), 0.03, 0.06);
near('contracciones encontradas', found.length, 3, 0);
found.forEach((b, i) => {
  if (!truth[i]) return;
  near('  inicio ' + (i + 1) + ' (s)', b[0] / fs, truth[i][0], 0.06);
  near('  duración ' + (i + 1) + ' (s)', (b[1] - b[0]) / fs, truth[i][1] - truth[i][0], 0.11);
});
near('RMS dentro de la ráfaga', D.timeFeatures(B.filt, fs, 1050, 1350).rms, 200, 25);

// 4. Co-contracción y retraso entre músculos.
const a = new Float32Array(2000), b2 = new Float32Array(2000);
for (let i = 0; i < 2000; i++) { a[i] = Math.exp(-0.5 * Math.pow((i - 800) / 60, 2)); b2[i] = Math.exp(-0.5 * Math.pow((i - 950) / 60, 2)); }
near('retraso por correlación (s)', D.xcorrLag(a, b2, fs, 0.5), 0.150, 0.006);
near('co-contracción de una señal consigo misma', D.cci(a, a, 0, 2000, 1, 1), 100, 0.01);
near('co-contracción sin traslape', D.cci(a, new Float32Array(2000), 0, 2000, 1, 1), 0, 0.01);
const lr = D.linreg([1, 2, 3, 4], [10, 8, 6, 4]);
near('pendiente de regresión', lr.slope, -2, 1e-9);

// 5. El simulador produce algo con forma de EMG y marca un golpeo cada 5 s.
const sim = D.makeSim(fs, 2), data = sim.next(30 * fs);
near('golpeos marcados en 30 s', sim.events.length, 6, 0);
const S0 = D.preprocess(data[0], fs, cfg), b0 = D.baseline(S0.env, fs);
const sb = D.detectBursts(S0.env, fs, Math.max(b0.mu + 3 * b0.sd, 0.08 * Math.max(...S0.env)), 0.03, 0.06);
const big = sb.filter(q => Math.max(...S0.env.subarray(q[0], q[1])) > 0.5 * Math.max(...S0.env));
near('golpes fuertes de cuádriceps', big.length, 6, 0);
const ss = D.spectral(D.welch(S0.filt, fs, big[0][0], big[0][1]), 20, 450);
console.log('     simulador · MDF del golpe ' + ss.mdf.toFixed(1) + ' Hz · MNF ' + ss.mnf.toFixed(1) + ' Hz · RMS '
  + D.timeFeatures(S0.filt, fs, big[0][0], big[0][1]).rms.toFixed(0) + ' µV');
if (!(ss.mdf > 50 && ss.mdf < 150)) { fails++; console.log('FALLA MDF del simulador fuera del rango de un EMG de superficie'); }
near('inicio del cuádriceps antes del golpeo (s)', sim.events[0].t - big[0][0] / fs, 0.2, 0.12);

console.log(fails ? '\n' + fails + ' PRUEBAS FALLARON' : '\nTodas las pruebas pasaron');
process.exit(fails ? 1 : 0);
