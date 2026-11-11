using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Penales.Core;
using CoreVec2 = System.Numerics.Vector2;

namespace Penales.Game
{
    /// <summary>
    /// Lleva la tanda de principio a fin: a quién le toca, el tiro del jugador, la atajada del
    /// jugador, el vuelo del balón y lo que pasa después. Las reglas y la física vienen de la
    /// lógica (Penales.Core); aquí solo se les pone cuerpo y tiempo.
    /// </summary>
    public class MatchDirector : MonoBehaviour
    {
        public Difficulty Difficulty = Difficulty.Normal;
        public int Seed;

        public InputRouter Controls;
        public EmgLink Emg;
        public EmgSetupScreen EmgSetup;
        public Transform Ball;
        public ShooterView Shooter;
        public KeeperView Keeper;
        public ReticleView Reticle;
        public Hud Hud;
        public Camera View;
        public MenuScreen Menu;
        public AudioDirector Sound;

        private const float RunUpTime = 1.3f;
        private const float ChargeTime = 1.1f;
        /// <summary>Si el portero se adelanta más que esto, el tirador lo ve y cambia de lado.</summary>
        private const float EarlyDive = -0.18f;


        private System.Random _rng;
        private List<Team> _teams;
        private Team _home;
        private Team _away;
        private Shootout _shootout;
        private CoreVec2 _aim = new CoreVec2(0f, 1.1f);
        private bool _lastScored;
        private bool _abort;

        private void Start()
        {
            _rng = Seed != 0 ? new System.Random(Seed) : new System.Random();
            _teams = Teams.CreateDefault();
            StartCoroutine(Run());
        }

        private void Update()
        {
            if (Controls != null && Controls.MenuPressed && _shootout != null && !_shootout.IsOver) _abort = true;
        }

        /// <summary>El juego completo: menú, partido o torneo, resultado, y de vuelta al menú.</summary>
        private IEnumerator Run()
        {
            // Configuración EMG al inicio: contracción máxima de cada canal -> umbral de tiro al 70 %.
            Hud.Visible = false;
            if (EmgSetup != null) yield return EmgSetup.Run();

            while (true)
            {
                Hud.Visible = false;
                Menu.ShowMain(_teams);
                while (!Menu.Confirmed)
                {
                    UnityEngine.InputSystem.Keyboard k = UnityEngine.InputSystem.Keyboard.current;
                    if (EmgSetup != null && k != null && (k.digit8Key.wasPressedThisFrame || k.numpad8Key.wasPressedThisFrame))
                    {
                        Menu.Hide();
                        yield return EmgSetup.Run();
                        Menu.ShowMain(_teams);
                    }
                    yield return null;
                }
                Difficulty = Menu.Difficulty;
                _home = _teams[Menu.TeamIndex];
                _abort = false;

                if (Menu.Mode == GameMode.Quick)
                {
                    List<Team> rivals = new List<Team>(_teams);
                    rivals.Remove(_home);
                    _away = rivals[_rng.Next(rivals.Count)];
                    yield return PlayMatch("Tanda rápida");
                    if (_abort) continue;
                    bool won = _shootout.Winner == Side.Home;
                    yield return Notice(won ? "¡GANASTE!" : "PERDISTE",
                        _home.Name + "  " + _shootout.HomeGoals + " - " + _shootout.AwayGoals + "  " + _away.Name, "Volver al menú");
                    continue;
                }

                Tournament cup = new Tournament(_teams, _home, _rng);
                while (!cup.IsOver && cup.PlayerAlive && !_abort)
                {
                    Fixture f = cup.PlayerFixture;
                    _away = f.Home == _home ? f.Away : f.Home;
                    string round = cup.RoundName;
                    yield return Notice(round.ToUpperInvariant(), _home.Name + "  vs  " + _away.Name + "\n\n" + Bracket(cup), "Jugar");
                    yield return PlayMatch(round);
                    if (_abort) break;
                    bool won = _shootout.Winner == Side.Home;
                    cup.ReportPlayerResult(won);
                    cup.Advance(Difficulty, _rng);
                    if (!won)
                    {
                        yield return Notice("ELIMINADO", "Caíste en " + round.ToLowerInvariant() + " ante " + _away.Name + ".\n"
                            + _shootout.HomeGoals + " - " + _shootout.AwayGoals, "Volver al menú");
                    }
                    else if (cup.IsOver)
                    {
                        Sound.Roar();
                        yield return Notice("¡CAMPEÓN!", _home.Name + " gana el torneo.", "Volver al menú");
                    }
                }
            }
        }

        private IEnumerator Notice(string title, string body, string button)
        {
            Hud.Visible = false;
            Menu.ShowNotice(title, body, button);
            while (!Menu.Dismissed) yield return null;
        }

        private static string Bracket(Tournament cup)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            foreach (Fixture f in cup.CurrentRound)
                sb.AppendLine(f.Home.ShortName + "  –  " + f.Away.ShortName);
            return sb.ToString();
        }

        /// <summary>Una tanda completa. Si el jugador pide el menú, se corta y queda <see cref="_abort"/>.</summary>
        private IEnumerator PlayMatch(string stage)
        {
            _shootout = new Shootout();
            Hud.Bind(_home, _away, _shootout);
            if (Emg != null) { Emg.HomeName = _home.Name; Emg.AwayName = _away.Name; }
            Hud.Stage = stage;
            Hud.Visible = true;
            Sound.Murmur();

            while (!_shootout.IsOver && !_abort)
            {
                bool playerKicks = _shootout.Next == Side.Home;
                ResetPositions(playerKicks);
                yield return new WaitForSeconds(0.6f);
                if (playerKicks) yield return PlayerKicks();
                else yield return PlayerSaves();
                if (_abort) break;
                for (float t = 0f; t < 1.9f; t += Time.deltaTime)
                {
                    Shooter.React(_lastScored, t);
                    yield return null;
                }
                Sound.Murmur();
            }
            Hud.Banner = "";
            Hud.Hint = "";
            Hud.Turn = "";
        }

        private void ResetPositions(bool playerKicks)
        {
            Ball.position = Conv.ToUnity(Field.PenaltySpot);
            Team kicking = playerKicks ? _home : _away;
            Team defending = playerKicks ? _away : _home;
            Shooter.SetKit(kicking);
            Shooter.Ready();
            Keeper.SetKit(defending);
            Keeper.Pose(KeeperModel.Ready, 0, 0f);
            Reticle.Show(false);
            Hud.Banner = "";
            Hud.Power = -1f;
        }

        // ── El jugador patea ────────────────────────────────────────────────────────────────

        private IEnumerator PlayerKicks()
        {
            Player kicker = _home.Kicker(_shootout.Taken(Side.Home));
            float clutch = _shootout.Clutch();
            Hud.Hint = Versus
                ? "Patea " + _home.Name + ": contrae el canal 1 (A2) · o apunta con el ratón y ESPACIO"
                : "Apunta con el ratón o las flechas · mantén ESPACIO o clic para la potencia · Q/E efecto";
            Hud.Turn = (Versus ? "PATEA " + _home.Name.ToUpperInvariant() + " (A2) · " : "PATEAS TÚ · ") + kicker.Name + " (" + kicker.Number + ")";

            Reticle.Show(true);
            Sound.Hush();
            float power = 0f;
            float chargeClock = 0f;
            bool charging = false;

            while (true)
            {
                MoveAim();
                if (Emg != null && Emg.KickHome)
                {
                    // Pico en el canal 1: tiro a gol a un lado, a media altura y con potencia controlada.
                    _aim = EmgTarget();
                    power = EmgPower;
                    Hud.Power = power;
                    break;
                }
                if (Controls.ActionPressed) { charging = true; chargeClock = 0f; }
                if (charging)
                {
                    chargeClock += Time.deltaTime;
                    power = Mathf.PingPong(chargeClock / ChargeTime, 1f);    // sube y baja: hay que soltar a tiempo
                    Hud.Power = power;
                }
                float radius = Pressure.ErrorRadius(power, _aim, clutch, kicker.Tendencies.Composure);
                Reticle.Set(_aim, radius, power);
                if (charging && Controls.ActionReleased) break;
                if (_abort) { Reticle.Show(false); Hud.Power = -1f; yield break; }
                yield return null;
            }

            float curve = Controls.Curve;
            ShotIntent wanted = new ShotIntent(_aim, power, curve);
            ShotIntent actual = ShooterBrain.Execute(wanted, kicker.Tendencies, clutch, _rng);
            KeeperDive dive = KeeperBrain.Decide(actual.Target, kicker.Tendencies, Difficulty, _rng);

            Reticle.Show(false);
            Hud.Hint = "";
            Sound.Whistle();
            ShotResult result = default(ShotResult);

            if (!Versus)
            {
                yield return RunUp();
                yield return Fly(ShotSolver.Solve(actual), t => dive, _away.Keeper, r => result = r);
                Finish(result, true);
                yield break;
            }

            // Versus: el portero rival se lanza solo, adivinando lado y altura (no depende del EMG).
            Hud.Hint = "El portero de " + _away.Name + " intenta adivinar";
            KeeperDive rivalDive = GuessDive(Mathf.Lerp(0f, 0.08f, (float)_rng.NextDouble()));
            yield return RunUp();
            yield return Fly(ShotSolver.Solve(actual), t => rivalDive, _away.Keeper, r => result = r);
            Hud.Hint = "";
            Finish(result, true);
        }

        private void MoveAim()
        {
            if (Controls.PointerMoved)
            {
                Ray ray = View.ScreenPointToRay(Controls.PointerScreen);
                if (Mathf.Abs(ray.direction.z) > 1e-4f)
                {
                    float k = -ray.origin.z / ray.direction.z;      // el plano de gol es Z = 0
                    Vector3 hit = ray.origin + ray.direction * k;
                    _aim = new CoreVec2(hit.x, hit.y);
                }
            }
            else if (Controls.Direction.sqrMagnitude > 0.01f)
            {
                Vector2 step = Controls.Direction * (3.2f * Time.deltaTime);
                _aim = new CoreVec2(_aim.X + step.x, _aim.Y + step.y);
            }
            _aim = new CoreVec2(
                Mathf.Clamp(_aim.X, -Field.HalfGoal - 0.8f, Field.HalfGoal + 0.8f),
                Mathf.Clamp(_aim.Y, Field.BallRadius, Field.GoalHeight + 0.7f));
        }

        // ── El jugador ataja ────────────────────────────────────────────────────────────────

        private IEnumerator PlayerSaves()
        {
            Player kicker = _away.Kicker(_shootout.Taken(Side.Away));
            float clutch = _shootout.Clutch();
            bool versus = Versus;
            Hud.Turn = versus
                ? "PATEA " + _away.Name.ToUpperInvariant() + " (A3) · " + kicker.Name
                : "ATAJAS TÚ · patea " + kicker.Name;
            Hud.Hint = versus
                ? "Patea " + _away.Name + ": contrae el canal 2 (A3)"
                : "Elige lado y altura con las flechas y lánzate con ESPACIO · no te adelantes";

            ShotIntent wanted = ShooterBrain.Decide(kicker.Tendencies, Difficulty, _rng);
            Sound.Hush();

            if (versus)
            {
                // El tiro del rival lo da el pico del canal 2. Si el Arduino se cae, patea la máquina.
                while (!Emg.KickAway && Emg.Live)
                {
                    if (_abort) yield break;
                    yield return null;
                }
                if (Emg.Live) wanted = new ShotIntent(EmgTarget(), EmgPower, 0f);
                else versus = false;
                Hud.Hint = versus
                    ? "El portero de " + _home.Name + " intenta adivinar"
                    : "Elige lado y altura con las flechas y lánzate con ESPACIO · no te adelantes";
            }
            Sound.Whistle();

            // Versus: el portero local también se lanza solo, adivinando; sin EMG lo controla el teclado.
            bool dived = versus;
            KeeperDive dive = versus ? GuessDive(Mathf.Lerp(-0.12f, 0.05f, (float)_rng.NextDouble())) : new KeeperDive(0, 1, 0f);
            float clock = -RunUpTime;                 // 0 = el golpeo

            // Carrera del tirador: el jugador ya puede lanzarse (y delatarse).
            while (clock < 0f)
            {
                clock += Time.deltaTime;
                Shooter.RunUp(1f + clock / RunUpTime);
                if (!dived && Controls.ActionPressed)
                {
                    dived = true;
                    dive = DiveFromInput(Mathf.Max(clock, -0.45f));
                }
                if (dived) PoseKeeper(dive, clock, _home.Keeper);
                yield return null;
            }

            // Si se adelantó de más, el tirador lo ve y la manda al otro lado.
            if (!versus && dived && dive.StartTime < EarlyDive && dive.Column != 0 && _rng.NextDouble() < 0.75)
            {
                wanted.Target = new CoreVec2(-dive.Column * Mathf.Abs(wanted.Target.X), wanted.Target.Y);
                if (Mathf.Abs(wanted.Target.X) < 1.6f) wanted.Target = new CoreVec2(-dive.Column * 2.4f, wanted.Target.Y);
            }
            ShotIntent actual = ShooterBrain.Execute(wanted, kicker.Tendencies, clutch, _rng);

            ShotResult result = default(ShotResult);
            yield return Fly(ShotSolver.Solve(actual), t =>
            {
                if (!dived && Controls.ActionPressed)
                {
                    dived = true;
                    dive = DiveFromInput(t);
                }
                // Si nunca se lanza, se queda parado: el "lance" empieza cuando ya pasó el balón.
                return dived ? dive : new KeeperDive(0, 1, 99f);
            }, _home.Keeper, r => result = r);
            Hud.Hint = "";
            Finish(result, false);
        }

        // ── EMG: canal 1 (A2) = local, canal 2 (A3) = rival ──────────────────────────────

        /// <summary>Hay Arduino mandando datos: el rival lo juega el canal 2 en vez de la máquina.</summary>
        private bool Versus => Emg != null && Emg.Live;
        private const float EmgPower = 0.6f;

        /// <summary>Tiro por EMG: a un lado, a media altura, siempre dentro del arco.</summary>
        private CoreVec2 EmgTarget()
        {
            float side = _rng.NextDouble() < 0.5 ? -1f : 1f;
            return new CoreVec2(side * Mathf.Lerp(1.5f, 2.3f, (float)_rng.NextDouble()), Mathf.Lerp(0.4f, 1.4f, (float)_rng.NextDouble()));
        }

        /// <summary>El portero adivina: izquierda o derecha (a veces se queda al centro), baja o media.</summary>
        private KeeperDive GuessDive(float startTime)
        {
            double r = _rng.NextDouble();
            int column = r < 0.42 ? -1 : (r < 0.84 ? 1 : 0);
            return new KeeperDive(column, _rng.Next(0, 2), startTime);
        }

        private KeeperDive DiveFromInput(float startTime)
        {
            Vector2 d = Controls.Direction;
            int column = d.x > 0.35f ? 1 : (d.x < -0.35f ? -1 : 0);
            int row = d.y > 0.35f ? 2 : (d.y < -0.35f ? 0 : 1);
            return new KeeperDive(column, row, startTime);
        }

        // ── Lo común: carrera, vuelo y desenlace ────────────────────────────────────────────

        private IEnumerator RunUp()
        {
            float t = 0f;
            while (t < RunUpTime)
            {
                t += Time.deltaTime;
                Shooter.RunUp(t / RunUpTime);
                yield return null;
            }
        }

        private void PoseKeeper(KeeperDive dive, float time, KeeperProfile profile)
        {
            CoreVec2 hands = KeeperModel.HandPosition(dive, time, profile);
            float total = CoreVec2.Distance(KeeperModel.Ready, KeeperModel.HandTarget(dive));
            float progress = total < 1e-3f ? 0f : CoreVec2.Distance(KeeperModel.Ready, hands) / total;
            Keeper.Pose(hands, dive.Column, progress);
        }

        /// <summary>
        /// Mueve el balón con el vuelo de la lógica hasta la línea, decide el resultado con el
        /// lance que haya en ese momento y deja que el balón termine su recorrido.
        /// </summary>
        private IEnumerator Fly(BallState kick, System.Func<float, KeeperDive> diveAt, KeeperProfile keeper, System.Action<ShotResult> done)
        {
            GoalCrossing crossing = BallFlight.FlyToGoalPlane(kick);
            Sound.Kick(Mathf.InverseLerp(ShotSolver.MinSpeed, ShotSolver.MaxSpeed, kick.Vel.Length()));
            float end = crossing.Crossed ? crossing.Time : Mathf.Min(crossing.Time, 3f);

            BallState s = kick;
            float simulated = 0f;
            float clock = 0f;
            KeeperDive dive = diveAt(0f);
            while (clock < end)
            {
                clock = Mathf.Min(end, clock + Time.deltaTime);
                while (simulated + BallFlight.FixedDt <= clock)
                {
                    s = BallFlight.Step(s, BallFlight.FixedDt);
                    simulated += BallFlight.FixedDt;
                }
                Ball.position = Conv.ToUnity(s.Pos);
                dive = diveAt(clock);
                PoseKeeper(dive, clock, keeper);
                Shooter.FollowThrough(clock);
                yield return null;
            }

            ShotResult result = ShotResolver.Resolve(kick, dive, keeper);
            done(result);

            // Después de la línea: red, rebote en el portero, palo o a la tribuna.
            Vector3 vel = Conv.ToUnity(crossing.Velocity);
            Vector3 pos = crossing.Crossed ? Conv.ToUnity(crossing.Point) : Ball.position;
            switch (result.Outcome)
            {
                case ShotOutcome.Goal: vel *= 0.22f; break;
                case ShotOutcome.Saved:
                    vel = new Vector3(vel.x * 0.35f + (float)(_rng.NextDouble() - 0.5) * 3f, 2.2f, -Mathf.Abs(vel.z) * 0.22f);
                    break;
                case ShotOutcome.Post:
                    vel = new Vector3(-vel.x * 0.3f, vel.y * 0.3f, -Mathf.Abs(vel.z) * 0.4f);
                    break;
            }
            float after = 0f;
            while (after < 1.2f)
            {
                float dt = Time.deltaTime;
                after += dt;
                clock += dt;
                vel.y -= BallFlight.Gravity * dt;
                pos += vel * dt;
                if (result.Outcome == ShotOutcome.Goal && pos.z > 1.7f) { pos.z = 1.7f; vel.x = 0f; vel.z = 0f; }
                if (pos.y < Field.BallRadius) { pos.y = Field.BallRadius; vel.y = -vel.y * 0.35f; vel.x *= 0.8f; vel.z *= 0.8f; }
                Ball.position = pos;
                PoseKeeper(dive, clock, keeper);
                Shooter.FollowThrough(clock);
                yield return null;
            }
        }

        private void Finish(ShotResult result, bool playerKicked)
        {
            _shootout.Record(result.Scored);
            _lastScored = result.Scored;
            if (result.Outcome == ShotOutcome.Goal) { Sound.Net(); Sound.Roar(); }
            else { if (result.Outcome == ShotOutcome.Post) Sound.Post(); Sound.Groan(); }
            Hud.Power = -1f;
            Hud.Hint = "";
            switch (result.Outcome)
            {
                case ShotOutcome.Goal: Hud.Banner = "¡GOL!"; break;
                case ShotOutcome.Saved: Hud.Banner = playerKicked ? "ATAJADO" : "¡ATAJADA!"; break;
                case ShotOutcome.Post: Hud.Banner = "¡AL PALO!"; break;
                default: Hud.Banner = "FUERA"; break;
            }
        }
    }
}
