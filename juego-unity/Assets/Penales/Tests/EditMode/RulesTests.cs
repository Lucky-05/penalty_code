using System;
using System.Collections.Generic;
using System.Numerics;
using NUnit.Framework;
using Penales.Core;

namespace Penales.Tests
{
    public class PressureTests
    {
        [Test]
        public void ElMargenCreceConLaPotencia()
        {
            Vector2 t = new Vector2(1f, 1f);
            Assert.That(Pressure.ErrorRadius(1f, t, 0f, 0.5f), Is.GreaterThan(Pressure.ErrorRadius(0.2f, t, 0f, 0.5f)));
        }

        [Test]
        public void ElMargenCreceHaciaLaEsquina()
        {
            float centro = Pressure.ErrorRadius(0.5f, new Vector2(0f, 1f), 0f, 0.5f);
            float poste = Pressure.ErrorRadius(0.5f, new Vector2(3.4f, 1f), 0f, 0.5f);
            float angulo = Pressure.ErrorRadius(0.5f, new Vector2(3.4f, 2.3f), 0f, 0.5f);
            Assert.That(poste, Is.GreaterThan(centro));
            Assert.That(angulo, Is.GreaterThan(poste));
        }

        [Test]
        public void ElTiroDecisivoPesaYLaSangreFriaAyuda()
        {
            Vector2 t = new Vector2(2f, 1f);
            Assert.That(Pressure.ErrorRadius(0.5f, t, 1f, 0.5f), Is.GreaterThan(Pressure.ErrorRadius(0.5f, t, 0f, 0.5f)));
            Assert.That(Pressure.ErrorRadius(0.5f, t, 1f, 1f), Is.LessThan(Pressure.ErrorRadius(0.5f, t, 1f, 0f)));
        }

        [Test]
        public void ElDesvioNuncaSaleDelMargen()
        {
            Random rng = new Random(7);
            Vector2 t = new Vector2(1f, 1f);
            for (int i = 0; i < 2000; i++)
                Assert.That(Vector2.Distance(Pressure.Disturb(t, 0.4f, rng), t), Is.LessThanOrEqualTo(0.4f + 1e-4f));
        }

        [Test]
        public void UnTiroColocadoAlCentroCasiNoSeDesvia()
        {
            Assert.That(Pressure.ErrorRadius(0f, new Vector2(0f, 1f), 0f, 1f), Is.LessThan(0.06f));
        }
    }

    public class KeeperTests
    {
        private static readonly KeeperProfile P = KeeperProfile.Default();

        [Test]
        public void AntesDeArrancarEstaEnElCentro()
        {
            KeeperDive d = new KeeperDive(1, 0, 0.2f);
            Assert.That(KeeperModel.HandPosition(d, 0.1f, P), Is.EqualTo(KeeperModel.Ready));
        }

        [Test]
        public void ConTiempoLlegaASuZonaYNoSeSigueDeLargo()
        {
            KeeperDive d = new KeeperDive(-1, 2, 0f);
            Vector2 fin = KeeperModel.HandPosition(d, 5f, P);
            Assert.That(fin.X, Is.EqualTo(-KeeperModel.SideReach).Within(1e-4f));
            Assert.That(fin.Y, Is.EqualTo(2.15f).Within(1e-4f));
        }

        [Test]
        public void SiAdivinaElLadoAtajaUnTiroColocadoPeroNoUnoAlAngulo()
        {
            KeeperDive buena = new KeeperDive(1, 0, 0.05f);
            ShotResult colocado = ShotResolver.Resolve(ShotSolver.Solve(new Vector2(2.4f, 0.4f), 0.3f, 0f), buena, P);
            Assert.That(colocado.Outcome, Is.EqualTo(ShotOutcome.Saved));

            KeeperDive arriba = new KeeperDive(1, 0, 0.05f);
            ShotResult angulo = ShotResolver.Resolve(ShotSolver.Solve(new Vector2(3.35f, 2.2f), 0.9f, 0f), arriba, P);
            Assert.That(angulo.Outcome, Is.EqualTo(ShotOutcome.Goal));
        }

        [Test]
        public void SiSeVaAlOtroLadoEsGol()
        {
            KeeperDive mala = new KeeperDive(-1, 0, 0f);
            ShotResult r = ShotResolver.Resolve(ShotSolver.Solve(new Vector2(2.4f, 0.4f), 0.3f, 0f), mala, P);
            Assert.That(r.Outcome, Is.EqualTo(ShotOutcome.Goal));
        }

        [Test]
        public void FueraYPoste()
        {
            KeeperDive d = new KeeperDive(0, 1, 0f);
            Assert.That(ShotResolver.Resolve(ShotSolver.Solve(new Vector2(5f, 1f), 0.6f, 0f), d, P).Outcome,
                Is.EqualTo(ShotOutcome.Out));
            Assert.That(ShotResolver.Resolve(ShotSolver.Solve(new Vector2(0f, 4f), 0.6f, 0f), d, P).Outcome,
                Is.EqualTo(ShotOutcome.Out));
            Assert.That(ShotResolver.Resolve(ShotSolver.Solve(new Vector2(Field.HalfGoal + 0.03f, 1f), 0.6f, 0f), d, P).Outcome,
                Is.EqualTo(ShotOutcome.Post));
            Assert.That(ShotResolver.Resolve(ShotSolver.Solve(new Vector2(0f, Field.GoalHeight + 0.03f), 0.6f, 0f), d, P).Outcome,
                Is.EqualTo(ShotOutcome.Post));
        }

        [Test]
        public void LasNueveZonas()
        {
            int c, r;
            KeeperModel.ZoneOf(new Vector2(-3f, 0.2f), out c, out r);
            Assert.That(new[] { c, r }, Is.EqualTo(new[] { -1, 0 }));
            KeeperModel.ZoneOf(new Vector2(0f, 1.2f), out c, out r);
            Assert.That(new[] { c, r }, Is.EqualTo(new[] { 0, 1 }));
            KeeperModel.ZoneOf(new Vector2(3f, 2.3f), out c, out r);
            Assert.That(new[] { c, r }, Is.EqualTo(new[] { 1, 2 }));
        }
    }

    public class ShootoutTests
    {
        private static Shootout Jugar(string homeKicks, string awayKicks)
        {
            // "1" gol, "0" fallo; se alternan empezando por el local.
            Shootout s = new Shootout();
            int h = 0, a = 0;
            while (!s.IsOver && (h < homeKicks.Length || a < awayKicks.Length))
            {
                if (s.Next == Side.Home)
                {
                    if (h >= homeKicks.Length) break;
                    s.Record(homeKicks[h++] == '1');
                }
                else
                {
                    if (a >= awayKicks.Length) break;
                    s.Record(awayKicks[a++] == '1');
                }
            }
            return s;
        }

        [Test]
        public void EmpiezaElLocalYSeAlternan()
        {
            Shootout s = new Shootout();
            Assert.That(s.Next, Is.EqualTo(Side.Home));
            s.Record(true);
            Assert.That(s.Next, Is.EqualTo(Side.Away));
            s.Record(false);
            Assert.That(s.Next, Is.EqualTo(Side.Home));
            Assert.That(s.HomeGoals, Is.EqualTo(1));
            Assert.That(s.AwayGoals, Is.EqualTo(0));
        }

        [Test]
        public void SeCortaCuandoYaNoLoAlcanzan()
        {
            // 3-0 tras tres tiros cada uno: al visitante le quedan 2 y necesita 3.
            Shootout s = Jugar("111", "000");
            Assert.That(s.IsOver, Is.True);
            Assert.That(s.Winner, Is.EqualTo(Side.Home));
            Assert.That(s.HomeKicks.Count, Is.EqualTo(3));
            Assert.That(s.AwayKicks.Count, Is.EqualTo(3));
        }

        [Test]
        public void NoSeCortaMientrasSeaAlcanzable()
        {
            Shootout s = Jugar("11", "00");     // 2-0, al visitante le quedan 3
            Assert.That(s.IsOver, Is.False);
        }

        [Test]
        public void ElVisitanteGanaSiElLocalYaNoLlega()
        {
            // Local 0 de 4, visitante 2 de 3: al local le queda 1 y necesita 2.
            Shootout s = Jugar("0000", "110");
            Assert.That(s.IsOver, Is.True);
            Assert.That(s.Winner, Is.EqualTo(Side.Away));
        }

        [Test]
        public void SeDefineEnElQuintoTiro()
        {
            Shootout s = Jugar("11111", "11110");
            Assert.That(s.IsOver, Is.True);
            Assert.That(s.Winner, Is.EqualTo(Side.Home));
        }

        [Test]
        public void EmpateACincoVaAMuerteSubita()
        {
            Shootout s = Jugar("11111", "11111");
            Assert.That(s.IsOver, Is.False);
            Assert.That(s.SuddenDeath, Is.True);
            s.Record(true);                       // local mete el sexto
            Assert.That(s.IsOver, Is.False, "el visitante todavía tiene su tiro");
            s.Record(false);                      // visitante falla
            Assert.That(s.IsOver, Is.True);
            Assert.That(s.Winner, Is.EqualTo(Side.Home));
        }

        [Test]
        public void EnMuerteSubitaSiLosDosFallanSigue()
        {
            Shootout s = Jugar("11111", "11111");
            s.Record(false);
            s.Record(false);
            Assert.That(s.IsOver, Is.False);
            s.Record(false);
            s.Record(true);
            Assert.That(s.Winner, Is.EqualTo(Side.Away));
        }

        [Test]
        public void NoSePuedeAnotarDespuesDeTerminar()
        {
            Shootout s = Jugar("111", "000");
            Assert.Throws<InvalidOperationException>(() => s.Record(true));
        }

        [Test]
        public void ElTiroQueDefineTienePresionMaxima()
        {
            Shootout inicio = new Shootout();
            Assert.That(inicio.Clutch(), Is.LessThan(0.3f));

            Shootout s = Jugar("1111", "1110");   // 4-3, patea el local el quinto: si mete, gana
            Assert.That(s.Next, Is.EqualTo(Side.Home));
            Assert.That(s.Clutch(), Is.EqualTo(1f));
        }
    }

    public class MachineTests
    {
        private static float TasaDeGol(Difficulty d, int seed, int n)
        {
            Random rng = new Random(seed);
            List<Team> teams = Teams.CreateDefault();
            int goles = 0;
            for (int i = 0; i < n; i++)
            {
                Team t = teams[i % teams.Count];
                Player p = t.Kicker(i);
                if (ShootoutSimulator.Kick(p.Tendencies, teams[(i + 1) % teams.Count].Keeper, d, 0.3f, rng).Scored) goles++;
            }
            return goles / (float)n;
        }

        [Test]
        public void LaTasaDeGolEsCreibleEnTodosLosNiveles()
        {
            // En el futbol real se anotan alrededor de tres de cada cuatro penales.
            foreach (Difficulty d in new[] { Difficulty.Easy, Difficulty.Normal, Difficulty.Hard })
            {
                float tasa = TasaDeGol(d, 11, 10000);
                Assert.That(tasa, Is.GreaterThan(0.60f).And.LessThan(0.92f), d + " tasa de gol " + tasa);
            }
        }

        [Test]
        public void ElPorteroAtajaMasMientrasMasDificil()
        {
            Random rng = new Random(3);
            KeeperProfile keeper = KeeperProfile.Default();
            ShooterTendencies shooter = new ShooterTendencies();
            int[] atajadas = new int[3];
            Difficulty[] niveles = { Difficulty.Easy, Difficulty.Normal, Difficulty.Hard };
            for (int k = 0; k < 3; k++)
            {
                for (int i = 0; i < 10000; i++)
                {
                    // El mismo tipo de tiro para los tres niveles: colocado, a media esquina.
                    float lado = rng.Next(2) == 0 ? -1f : 1f;
                    Vector2 target = new Vector2(lado * 2.3f, 0.2f + (float)rng.NextDouble() * 1.8f);
                    KeeperDive dive = KeeperBrain.Decide(target, shooter, niveles[k], rng);
                    if (ShotResolver.Resolve(ShotSolver.Solve(target, 0.45f, 0f), dive, keeper).Outcome == ShotOutcome.Saved)
                        atajadas[k]++;
                }
            }
            Assert.That(atajadas[1], Is.GreaterThan(atajadas[0]), "normal vs fácil");
            Assert.That(atajadas[2], Is.GreaterThan(atajadas[1]), "difícil vs normal");
        }

        [Test]
        public void LaMaquinaApuntaDentroDeLaPorteria()
        {
            Random rng = new Random(5);
            foreach (Team t in Teams.CreateDefault())
                foreach (Player p in t.Players)
                    for (int i = 0; i < 200; i++)
                    {
                        ShotIntent s = ShooterBrain.Decide(p.Tendencies, Difficulty.Hard, rng);
                        Assert.That(Field.InsideGoal(s.Target.X, s.Target.Y), Is.True, s.Target.ToString());
                    }
        }

        [Test]
        public void LaMismaSemillaDaLaMismaTanda()
        {
            List<Team> teams = Teams.CreateDefault();
            Shootout a = ShootoutSimulator.Play(teams[0], teams[1], Difficulty.Normal, new Random(42));
            Shootout b = ShootoutSimulator.Play(teams[0], teams[1], Difficulty.Normal, new Random(42));
            Assert.That(a.IsOver && b.IsOver, Is.True);
            Assert.That(a.Winner, Is.EqualTo(b.Winner));
            Assert.That(a.HomeKicks, Is.EqualTo(b.HomeKicks));
            Assert.That(a.AwayKicks, Is.EqualTo(b.AwayKicks));
        }
    }

    public class TeamsAndTournamentTests
    {
        [Test]
        public void SonOchoEquiposCompletosYSiempreLosMismos()
        {
            List<Team> a = Teams.CreateDefault();
            List<Team> b = Teams.CreateDefault();
            Assert.That(a.Count, Is.EqualTo(8));
            for (int i = 0; i < 8; i++)
            {
                Assert.That(a[i].Players.Count, Is.EqualTo(Teams.PlayersPerTeam));
                Assert.That(a[i].Players[0].Name, Is.EqualTo(b[i].Players[0].Name));
                Assert.That(a[i].PrimaryColor.Length, Is.EqualTo(6));
            }
        }

        [Test]
        public void ElTiradorRotaCuandoSeAcabaLaLista()
        {
            Team t = Teams.CreateDefault()[0];
            Assert.That(t.Kicker(Teams.PlayersPerTeam), Is.SameAs(t.Kicker(0)));
        }

        [Test]
        public void ElTorneoLlegaAUnCampeonSiElJugadorGanaTodo()
        {
            List<Team> teams = Teams.CreateDefault();
            Random rng = new Random(1);
            Tournament t = new Tournament(teams, teams[3], rng);
            string[] rondas = { "Cuartos de final", "Semifinal", "Final" };
            for (int r = 0; r < 3; r++)
            {
                Assert.That(t.RoundName, Is.EqualTo(rondas[r]));
                Assert.That(t.PlayerFixture, Is.Not.Null);
                t.ReportPlayerResult(true);
                Assert.That(t.PlayerAlive, Is.True);
                t.Advance(Difficulty.Normal, rng);
            }
            Assert.That(t.IsOver, Is.True);
            Assert.That(t.Champion, Is.SameAs(teams[3]));
        }

        [Test]
        public void SiElJugadorPierdeElTorneoSigueSinEl()
        {
            List<Team> teams = Teams.CreateDefault();
            Random rng = new Random(2);
            Tournament t = new Tournament(teams, teams[0], rng);
            t.ReportPlayerResult(false);
            Assert.That(t.PlayerAlive, Is.False);
            t.Advance(Difficulty.Normal, rng);
            Assert.That(t.PlayerFixture, Is.Null);
            t.Advance(Difficulty.Normal, rng);
            t.Advance(Difficulty.Normal, rng);
            Assert.That(t.IsOver, Is.True);
            Assert.That(t.Champion, Is.Not.SameAs(teams[0]));
        }

        [Test]
        public void NoSeAvanzaSinJugarElPartidoDelJugador()
        {
            List<Team> teams = Teams.CreateDefault();
            Tournament t = new Tournament(teams, teams[0], new Random(2));
            Assert.Throws<InvalidOperationException>(() => t.Advance(Difficulty.Normal, new Random(2)));
        }

        [Test]
        public void NoAceptaUnNumeroDeEquiposQueNoArmaLlave()
        {
            List<Team> teams = Teams.CreateDefault();
            teams.RemoveAt(7);
            Assert.Throws<ArgumentException>(() => new Tournament(teams, teams[0], new Random(1)));
        }
    }
}
