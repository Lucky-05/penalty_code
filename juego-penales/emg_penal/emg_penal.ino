/*
  emg_penal.ino — sEMG de DOS canales -> "KICK,<canal>" solo en picos
    Canal 1 = A2 (equipo local)    Canal 2 = A3 (equipo rival)

  Cadena por canal (fs = 1000 Hz por canal, muestreo con micros()):
    lectura sobremuestreada: ADC a 14 bits, 1 lectura de descarte al cambiar de canal,
       8 lecturas, se tiran la mayor y la menor y se promedian 6 (quita chispazos y baja el
       ruido blanco del ADC ~4x: sd 2.5 -> 0.65 cuentas medido). Se reporta en escala de 10 bits.
    -> pasa-altas 20 Hz -> notch en 60 Hz y TODOS sus armónicos hasta 480 Hz (Q = 30)
    -> |x| -> pasa-bajas 5 Hz (envolvente) -> detector de picos:
       * umbral = el que fija el juego con 'U' (70 % de la contracción máxima medida al inicio);
         si no hay, umbral automático = base + max(K·sd, (RAZON-1)·base)
       * debe sostenerse SOSTENER_MS sobre el umbral (descarta chispazos)
       * histéresis + periodo refractario entre tiros
       * la base y su sd se reajustan lento solo mientras ese canal está en reposo
  (El ruido medido con paciente fue 86 % a 120 Hz + 240/360 Hz: red rectificada.)

  Calibración: 3 s al arrancar (o con 'c'), ambos canales a la vez. Músculos RELAJADOS.

  Protocolo serial (BAUDIOS, ver abajo):
    Arduino -> PC:  "E,<env1>,<umbral1>,<env2>,<umbral2>"  cada 20 ms
                    "KICK,1" / "KICK,2"                       pico en ese canal
                    "CAL" / "LISTO"
                    "<crudo1>,<crudo2>"  cada 1 ms, solo con el flujo encendido ('S'): señal
                               sin filtrar en cuentas de 10 bits para el Banco del técnico.
                               Empieza con dígito; todas las demás líneas con letra.
    PC -> Arduino:  'c' recalibrar ambos
                    "U1=<v>" / "U2=<v>" (fin de línea) umbral fijo del canal en cuentas de
                               envolvente; "U1=0" vuelve al umbral automático de reposo
                    '+' / '-'  canal 1 más / menos exigente
                    ']' / '['  canal 2 más / menos exigente
                    'd' diagnóstico de tiempo: "DIAG,<µs máx>,<resincros>,<crudo1>,<crudo2>"
                    'S' / 's'  enciende / apaga el flujo crudo de los dos canales

  SEGURIDAD: A2 y A3 solo LEEN la salida de los amplificadores sEMG. Ningún pin del
  Arduino va conectado a los electrodos del paciente.
*/

const uint32_t BAUDIOS       = 460800;   // el flujo crudo (2 canales a 1 kHz) no cabe en 115200
const uint32_t TS_US         = 1000;     // periodo de muestreo
const int      BITS_ADC      = 14;       // el R4 tiene ADC de 14 bits
const int      N_SOBRE       = 8;        // lecturas por muestra (≈230 µs por canal)
const float    ESCALA_ADC    = (float)(1 << (BITS_ADC - 10));   // todo se expresa en cuentas de 10 bits
const float    FS            = 1000.0;   // Hz
const float    F_RED         = 60.0;     // Hz (cambia a 50 si la red es de 50 Hz)
const float    Q_NOTCH       = 30.0;
const int      N_NOTCH       = 8;        // 60, 120, … 480 Hz (Nyquist = 500)
const float    F_HP          = 20.0;     // Hz
const float    F_ENV         = 5.0;      // Hz
const float    K_SD          = 8.0;      // desviaciones estándar sobre el reposo
const float    RAZON         = 2.0;      // el pico debe ser al menos 2x el nivel de reposo
const float    UMBRAL_MIN    = 4.0;      // cuentas ADC sobre la base, mínimo absoluto
const uint32_t SOSTENER_MS   = 60;       // tiempo continuo sobre el umbral para contar como pico
const float    HISTERESIS    = 0.6;      // se rearma cuando env < base + 0.6 * (umbral - base)
const uint32_t REFRACT_MS    = 1500;     // tiempo mínimo entre tiros del mismo canal
const uint32_t CAL_MS        = 3000;
const uint32_t REPORTE_MS    = 20;
const float    TAU_BASE_S    = 20.0;     // qué tan lento se reajusta la base en reposo

struct Biquad {
  float b0, b1, b2, a1, a2, z1 = 0, z2 = 0;
  float step(float x) {                 // forma directa II transpuesta
    float y = b0 * x + z1;
    z1 = b1 * x - a1 * y + z2;
    z2 = b2 * x - a2 * y;
    return y;
  }
};

// Coeficientes RBJ (Audio EQ Cookbook)
void disenarNotch(Biquad &f, float f0, float q) {
  float w = 2 * PI * f0 / FS, c = cos(w), al = sin(w) / (2 * q), a0 = 1 + al;
  f.b0 = 1 / a0; f.b1 = -2 * c / a0; f.b2 = 1 / a0;
  f.a1 = -2 * c / a0; f.a2 = (1 - al) / a0;
}
void disenarPasaAltas(Biquad &f, float f0) {
  float w = 2 * PI * f0 / FS, c = cos(w), al = sin(w) / (2 * 0.7071), a0 = 1 + al;
  f.b0 = (1 + c) / 2 / a0; f.b1 = -(1 + c) / a0; f.b2 = (1 + c) / 2 / a0;
  f.a1 = -2 * c / a0; f.a2 = (1 - al) / a0;
}

float aEnv, aBase;

struct Canal {
  uint8_t pin, numero;
  Biquad hp, notch[N_NOTCH];
  float env = 0, base = 0, varBase = 0, exigencia = 1.0;
  float umbralFijo = 0;                  // > 0: lo fijó el juego (70 % de la contracción máxima)
  bool armado = true, sobre = false;
  uint32_t tSobre = 0, tUltimoTiro = 0;
  uint32_t nCal = 0; float sumCal = 0, sum2Cal = 0;
  float crudo = 0, x = 0;

  void iniciar() {
    disenarPasaAltas(hp, F_HP);
    for (int i = 0; i < N_NOTCH; i++) disenarNotch(notch[i], F_RED * (i + 1), Q_NOTCH);
  }
  float filtrar(float v) {
    v = hp.step(v);
    for (auto &n : notch) v = n.step(v);
    return v;
  }
  // Promedio recortado de N_SOBRE lecturas, en cuentas de 10 bits
  float leer() {
    analogRead(pin);                     // descarte: el ADC se asienta tras cambiar de canal
    float suma = 0, mn = 1e9, mx = -1e9;
    for (int i = 0; i < N_SOBRE; i++) {
      float v = analogRead(pin);
      suma += v; mn = min(mn, v); mx = max(mx, v);
    }
    return (suma - mn - mx) / (N_SOBRE - 2) / ESCALA_ADC;
  }
  void muestrear() {
    crudo = leer();
    x = filtrar((float)crudo);
    env += aEnv * (fabs(x) - env);
  }
  float umbral() const {
    if (umbralFijo > 0) return umbralFijo * exigencia;
    float sd = sqrt(max(0.0f, varBase));
    float margen = max(max(K_SD * sd, (RAZON - 1) * base), UMBRAL_MIN);
    return base + margen * exigencia;
  }
  void empezarCal() { nCal = 0; sumCal = sum2Cal = 0; }
  void acumularCal() { nCal++; sumCal += env; sum2Cal += env * env; }
  void terminarCal() {
    if (nCal == 0) return;
    base = sumCal / nCal;
    varBase = max(0.0f, sum2Cal / nCal - base * base);
    armado = true; sobre = false;
  }
  void detectar(uint32_t ahora) {
    float thr = umbral();
    if (env > thr) {
      if (!sobre) { sobre = true; tSobre = ahora; }
      if (armado && ahora - tSobre >= SOSTENER_MS && ahora - tUltimoTiro > REFRACT_MS) {
        Serial.print(F("KICK,")); Serial.println(numero);
        armado = false; tUltimoTiro = ahora;
      }
    } else {
      sobre = false;
      if (!armado && env < base + HISTERESIS * (thr - base)) armado = true;
    }
    // Reajuste lento de la base, solo en reposo (lejos del umbral y fuera del refractario)
    if (armado && !sobre && env < base + 0.5 * (thr - base) && ahora - tUltimoTiro > REFRACT_MS) {
      float d = env - base;
      base += aBase * d;
      varBase += aBase * (d * d - varBase);
    }
  }
};

Canal canales[2];
bool calibrando = false;
bool flujo = false;                      // 'S': manda la señal cruda cada milisegundo
uint32_t tSig, tReporte = 0, tCal = 0;
uint32_t usMax = 0, resincros = 0;       // diagnóstico de tiempo ('d')

void iniciarCalibracion() {
  calibrando = true; tCal = millis();
  for (auto &c : canales) c.empezarCal();
  Serial.println(F("CAL"));
}

void setup() {
  Serial.begin(BAUDIOS);
  Serial.setTimeout(50);                 // para leer el número de 'U' sin bloquear
  analogReadResolution(BITS_ADC);
  canales[0].pin = A2; canales[0].numero = 1;
  canales[1].pin = A3; canales[1].numero = 2;
  for (auto &c : canales) c.iniciar();
  aEnv = 1 - exp(-2 * PI * F_ENV / FS);
  aBase = 1 - exp(-1.0 / (TAU_BASE_S * FS));
  // Precarga: deja que los filtros se asienten 0.5 s antes de calibrar
  for (int i = 0; i < 500; i++) { for (auto &c : canales) c.muestrear(); delayMicroseconds(800); }
  iniciarCalibracion();
  tSig = micros();
}

void loop() {
  if ((int32_t)(micros() - tSig) < 0) return;
  if ((int32_t)(micros() - tSig) > 5000) { tSig = micros(); resincros++; }   // si se atrasó mucho, se resincroniza
  tSig += TS_US;
  uint32_t tInicio = micros();

  for (auto &c : canales) c.muestrear();
  usMax = max(usMax, micros() - tInicio);
  if (flujo) {
    Serial.print(canales[0].crudo, 1); Serial.print(','); Serial.println(canales[1].crudo, 1);
  }
  uint32_t ahora = millis();

  if (calibrando) {
    if (ahora - tCal > 500) for (auto &c : canales) c.acumularCal();   // descarta el primer medio segundo
    if (ahora - tCal >= CAL_MS) {
      for (auto &c : canales) c.terminarCal();
      calibrando = false;
      Serial.println(F("LISTO"));
    }
  } else {
    for (auto &c : canales) c.detectar(ahora);
  }

  if (ahora - tReporte >= REPORTE_MS) {
    tReporte = ahora;
    Serial.print(F("E"));
    for (auto &c : canales) {
      Serial.print(','); Serial.print(c.env, 1);
      Serial.print(','); Serial.print(calibrando ? 0.0 : c.umbral(), 1);
    }
    Serial.println();
  }

  while (Serial.available()) {
    char ch = Serial.read();
    if (ch == 'c') iniciarCalibracion();
    else if (ch == 'U') {
      int n = Serial.parseInt();
      float v = Serial.parseFloat();
      if (n == 1 || n == 2) {
        canales[n - 1].umbralFijo = max(0.0f, v);
        canales[n - 1].exigencia = 1.0;
        canales[n - 1].armado = false;   // se rearma cuando la señal baje del nuevo umbral
        Serial.print(F("UMBRAL,")); Serial.print(n); Serial.print(','); Serial.println(canales[n - 1].umbral(), 1);
      }
    }
    else if (ch == '+') canales[0].exigencia *= 1.15;
    else if (ch == '-') canales[0].exigencia = max(0.3f, canales[0].exigencia / 1.15f);
    else if (ch == ']') canales[1].exigencia *= 1.15;
    else if (ch == '[') canales[1].exigencia = max(0.3f, canales[1].exigencia / 1.15f);
    else if (ch == 'S') flujo = true;
    else if (ch == 's') flujo = false;
    else if (ch == 'd') {                // "DIAG,<µs máx. de lectura+filtros>,<resincronizaciones>,<crudo1>,<crudo2>"
      Serial.print(F("DIAG,")); Serial.print(usMax); Serial.print(','); Serial.print(resincros);
      Serial.print(','); Serial.print(canales[0].crudo, 1); Serial.print(','); Serial.println(canales[1].crudo, 1);
      usMax = 0; resincros = 0;
    }
  }
}
