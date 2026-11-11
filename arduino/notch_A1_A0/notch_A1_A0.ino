const float    FS       = 2000.0;
const uint32_t TS_US    = 500;
const float    F_RED    = 60.0;
const float    Q_NOTCH  = 30.0;
const int      N_NOTCH  = 3;
const int      N_SOBRE  = 4;
const float    VREF     = 5.0;

struct Biquad {
  float b0, b1, b2, a1, a2, z1 = 0, z2 = 0;
  float step(float x) {
    float y = b0 * x + z1;
    z1 = b1 * x - a1 * y + z2;
    z2 = b2 * x - a2 * y;
    return y;
  }
};

void disenarNotch(Biquad &f, float f0, float q) {
  float w = 2 * PI * f0 / FS, c = cos(w), al = sin(w) / (2 * q), a0 = 1 + al;
  f.b0 = 1 / a0;  f.b1 = -2 * c / a0;  f.b2 = 1 / a0;
  f.a1 = -2 * c / a0;  f.a2 = (1 - al) / a0;
}

Biquad notch[N_NOTCH];
uint32_t tSig, tImp = 0;
float entradaV = 0, salidaV = 0;

float leerA1() {
  float s = 0;
  for (int i = 0; i < N_SOBRE; i++) s += analogRead(A1);
  return s / N_SOBRE;
}

void setup() {
  Serial.begin(115200);
  analogReadResolution(14);
  analogWriteResolution(12);
  for (int i = 0; i < N_NOTCH; i++) disenarNotch(notch[i], F_RED * (i + 1), Q_NOTCH);

  float x0 = leerA1();
  for (int k = 0; k < 2000; k++) { float v = x0; for (auto &n : notch) v = n.step(v); }
  tSig = micros();
}

void loop() {
  if ((int32_t)(micros() - tSig) < 0) return;
  tSig += TS_US;

  float x = leerA1();
  float y = x;
  for (auto &n : notch) y = n.step(y);

  int dac = (int)(y / 4.0 + 0.5);
  analogWrite(DAC, constrain(dac, 0, 4095));

  entradaV = x * VREF / 16383.0;
  salidaV  = y * VREF / 16383.0;
  uint32_t ahora = millis();
  if (ahora - tImp >= 10) {
    tImp = ahora;
    Serial.print(entradaV, 3); Serial.print(','); Serial.println(salidaV, 3);
  }
}
