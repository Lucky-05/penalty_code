using System.Collections.Generic;
using UnityEngine;
using Penales.Core;

namespace Penales.Game
{
    /// <summary>
    /// El marcador provisional: equipos, goles, la tira de tiros, el medidor de potencia y los
    /// letreros. Está hecho con la interfaz inmediata de Unity para no depender de arte; el
    /// marcador definitivo de transmisión llega en la etapa de menús.
    /// </summary>
    public class Hud : MonoBehaviour
    {
        public string Banner = "";
        public string Hint = "";
        public string Turn = "";
        /// <summary>Qué se está jugando: "Tanda rápida", "Semifinal"…</summary>
        public string Stage = "";
        public bool Visible = true;
        /// <summary>0 a 1; negativo = no mostrar.</summary>
        public float Power = -1f;

        private Team _home;
        private Team _away;
        private Shootout _shootout;
        private Texture2D _white;
        private GUIStyle _big, _mid, _small, _banner;

        public void Bind(Team home, Team away, Shootout shootout)
        {
            _home = home;
            _away = away;
            _shootout = shootout;
            Banner = "";
            Hint = "";
            Turn = "";
        }

        private void Styles(float s)
        {
            if (_white == null)
            {
                _white = new Texture2D(1, 1);
                _white.SetPixel(0, 0, Color.white);
                _white.Apply();
            }
            _big = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(34 * s), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            _mid = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(19 * s), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            _small = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(15 * s), alignment = TextAnchor.MiddleCenter };
            _banner = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(84 * s), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            _big.normal.textColor = _mid.normal.textColor = _small.normal.textColor = _banner.normal.textColor = Color.white;
        }

        private void Box(Rect r, Color c)
        {
            Color old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, _white);
            GUI.color = old;
        }

        private void OnGUI()
        {
            if (_shootout == null || !Visible) return;
            float s = Screen.height / 900f;
            Styles(s);
            float w = Screen.width;

            // Marcador
            float bw = 560f * s, bh = 92f * s, bx = (w - bw) / 2f, by = 18f * s;
            Box(new Rect(bx, by, bw, bh), new Color(0.04f, 0.06f, 0.10f, 0.86f));
            Box(new Rect(bx, by, 10f * s, bh), Paint.Hex(_home.PrimaryColor, Color.white));
            Box(new Rect(bx + bw - 10f * s, by, 10f * s, bh), Paint.Hex(_away.PrimaryColor, Color.white));
            GUI.Label(new Rect(bx + 18f * s, by + 4f * s, 190f * s, 44f * s), _home.ShortName, _big);
            GUI.Label(new Rect(bx + bw - 208f * s, by + 4f * s, 190f * s, 44f * s), _away.ShortName, _big);
            GUI.Label(new Rect(bx, by + 4f * s, bw, 44f * s), _shootout.HomeGoals + "  -  " + _shootout.AwayGoals, _big);
            Strip(_shootout.HomeKicks, bx + 26f * s, by + 58f * s, s, false);
            Strip(_shootout.AwayKicks, bx + bw - 26f * s, by + 58f * s, s, true);
            if (_shootout.SuddenDeath)
                GUI.Label(new Rect(bx, by + 50f * s, bw, 30f * s), "MUERTE SÚBITA", _small);

            if (!string.IsNullOrEmpty(Stage))
                GUI.Label(new Rect(bx, by - 16f * s, bw, 18f * s), Stage.ToUpperInvariant(), _small);
            if (!string.IsNullOrEmpty(Turn))
                GUI.Label(new Rect(0f, by + bh + 6f * s, w, 30f * s), Turn, _mid);

            // Medidor de potencia
            if (Power >= 0f)
            {
                float pw = 420f * s, ph = 22f * s, px = (w - pw) / 2f, py = Screen.height - 120f * s;
                Box(new Rect(px - 3f * s, py - 3f * s, pw + 6f * s, ph + 6f * s), new Color(0f, 0f, 0f, 0.7f));
                Box(new Rect(px, py, pw * Power, ph), Color.Lerp(new Color(0.3f, 1f, 0.4f), new Color(1f, 0.25f, 0.2f), Power));
                GUI.Label(new Rect(px, py - 30f * s, pw, 26f * s), "POTENCIA", _small);
            }

            if (!string.IsNullOrEmpty(Hint))
            {
                Box(new Rect(0f, Screen.height - 62f * s, w, 40f * s), new Color(0f, 0f, 0f, 0.55f));
                GUI.Label(new Rect(0f, Screen.height - 62f * s, w, 40f * s), Hint, _small);
            }

            if (!string.IsNullOrEmpty(Banner))
            {
                Rect r = new Rect(0f, Screen.height * 0.36f, w, 120f * s);
                Box(new Rect(0f, r.y - 6f * s, w, r.height + 12f * s), new Color(0f, 0f, 0f, 0.45f));
                GUI.Label(r, Banner, _banner);
            }
        }

        private void Strip(IReadOnlyList<bool> kicks, float x, float y, float s, bool rightToLeft)
        {
            int slots = Mathf.Max(5, kicks.Count);
            int first = Mathf.Max(0, slots - 5);          // en muerte súbita se ven los últimos cinco
            float size = 18f * s, gap = 8f * s;
            for (int i = 0; i < 5; i++)
            {
                int k = first + i;
                Color c = new Color(1f, 1f, 1f, 0.22f);
                if (k < kicks.Count) c = kicks[k] ? new Color(0.25f, 0.9f, 0.35f) : new Color(0.95f, 0.25f, 0.2f);
                float px = rightToLeft ? x - size - (4 - i) * (size + gap) : x + i * (size + gap);
                Box(new Rect(px, y, size, size), c);
            }
        }
    }
}
