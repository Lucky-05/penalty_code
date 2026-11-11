using System.Collections.Generic;
using UnityEngine;
using Penales.Core;

namespace Penales.Game
{
    public enum GameMode
    {
        Quick,
        Tournament
    }

    /// <summary>
    /// El menú de inicio y las pantallas entre partidos: elegir equipo, dificultad y modo, y
    /// ver cómo va la llave del torneo. Se dibuja encima de la cancha, con el ratón o el teclado.
    /// </summary>
    public class MenuScreen : MonoBehaviour
    {
        public int TeamIndex;
        public Difficulty Difficulty = Difficulty.Normal;
        public GameMode Mode = GameMode.Quick;
        /// <summary>El jugador ya eligió y quiere empezar.</summary>
        public bool Confirmed { get; private set; }
        /// <summary>El jugador cerró la pantalla de aviso.</summary>
        public bool Dismissed { get; private set; }

        private enum View { Hidden, Main, Notice }
        private View _view = View.Hidden;
        private List<Team> _teams;
        private string _noticeTitle = "", _noticeBody = "", _noticeButton = "";
        private Texture2D _white;
        private GUIStyle _title, _label, _button, _small, _body;

        public void ShowMain(List<Team> teams)
        {
            _teams = teams;
            Confirmed = false;
            _view = View.Main;
        }

        /// <summary>Una pantalla de aviso con un botón: resultado, siguiente ronda, campeón.</summary>
        public void ShowNotice(string title, string body, string button)
        {
            _noticeTitle = title;
            _noticeBody = body;
            _noticeButton = button;
            Dismissed = false;
            _view = View.Notice;
        }

        public void Hide() { _view = View.Hidden; }

        private void Styles(float s)
        {
            if (_white == null)
            {
                _white = new Texture2D(1, 1);
                _white.SetPixel(0, 0, Color.white);
                _white.Apply();
            }
            _title = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(64 * s), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            _label = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(20 * s), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
            _small = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(15 * s), alignment = TextAnchor.MiddleCenter };
            _body = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(22 * s), alignment = TextAnchor.UpperCenter, wordWrap = true };
            _button = new GUIStyle(GUI.skin.button) { fontSize = Mathf.RoundToInt(19 * s), fontStyle = FontStyle.Bold };
            _title.normal.textColor = _label.normal.textColor = _small.normal.textColor = _body.normal.textColor = Color.white;
        }

        private void Box(Rect r, Color c)
        {
            Color old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, _white);
            GUI.color = old;
        }

        /// <summary>Un botón que se ve encendido cuando es la opción elegida.</summary>
        private bool Option(Rect r, string text, bool selected, Color accent)
        {
            Box(r, selected ? accent : new Color(1f, 1f, 1f, 0.10f));
            Color old = GUI.contentColor;
            GUI.contentColor = selected ? Contrast(accent) : Color.white;
            bool hit = GUI.Button(r, text, new GUIStyle(_button) { normal = { background = null }, hover = { background = null }, active = { background = null } });
            GUI.contentColor = old;
            return hit;
        }

        private static Color Contrast(Color c)
        {
            return (c.r * 0.3f + c.g * 0.59f + c.b * 0.11f) > 0.55f ? Color.black : Color.white;
        }

        private void OnGUI()
        {
            if (_view == View.Hidden) return;
            float s = Screen.height / 900f;
            Styles(s);
            float w = Screen.width, h = Screen.height;
            Box(new Rect(0f, 0f, w, h), new Color(0.02f, 0.04f, 0.08f, 0.78f));

            if (_view == View.Notice)
            {
                GUI.Label(new Rect(0f, h * 0.24f, w, 90f * s), _noticeTitle, _title);
                GUI.Label(new Rect(w * 0.2f, h * 0.24f + 110f * s, w * 0.6f, h * 0.38f), _noticeBody, _body);
                Rect b = new Rect((w - 300f * s) / 2f, h * 0.78f, 300f * s, 58f * s);
                if (Option(b, _noticeButton, true, new Color(0.20f, 0.78f, 0.36f)) || Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Return)
                {
                    Dismissed = true;
                    _view = View.Hidden;
                }
                return;
            }

            GUI.Label(new Rect(0f, 46f * s, w, 90f * s), "PENALES", _title);
            GUI.Label(new Rect(0f, 128f * s, w, 26f * s), "Tanda de penales · tú pateas y atajas", _small);

            float pw = 980f * s, px = (w - pw) / 2f, y = 190f * s;

            GUI.Label(new Rect(px, y, pw, 30f * s), "TU EQUIPO", _label);
            y += 38f * s;
            float cw = (pw - 3f * 12f * s) / 4f, ch = 70f * s;
            for (int i = 0; i < _teams.Count; i++)
            {
                Rect r = new Rect(px + (i % 4) * (cw + 12f * s), y + (i / 4) * (ch + 12f * s), cw, ch);
                Color primary = Paint.Hex(_teams[i].PrimaryColor, Color.white);
                if (Option(r, _teams[i].Name, TeamIndex == i, primary)) TeamIndex = i;
                Box(new Rect(r.x, r.yMax - 6f * s, r.width, 6f * s), primary);
                Box(new Rect(r.x, r.yMax - 6f * s, r.width * 0.3f, 6f * s), Paint.Hex(_teams[i].SecondaryColor, Color.gray));
            }
            y += 2f * (ch + 12f * s) + 26f * s;

            GUI.Label(new Rect(px, y, pw, 30f * s), "DIFICULTAD", _label);
            y += 38f * s;
            string[] levels = { "Fácil", "Normal", "Difícil" };
            float dw = (pw - 2f * 12f * s) / 3f;
            for (int i = 0; i < 3; i++)
                if (Option(new Rect(px + i * (dw + 12f * s), y, dw, 54f * s), levels[i], (int)Difficulty == i, new Color(0.95f, 0.78f, 0.20f)))
                    Difficulty = (Difficulty)i;
            y += 54f * s + 26f * s;

            GUI.Label(new Rect(px, y, pw, 30f * s), "MODO", _label);
            y += 38f * s;
            float mw = (pw - 12f * s) / 2f;
            if (Option(new Rect(px, y, mw, 54f * s), "Tanda rápida", Mode == GameMode.Quick, new Color(0.30f, 0.62f, 0.95f))) Mode = GameMode.Quick;
            if (Option(new Rect(px + mw + 12f * s, y, mw, 54f * s), "Torneo de 8 equipos", Mode == GameMode.Tournament, new Color(0.30f, 0.62f, 0.95f))) Mode = GameMode.Tournament;
            y += 54f * s + 40f * s;

            Rect play = new Rect((w - 340f * s) / 2f, y, 340f * s, 66f * s);
            if (Option(play, "JUGAR", true, new Color(0.20f, 0.78f, 0.36f)) || Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Return)
            {
                Confirmed = true;
                _view = View.Hidden;
            }
            GUI.Label(new Rect(0f, h - 44f * s, w, 26f * s), "Ratón para elegir · Enter para jugar · Esc para volver al menú durante el partido · 8 configurar EMG", _small);
        }
    }
}
