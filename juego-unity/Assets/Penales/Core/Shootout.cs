using System;
using System.Collections.Generic;

namespace Penales.Core
{
    public enum Side
    {
        Home,
        Away
    }

    /// <summary>
    /// Las reglas de la tanda: turnos alternos, cinco tiros por lado, se corta en cuanto uno ya
    /// no puede alcanzar al otro y, si siguen empatados, muerte súbita de a un tiro por lado.
    /// </summary>
    public class Shootout
    {
        private readonly List<bool> _home = new List<bool>();
        private readonly List<bool> _away = new List<bool>();

        public int KicksEach { get; private set; }
        public Side First { get; private set; }
        public bool IsOver { get; private set; }
        public Side? Winner { get; private set; }

        public Shootout(int kicksEach = 5, Side first = Side.Home)
        {
            if (kicksEach < 1) throw new ArgumentOutOfRangeException("kicksEach");
            KicksEach = kicksEach;
            First = first;
        }

        public IReadOnlyList<bool> HomeKicks { get { return _home; } }
        public IReadOnlyList<bool> AwayKicks { get { return _away; } }
        public int HomeGoals { get { return Count(_home); } }
        public int AwayGoals { get { return Count(_away); } }

        public int Goals(Side s) { return s == Side.Home ? HomeGoals : AwayGoals; }
        public int Taken(Side s) { return s == Side.Home ? _home.Count : _away.Count; }

        /// <summary>A quién le toca patear.</summary>
        public Side Next
        {
            get
            {
                if (_home.Count == _away.Count) return First;
                return _home.Count < _away.Count ? Side.Home : Side.Away;
            }
        }

        public bool SuddenDeath
        {
            get { return _home.Count >= KicksEach && _away.Count >= KicksEach && !IsOver; }
        }

        /// <summary>Anota el resultado del tiro de quien le tocaba.</summary>
        public void Record(bool scored)
        {
            if (IsOver) throw new InvalidOperationException("La tanda ya terminó.");
            (Next == Side.Home ? _home : _away).Add(scored);
            Evaluate();
        }

        /// <summary>
        /// Qué tanto pesa el tiro que sigue, de 0 a 1. Vale 1 cuando define la tanda: si lo mete
        /// gana, o si lo falla pierde.
        /// </summary>
        public float Clutch()
        {
            if (IsOver) return 0f;
            if (WouldEnd(true) || WouldEnd(false)) return 1f;
            if (SuddenDeath) return 0.7f;
            int taken = _home.Count + _away.Count;
            return Math.Min(0.6f, 0.15f + 0.45f * taken / (2f * KicksEach));
        }

        private bool WouldEnd(bool scored)
        {
            Shootout copy = new Shootout(KicksEach, First);
            copy._home.AddRange(_home);
            copy._away.AddRange(_away);
            copy.Record(scored);
            return copy.IsOver;
        }

        private void Evaluate()
        {
            int h = HomeGoals, a = AwayGoals;
            if (_home.Count <= KicksEach && _away.Count <= KicksEach
                && !(_home.Count == KicksEach && _away.Count == KicksEach))
            {
                int homeLeft = KicksEach - _home.Count;
                int awayLeft = KicksEach - _away.Count;
                if (h > a + awayLeft) Finish(Side.Home);
                else if (a > h + homeLeft) Finish(Side.Away);
                return;
            }
            // Ya tiraron los cinco (o más): se define cuando llevan los mismos tiros y hay diferencia.
            if (_home.Count == _away.Count && h != a) Finish(h > a ? Side.Home : Side.Away);
        }

        private void Finish(Side winner)
        {
            IsOver = true;
            Winner = winner;
        }

        private static int Count(List<bool> kicks)
        {
            int n = 0;
            foreach (bool k in kicks) if (k) n++;
            return n;
        }
    }

    /// <summary>Juega una tanda completa máquina contra máquina.</summary>
    public static class ShootoutSimulator
    {
        public static Shootout Play(Team home, Team away, Difficulty d, Random rng)
        {
            Shootout s = new Shootout();
            while (!s.IsOver)
            {
                Side side = s.Next;
                Team kicking = side == Side.Home ? home : away;
                Team defending = side == Side.Home ? away : home;
                Player kicker = kicking.Kicker(s.Taken(side));
                s.Record(Kick(kicker.Tendencies, defending.Keeper, d, s.Clutch(), rng).Scored);
            }
            return s;
        }

        /// <summary>Un penal máquina contra máquina, de la decisión al resultado.</summary>
        public static ShotResult Kick(ShooterTendencies shooter, KeeperProfile keeper, Difficulty d, float clutch, Random rng)
        {
            ShotIntent wanted = ShooterBrain.Decide(shooter, d, rng);
            ShotIntent actual = ShooterBrain.Execute(wanted, shooter, clutch, rng);
            KeeperDive dive = KeeperBrain.Decide(actual.Target, shooter, d, rng);
            return ShotResolver.Resolve(ShotSolver.Solve(actual), dive, keeper);
        }
    }
}
