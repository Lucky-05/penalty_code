using System.Collections.Generic;
using UnityEngine;
using Penales.Core;
using CoreVec2 = System.Numerics.Vector2;

namespace Penales.Game
{
    /// <summary>Lo que el partido le pide al tirador, sea una cápsula o una persona.</summary>
    public abstract class ShooterView : MonoBehaviour
    {
        /// <summary>Dónde arranca la carrera y dónde queda parado al golpear (a ras de pasto).</summary>
        public static readonly Vector3 StartSpot = new Vector3(-1.55f, 0f, -13.5f);
        public static readonly Vector3 BallSpot = new Vector3(-0.24f, 0f, -11.3f);

        public abstract void SetKit(Team team);
        /// <summary>Parado al inicio de la carrera.</summary>
        public abstract void Ready();
        /// <summary>La carrera hacia el balón: 0 = arranca, 1 = golpea.</summary>
        public abstract void RunUp(float t01);
        /// <summary>Después del golpeo; <paramref name="seconds"/> desde el impacto.</summary>
        public abstract void FollowThrough(float seconds);
        /// <summary>Festejo o lamento tras el resultado.</summary>
        public abstract void React(bool happy, float seconds);
    }

    /// <summary>El tirador provisional: una cápsula que se desliza hacia el balón.</summary>
    public class CapsuleShooterView : ShooterView
    {
        private Renderer _renderer;

        public static CapsuleShooterView Create(Transform parent)
        {
            GameObject g = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            g.name = "Tirador";
            Destroy(g.GetComponent<Collider>());
            g.transform.SetParent(parent, false);
            g.transform.localScale = new Vector3(0.55f, 0.95f, 0.4f);
            CapsuleShooterView v = g.AddComponent<CapsuleShooterView>();
            v._renderer = g.GetComponent<Renderer>();
            v._renderer.sharedMaterial = Paint.Lit(Color.white);
            return v;
        }

        public override void SetKit(Team team)
        {
            _renderer.sharedMaterial = Paint.Lit(Paint.Hex(team.PrimaryColor, Color.white));
        }

        public override void Ready() { transform.position = StartSpot + Vector3.up * 0.95f; }

        public override void RunUp(float t01)
        {
            transform.position = Vector3.Lerp(StartSpot, BallSpot, Mathf.SmoothStep(0f, 1f, t01)) + Vector3.up * 0.95f;
        }

        public override void FollowThrough(float seconds) { }
        public override void React(bool happy, float seconds) { }
    }

    /// <summary>El tirador de verdad: una persona que corre, golpea y festeja.</summary>
    public class HumanShooterView : ShooterView
    {
        private const float Strides = 3.5f;         // zancadas de la carrera
        private const float WindUp = 0.74f;         // a partir de aquí arma la pierna
        private HumanRig _rig;
        private Vector3 _facing = Vector3.forward;

        public static HumanShooterView Create(GameObject prefab, Transform parent)
        {
            HumanRig rig = HumanRig.Spawn(prefab, parent, "Tirador");
            HumanShooterView v = rig.gameObject.AddComponent<HumanShooterView>();
            v._rig = rig;
            return v;
        }

        public override void SetKit(Team team)
        {
            Kit.Apply(gameObject, Paint.Hex(team.PrimaryColor, Color.white), Paint.Hex(team.SecondaryColor, Color.black));
        }

        public override void Ready()
        {
            _facing = (BallSpot - StartSpot).normalized;
            _rig.Place(StartSpot, _facing);
            _rig.Apply(Poses.Stand());
        }

        public override void RunUp(float t01)
        {
            t01 = Mathf.Clamp01(t01);
            // Arranca suave y llega lanzado; al final gira el cuerpo hacia el arco.
            float along = t01 * t01 * (3f - 2f * t01);
            Vector3 toGoal = Vector3.forward;
            Vector3 facing = Vector3.Slerp(_facing, toGoal, Poses.Ease(0.55f, 1f, t01));
            _rig.Place(Vector3.Lerp(StartSpot, BallSpot, along), facing);

            BodyPose run = Poses.Run(t01 * Strides * Mathf.PI * 2f, Poses.Ease(0f, 0.2f, t01));
            if (t01 < WindUp) _rig.Apply(run);
            else
            {
                float k = (t01 - WindUp) / (1f - WindUp);
                // De la zancada al armado (primera mitad) y del armado al impacto (segunda).
                BodyPose kick = Poses.Kick(Mathf.Clamp01(k * 1.15f - 0.15f) * 0.5f);
                _rig.Apply(BodyPose.Lerp(run, kick, Poses.Ease(0f, 0.45f, k)));
            }
        }

        public override void FollowThrough(float seconds)
        {
            const float swing = 0.32f, settle = 0.55f;
            _rig.Place(BallSpot + Vector3.forward * Mathf.Min(0.35f, seconds * 0.9f), Vector3.forward);
            if (seconds < swing) _rig.Apply(Poses.Kick(0.5f + 0.5f * seconds / swing));
            else _rig.Apply(BodyPose.Lerp(Poses.Kick(1f), Poses.Stand(), (seconds - swing) / settle));
        }

        public override void React(bool happy, float seconds)
        {
            BodyPose target = happy ? Poses.Celebrate(seconds) : Poses.Dejected();
            _rig.Apply(BodyPose.Lerp(_rig.Current, target, Mathf.Clamp01(Time.deltaTime * 7f)));
        }
    }

    /// <summary>El portero de verdad: guardia, lance a las nueve zonas y caída.</summary>
    public class HumanKeeperView : KeeperView
    {
        private const float Line = -0.4f;           // un paso delante de la línea de gol
        private HumanRig _rig;
        private float _clock;

        public static HumanKeeperView Create(GameObject prefab, Transform parent)
        {
            HumanRig rig = HumanRig.Spawn(prefab, parent, "Portero");
            HumanKeeperView v = rig.gameObject.AddComponent<HumanKeeperView>();
            v._rig = rig;
            v.Pose(KeeperModel.Ready, 0, 0f);
            return v;
        }

        public override void SetKit(Team team)
        {
            // El portero viste distinto a los suyos: se invierten los colores.
            Kit.Apply(gameObject, Paint.Hex(team.SecondaryColor, Color.yellow), Paint.Hex(team.PrimaryColor, Color.black));
        }

        public override void Pose(CoreVec2 hands, int column, float progress)
        {
            _clock += Time.deltaTime;
            progress = Mathf.Clamp01(progress);
            Vector3 facing = Vector3.back;                  // de cara al tirador
            int side = -column;                             // su derecha es la izquierda del tirador

            BodyPose guard = Poses.Guard(Mathf.Sin(_clock * 2.2f));
            BodyPose pose;
            if (column == 0)
            {
                bool high = hands.Y > 1.7f;
                bool low = hands.Y < 0.8f;
                BodyPose target = high ? Poses.Dive(0) : guard;
                if (low) { target.Lift = -0.42f; target.Spine = new Vector3(38f, 0f, 0f); target.ThighL.x = -75f; target.ThighR.x = -75f; target.CalfL.x = 110f; target.CalfR.x = 110f; }
                pose = BodyPose.Lerp(guard, target, progress);
            }
            else pose = BodyPose.Lerp(guard, Poses.Dive(side), Mathf.SmoothStep(0f, 1f, progress * 1.6f));

            // Raso se tira casi acostado; arriba va más de pie, estirado.
            float lean = Mathf.Lerp(80f, 46f, Mathf.InverseLerp(0.35f, 2.15f, hands.Y));
            float roll = -side * lean * Poses.Ease(0f, 1f, progress);
            Vector3 basePos = new Vector3(0f, 0f, Line);
            _rig.Place(basePos, facing, roll);
            _rig.Apply(pose);

            // Se lleva el cuerpo entero a donde tienen que llegar las manos.
            Vector3 target3 = new Vector3(hands.X, hands.Y, Line);
            Vector3 delta = target3 - _rig.HandsCenter;
            delta.z = 0f;
            float pull = column == 0 ? progress * (hands.Y > 1.7f ? 1f : 0f) : progress;
            Vector3 moved = basePos + delta * pull;
            moved.y = Mathf.Max(moved.y, -0.05f);
            _rig.Place(moved, facing, roll);
        }
    }

    /// <summary>
    /// El uniforme. No hay prenda modelada: se pinta sobre la piel del cuerpo usando un mapa de
    /// zonas (camiseta, short, medias, botines) que se genera al armar al jugador.
    /// </summary>
    public static class Kit
    {
        private static readonly Dictionary<string, Texture2D> Cache = new Dictionary<string, Texture2D>();

        public static void Apply(GameObject human, Color jersey, Color shorts)
        {
            HumanKitData data = human.GetComponentInChildren<HumanKitData>();
            if (data == null || data.Body == null || data.BaseColor == null || data.Mask == null)
            {
                Debug.LogWarning("[Penales] Este jugador no trae datos de uniforme.");
                return;
            }

            string key = data.BaseColor.GetInstanceID() + "|" + ColorUtility.ToHtmlStringRGB(jersey) + "|" + ColorUtility.ToHtmlStringRGB(shorts);
            Texture2D tex;
            if (!Cache.TryGetValue(key, out tex))
            {
                tex = Paint(data.BaseColor, data.Mask, jersey, shorts);
                Cache[key] = tex;
            }
            Material m = data.Body.material;                // copia propia de este jugador
            m.SetTexture("_BaseColorMap", tex);

            // La ropa con volumen toma sus colores de una tira: prenda y vivo, de arriba abajo.
            if (data.Garment != null)
            {
                Color boots = new Color(0.03f, 0.03f, 0.035f);
                Color[] strip = { jersey, shorts, shorts, jersey, jersey, shorts, boots, Color.Lerp(boots, jersey, 0.6f) };
                string gkey = "tela|" + ColorUtility.ToHtmlStringRGB(jersey) + "|" + ColorUtility.ToHtmlStringRGB(shorts);
                Texture2D palette;
                if (!Cache.TryGetValue(gkey, out palette))
                {
                    palette = new Texture2D(strip.Length, 1, TextureFormat.RGBA32, false, false);
                    palette.filterMode = FilterMode.Point;
                    palette.wrapMode = TextureWrapMode.Clamp;
                    palette.SetPixels(strip);
                    palette.Apply();
                    Cache[gkey] = palette;
                }
                data.Garment.material.SetTexture("_BaseColorMap", palette);
                if (data.Yoke != null) data.Yoke.material.SetTexture("_BaseColorMap", palette);
            }
        }

        private static Texture2D Paint(Texture2D skin, Texture2D mask, Color jersey, Color shorts)
        {
            Color32[] s = skin.GetPixels32();
            Color32[] k = mask.GetPixels32();
            if (s.Length != k.Length)
            {
                Debug.LogWarning("[Penales] El mapa de uniforme no coincide con la textura de piel.");
                return skin;
            }
            Color boots = new Color(0.035f, 0.035f, 0.04f);
            Color socks = jersey;
            Color32[] o = new Color32[s.Length];
            for (int i = 0; i < s.Length; i++)
            {
                Color32 m = k[i];
                if (m.r < 8 && m.g < 8 && m.b < 8 && m.a < 8) { o[i] = s[i]; continue; }
                // Se conserva un poco del claroscuro de la piel para que la tela tenga volumen.
                float lum = (s[i].r * 0.3f + s[i].g * 0.59f + s[i].b * 0.11f) / 255f;
                float shade = 0.72f + 0.55f * Mathf.Clamp01(lum);
                Color c = (Color)s[i];
                c = Color.Lerp(c, jersey * shade, m.r / 255f);
                c = Color.Lerp(c, shorts * shade, m.g / 255f);
                c = Color.Lerp(c, socks * shade, m.b / 255f);
                c = Color.Lerp(c, boots * shade, m.a / 255f);
                c.a = 1f;
                o[i] = c;
            }
            int painted = 0;
            for (int i = 0; i < o.Length; i += 97) if (k[i].r > 127 || k[i].g > 127 || k[i].b > 127 || k[i].a > 127) painted++;
            Debug.Log("[Penales] Uniforme: " + skin.width + "px, zonas pintadas en la muestra: " + painted + " de " + (o.Length / 97));
            Texture2D t = new Texture2D(skin.width, skin.height, TextureFormat.RGBA32, true, false);
            t.SetPixels32(o);
            t.Apply(true, true);
            return t;
        }
    }
}
