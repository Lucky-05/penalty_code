using System;
using System.Numerics;

namespace Penales.Core
{
    /// <summary>Dónde está el balón, a qué velocidad va y cómo gira (rad/s).</summary>
    public struct BallState
    {
        public Vector3 Pos;
        public Vector3 Vel;
        public Vector3 Spin;

        public BallState(Vector3 pos, Vector3 vel, Vector3 spin)
        {
            Pos = pos;
            Vel = vel;
            Spin = spin;
        }
    }

    /// <summary>El momento en que el balón cruza el plano de la portería (Z = 0).</summary>
    public struct GoalCrossing
    {
        public bool Crossed;
        public Vector3 Point;
        public Vector3 Velocity;
        public float Time;
    }

    /// <summary>
    /// El vuelo del balón: gravedad, arrastre del aire y efecto Magnus (la curva que da el giro).
    /// Es la única fuente de verdad de la trayectoria: la usa el tirador para apuntar, la
    /// máquina para decidir y la escena para mover el balón, así que lo que se apunta es lo
    /// que se ve.
    /// </summary>
    public static class BallFlight
    {
        public const float Gravity = 9.81f;
        public const float AirDensity = 1.2f;
        public const float DragCoefficient = 0.25f;
        public const float MagnusCoefficient = 0.6f;
        public const float FixedDt = 1f / 240f;

        /// <summary>Lo que pierde el balón al rozar el pasto, por segundo.</summary>
        public const float GroundFriction = 1.2f;

        private const float Area = (float)Math.PI * Field.BallRadius * Field.BallRadius;

        public static Vector3 Acceleration(Vector3 vel, Vector3 spin)
        {
            float speed = vel.Length();
            Vector3 drag = -0.5f * AirDensity * DragCoefficient * Area * speed * vel;
            Vector3 magnus = 0.5f * AirDensity * Area * Field.BallRadius * MagnusCoefficient
                             * Vector3.Cross(spin, vel);
            return new Vector3(0f, -Gravity, 0f) + (drag + magnus) / Field.BallMass;
        }

        /// <summary>Un paso de simulación (punto medio). Si toca el pasto, rueda.</summary>
        public static BallState Step(BallState s, float dt)
        {
            Vector3 a1 = Acceleration(s.Vel, s.Spin);
            Vector3 midVel = s.Vel + a1 * (dt * 0.5f);
            Vector3 a2 = Acceleration(midVel, s.Spin);

            Vector3 pos = s.Pos + midVel * dt;
            Vector3 vel = s.Vel + a2 * dt;

            if (pos.Y < Field.BallRadius)
            {
                pos.Y = Field.BallRadius;
                if (vel.Y < 0f) vel.Y = 0f;
                float keep = Math.Max(0f, 1f - GroundFriction * dt);
                vel.X *= keep;
                vel.Z *= keep;
            }

            return new BallState(pos, vel, s.Spin);
        }

        /// <summary>Simula hasta que el balón cruza el plano de gol o deja de acercarse.</summary>
        public static GoalCrossing FlyToGoalPlane(BallState start, float maxTime = 4f)
        {
            BallState s = start;
            float t = 0f;
            while (t < maxTime)
            {
                BallState next = Step(s, FixedDt);
                if (next.Pos.Z <= 0f)
                {
                    float span = s.Pos.Z - next.Pos.Z;
                    float k = span > 1e-6f ? s.Pos.Z / span : 0f;
                    return new GoalCrossing
                    {
                        Crossed = true,
                        Point = Vector3.Lerp(s.Pos, next.Pos, k),
                        Velocity = Vector3.Lerp(s.Vel, next.Vel, k),
                        Time = t + FixedDt * k
                    };
                }
                if (next.Vel.Z > -0.05f) break;     // se frenó o va de regreso: no llega
                s = next;
                t += FixedDt;
            }
            return new GoalCrossing { Crossed = false, Point = s.Pos, Velocity = s.Vel, Time = t };
        }
    }
}
