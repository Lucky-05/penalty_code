using System.Numerics;

namespace Penales.Core
{
    public enum ShotOutcome
    {
        Goal,
        Saved,
        Post,
        Out
    }

    public struct ShotResult
    {
        public ShotOutcome Outcome;
        /// <summary>Por dónde cruzó (o dónde murió) el balón, sobre el plano de gol.</summary>
        public Vector2 Point;
        /// <summary>Segundos desde el golpeo.</summary>
        public float Time;

        public bool Scored
        {
            get { return Outcome == ShotOutcome.Goal; }
        }
    }

    /// <summary>
    /// Resuelve un penal sin escena: vuelo del balón contra lance del portero. Lo usan las
    /// tandas simuladas (los otros partidos del torneo) y las pruebas. En la escena el
    /// resultado lo dan las colisiones reales, que parten de este mismo vuelo.
    /// </summary>
    public static class ShotResolver
    {
        public static ShotResult Resolve(BallState kick, KeeperDive dive, KeeperProfile keeper)
        {
            GoalCrossing c = BallFlight.FlyToGoalPlane(kick);
            Vector2 p = new Vector2(c.Point.X, c.Point.Y);
            ShotResult r = new ShotResult { Point = p, Time = c.Time };

            if (!c.Crossed)
            {
                r.Outcome = ShotOutcome.Out;
                return r;
            }
            if (Field.InsideGoal(p.X, p.Y))
            {
                r.Outcome = KeeperModel.Reaches(p, c.Time, dive, keeper) ? ShotOutcome.Saved : ShotOutcome.Goal;
                return r;
            }
            r.Outcome = Field.TouchesFrame(p.X, p.Y) ? ShotOutcome.Post : ShotOutcome.Out;
            return r;
        }
    }
}
