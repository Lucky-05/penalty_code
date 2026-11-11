# penalty_code

Juego de penales controlado por sEMG (electromiografía de superficie) de cuádriceps e isquiotibiales,
con una app para el especialista que muestra la señal en vivo, la analiza y graba sesiones en Supabase.

```
Electrodos → amplificador sEMG → Arduino UNO R4 WiFi (A2, A3)
                                   │ USB 460800 baudios
                                   ▼
                     juego-penales/emg_penal/emg_puente.py
                      ├─ UDP 5005 → juego de Unity (envolventes y tiros)
                      ├─ HTTP 5010 → app técnica (señal cruda a 1 kHz, tiros, grabar/detener)
                      └─ Supabase (sesiones grabadas, emg_nube.py)
```

## Carpetas

| Carpeta | Qué es |
|---|---|
| `juego-penales/emg_penal/emg_penal.ino` | Sketch del Arduino: 2 canales a 1 kHz, pasa-altas 20 Hz, notch 60 Hz + armónicos, envolvente, detección de tiro (≥ 70 % de la MVC sostenido 60 ms, histéresis y refractario de 1.5 s) |
| `juego-penales/emg_penal/emg_puente.py` | Puente serial → juego (UDP) y app técnica (HTTP/SSE). Graba sesiones cuando la app lo pide |
| `juego-penales/emg_penal/emg_nube.py` | Sube cada sesión a Supabase en tiempo real (tablas `sesiones`, `bloques`, `eventos`) |
| `juego-penales/iniciar_todo.command` | Doble clic: abre puente, juego y app técnica |
| `juego-penales/index.html` | Versión web anterior del juego |
| `juego-unity/` | Juego de penales en Unity 6000.3 (HDRP). Configuración EMG con la tecla 8 |
| `plataforma-tecnico/` | «Banco del técnico» (Electron): panel en vivo, botón Grabar, sesiones grabadas |
| `arduino/notch_A1_A0/` | Sketch aparte: notch 60/120/180 Hz de A1 a la salida DAC A0 |

## Cómo correrlo

1. Subir `juego-penales/emg_penal/emg_penal.ino` al UNO R4 WiFi (`arduino:renesas_uno:unor4wifi`).
2. `pip install pyserial` · Unity: compilar `juego-unity` (menú o `ProjectSetup.BuildMac`).
3. App técnica: `cd plataforma-tecnico && npm install && npm start` (o `npm run app` para empaquetarla).
4. Credenciales de Supabase en `~/.config/emg-penales/supabase.env` (`SUPABASE_URL`, `SUPABASE_KEY`).
   **Nunca se suben al repo.**
5. Doble clic en `juego-penales/iniciar_todo.command`.

El MetaHuman del juego (~90 MB) no está en el repo; se regenera con los scripts de MetaHumanBuilder.

## Seguridad

Los pines del Arduino solo leen la salida del amplificador; ningún pin va a los electrodos del paciente.
Con el paciente conectado, la laptop debe ir con batería, desconectada de la corriente.
Las sesiones se guardan con código de paciente (`P001`), nunca con su nombre.
