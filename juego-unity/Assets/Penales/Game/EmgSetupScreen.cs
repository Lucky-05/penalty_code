using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Penales.Game
{
    /// <summary>
    /// Configuración EMG al inicio del juego: mide el reposo y la contracción máxima de cada canal,
    /// y fija el umbral de tiro de cada canal en el <see cref="Percent"/> de esa contracción máxima.
    /// Si el 70 % de la máxima no supera el ruido de reposo, pide repetir (si no, tiraría solo).
    /// Enter continúa · R repite todo · 1/2 repite un canal · Esc salta (umbral automático).
    /// </summary>
    public class EmgSetupScreen : MonoBehaviour
    {
        /// <summary>Fracción de la contracción máxima que hay que alcanzar para tirar.</summary>
        public const float Percent = 0.70f;
        private const float PrepSeconds = 3f;
        private const float ContractSeconds = 5f;
        private const float RestTimeout = 6f;

        public EmgLink Emg;

        private enum Step { Hidden, NoArduino, Intro, Rest, Prep, Contract, Result }

        private static readonly string[] ChannelNames = { "Canal 1 (A2) · tu equipo", "Canal 2 (A3) · rival" };

        private Step _step = Step.Hidden;
        private int _ch;
        private float _timer;
        private readonly float[] _rest = new float[2];
        private readonly float[] _max = new float[2];
        private readonly bool[] _ok = new bool[2];
        private readonly bool[] _measured = new bool[2];

        /// <summary>Corre la configuración completa; termina cuando el terapeuta continúa o salta.</summary>
        public IEnumerator Run()
        {
            if (Emg == null) yield break;
            Emg.ShowOverlay = false;

            // ¿Llega señal? Si no, se puede jugar sin EMG o esperar a que se conecte.
            for (float t = 0f; !Emg.Live && t < 3f; t += Time.unscaledDeltaTime) yield return null;
            if (!Emg.Live)
            {
                _step = Step.NoArduino;
                while (!Emg.Live)
                {
                    if (Pressed(Key.Enter) || Pressed(Key.Escape)) { yield return Close(); yield break; }
                    yield return null;
                }
            }

            _step = Step.Intro;
            while (true)
            {
                if (Pressed(Key.Escape)) { yield return Close(); yield break; }
                if (Pressed(Key.Enter)) break;
                yield return null;
            }

            bool again = true;
            while (again)
            {
                yield return MeasureRest();
                yield return MeasureChannel(0);
                yield return MeasureChannel(1);

                _step = Step.Result;
                again = false;
                while (true)
                {
                    if (Pressed(Key.Enter)) break;
                    if (Pressed(Key.Escape)) { yield return Close(); yield break; }
                    if (Pressed(Key.R)) { again = true; break; }
                    if (Pressed(Key.Digit1)) { yield return MeasureChannel(0); _step = Step.Result; }
                    if (Pressed(Key.Digit2)) { yield return MeasureChannel(1); _step = Step.Result; }
                    yield return null;
                }
            }

            for (int ch = 0; ch < 2; ch++)
            {
                Emg.MaxContraction[ch] = _ok[ch] ? _max[ch] : 0f;
                Emg.SetThreshold(ch, _ok[ch] ? Percent * _max[ch] : 0f);
            }
            yield return Close();
        }

        private IEnumerator MeasureRest()
        {
            // Quita los umbrales anteriores para que el Arduino calcule el ruido de reposo limpio.
            Emg.SetThreshold(0, 0f);
            Emg.SetThreshold(1, 0f);
            _measured[0] = _measured[1] = false;
            int before = Emg.RestCalibrations;
            Emg.RecalibrateRest();
            _step = Step.Rest;
            for (_timer = 0f; Emg.RestCalibrations == before && _timer < RestTimeout; _timer += Time.unscaledDeltaTime)
                yield return null;
            for (float t = 0f; t < 0.3f; t += Time.unscaledDeltaTime) yield return null;   // espera un reporte con el umbral nuevo
            _rest[0] = Emg.Threshold(0);
            _rest[1] = Emg.Threshold(1);
        }

        private IEnumerator MeasureChannel(int ch)
        {
            _ch = ch;
            _step = Step.Prep;
            for (_timer = PrepSeconds; _timer > 0f; _timer -= Time.unscaledDeltaTime) yield return null;

            _step = Step.Contract;
            Emg.BeginPeak(ch);
            for (_timer = ContractSeconds; _timer > 0f; _timer -= Time.unscaledDeltaTime) yield return null;
            _max[ch] = Emg.EndPeak();
            _ok[ch] = Percent * _max[ch] > _rest[ch];
            _measured[ch] = true;
        }

        private IEnumerator Close()
        {
            _step = Step.Hidden;
            Emg.ShowOverlay = true;
            // Que el Enter de aquí no confirme también el menú que aparece después.
            while (Keyboard.current != null && (Keyboard.current.enterKey.isPressed || Keyboard.current.numpadEnterKey.isPressed))
                yield return null;
            yield return null;
        }

        private static bool Pressed(Key key)
        {
            Keyboard k = Keyboard.current;
            if (k == null) return false;
            if (key == Key.Enter) return k.enterKey.wasPressedThisFrame || k.numpadEnterKey.wasPressedThisFrame;
            return k[key].wasPressedThisFrame;
        }

        // ── Dibujo ───────────────────────────────────────────────────────────────────────

        private GUIStyle _title, _big, _body, _small, _label, _left;
        private float _styleScale = -1f;

        private void Styles(float s)
        {
            if (Mathf.Approximately(s, _styleScale)) return;
            _styleScale = s;
            _title = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(54 * s), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            _big = new GUIStyle(_title) { fontSize = Mathf.RoundToInt(120 * s) };
            _body = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(26 * s), alignment = TextAnchor.UpperCenter, wordWrap = true };
            _small = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(18 * s), alignment = TextAnchor.MiddleCenter, wordWrap = true };
            _label = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(20 * s), fontStyle = FontStyle.Bold };
            _left = new GUIStyle(_small) { alignment = TextAnchor.UpperLeft };
            foreach (GUIStyle st in new[] { _title, _big, _body, _small, _label, _left }) st.normal.textColor = Color.white;
        }

        private static void Box(Rect r, Color c)
        {
            Color old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = old;
        }

        private void OnGUI()
        {
            if (_step == Step.Hidden) return;
            float s = Screen.height / 900f;
            Styles(s);
            float w = Screen.width, h = Screen.height;
            GUI.depth = -10;
            Box(new Rect(0f, 0f, w, h), new Color(0.02f, 0.04f, 0.08f, 0.92f));
            GUI.Label(new Rect(0f, 40f * s, w, 70f * s), "CONFIGURACIÓN EMG", _title);

            Rect msg = new Rect(w * 0.15f, 140f * s, w * 0.7f, 120f * s);
            string footer = "";
            switch (_step)
            {
                case Step.NoArduino:
                    GUI.Label(msg, "No llega señal del Arduino.\nConéctalo por USB y corre emg_puente.py; esta pantalla sigue sola.", _body);
                    footer = "Enter o Esc: jugar sin EMG";
                    break;
                case Step.Intro:
                    GUI.Label(msg, "Vamos a medir la contracción máxima de cada canal.\nPara tirar a gol habrá que llegar al " + Mathf.RoundToInt(Percent * 100f) + " % de esa contracción máxima.\n\nPrimero: los dos músculos relajados.", _body);
                    footer = "Enter: empezar · Esc: saltar (umbral automático de reposo)";
                    break;
                case Step.Rest:
                    GUI.Label(msg, "Relaja los dos músculos y no te muevas…", _body);
                    GUI.Label(new Rect(0f, 300f * s, w, 140f * s), Mathf.CeilToInt(Mathf.Max(0f, 3f - _timer)).ToString(), _big);
                    break;
                case Step.Prep:
                    GUI.Label(msg, "Prepárate: " + ChannelNames[_ch] + "\nContrae lo más fuerte que puedas cuando aparezca ¡YA!", _body);
                    GUI.Label(new Rect(0f, 300f * s, w, 140f * s), Mathf.CeilToInt(_timer).ToString(), _big);
                    break;
                case Step.Contract:
                    GUI.Label(msg, "¡YA! Contracción máxima · " + ChannelNames[_ch] + "\nAguanta hasta que termine el tiempo", _body);
                    GUI.Label(new Rect(0f, 300f * s, w, 140f * s), _timer.ToString("0.0"), _big);
                    break;
                case Step.Result:
                    GUI.Label(msg, "Resultado", _body);
                    footer = "Enter: guardar y jugar · R: repetir todo · 1 / 2: repetir ese canal · Esc: saltar";
                    break;
            }

            float bw = Mathf.Min(900f * s, w * 0.8f), bx = (w - bw) / 2f;
            for (int ch = 0; ch < 2; ch++)
                DrawChannel(ch, new Rect(bx, 470f * s + ch * 170f * s, bw, 150f * s), s);

            if (footer.Length > 0) GUI.Label(new Rect(0f, h - 60f * s, w, 30f * s), footer, _small);
        }

        private void DrawChannel(int ch, Rect r, float s)
        {
            bool active = (_step == Step.Prep || _step == Step.Contract) && _ch == ch;
            Box(r, active ? new Color(0.2f, 0.6f, 0.4f, 0.25f) : new Color(1f, 1f, 1f, 0.05f));
            GUI.Label(new Rect(r.x + 16f * s, r.y + 10f * s, r.width, 30f * s), ChannelNames[ch], _label);

            float env = Emg.Env(ch);
            float peak = active && _step == Step.Contract ? Emg.CurrentPeak : (_measured[ch] ? _max[ch] : 0f);
            float scale = Mathf.Max(10f, _rest[ch] * 3f, peak * 1.25f, env * 1.1f);
            Rect bar = new Rect(r.x + 16f * s, r.y + 50f * s, r.width - 32f * s, 26f * s);
            Box(bar, new Color(1f, 1f, 1f, 0.12f));
            Box(new Rect(bar.x, bar.y, bar.width * Mathf.Clamp01(env / scale), bar.height), ch == 0 ? new Color(0.02f, 0.84f, 0.63f) : new Color(1f, 0.82f, 0.4f));
            Marker(bar, _rest[ch] / scale, new Color(0.6f, 0.6f, 0.6f));                     // ruido de reposo
            if (peak > 0f)
            {
                Marker(bar, peak / scale, Color.white);                                      // contracción máxima
                Marker(bar, Percent * peak / scale, new Color(0.94f, 0.28f, 0.44f));         // 70 %: umbral de tiro
            }

            string info;
            if (!_measured[ch])
                info = "Reposo: " + _rest[ch].ToString("0.0") + "   (gris = ruido de reposo · blanco = máxima · rojo = " + Mathf.RoundToInt(Percent * 100f) + " %)";
            else if (_ok[ch])
                info = "Máxima: " + _max[ch].ToString("0.0") + "  ·  umbral de tiro (" + Mathf.RoundToInt(Percent * 100f) + " %): " + (Percent * _max[ch]).ToString("0.0") + "  ✓";
            else
                info = "Máxima: " + _max[ch].ToString("0.0") + "  ✗ muy débil: el " + Mathf.RoundToInt(Percent * 100f) + " % (" + (Percent * _max[ch]).ToString("0.0")
                       + ") no supera el ruido de reposo (" + _rest[ch].ToString("0.0") + "). Repite con " + (ch + 1) + "; si no, usa el umbral automático.";
            GUI.Label(new Rect(r.x + 16f * s, r.y + 90f * s, r.width - 32f * s, 50f * s), info, _left);
        }

        private static void Marker(Rect bar, float frac, Color c)
        {
            if (frac <= 0f) return;
            float x = bar.x + bar.width * Mathf.Clamp01(frac);
            Box(new Rect(x - 1.5f, bar.y - 6f, 3f, bar.height + 12f), c);
        }
    }
}
