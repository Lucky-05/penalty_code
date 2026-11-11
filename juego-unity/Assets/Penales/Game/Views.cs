using UnityEngine;
using Penales.Core;
using CoreVec2 = System.Numerics.Vector2;
using CoreVec3 = System.Numerics.Vector3;

namespace Penales.Game
{
    /// <summary>
    /// El puente entre los ejes de la lógica y los de Unity. En la lógica Z crece hacia el
    /// tirador; en la escena el tirador mira hacia +Z, así que Z va invertida. X (derecha del
    /// tirador) y Y (arriba) son iguales.
    /// </summary>
    public static class Conv
    {
        public static Vector3 ToUnity(CoreVec3 v)
        {
            return new Vector3(v.X, v.Y, -v.Z);
        }

        /// <summary>Un punto del plano de gol, puesto en la escena.</summary>
        public static Vector3 OnGoalPlane(CoreVec2 p, float zOffset = 0f)
        {
            return new Vector3(p.X, p.Y, zOffset);
        }
    }

    /// <summary>Crea materiales que funcionen con el pipeline que esté activo.</summary>
    public static class Paint
    {
        private static Shader _lit;
        private static Shader _unlit;

        public static Material Lit(Color color, float smoothness = 0.2f)
        {
            if (_lit == null)
                _lit = Shader.Find("HDRP/Lit") ?? Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            Material m = new Material(_lit);
            m.color = color;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smoothness);
            return m;
        }

        public static Material Unlit(Color color)
        {
            if (_unlit == null)
                _unlit = Shader.Find("HDRP/Unlit") ?? Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
            Material m = new Material(_unlit);
            m.color = color;
            if (m.HasProperty("_UnlitColor")) m.SetColor("_UnlitColor", color);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
            return m;
        }

        public static Color Hex(string hex, Color fallback)
        {
            Color c;
            return ColorUtility.TryParseHtmlString("#" + hex, out c) ? c : fallback;
        }
    }

    /// <summary>
    /// El portero provisional: un cuerpo y dos manos que siguen al modelo de la lógica. Cuando
    /// lleguen las animaciones, este es el único archivo que cambia del lado del portero.
    /// </summary>
    public class KeeperView : MonoBehaviour
    {
        private Transform _body;
        private Transform _hands;
        private Renderer _bodyRenderer;

        public static KeeperView Create(Transform parent)
        {
            GameObject root = new GameObject("Portero");
            root.transform.SetParent(parent, false);
            KeeperView v = root.AddComponent<KeeperView>();

            GameObject body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "Cuerpo";
            Destroy(body.GetComponent<Collider>());
            body.transform.SetParent(root.transform, false);
            body.transform.localScale = new Vector3(0.55f, 0.92f, 0.4f);
            v._body = body.transform;
            v._bodyRenderer = body.GetComponent<Renderer>();

            GameObject hands = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            hands.name = "Manos";
            Destroy(hands.GetComponent<Collider>());
            hands.transform.SetParent(root.transform, false);
            hands.transform.localScale = new Vector3(0.42f, 0.3f, 0.3f);
            hands.GetComponent<Renderer>().sharedMaterial = Paint.Lit(new Color(0.95f, 0.95f, 0.2f));
            v._hands = hands.transform;

            v.Pose(KeeperModel.Ready, 0, 0f);
            return v;
        }

        public virtual void SetKit(Team team)
        {
            _bodyRenderer.sharedMaterial = Paint.Lit(Paint.Hex(team.SecondaryColor, Color.gray));
        }

        /// <param name="hands">Dónde están las manos, sobre el plano de gol.</param>
        /// <param name="column">Hacia qué lado va el lance (-1, 0, 1).</param>
        /// <param name="progress">0 parado, 1 lance completo.</param>
        public virtual void Pose(CoreVec2 hands, int column, float progress)
        {
            const float line = -0.35f;      // un paso delante de la línea
            _hands.localPosition = new Vector3(hands.X, hands.Y, line - 0.15f);

            float lean = column * -72f * progress;
            float bodyX = hands.X * 0.78f;
            float bodyY = Mathf.Lerp(0.95f, Mathf.Max(0.35f, hands.Y - 0.25f), progress * Mathf.Abs(column));
            _body.localPosition = new Vector3(bodyX, bodyY, line);
            _body.localRotation = Quaternion.Euler(0f, 0f, lean);
        }
    }

    /// <summary>La mira del tirador: el punto apuntado y el círculo del margen de error.</summary>
    public class ReticleView : MonoBehaviour
    {
        private const int Segments = 48;
        private LineRenderer _ring;
        private Transform _dot;

        public static ReticleView Create(Transform parent)
        {
            GameObject root = new GameObject("Mira");
            root.transform.SetParent(parent, false);
            ReticleView v = root.AddComponent<ReticleView>();

            GameObject dot = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            dot.name = "Punto";
            Destroy(dot.GetComponent<Collider>());
            dot.transform.SetParent(root.transform, false);
            dot.transform.localScale = Vector3.one * 0.12f;
            dot.GetComponent<Renderer>().sharedMaterial = Paint.Unlit(Color.white);
            v._dot = dot.transform;

            v._ring = root.AddComponent<LineRenderer>();
            v._ring.useWorldSpace = false;
            v._ring.loop = true;
            v._ring.positionCount = Segments;
            v._ring.widthMultiplier = 0.035f;
            v._ring.sharedMaterial = Paint.Unlit(Color.white);
            v._ring.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return v;
        }

        public void Show(bool visible)
        {
            gameObject.SetActive(visible);
        }

        public void Set(CoreVec2 target, float errorRadius, float power)
        {
            transform.position = Conv.OnGoalPlane(target, -0.05f);
            for (int i = 0; i < Segments; i++)
            {
                float a = i * Mathf.PI * 2f / Segments;
                _ring.SetPosition(i, new Vector3(Mathf.Cos(a) * errorRadius, Mathf.Sin(a) * errorRadius, 0f));
            }
            // De verde (colocado) a rojo (a romperla): el color avisa del riesgo.
            Color c = Color.Lerp(new Color(0.3f, 1f, 0.4f), new Color(1f, 0.25f, 0.2f), power);
            _ring.sharedMaterial.color = c;
            if (_ring.sharedMaterial.HasProperty("_UnlitColor")) _ring.sharedMaterial.SetColor("_UnlitColor", c);
        }
    }
}
