using System;
using System.Numerics;

namespace Penales.Core
{
    /// <summary>El lance del portero: a qué zona va y cuándo arranca.</summary>
    public struct KeeperDive
    {
        /// <summary>-1 izquierda del tirador, 0 centro, +1 derecha del tirador.</summary>
        public int Column;
        /// <summary>0 abajo, 1 media altura, 2 arriba.</summary>
        public int Row;
        /// <summary>Segundos desde el golpeo. Negativo = se adelantó.</summary>
        public float StartTime;

        public KeeperDive(int column, int row, float startTime)
        {
            Column = column;
            Row = row;
            StartTime = startTime;
        }
    }

    /// <summary>Qué tan bueno es el portero físicamente.</summary>
    public class KeeperProfile
    {
        /// <summary>Velocidad de las manos durante el lance, m/s.</summary>
        public float Speed = 5.2f;
        /// <summary>Qué tan lejos de las manos puede pasar el balón y aun así tocarlo, m.</summary>
        public float CatchRadius = 0.50f;

        public static KeeperProfile Default()
        {
            return new KeeperProfile();
        }
    }

    /// <summary>
    /// El modelo del portero: dónde tiene las manos en cada instante del lance. Es el mismo
    /// para el portero máquina y para el del jugador, y la animación se ajusta a él.
    /// </summary>
    public static class KeeperModel
    {
        public static readonly Vector2 Ready = new Vector2(0f, 1.1f);

        public const float SideReach = 2.75f;
        private static readonly float[] RowHeight = { 0.35f, 1.25f, 2.15f };

        public static Vector2 HandTarget(KeeperDive dive)
        {
            int col = Math.Max(-1, Math.Min(1, dive.Column));
            int row = Math.Max(0, Math.Min(2, dive.Row));
            return new Vector2(col * SideReach, RowHeight[row]);
        }

        public static Vector2 HandPosition(KeeperDive dive, float time, KeeperProfile profile)
        {
            Vector2 target = HandTarget(dive);
            float elapsed = Math.Max(0f, time - dive.StartTime);
            float dist = Vector2.Distance(Ready, target);
            if (dist < 1e-4f) return target;
            float k = Math.Min(1f, profile.Speed * elapsed / dist);
            return Vector2.Lerp(Ready, target, k);
        }

        /// <summary>¿Llegan las manos al balón cuando cruza la línea?</summary>
        public static bool Reaches(Vector2 crossing, float time, KeeperDive dive, KeeperProfile profile)
        {
            Vector2 hands = HandPosition(dive, time, profile);
            return Vector2.Distance(hands, crossing) <= profile.CatchRadius + Field.BallRadius;
        }

        /// <summary>La zona (columna, fila) en la que cae un punto de la portería.</summary>
        public static void ZoneOf(Vector2 point, out int column, out int row)
        {
            float third = Field.GoalWidth / 6f;
            column = point.X < -third ? -1 : (point.X > third ? 1 : 0);
            row = point.Y < Field.GoalHeight / 3f ? 0 : (point.Y < 2f * Field.GoalHeight / 3f ? 1 : 2);
        }
    }
}
