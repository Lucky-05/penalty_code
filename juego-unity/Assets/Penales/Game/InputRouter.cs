using UnityEngine;
using UnityEngine.InputSystem;

namespace Penales.Game
{
    /// <summary>
    /// Junta teclado, ratón y mando en lo que el juego necesita saber cada cuadro: a dónde se
    /// apunta, si el botón de acción está apretado, el efecto y hacia dónde se lanza el portero.
    /// Corre antes que todo lo demás para que nadie lea datos del cuadro anterior.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class InputRouter : MonoBehaviour
    {
        /// <summary>Posición del puntero en pantalla; solo vale si <see cref="PointerMoved"/>.</summary>
        public Vector2 PointerScreen { get; private set; }
        /// <summary>El ratón se movió este cuadro (manda sobre la palanca).</summary>
        public bool PointerMoved { get; private set; }
        /// <summary>Palanca izquierda o flechas, de -1 a 1.</summary>
        public Vector2 Direction { get; private set; }
        /// <summary>Efecto: Q/E o palanca derecha, de -1 a 1.</summary>
        public float Curve { get; private set; }

        public bool ActionHeld { get; private set; }
        public bool ActionPressed { get; private set; }
        public bool ActionReleased { get; private set; }
        public bool RestartPressed { get; private set; }
        /// <summary>Esc o Select: volver al menú.</summary>
        public bool MenuPressed { get; private set; }

        private void Update()
        {
            Keyboard k = Keyboard.current;
            Mouse m = Mouse.current;
            Gamepad g = Gamepad.current;

            PointerMoved = false;
            if (m != null)
            {
                PointerScreen = m.position.ReadValue();
                PointerMoved = m.delta.ReadValue().sqrMagnitude > 0.01f;
            }

            Vector2 dir = Vector2.zero;
            if (k != null)
            {
                if (k.leftArrowKey.isPressed || k.aKey.isPressed) dir.x -= 1f;
                if (k.rightArrowKey.isPressed || k.dKey.isPressed) dir.x += 1f;
                if (k.downArrowKey.isPressed || k.sKey.isPressed) dir.y -= 1f;
                if (k.upArrowKey.isPressed || k.wKey.isPressed) dir.y += 1f;
            }
            if (g != null && g.leftStick.ReadValue().sqrMagnitude > 0.04f) dir = g.leftStick.ReadValue();
            Direction = Vector2.ClampMagnitude(dir, 1f);

            float curve = 0f;
            if (k != null)
            {
                if (k.qKey.isPressed) curve -= 1f;
                if (k.eKey.isPressed) curve += 1f;
            }
            if (g != null && Mathf.Abs(g.rightStick.ReadValue().x) > 0.2f) curve = g.rightStick.ReadValue().x;
            Curve = Mathf.Clamp(curve, -1f, 1f);

            bool held = false, pressed = false, released = false;
            if (k != null)
            {
                held |= k.spaceKey.isPressed;
                pressed |= k.spaceKey.wasPressedThisFrame;
                released |= k.spaceKey.wasReleasedThisFrame;
            }
            if (m != null)
            {
                held |= m.leftButton.isPressed;
                pressed |= m.leftButton.wasPressedThisFrame;
                released |= m.leftButton.wasReleasedThisFrame;
            }
            if (g != null)
            {
                held |= g.buttonSouth.isPressed;
                pressed |= g.buttonSouth.wasPressedThisFrame;
                released |= g.buttonSouth.wasReleasedThisFrame;
            }
            ActionHeld = held;
            ActionPressed = pressed;
            ActionReleased = released;

            MenuPressed = (k != null && k.escapeKey.wasPressedThisFrame)
                          || (g != null && g.selectButton.wasPressedThisFrame);
            RestartPressed = (k != null && k.rKey.wasPressedThisFrame)
                             || (g != null && g.startButton.wasPressedThisFrame);
        }
    }
}
