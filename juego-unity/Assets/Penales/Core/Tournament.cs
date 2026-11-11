using System;
using System.Collections.Generic;

namespace Penales.Core
{
    public class Fixture
    {
        public Team Home;
        public Team Away;
        public Team Winner;

        public bool Played
        {
            get { return Winner != null; }
        }
    }

    /// <summary>
    /// Torneo de eliminación directa: cuartos, semifinal y final, cada cruce a una tanda.
    /// El jugador reporta su partido; los demás se simulan.
    /// </summary>
    public class Tournament
    {
        private readonly List<List<Fixture>> _rounds = new List<List<Fixture>>();

        public Team PlayerTeam { get; private set; }
        public int RoundIndex { get; private set; }
        public Team Champion { get; private set; }

        public Tournament(IList<Team> teams, Team playerTeam, Random rng)
        {
            if (teams == null || teams.Count < 2 || (teams.Count & (teams.Count - 1)) != 0)
                throw new ArgumentException("El torneo necesita 2, 4, 8 o 16 equipos.");
            if (!teams.Contains(playerTeam))
                throw new ArgumentException("El equipo del jugador no está en el torneo.");

            PlayerTeam = playerTeam;
            List<Team> order = new List<Team>(teams);
            for (int i = order.Count - 1; i > 0; i--)       // sorteo
            {
                int j = rng.Next(i + 1);
                Team tmp = order[i];
                order[i] = order[j];
                order[j] = tmp;
            }
            _rounds.Add(Pair(order));
        }

        public IReadOnlyList<Fixture> CurrentRound
        {
            get { return _rounds[RoundIndex]; }
        }

        public bool IsOver
        {
            get { return Champion != null; }
        }

        /// <summary>¿Sigue vivo el equipo del jugador?</summary>
        public bool PlayerAlive
        {
            get
            {
                if (IsOver) return Champion == PlayerTeam;
                Fixture f = PlayerFixture;
                return f != null && (!f.Played || f.Winner == PlayerTeam);
            }
        }

        /// <summary>El partido que le toca al jugador en esta ronda, o null si ya quedó fuera.</summary>
        public Fixture PlayerFixture
        {
            get
            {
                if (IsOver) return null;
                foreach (Fixture f in CurrentRound)
                    if (f.Home == PlayerTeam || f.Away == PlayerTeam) return f;
                return null;
            }
        }

        public string RoundName
        {
            get
            {
                switch (CurrentRound.Count)
                {
                    case 1: return "Final";
                    case 2: return "Semifinal";
                    case 4: return "Cuartos de final";
                    default: return "Octavos de final";
                }
            }
        }

        /// <summary>Anota quién ganó el partido del jugador.</summary>
        public void ReportPlayerResult(bool playerWon)
        {
            Fixture f = PlayerFixture;
            if (f == null) throw new InvalidOperationException("El jugador no tiene partido en esta ronda.");
            if (f.Played) throw new InvalidOperationException("Ese partido ya se jugó.");
            Team rival = f.Home == PlayerTeam ? f.Away : f.Home;
            f.Winner = playerWon ? PlayerTeam : rival;
        }

        /// <summary>Simula lo que falte de la ronda y arma la siguiente.</summary>
        public void Advance(Difficulty d, Random rng)
        {
            if (IsOver) return;
            List<Team> winners = new List<Team>();
            foreach (Fixture f in CurrentRound)
            {
                if (!f.Played)
                {
                    if (f.Home == PlayerTeam || f.Away == PlayerTeam)
                        throw new InvalidOperationException("Falta jugar el partido del jugador.");
                    Shootout s = ShootoutSimulator.Play(f.Home, f.Away, d, rng);
                    f.Winner = s.Winner == Side.Home ? f.Home : f.Away;
                }
                winners.Add(f.Winner);
            }
            if (winners.Count == 1)
            {
                Champion = winners[0];
                return;
            }
            _rounds.Add(Pair(winners));
            RoundIndex++;
        }

        private static List<Fixture> Pair(List<Team> teams)
        {
            List<Fixture> round = new List<Fixture>();
            for (int i = 0; i < teams.Count; i += 2)
                round.Add(new Fixture { Home = teams[i], Away = teams[i + 1] });
            return round;
        }
    }
}
