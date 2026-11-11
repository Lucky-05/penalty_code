using System.Numerics;

namespace Penales.Core
{
    /// <summary>
    /// Las medidas del campo que importan para un penal, en metros.
    /// Origen: centro de la línea de gol, a ras de suelo. X a la derecha del tirador,
    /// Y hacia arriba, Z hacia el tirador (el punto penal está en Z = 11).
    /// </summary>
    public static class Field
    {
        public const float GoalWidth = 7.32f;
        public const float GoalHeight = 2.44f;
        public const float HalfGoal = GoalWidth / 2f;
        public const float PostRadius = 0.06f;
        public const float PenaltyDistance = 11f;

        public const float BallRadius = 0.11f;
        public const float BallMass = 0.43f;

        public static readonly Vector3 PenaltySpot = new Vector3(0f, BallRadius, PenaltyDistance);

        /// <summary>¿El centro del balón cruza limpio entre los tres palos?</summary>
        public static bool InsideGoal(float x, float y)
        {
            return System.Math.Abs(x) <= HalfGoal - BallRadius
                   && y >= 0f && y <= GoalHeight - BallRadius;
        }

        /// <summary>¿El balón toca un poste o el travesaño al cruzar el plano de gol?</summary>
        public static bool TouchesFrame(float x, float y)
        {
            float ax = System.Math.Abs(x);
            float outerX = HalfGoal + 2f * PostRadius + BallRadius;
            float outerY = GoalHeight + 2f * PostRadius + BallRadius;
            bool post = ax > HalfGoal - BallRadius && ax < outerX && y < outerY;
            bool bar = y > GoalHeight - BallRadius && y < outerY && ax < outerX;
            return post || bar;
        }
    }
}
