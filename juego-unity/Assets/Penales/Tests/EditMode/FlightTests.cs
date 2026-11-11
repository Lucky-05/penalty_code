using System;
using System.Numerics;
using NUnit.Framework;
using Penales.Core;

namespace Penales.Tests
{
    public class FlightTests
    {
        [Test]
        public void SinGiroElBalonNoSeDesviaDeLado()
        {
            BallState s = new BallState(Field.PenaltySpot, new Vector3(0f, 3f, -25f), Vector3.Zero);
            GoalCrossing c = BallFlight.FlyToGoalPlane(s);
            Assert.That(c.Crossed, Is.True);
            Assert.That(Math.Abs(c.Point.X), Is.LessThan(1e-4f));
            Assert.That(c.Time, Is.GreaterThan(0.40f).And.LessThan(0.55f));
        }

        [Test]
        public void ElAireFrenaElBalon()
        {
            BallState s = new BallState(Field.PenaltySpot, new Vector3(0f, 3f, -25f), Vector3.Zero);
            GoalCrossing c = BallFlight.FlyToGoalPlane(s);
            Assert.That(c.Velocity.Length(), Is.LessThan(s.Vel.Length()));
        }

        [Test]
        public void ElEfectoCurvaHaciaElLadoQueSePide()
        {
            Vector3 vel = new Vector3(0f, 3f, -22f);
            GoalCrossing derecha = BallFlight.FlyToGoalPlane(
                new BallState(Field.PenaltySpot, vel, new Vector3(0f, -ShotSolver.MaxSpin, 0f)));
            GoalCrossing izquierda = BallFlight.FlyToGoalPlane(
                new BallState(Field.PenaltySpot, vel, new Vector3(0f, ShotSolver.MaxSpin, 0f)));
            Assert.That(derecha.Point.X, Is.GreaterThan(0.3f));
            Assert.That(izquierda.Point.X, Is.LessThan(-0.3f));
            Assert.That(derecha.Point.X, Is.EqualTo(-izquierda.Point.X).Within(1e-3f));
        }

        [Test]
        public void UnBalonQueNoAvanzaNoCruza()
        {
            BallState s = new BallState(Field.PenaltySpot, new Vector3(0f, 0f, -0.5f), Vector3.Zero);
            Assert.That(BallFlight.FlyToGoalPlane(s).Crossed, Is.False);
        }

        [Test]
        public void ElTiroLlegaADondeSeApunta_EnTodaLaPorteriaYATodaPotencia()
        {
            float peor = 0f;
            float[] xs = { -3.4f, -2.0f, 0f, 1.5f, 3.4f };
            float[] ys = { 0.15f, 0.9f, 1.6f, 2.25f };
            float[] potencias = { 0f, 0.35f, 0.7f, 1f };
            float[] curvas = { -1f, 0f, 0.6f };
            foreach (float x in xs)
                foreach (float y in ys)
                    foreach (float p in potencias)
                        foreach (float cu in curvas)
                        {
                            Vector2 target = new Vector2(x, y);
                            GoalCrossing c = BallFlight.FlyToGoalPlane(ShotSolver.Solve(target, p, cu));
                            Assert.That(c.Crossed, Is.True, "no cruzó: " + target + " p=" + p + " c=" + cu);
                            float error = Vector2.Distance(new Vector2(c.Point.X, c.Point.Y), target);
                            peor = Math.Max(peor, error);
                        }
            Assert.That(peor, Is.LessThan(0.05f), "peor error de apuntado (m)");
        }

        [Test]
        public void MasPotenciaEsMenosTiempoDeVuelo()
        {
            Vector2 target = new Vector2(2.5f, 1.0f);
            float lento = BallFlight.FlyToGoalPlane(ShotSolver.Solve(target, 0f, 0f)).Time;
            float fuerte = BallFlight.FlyToGoalPlane(ShotSolver.Solve(target, 1f, 0f)).Time;
            Assert.That(fuerte, Is.LessThan(lento));
            Assert.That(fuerte, Is.LessThan(0.40f));
            Assert.That(lento, Is.GreaterThan(0.70f));
        }

        [Test]
        public void LaVelocidadDeSalidaEsLaQueDiceLaPotencia()
        {
            Assert.That(ShotSolver.Solve(new Vector2(0f, 1f), 0f, 0f).Vel.Length(),
                Is.EqualTo(ShotSolver.MinSpeed).Within(1e-3f));
            Assert.That(ShotSolver.Solve(new Vector2(0f, 1f), 1f, 0f).Vel.Length(),
                Is.EqualTo(ShotSolver.MaxSpeed).Within(1e-3f));
            Assert.That(ShotSolver.Solve(new Vector2(0f, 1f), 7f, 0f).Vel.Length(),
                Is.EqualTo(ShotSolver.MaxSpeed).Within(1e-3f));
        }
    }
}
