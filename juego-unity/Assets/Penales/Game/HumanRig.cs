using System.Collections.Generic;
using UnityEngine;

namespace Penales.Game
{
    /// <summary>
    /// Una pose del cuerpo, en grados y en el espacio del personaje: X = su derecha, Y = arriba,
    /// Z = hacia donde mira. Girar un hueso en X negativo lo lleva al frente (pierna que patea,
    /// brazo que se adelanta); en Z, lo abre o lo cierra hacia los lados.
    /// </summary>
    public struct BodyPose
    {
        public Vector3 Pelvis, Spine, Head;
        public Vector3 ArmL, ForeL, ArmR, ForeR;
        public Vector3 ThighL, CalfL, FootL, ThighR, CalfR, FootR;
        /// <summary>Cuánto sube o baja la cadera, en metros.</summary>
        public float Lift;

        public static BodyPose Lerp(BodyPose a, BodyPose b, float t)
        {
            t = Mathf.Clamp01(t);
            return new BodyPose
            {
                Pelvis = Vector3.Lerp(a.Pelvis, b.Pelvis, t), Spine = Vector3.Lerp(a.Spine, b.Spine, t),
                Head = Vector3.Lerp(a.Head, b.Head, t),
                ArmL = Vector3.Lerp(a.ArmL, b.ArmL, t), ForeL = Vector3.Lerp(a.ForeL, b.ForeL, t),
                ArmR = Vector3.Lerp(a.ArmR, b.ArmR, t), ForeR = Vector3.Lerp(a.ForeR, b.ForeR, t),
                ThighL = Vector3.Lerp(a.ThighL, b.ThighL, t), CalfL = Vector3.Lerp(a.CalfL, b.CalfL, t),
                FootL = Vector3.Lerp(a.FootL, b.FootL, t),
                ThighR = Vector3.Lerp(a.ThighR, b.ThighR, t), CalfR = Vector3.Lerp(a.CalfR, b.CalfR, t),
                FootR = Vector3.Lerp(a.FootR, b.FootR, t),
                Lift = Mathf.Lerp(a.Lift, b.Lift, t)
            };
        }
    }

    /// <summary>Las poses que usa el juego. No hay captura de movimiento: se calculan.</summary>
    public static class Poses
    {
        /// <summary>0 antes de <paramref name="from"/>, 1 después de <paramref name="to"/>, suave en medio.</summary>
        public static float Ease(float from, float to, float x)
        {
            float t = Mathf.Clamp01((x - from) / Mathf.Max(1e-5f, to - from));
            return t * t * (3f - 2f * t);
        }

        /// <summary>De pie, brazos sueltos (el modelo llega con los brazos abiertos en A).</summary>
        public static BodyPose Stand()
        {
            return new BodyPose
            {
                ArmL = new Vector3(0f, 0f, 36f), ArmR = new Vector3(0f, 0f, -36f),
                ForeL = new Vector3(-12f, 0f, 0f), ForeR = new Vector3(-12f, 0f, 0f)
            };
        }

        /// <summary>Un punto del ciclo de carrera; <paramref name="phase"/> en radianes.</summary>
        public static BodyPose Run(float phase, float intensity = 1f)
        {
            float s = Mathf.Sin(phase), c = Mathf.Cos(phase);
            BodyPose p = Stand();
            p.Spine = new Vector3(9f * intensity, -6f * s * intensity, 0f);
            p.Pelvis = new Vector3(4f * intensity, 5f * s * intensity, 0f);
            p.ThighL = new Vector3(-42f * s * intensity, 0f, 0f);
            p.ThighR = new Vector3(42f * s * intensity, 0f, 0f);
            p.CalfL = new Vector3((38f + 34f * c) * intensity, 0f, 0f);
            p.CalfR = new Vector3((38f - 34f * c) * intensity, 0f, 0f);
            p.ArmL = new Vector3(34f * s * intensity, 0f, 30f);
            p.ArmR = new Vector3(-34f * s * intensity, 0f, -30f);
            p.ForeL = new Vector3(-70f * intensity, 0f, 0f);
            p.ForeR = new Vector3(-70f * intensity, 0f, 0f);
            p.Lift = -0.03f * intensity + 0.025f * Mathf.Abs(c) * intensity;
            return p;
        }

        /// <summary>El golpeo con la pierna derecha: 0 = armado atrás, 0.5 = impacto, 1 = final.</summary>
        public static BodyPose Kick(float t)
        {
            BodyPose back = Stand();
            back.Spine = new Vector3(-4f, 22f, 0f);
            back.Pelvis = new Vector3(0f, 14f, 0f);
            back.ThighR = new Vector3(48f, 0f, 0f);
            back.CalfR = new Vector3(95f, 0f, 0f);
            back.ThighL = new Vector3(-12f, 0f, 0f);
            back.CalfL = new Vector3(22f, 0f, 0f);
            back.ArmL = new Vector3(-20f, 0f, -42f);
            back.ArmR = new Vector3(35f, 0f, -28f);
            back.ForeR = new Vector3(-35f, 0f, 0f);
            back.Lift = -0.05f;

            BodyPose hit = Stand();
            hit.Spine = new Vector3(10f, -14f, 0f);
            hit.Pelvis = new Vector3(0f, -10f, 0f);
            hit.ThighR = new Vector3(-58f, 0f, 0f);
            hit.CalfR = new Vector3(12f, 0f, 0f);
            hit.FootR = new Vector3(28f, 0f, 0f);
            hit.ThighL = new Vector3(6f, 0f, 0f);
            hit.CalfL = new Vector3(20f, 0f, 0f);
            hit.ArmL = new Vector3(20f, 0f, -58f);
            hit.ArmR = new Vector3(30f, 0f, -18f);
            hit.Lift = -0.04f;

            BodyPose end = Stand();
            end.Spine = new Vector3(14f, -26f, 0f);
            end.Pelvis = new Vector3(0f, -18f, 0f);
            end.ThighR = new Vector3(-92f, 0f, 0f);
            end.CalfR = new Vector3(34f, 0f, 0f);
            end.ThighL = new Vector3(10f, 0f, 0f);
            end.CalfL = new Vector3(8f, 0f, 0f);
            end.ArmL = new Vector3(30f, 0f, -30f);
            end.ArmR = new Vector3(-10f, 0f, -48f);
            end.Lift = 0.02f;

            return t < 0.5f ? BodyPose.Lerp(back, hit, Mathf.SmoothStep(0f, 1f, t * 2f))
                            : BodyPose.Lerp(hit, end, Mathf.SmoothStep(0f, 1f, (t - 0.5f) * 2f));
        }

        /// <summary>Portero en guardia: rodillas flexionadas, manos al frente.</summary>
        public static BodyPose Guard(float sway)
        {
            BodyPose p = Stand();
            p.Spine = new Vector3(16f, 0f, 3f * sway);
            p.ThighL = new Vector3(-30f, 0f, -12f);
            p.ThighR = new Vector3(-30f, 0f, 12f);
            p.CalfL = new Vector3(52f, 0f, 0f);
            p.CalfR = new Vector3(52f, 0f, 0f);
            p.FootL = new Vector3(-20f, 0f, 0f);
            p.FootR = new Vector3(-20f, 0f, 0f);
            p.ArmL = new Vector3(-30f, 0f, 8f);
            p.ArmR = new Vector3(-30f, 0f, -8f);
            p.ForeL = new Vector3(-62f, 0f, 0f);
            p.ForeR = new Vector3(-62f, 0f, 0f);
            p.Lift = -0.16f;
            return p;
        }

        /// <summary>El lance estirado. <paramref name="side"/>: -1 a su izquierda, +1 a su derecha, 0 arriba.</summary>
        public static BodyPose Dive(int side)
        {
            BodyPose p = Stand();
            if (side == 0)
            {
                p.ArmL = new Vector3(-10f, 0f, -118f);
                p.ArmR = new Vector3(-10f, 0f, 118f);
                p.Spine = new Vector3(-8f, 0f, 0f);
                return p;
            }
            // Los dos brazos hacia el lado del lance, uno por encima de la cabeza.
            p.ArmL = new Vector3(-8f, 0f, side > 0 ? -150f : -72f);
            p.ArmR = new Vector3(-8f, 0f, side > 0 ? 72f : 150f);
            p.Spine = new Vector3(0f, 0f, -14f * side);
            p.ThighL = new Vector3(side > 0 ? -8f : -30f, 0f, side > 0 ? 16f : -6f);
            p.ThighR = new Vector3(side > 0 ? -30f : -8f, 0f, side > 0 ? 6f : -16f);
            p.CalfL = new Vector3(side > 0 ? 18f : 48f, 0f, 0f);
            p.CalfR = new Vector3(side > 0 ? 48f : 18f, 0f, 0f);
            return p;
        }

        public static BodyPose Celebrate(float t)
        {
            BodyPose p = Stand();
            float pump = Mathf.Sin(t * 9f) * 8f;
            p.ArmL = new Vector3(-10f, 0f, -128f + pump);
            p.ArmR = new Vector3(-10f, 0f, 128f - pump);
            p.ForeL = new Vector3(-25f, 0f, 0f);
            p.ForeR = new Vector3(-25f, 0f, 0f);
            p.Spine = new Vector3(-10f, 0f, 0f);
            p.Head = new Vector3(-14f, 0f, 0f);
            p.Lift = 0.04f * Mathf.Abs(Mathf.Sin(t * 9f));
            return p;
        }

        public static BodyPose Dejected()
        {
            BodyPose p = Stand();
            p.Spine = new Vector3(24f, 0f, 0f);
            p.Head = new Vector3(30f, 0f, 0f);
            p.ArmL = new Vector3(-55f, 0f, -20f);
            p.ArmR = new Vector3(-55f, 0f, 20f);
            p.ForeL = new Vector3(-120f, 0f, 0f);
            p.ForeR = new Vector3(-120f, 0f, 0f);
            return p;
        }
    }

    /// <summary>
    /// Mueve el esqueleto de un jugador con poses calculadas. Encuentra los huesos por su
    /// nombre estándar, recuerda cómo venían y aplica cada giro en el espacio del personaje,
    /// de la cadera hacia afuera, para que las poses se puedan escribir sin conocer los ejes
    /// internos de cada hueso.
    /// </summary>
    public class HumanRig : MonoBehaviour
    {
        private Transform _model;
        private Transform _pelvis, _spineA, _spineB, _head;
        private Transform _armL, _foreL, _armR, _foreR;
        private Transform _thighL, _calfL, _footL, _thighR, _calfR, _footR;
        private Transform _handL, _handR;
        private readonly Dictionary<Transform, Quaternion> _bind = new Dictionary<Transform, Quaternion>();
        private Vector3 _pelvisBindLocal;
        private Quaternion _frame = Quaternion.identity;
        private BodyPose _current;

        public bool Ready { get; private set; }
        public float Height { get; private set; }
        /// <summary>Punto medio entre las dos manos, en el mundo.</summary>
        public Vector3 HandsCenter
        {
            get { return Ready ? (_handL.position + _handR.position) * 0.5f : transform.position; }
        }

        public Transform RightFoot { get { return _footR; } }

        /// <summary>Crea el jugador a partir de su prefab, colgado de un contenedor que se puede girar y mover.</summary>
        public static HumanRig Spawn(GameObject prefab, Transform parent, string name)
        {
            GameObject holder = new GameObject(name);
            holder.transform.SetParent(parent, false);
            GameObject model = Instantiate(prefab, holder.transform);
            model.name = "Modelo";
            HumanRig rig = holder.AddComponent<HumanRig>();
            rig.Bind(model.transform);
            return rig;
        }

        private void Bind(Transform model)
        {
            _model = model;
            Dictionary<string, Transform> all = new Dictionary<string, Transform>();
            foreach (Transform t in model.GetComponentsInChildren<Transform>(true))
                if (!all.ContainsKey(t.name)) all[t.name] = t;

            _pelvis = Find(all, "pelvis");
            _spineA = Find(all, "spine_02");
            _spineB = Find(all, "spine_04");
            _head = Find(all, "head");
            _armL = Find(all, "upperarm_l"); _foreL = Find(all, "lowerarm_l"); _handL = Find(all, "hand_l");
            _armR = Find(all, "upperarm_r"); _foreR = Find(all, "lowerarm_r"); _handR = Find(all, "hand_r");
            _thighL = Find(all, "thigh_l"); _calfL = Find(all, "calf_l"); _footL = Find(all, "foot_l");
            _thighR = Find(all, "thigh_r"); _calfR = Find(all, "calf_r"); _footR = Find(all, "foot_r");
            Transform ballL = Find(all, "ball_l"), ballR = Find(all, "ball_r");

            Transform[] animated = { _pelvis, _spineA, _spineB, _head, _armL, _foreL, _armR, _foreR, _thighL, _calfL, _footL, _thighR, _calfR, _footR, _handL, _handR };
            foreach (Transform t in animated)
            {
                if (t == null) { Debug.LogWarning("[Penales] Al esqueleto le falta un hueso; el jugador no se animará."); return; }
                _bind[t] = t.localRotation;
            }
            _pelvisBindLocal = _pelvis.localPosition;
            Height = _head.position.y - transform.position.y + 0.13f;

            // Hacia dónde mira: de los talones a las puntas de los pies. Su derecha: del brazo
            // izquierdo al derecho. Con eso se arma el marco en el que se escriben las poses.
            Vector3 forward = Vector3.forward;
            if (ballL != null && ballR != null)
                forward = (ballL.position + ballR.position) * 0.5f - (_footL.position + _footR.position) * 0.5f;
            forward = transform.InverseTransformDirection(forward);
            forward.y = 0f;
            if (forward.sqrMagnitude < 1e-6f) forward = Vector3.forward;
            _frame = Quaternion.LookRotation(forward.normalized, Vector3.up);
            Vector3 right = transform.InverseTransformDirection(_armR.position - _armL.position);
            if (Vector3.Dot(_frame * Vector3.right, right) < 0f)
                Debug.LogWarning("[Penales] El esqueleto llega espejeado respecto a lo esperado.");

            Ready = true;
            _current = Poses.Stand();
            Apply(_current);
        }

        private static Transform Find(Dictionary<string, Transform> all, string name)
        {
            Transform t;
            return all.TryGetValue(name, out t) ? t : null;
        }

        /// <summary>Coloca al jugador en el mundo mirando hacia <paramref name="facing"/>.</summary>
        public void Place(Vector3 position, Vector3 facing)
        {
            facing.y = 0f;
            if (facing.sqrMagnitude < 1e-6f) facing = Vector3.forward;
            transform.SetPositionAndRotation(position, Quaternion.LookRotation(facing.normalized, Vector3.up) * Quaternion.Inverse(_frame));
        }

        /// <summary>Lo mismo, pero además inclinado: se usa en el lance del portero.</summary>
        public void Place(Vector3 position, Vector3 facing, float rollDegrees)
        {
            facing.y = 0f;
            if (facing.sqrMagnitude < 1e-6f) facing = Vector3.forward;
            Quaternion look = Quaternion.LookRotation(facing.normalized, Vector3.up);
            transform.SetPositionAndRotation(position, look * Quaternion.Euler(0f, 0f, rollDegrees) * Quaternion.Inverse(_frame));
        }

        public BodyPose Current { get { return _current; } }

        public void Apply(BodyPose p)
        {
            if (!Ready) return;
            _current = p;
            _pelvis.localPosition = _pelvisBindLocal;
            _pelvis.position += transform.up * p.Lift;

            Turn(_pelvis, p.Pelvis);
            Turn(_spineA, p.Spine * 0.5f);
            Turn(_spineB, p.Spine * 0.5f);
            Turn(_head, p.Head);
            Turn(_armL, p.ArmL); Turn(_foreL, p.ForeL); Turn(_handL, Vector3.zero);
            Turn(_armR, p.ArmR); Turn(_foreR, p.ForeR); Turn(_handR, Vector3.zero);
            Turn(_thighL, p.ThighL); Turn(_calfL, p.CalfL); Turn(_footL, p.FootL);
            Turn(_thighR, p.ThighR); Turn(_calfR, p.CalfR); Turn(_footR, p.FootR);
        }

        /// <summary>
        /// Gira un hueso <paramref name="euler"/> grados en el espacio del personaje, encima de
        /// lo que ya hereda de su padre. Hay que llamar de la cadera hacia las puntas.
        /// </summary>
        private void Turn(Transform bone, Vector3 euler)
        {
            Quaternion inherited = bone.parent.rotation * _bind[bone];
            Quaternion space = transform.rotation * _frame;
            bone.rotation = space * Quaternion.Euler(euler) * Quaternion.Inverse(space) * inherited;
        }
    }
}
