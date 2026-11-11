using System;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Penales.Game
{
    /// <summary>
    /// Recibe la señal sEMG de dos canales ya procesada por el Arduino (notch 60 Hz y armónicos +
    /// envolvente, ver juego-penales/emg_penal). Un puente en Python (emg_puente.py) reenvía cada
    /// línea serial por UDP a 127.0.0.1:5005. Canal 1 (A2) = equipo local, canal 2 (A3) = rival.
    /// F5 recalibra; + / - mueven el umbral del canal 1 y ] / [ el del canal 2 (se mandan al
    /// puente por el puerto 5006).
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class EmgLink : MonoBehaviour
    {
        public const int ListenPort = 5005;
        public const int CommandPort = 5006;

        /// <summary>Verdadero solo el cuadro en que llegó un pico del canal 1 (A2, equipo local).</summary>
        public bool KickHome { get; private set; }
        /// <summary>Verdadero solo el cuadro en que llegó un pico del canal 2 (A3, equipo rival).</summary>
        public bool KickAway { get; private set; }
        /// <summary>Llegan datos del Arduino: el rival lo juega el canal 2 y no la máquina.</summary>
        public bool Live => Time.unscaledTime - _lastData < 1.5f;

        /// <summary>Envolvente actual del canal (0 = A2, 1 = A3), en cuentas de 10 bits.</summary>
        public float Env(int ch) => _env[ch];
        /// <summary>Umbral con el que el Arduino decide el tiro en ese canal.</summary>
        public float Threshold(int ch) => _thr[ch];
        /// <summary>Cuántas calibraciones de reposo terminaron ("LISTO") desde que arrancó el juego.</summary>
        public int RestCalibrations => _restCalibrations;
        /// <summary>Contracción máxima medida por canal (0 = sin medir).</summary>
        public readonly float[] MaxContraction = new float[2];
        /// <summary>Esconde las barras (la pantalla de configuración dibuja las suyas).</summary>
        public bool ShowOverlay = true;

        /// <summary>Empieza a registrar el pico de la envolvente de un canal.</summary>
        public void BeginPeak(int ch) { _peakChannel = -1; _peak = 0f; _peakChannel = ch; }
        /// <summary>Deja de registrar y devuelve el pico.</summary>
        public float EndPeak() { _peakChannel = -1; return _peak; }
        public float CurrentPeak => _peak;

        public void RecalibrateRest() => Send("c");

        /// <summary>Fija el umbral de un canal en el Arduino (0 = umbral automático de reposo).</summary>
        public void SetThreshold(int ch, float value)
        {
            _fixedThr[ch] = value;
            Send("U" + (ch + 1) + "=" + value.ToString("0.0", CultureInfo.InvariantCulture) + "\n");
        }

        /// <summary>Nombres que se muestran junto a cada barra.</summary>
        public string HomeName = "Local";
        public string AwayName = "Rival";

        private UdpClient _udp;
        private Thread _thread;
        private volatile bool _running;
        private int _pendingHome, _pendingAway;
        private readonly float[] _env = new float[2];
        private readonly float[] _thr = new float[2];
        private volatile string _state = "Esperando puente EMG…";
        private volatile bool _gotPacket, _gotData;
        private float _lastPacket = -999f, _lastData = -999f;
        private volatile int _restCalibrations;
        private volatile bool _resendThresholds;
        private volatile int _peakChannel = -1;
        private volatile float _peak;
        private readonly float[] _fixedThr = new float[2];

        private void OnEnable()
        {
            try
            {
                _udp = new UdpClient(new IPEndPoint(IPAddress.Loopback, ListenPort));
                _running = true;
                _thread = new Thread(Listen) { IsBackground = true, Name = "EMG UDP" };
                _thread.Start();
            }
            catch (Exception e)
            {
                _state = "EMG sin puerto UDP: " + e.Message;
            }
        }

        private void OnDisable()
        {
            _running = false;
            _udp?.Close();
            _udp = null;
        }

        private void Listen()
        {
            IPEndPoint from = new IPEndPoint(IPAddress.Any, 0);
            while (_running)
            {
                try
                {
                    string line = Encoding.ASCII.GetString(_udp.Receive(ref from)).Trim();
                    _gotPacket = true;
                    if (line == "KICK,1") Interlocked.Increment(ref _pendingHome);
                    else if (line == "KICK,2") Interlocked.Increment(ref _pendingAway);
                    else if (line == "CAL") _state = "Calibrando: músculos relajados 3 s…";
                    else if (line == "LISTO")
                    {
                        _state = "Listo: contrae para patear";
                        _restCalibrations++;
                        _resendThresholds = true;    // si el Arduino se reinició, recupera los umbrales del 70 %
                    }
                    else if (line == "SIN_ARDUINO")
                    {
                        _state = "Arduino desconectado: revisa el USB";
                        _env[0] = _env[1] = _thr[0] = _thr[1] = 0f;
                    }
                    else if (line.StartsWith("E,"))
                    {
                        string[] p = line.Split(',');
                        if (p.Length == 5
                            && Parse(p[1], out float e1) && Parse(p[2], out float t1)
                            && Parse(p[3], out float e2) && Parse(p[4], out float t2))
                        {
                            _env[0] = e1; _thr[0] = t1; _env[1] = e2; _thr[1] = t2;
                            int pc = _peakChannel;
                            if (pc >= 0 && _env[pc] > _peak) _peak = _env[pc];
                            _gotData = true;
                            if (_state.StartsWith("Arduino desconectado") || _state.StartsWith("Esperando")) _state = "Conectado";
                        }
                    }
                }
                catch (SocketException) { if (!_running) return; }
                catch (ObjectDisposedException) { return; }
            }
        }

        private static bool Parse(string s, out float v)
        {
            return float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v);
        }

        private void Update()
        {
            KickHome = Interlocked.Exchange(ref _pendingHome, 0) > 0;
            KickAway = Interlocked.Exchange(ref _pendingAway, 0) > 0;
            if (_gotPacket) { _gotPacket = false; _lastPacket = Time.unscaledTime; }
            if (_gotData) { _gotData = false; _lastData = Time.unscaledTime; }
            if (_resendThresholds)
            {
                _resendThresholds = false;
                for (int ch = 0; ch < 2; ch++)
                    if (_fixedThr[ch] > 0f) SetThreshold(ch, _fixedThr[ch]);
            }

            Keyboard k = Keyboard.current;
            if (k == null) return;
            if (k.f5Key.wasPressedThisFrame) Send("c");
            if (k.equalsKey.wasPressedThisFrame || k.numpadPlusKey.wasPressedThisFrame) Send("+");
            if (k.minusKey.wasPressedThisFrame || k.numpadMinusKey.wasPressedThisFrame) Send("-");
            if (k.rightBracketKey.wasPressedThisFrame) Send("]");
            if (k.leftBracketKey.wasPressedThisFrame) Send("[");
        }

        private static void Send(string cmd)
        {
            try
            {
                using (UdpClient c = new UdpClient())
                    c.Send(Encoding.ASCII.GetBytes(cmd), cmd.Length, "127.0.0.1", CommandPort);
            }
            catch (Exception) { }
        }

        private GUIStyle _style, _small;
        /// <summary>Tamaño del recuadro de barras (1 = original).</summary>
        public float OverlayScale = 1.7f;

        private void OnGUI()
        {
            if (!ShowOverlay) return;
            float k = OverlayScale;
            if (_style == null)
            {
                _style = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(14 * k), fontStyle = FontStyle.Bold };
                _small = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(12 * k) };
            }
            bool alive = Time.unscaledTime - _lastPacket < 1.5f;
            string state = alive ? _state : "EMG desconectado (¿corre emg_puente.py?)";

            Rect box = new Rect(Screen.width - 350 * k, 10, 340 * k, 96 * k);
            GUI.color = new Color(0, 0, 0, 0.6f);
            GUI.DrawTexture(box, Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(new Rect(box.x + 10 * k, box.y + 6 * k, 320 * k, 20 * k), "EMG · " + state, _style);
            Bar(box.x + 10 * k, box.y + 32 * k, "A2 · " + HomeName, 0, alive, "+/−", k);
            Bar(box.x + 10 * k, box.y + 62 * k, "A3 · " + AwayName, 1, alive, "] / [", k);
            GUI.color = Color.white;
        }

        private void Bar(float x, float y, string label, int ch, bool alive, string keys, float k)
        {
            GUI.color = Color.white;
            GUI.Label(new Rect(x, y - 2 * k, 120 * k, 18 * k), label, _small);
            GUI.Label(new Rect(x + 284 * k, y - 2 * k, 50 * k, 18 * k), keys, _small);
            Rect bar = new Rect(x + 120 * k, y + 2 * k, 160 * k, 12 * k);
            GUI.color = new Color(1, 1, 1, 0.15f);
            GUI.DrawTexture(bar, Texture2D.whiteTexture);
            if (!alive) return;
            float scale = Mathf.Max(_thr[ch] * 1.6f, 1f);
            GUI.color = ch == 0 ? new Color(0.02f, 0.84f, 0.63f) : new Color(1f, 0.82f, 0.4f);
            GUI.DrawTexture(new Rect(bar.x, bar.y, bar.width * Mathf.Clamp01(_env[ch] / scale), bar.height), Texture2D.whiteTexture);
            if (_thr[ch] > 0f)
            {
                GUI.color = new Color(0.94f, 0.28f, 0.44f);
                GUI.DrawTexture(new Rect(bar.x + bar.width * (_thr[ch] / scale) - 2 * k, bar.y - 3 * k, 3 * k, bar.height + 6 * k), Texture2D.whiteTexture);
            }
        }
    }
}
