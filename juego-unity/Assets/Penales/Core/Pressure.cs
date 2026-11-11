using System;
using System.Numerics;

namespace Penales.Core
{
    /// <summary>
    /// La presión del penal, convertida en margen de error. Pegarle fuerte y a la esquina es
    /// lo que más goles da y lo que más se falla; aquí se decide cuánto.
    /// </summary>
    public static class Pressure
    {
        public const float BaseRadius = 0.10f;
        public const float PowerRadius = 0.70f;
        public const float EdgeRadius = 0.40f;
        public const float ClutchRadius = 0.20f;
        public const float ComposureRelief = 0.45f;
        public const float ShankFactor = 3.5f;

        /// <summary>Probabilidad de pegarle mal al balón (el margen de error se multiplica).</summary>
        public static float ShankChance(float clutch, float composure)
        {
            return 0.05f + 0.10f * Clamp01(clutch) * (1f - Clamp01(composure));
        }

        /// <summary>
        /// Radio, en metros, del círculo alrededor del punto apuntado donde puede acabar el tiro.
        /// </summary>
        /// <param name="power">0 a 1.</param>
        /// <param name="target">Punto apuntado sobre el plano de gol.</param>
        /// <param name="clutch">0 a 1: qué tanto pesa este tiro en la tanda.</param>
        /// <param name="composure">0 a 1: sangre fría del tirador.</param>
        public static float ErrorRadius(float power, Vector2 target, float clutch, float composure)
        {
            float p = Clamp01(power);
            float edgeX = Clamp01((Math.Abs(target.X) - 2.2f) / (Field.HalfGoal - 2.2f));
            float edgeY = Clamp01((target.Y - 1.5f) / (Field.GoalHeight - 1.5f));
            float edge = 1f - (1f - edgeX) * (1f - edgeY);

            float r = BaseRadius + PowerRadius * p * p + EdgeRadius * edge + ClutchRadius * Clamp01(clutch);
            return r * (1f - ComposureRelief * Clamp01(composure));
        }

        /// <summary>Mueve el punto apuntado a un lugar al azar dentro del margen de error.</summary>
        public static Vector2 Disturb(Vector2 target, float radius, Random rng)
        {
            double r = radius * Math.Sqrt(rng.NextDouble());
            double a = rng.NextDouble() * Math.PI * 2.0;
            return new Vector2(target.X + (float)(r * Math.Cos(a)), target.Y + (float)(r * Math.Sin(a)));
        }

        internal static float Clamp01(float v)
        {
            return v < 0f ? 0f : (v > 1f ? 1f : v);
        }
    }
}
