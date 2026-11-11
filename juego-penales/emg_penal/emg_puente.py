#!/usr/bin/env python3
"""Puente Arduino (sEMG) -> juego de penales en Unity + Banco del técnico.

Lee las líneas del sketch emg_penal por serial y:
  * reenvía al juego por UDP a 127.0.0.1:5005 todo lo que no es muestra cruda
    (envolventes "E,...", "KICK,n", "CAL", "LISTO", ...);
  * sirve al Banco del técnico la señal cruda de los dos canales (1 kHz) y los tiros por
    http://127.0.0.1:5010/flujo (Server-Sent Events: "data: a,b;a,b;..." y "event: evento").
Lo que el juego manda a 127.0.0.1:5006 ('c', '+', '-', 'U1=..') se escribe al Arduino.
Si el USB se desconecta, espera a que vuelva y se reconecta solo.

Graba sesiones en Supabase en tiempo real (emg_nube.py) cuando el Banco del técnico lo pide:
  GET /grabar?paciente=P001   GET /detener   GET /estado

Uso:  python3 emg_puente.py [puerto]   (sin puerto, busca el primer usbmodem)
"""
import fcntl
import glob
import json
import os
import re
import signal
import termios
import queue
import socket
import sys
import threading
import time
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from urllib.parse import parse_qs, urlparse

import serial

from emg_nube import Nube

BAUDIOS = int(os.environ.get("EMG_BAUDIOS", 460800))   # igual que BAUDIOS en emg_penal.ino
JUEGO = ("127.0.0.1", 5005)
COMANDOS = ("127.0.0.1", 5006)
PANEL = ("127.0.0.1", 5010)
PATRONES = ("/dev/cu.usbmodem*", "/dev/cu.usbserial*", "/dev/cu.wchusbserial*")
LOTE_S = 0.05                        # el panel recibe las muestras en paquetes de 50 ms


def log(msg):
    print(time.strftime("%H:%M:%S"), msg, flush=True)


# ── Servidor para el Banco del técnico ────────────────────────────────────────────────
class Panel:
    """Reparte la señal a cada pantalla conectada; cada una tiene su propia cola."""

    def __init__(self):
        self.clientes = set()
        self.candado = threading.Lock()
        self.muestras = []
        self.t_lote = time.monotonic()

    def _a_todos(self, texto):
        with self.candado:
            for q in list(self.clientes):
                try:
                    q.put_nowait(texto)
                except queue.Full:            # pantalla colgada: se la desconecta
                    self.clientes.discard(q)

    def muestra(self, linea):
        if not self.clientes:
            return
        self.muestras.append(linea)
        if time.monotonic() - self.t_lote >= LOTE_S:
            self.vaciar()

    def vaciar(self):
        self.t_lote = time.monotonic()
        if self.muestras:
            self._a_todos("data: " + ";".join(self.muestras) + "\n\n")
            self.muestras = []

    def evento(self, linea):
        self.vaciar()                         # el evento cae justo después de sus muestras
        self._a_todos("event: evento\ndata: " + linea + "\n\n")


panel = Panel()
nube = None                          # Nube activa solo mientras se graba
candado_nube = threading.Lock()


def grabar(paciente):
    global nube
    with candado_nube:
        anterior, nube = nube, Nube(paciente, log)
    if anterior:
        threading.Thread(target=anterior.cerrar, daemon=True).start()
    log(f"grabando sesión del paciente {paciente}")


def detener():
    global nube
    with candado_nube:
        anterior, nube = nube, None
    if anterior:
        log("grabación detenida")
        anterior.cerrar()


def estado():
    n = nube
    if not n:
        return {"grabando": False}
    return {"grabando": True, "paciente": n.paciente, "segundos": round(time.time() - n.t0),
            "pendientes": n.cola.unfinished_tasks, "nube": n.activa, "fallas": n.fallas}


class Manejador(BaseHTTPRequestHandler):
    def log_message(self, *a):
        pass

    def _json(self, datos, codigo=200):
        cuerpo = json.dumps(datos).encode()
        self.send_response(codigo)
        self.send_header("Content-Type", "application/json")
        self.send_header("Access-Control-Allow-Origin", "*")
        self.send_header("Content-Length", str(len(cuerpo)))
        self.end_headers()
        self.wfile.write(cuerpo)

    def do_GET(self):
        url = urlparse(self.path)
        if url.path == "/estado":
            return self._json(estado())
        if url.path == "/grabar":
            paciente = parse_qs(url.query).get("paciente", [""])[0].strip()
            if not re.fullmatch(r"[A-Za-z0-9_-]{1,32}", paciente):
                return self._json({"error": "código de paciente inválido"}, 400)
            grabar(paciente)
            return self._json(estado())
        if url.path == "/detener":
            threading.Thread(target=detener, daemon=True).start()
            return self._json({"grabando": False})
        if url.path != "/flujo":
            self.send_error(404)
            return
        self.send_response(200)
        self.send_header("Content-Type", "text/event-stream")
        self.send_header("Cache-Control", "no-cache")
        self.send_header("Access-Control-Allow-Origin", "*")
        self.end_headers()
        q = queue.Queue(maxsize=400)
        with panel.candado:
            panel.clientes.add(q)
        log("Banco del técnico conectado")
        try:
            self.wfile.write(b"event: evento\ndata: HOLA\n\n")
            self.wfile.flush()
            while True:
                try:
                    texto = q.get(timeout=1)
                except queue.Empty:
                    texto = ": sigo aqui\n\n"     # mantiene viva la conexión
                self.wfile.write(texto.encode())
                self.wfile.flush()
        except (BrokenPipeError, ConnectionResetError, OSError):
            pass
        finally:
            with panel.candado:
                panel.clientes.discard(q)
            log("Banco del técnico desconectado")


def servir_panel():
    try:
        srv = ThreadingHTTPServer(PANEL, Manejador)
        srv.daemon_threads = True
        threading.Thread(target=srv.serve_forever, daemon=True).start()
        log(f"Banco del técnico -> http://{PANEL[0]}:{PANEL[1]}/flujo")
    except OSError as e:
        log(f"no pude abrir el puerto del panel ({e}); el juego sigue funcionando")


# ── Arduino ────────────────────────────────────────────────────────────────────────────
def buscar_puerto(preferido):
    if preferido and glob.glob(preferido):
        return preferido
    for patron in PATRONES:
        encontrados = sorted(glob.glob(patron))
        if encontrados:
            return encontrados[0]
    return None


def abrir(preferido, salida):
    """Bloquea hasta que hay un Arduino; avisa al juego y al panel mientras tanto."""
    avisado = False
    while True:
        puerto = buscar_puerto(preferido)
        if puerto:
            try:
                ser = serial.Serial(puerto, BAUDIOS, timeout=0.02)
                try:
                    fcntl.ioctl(ser.fileno(), termios.TIOCEXCL)   # nadie más puede abrir el puerto
                except OSError:
                    pass
                log(f"conectado a {puerto}")
                return ser
            except (serial.SerialException, OSError) as e:
                log(f"no pude abrir {puerto}: {e}")
        elif not avisado:
            log("Arduino desconectado; esperando a que vuelva…")
            avisado = True
        salida.sendto(b"SIN_ARDUINO", JUEGO)
        panel.evento("SIN_ARDUINO")
        time.sleep(1)


def main():
    preferido = sys.argv[1] if len(sys.argv) > 1 else None
    salida = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    entrada = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    entrada.bind(COMANDOS)
    entrada.setblocking(False)
    log(f"Puente EMG -> udp://{JUEGO[0]}:{JUEGO[1]}  (Ctrl+C para salir)")
    servir_panel()

    primera = True
    while True:
        ser = abrir(preferido, salida)
        time.sleep(0.5)
        ser.write(b"S")                  # flujo crudo para el Banco del técnico (se pierde si el Arduino se reinicia)
        if primera:
            ser.write(b"c")              # calibra al arrancar: pierna relajada
            primera = False
        buf = b""
        try:
            while True:
                buf += ser.read(4096)
                *lineas, buf = buf.split(b"\n")
                for linea in lineas:
                    linea = linea.strip()
                    if not linea:
                        continue
                    if linea[:1].isdigit() or linea[:1] == b"-":
                        texto = linea.decode(errors="replace")
                        panel.muestra(texto)
                        g = nube
                        if g:
                            g.muestra(texto)
                        continue
                    salida.sendto(linea, JUEGO)
                    if linea.startswith(b"E,"):
                        g = nube
                        if g:
                            g.envolvente(linea.decode(errors="replace"))
                    else:
                        texto = linea.decode(errors="replace")
                        log(texto)
                        panel.evento(texto)
                        g = nube
                        if g:
                            g.evento(texto)
                        if texto == "LISTO" or texto == "CAL":
                            ser.write(b"S")      # si el Arduino se reinició, vuelve a pedir el flujo
                if time.monotonic() - panel.t_lote >= LOTE_S:
                    panel.vaciar()
                try:
                    cmd, _ = entrada.recvfrom(16)
                    ser.write(cmd)
                    texto = cmd.decode(errors="replace").strip()
                    log("comando del juego: " + texto)
                    g = nube
                    if g:
                        g.evento(texto)
                except BlockingIOError:
                    pass
        except (serial.SerialException, OSError) as e:
            log(f"se perdió el USB ({e}); reconectando…")
            try:
                ser.close()
            except Exception:
                pass
            time.sleep(1)


def _salir(*_):
    raise KeyboardInterrupt


if __name__ == "__main__":
    signal.signal(signal.SIGTERM, _salir)
    signal.signal(signal.SIGHUP, _salir)          # se cerró la ventana de Terminal
    try:
        main()
    except KeyboardInterrupt:
        pass
    finally:
        detener()
