using System;
using System.Numerics;

namespace Penales.Core
{
    /// <summary>Lo que el tirador quiere hacer: a dónde, qué tan fuerte y con cuánto efecto.</summary>
    public struct ShotIntent
    {
        /// <summary>Punto sobre el plano de gol: X lateral, Y altura.</summary>
        public Vector2 Target;
        /// <summary>0 = colocado, 1 = a romper la red.</summary>
        public float Power;
        /// <summary>-1 = curva hacia la izquierda del tirador, +1 = hacia la derecha.</summary>
        public float Curve;

        public ShotIntent(Vector2 target, float power, float curve)
        {
            Target = target;
            Power = power;
            Curve = curve;
        }
    }

    /// <summary>
    /// Convierte una intención de tiro en el golpeo: con qué velocidad y giro sale el balón
    /// para pasar exactamente por el punto elegido. Como el aire frena y el efecto curva, no
    /// basta con apuntar en línea recta: se corrige el apuntado hasta que la simulación llega.
    /// </summary>
    public static class ShotSolver
    {
        public const float MinSpeed = 15f;      // m/s, unos 54 km/h
        public const float MaxSpeed = 34f;      // m/s, unos 122 km/h
        public const float MaxSpin = 60f;       // rad/s, casi 10 vueltas por segundo

        private const int MaxIterations = 16;
        private const float Tolerance = 0.004f;

        public static float SpeedFor(float power)
        {
            float p = Math.Min(1f, Math.Max(0f, power));
            return MinSpeed + (MaxSpeed - MinSpeed) * p;
        }

        public static BallState Solve(ShotIntent intent)
        {
            return Solve(intent.Target, intent.Power, intent.Curve);
        }

        public static BallState Solve(Vector2 target, float power, float curve)
        {
            float speed = SpeedFor(power);
            float c = Math.Min(1f, Math.Max(-1f, curve));
            // Giro sobre el eje vertical. El signo va invertido: con el balón viajando hacia -Z,
            // un giro negativo en Y empuja hacia +X (la derecha del tirador).
            Vector3 spin = new Vector3(0f, -c * MaxSpin, 0f);

            Vector3 aim = new Vector3(target.X, target.Y, 0f);
            BallState state = Launch(aim, speed, spin);

            for (int i = 0; i < MaxIterations; i++)
            {
                GoalCrossing hit = BallFlight.FlyToGoalPlane(state);
                if (!hit.Crossed)
                {
                    aim.Y += 0.5f;                  // se quedó corto: levantar el tiro
                    state = Launch(aim, speed, spin);
                    continue;
                }
                float ex = hit.Point.X - target.X;
                float ey = hit.Point.Y - target.Y;
                if (Math.Abs(ex) < Tolerance && Math.Abs(ey) < Tolerance) break;
                aim.X -= ex;
                aim.Y -= ey;
                state = Launch(aim, speed, spin);
            }
            return state;
        }

        private static BallState Launch(Vector3 aim, float speed, Vector3 spin)
        {
            Vector3 dir = Vector3.Normalize(aim - Field.PenaltySpot);
            return new BallState(Field.PenaltySpot, dir * speed, spin);
        }
    }
}
