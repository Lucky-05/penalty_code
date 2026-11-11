"""Sube cada sesión de EMG a Supabase en tiempo real (1 bloque por segundo).

Tablas (proyecto Supabase, esquema public):
  sesiones  una fila por sesión: paciente (código, nunca el nombre), inicio, fin, umbrales,
            MVC (umbral / 0.7) y tiros por canal.
  bloques   1000 muestras crudas de A2 y A3 (cuentas de 10 bits x10 en smallint) más las
            envolventes y umbrales que mandó el Arduino ese segundo. n0 = índice de la primera.
  eventos   KICK,1 / KICK,2 / CAL / LISTO / UMBRAL / U1= / U2= ... con su número de muestra.

Las credenciales se leen de ~/.config/emg-penales/supabase.env (SUPABASE_URL, SUPABASE_KEY);
nunca van en el código. Si no hay archivo o no hay internet, el juego sigue igual: los envíos
se encolan y se reintentan; si la cola pasa de una hora, se descartan los más viejos.
"""
import json
import os
import queue
import threading
import time
import urllib.error
import urllib.request

CONFIG = os.path.expanduser("~/.config/emg-penales/supabase.env")
MUESTRAS_POR_BLOQUE = 1000
COLA_MAX = 3600 + 600


def _leer_config():
    cfg = {}
    try:
        with open(CONFIG) as f:
            for linea in f:
                if "=" in linea and not linea.startswith("#"):
                    k, v = linea.strip().split("=", 1)
                    cfg[k] = v
    except FileNotFoundError:
        return None
    if not cfg.get("SUPABASE_URL") or not cfg.get("SUPABASE_KEY"):
        return None
    return cfg


class Nube:
    def __init__(self, paciente, log=print):
        self.log = log
        self.cfg = _leer_config()
        self.activa = self.cfg is not None
        self.paciente = paciente
        self.t0 = time.time()
        self.sesion_id = None
        self.n = 0
        self.a2, self.a3 = [], []
        self.env = ([], [], [], [])
        self.tiros = [0, 0]
        self.umbral = [None, None]
        self.cola = queue.Queue()
        self.enviados = 0
        self.fallas = 0
        if not self.activa:
            self.log("nube: sin " + CONFIG + "; la sesión no se sube")
            return
        threading.Thread(target=self._trabajar, daemon=True, name="Supabase").start()
        self._encolar("sesion", None)

    # ── Lo que llama el puente ────────────────────────────────────────────────────────
    def muestra(self, linea):
        if not self.activa:
            return
        try:
            a, b = linea.split(",")
            self.a2.append(int(round(float(a) * 10)))
            self.a3.append(int(round(float(b) * 10)))
        except ValueError:
            return
        self.n += 1
        if len(self.a2) >= MUESTRAS_POR_BLOQUE:
            self._cerrar_bloque()

    def envolvente(self, linea):
        if not self.activa:
            return
        try:
            v = [float(x) for x in linea.split(",")[1:5]]
        except ValueError:
            return
        if len(v) == 4:
            for lista, x in zip(self.env, v):
                lista.append(x)

    def evento(self, texto):
        if not self.activa or not texto or not texto.isascii() or not texto.isprintable():
            return
        tipo, _, valor = texto.partition(",")
        if tipo == "UMBRAL":
            try:
                ch, v = valor.split(",")
                self.umbral[int(ch) - 1] = float(v) or None
                self._encolar("patch", self._resumen())
            except (ValueError, IndexError):
                pass
        elif texto.startswith("U1=") or texto.startswith("U2="):
            tipo, valor = texto[:2], texto[3:]
            try:
                ch = int(tipo[1]) - 1
                self.umbral[ch] = float(valor) or None
                self._encolar("patch", self._resumen())
            except ValueError:
                pass
        elif texto == "KICK,1":
            self.tiros[0] += 1
        elif texto == "KICK,2":
            self.tiros[1] += 1
        self._encolar("evento", {"muestra": self.n, "tipo": tipo, "valor": valor or None})

    def cerrar(self):
        if not self.activa:
            return
        self._cerrar_bloque()
        r = self._resumen()
        r["fin"] = time.strftime("%Y-%m-%dT%H:%M:%S%z")
        self._encolar("patch", r)
        limite = time.time() + 15
        while self.cola.unfinished_tasks and time.time() < limite:
            time.sleep(0.2)
        self.log(f"nube: sesión cerrada ({self.enviados} envíos, {self.cola.unfinished_tasks} pendientes)")

    # ── Internos ──────────────────────────────────────────────────────────────────────
    def _resumen(self):
        mvc = [round(u / 0.7, 1) if u else None for u in self.umbral]
        return {"umbral": self.umbral, "mvc": mvc, "tiros": self.tiros}

    def _cerrar_bloque(self):
        if not self.a2:
            return
        fila = {"n0": self.n - len(self.a2), "a2": self.a2, "a3": self.a3,
                "env1": self.env[0], "umbral1": self.env[1], "env2": self.env[2], "umbral2": self.env[3]}
        self.a2, self.a3 = [], []
        self.env = ([], [], [], [])
        self._encolar("bloque", fila)
        if self.n % 30000 < MUESTRAS_POR_BLOQUE:          # cada ~30 s actualiza los tiros
            self._encolar("patch", self._resumen())

    def _encolar(self, tipo, datos):
        if self.cola.qsize() >= COLA_MAX:
            try:
                self.cola.get_nowait()
                self.cola.task_done()
            except queue.Empty:
                pass
        self.cola.put((tipo, datos))

    def _pedir(self, metodo, ruta, cuerpo, devolver=False):
        req = urllib.request.Request(
            self.cfg["SUPABASE_URL"] + "/rest/v1/" + ruta,
            data=json.dumps(cuerpo).encode(), method=metodo,
            headers={"apikey": self.cfg["SUPABASE_KEY"],
                     "Authorization": "Bearer " + self.cfg["SUPABASE_KEY"],
                     "Content-Type": "application/json",
                     "Prefer": "return=representation" if devolver else "return=minimal"})
        with urllib.request.urlopen(req, timeout=10) as r:
            return json.loads(r.read() or b"null") if devolver else None

    def _trabajar(self):
        espera = 1
        while True:
            tipo, datos = self.cola.get()
            while True:
                try:
                    if self.sesion_id is None:
                        fila = self._pedir("POST", "sesiones", {"paciente": self.paciente}, devolver=True)
                        self.sesion_id = fila[0]["id"]
                        self.log(f"nube: sesión {self.sesion_id} · paciente {self.paciente}")
                    if tipo == "sesion":
                        pass
                    elif tipo == "patch":
                        self._pedir("PATCH", "sesiones?id=eq." + self.sesion_id, datos)
                    else:
                        tabla = "bloques" if tipo == "bloque" else "eventos"
                        self._pedir("POST", tabla, dict(datos, sesion_id=self.sesion_id))
                    self.enviados += 1
                    if self.fallas:
                        self.log("nube: conexión recuperada")
                    self.fallas = 0
                    espera = 1
                    break
                except (urllib.error.URLError, OSError, KeyError, IndexError, ValueError) as e:
                    self.fallas += 1
                    if self.fallas in (1, 10) or self.fallas % 60 == 0:
                        detalle = e.read().decode(errors="replace")[:200] if isinstance(e, urllib.error.HTTPError) else e
                        self.log(f"nube: no pude subir ({detalle}); reintento…")
                    if isinstance(e, urllib.error.HTTPError) and 400 <= e.code < 500 and e.code != 429:
                        break                                    # dato rechazado: no se reintenta
                    time.sleep(espera)
                    espera = min(espera * 2, 15)
            self.cola.task_done()
