using System;
using System.Numerics;

namespace Penales.Core
{
    /// <summary>
    /// El portero máquina. A veces lee bien al tirador y va a donde va el balón; cuando no,
    /// adivina según las manías de quien patea. La dificultad cambia cuánto lee y qué tan
    /// rápido reacciona.
    /// </summary>
    public static class KeeperBrain
    {
        public static float ReadChance(Difficulty d)
        {
            switch (d)
            {
                case Difficulty.Easy: return 0.20f;
                case Difficulty.Hard: return 0.56f;
                default: return 0.38f;
            }
        }

        /// <summary>Cuánto tarda en arrancar después del golpeo, en segundos.</summary>
        public static float ReactionTime(Difficulty d)
        {
            switch (d)
            {
                case Difficulty.Easy: return 0.18f;
                case Difficulty.Hard: return 0.07f;
                default: return 0.12f;
            }
        }

        /// <param name="actualTarget">A dónde va de verdad el tiro (lo que podría leer).</param>
        public static KeeperDive Decide(Vector2 actualTarget, ShooterTendencies shooter, Difficulty d, Random rng)
        {
            float start = ReactionTime(d);
            if (rng.NextDouble() < ReadChance(d))
            {
                int col, row;
                KeeperModel.ZoneOf(actualTarget, out col, out row);
                return new KeeperDive(col, row, start);
            }
            return new KeeperDive(GuessColumn(shooter, rng), GuessRow(shooter, rng), start);
        }

        private static int GuessColumn(ShooterTendencies shooter, Random rng)
        {
            float bias = shooter != null ? shooter.SideBias : 0f;
            double center = 0.14;
            double right = (1.0 - center) * (0.5 + 0.35 * bias);
            double u = rng.NextDouble();
            if (u < center) return 0;
            return u < center + right ? 1 : -1;
        }

        private static int GuessRow(ShooterTendencies shooter, Random rng)
        {
            float h = shooter != null ? shooter.HeightBias : 0.4f;
            double high = 0.10 + 0.35 * h;
            double low = 0.55 - 0.30 * h;
            double u = rng.NextDouble();
            if (u < low) return 0;
            return u < low + high ? 2 : 1;
        }
    }

    /// <summary>
    /// El tirador máquina. Elige lado, altura y potencia según sus manías; mientras más
    /// difícil el nivel, más se anima a buscar la esquina.
    /// </summary>
    public static class ShooterBrain
    {
        public static float Ambition(Difficulty d)
        {
            switch (d)
            {
                case Difficulty.Easy: return 0.35f;
                case Difficulty.Hard: return 0.85f;
                default: return 0.60f;
            }
        }

        /// <summary>Lo que el tirador QUIERE hacer (antes de que la presión lo desvíe).</summary>
        public static ShotIntent Decide(ShooterTendencies t, Difficulty d, Random rng)
        {
            float ambition = Ambition(d);

            double center = 0.10;
            double right = (1.0 - center) * (0.5 + 0.40 * t.SideBias);
            double u = rng.NextDouble();
            int side = u < center ? 0 : (u < center + right ? 1 : -1);

            float reach = 1.9f + 1.35f * ambition * (0.6f + 0.4f * (float)rng.NextDouble());
            float x = side * reach + (float)(rng.NextDouble() - 0.5) * 0.5f;

            float y;
            double hu = rng.NextDouble();
            if (hu < 0.25 + 0.5 * t.HeightBias)
                y = 1.35f + 0.75f * ambition * (float)rng.NextDouble();
            else
                y = 0.20f + 0.55f * (float)rng.NextDouble();

            float power = Pressure.Clamp01(t.Power + (float)(rng.NextDouble() - 0.5) * 0.2f);
            float curve = Math.Max(-1f, Math.Min(1f, t.Curve + (float)(rng.NextDouble() - 0.5) * 0.3f));

            x = Math.Max(-Field.HalfGoal + 0.25f, Math.Min(Field.HalfGoal - 0.25f, x));
            y = Math.Max(Field.BallRadius + 0.02f, Math.Min(Field.GoalHeight - 0.25f, y));
            return new ShotIntent(new Vector2(x, y), power, curve);
        }

        /// <summary>El tiro que de verdad sale: la intención más el desvío de la presión.</summary>
        public static ShotIntent Execute(ShotIntent intent, ShooterTendencies t, float clutch, Random rng)
        {
            float radius = Pressure.ErrorRadius(intent.Power, intent.Target, clutch, t.Composure);
            // De vez en cuando alguien le pega mal: el tiro que se va a la tribuna. Pasa más
            // cuando el penal pesa y el tirador no tiene temple.
            if (rng.NextDouble() < Pressure.ShankChance(clutch, t.Composure)) radius *= Pressure.ShankFactor;
            return new ShotIntent(Pressure.Disturb(intent.Target, radius, rng), intent.Power, intent.Curve);
        }
    }
}
