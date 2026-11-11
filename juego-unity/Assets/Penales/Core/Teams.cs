using System;
using System.Collections.Generic;

namespace Penales.Core
{
    public enum Difficulty
    {
        Easy,
        Normal,
        Hard
    }

    /// <summary>Cómo patea un jugador: sus manías y su temple.</summary>
    public class ShooterTendencies
    {
        /// <summary>-1 siempre a la izquierda, +1 siempre a la derecha, 0 parejo.</summary>
        public float SideBias;
        /// <summary>0 raso, 1 arriba.</summary>
        public float HeightBias = 0.4f;
        /// <summary>0 colocado, 1 fuerte.</summary>
        public float Power = 0.6f;
        /// <summary>0 a 1: sangre fría.</summary>
        public float Composure = 0.5f;
        /// <summary>-1 a 1: efecto que le suele dar.</summary>
        public float Curve;
    }

    public class Player
    {
        public string Name;
        public int Number;
        public ShooterTendencies Tendencies = new ShooterTendencies();
    }

    public class Team
    {
        public string Name;
        public string ShortName;
        /// <summary>Colores del uniforme en hexadecimal, sin #.</summary>
        public string PrimaryColor;
        public string SecondaryColor;
        public string KeeperName;
        public KeeperProfile Keeper = KeeperProfile.Default();
        public List<Player> Players = new List<Player>();

        /// <summary>El tirador del penal número <paramref name="kickIndex"/> (rota si se acaban).</summary>
        public Player Kicker(int kickIndex)
        {
            return Players[((kickIndex % Players.Count) + Players.Count) % Players.Count];
        }
    }

    /// <summary>
    /// Los equipos de fábrica. Todo es inventado: ni clubes, ni escudos, ni jugadores reales.
    /// </summary>
    public static class Teams
    {
        private static readonly string[] FirstNames =
        {
            "Mateo", "Santiago", "Emiliano", "Leonardo", "Diego", "Sebastián", "Rodrigo", "Iker",
            "Andrés", "Bruno", "Tomás", "Nicolás", "Fabián", "Marco", "Joaquín", "Renato",
            "Gael", "Damián", "Octavio", "Julián"
        };

        private static readonly string[] LastNames =
        {
            "Arriaga", "Belmonte", "Cisneros", "Duarte", "Escobedo", "Fierro", "Garza", "Huerta",
            "Ibarra", "Juárez", "Lozano", "Montiel", "Nava", "Olvera", "Paredes", "Quintana",
            "Robles", "Solís", "Treviño", "Villaseñor"
        };

        private static readonly string[][] Definitions =
        {
            new[] { "Atlético Norte", "ATN", "C8102E", "FFFFFF" },
            new[] { "Real Costa", "RCO", "0B3D91", "F2C14E" },
            new[] { "Deportivo Sierra", "DSI", "1E7A46", "FFFFFF" },
            new[] { "Unión Central", "UCE", "111111", "E0B341" },
            new[] { "Racing del Valle", "RDV", "5BC0EB", "0D1B2A" },
            new[] { "Sporting Laguna", "SPL", "F26419", "1B1B1E" },
            new[] { "Club Marina", "CMA", "FFFFFF", "0A2463" },
            new[] { "Fénix FC", "FNX", "6A0DAD", "F5D547" }
        };

        public const int PlayersPerTeam = 7;

        /// <summary>Los ocho equipos. La misma semilla da siempre las mismas plantillas.</summary>
        public static List<Team> CreateDefault(int seed = 2026)
        {
            Random rng = new Random(seed);
            List<Team> teams = new List<Team>();
            foreach (string[] d in Definitions)
            {
                Team t = new Team
                {
                    Name = d[0],
                    ShortName = d[1],
                    PrimaryColor = d[2],
                    SecondaryColor = d[3],
                    KeeperName = RandomName(rng)
                };
                t.Keeper.Speed = 5.0f + (float)rng.NextDouble() * 0.5f;
                t.Keeper.CatchRadius = 0.47f + (float)rng.NextDouble() * 0.07f;

                int[] numbers = { 9, 10, 7, 11, 8, 4, 5 };
                for (int i = 0; i < PlayersPerTeam; i++)
                {
                    t.Players.Add(new Player
                    {
                        Name = RandomName(rng),
                        Number = numbers[i],
                        Tendencies = new ShooterTendencies
                        {
                            SideBias = (float)(rng.NextDouble() * 2.0 - 1.0) * 0.7f,
                            HeightBias = 0.15f + (float)rng.NextDouble() * 0.6f,
                            Power = 0.4f + (float)rng.NextDouble() * 0.5f,
                            Composure = 0.3f + (float)rng.NextDouble() * 0.6f,
                            Curve = (float)(rng.NextDouble() * 2.0 - 1.0) * 0.5f
                        }
                    });
                }
                teams.Add(t);
            }
            return teams;
        }

        private static string RandomName(Random rng)
        {
            return FirstNames[rng.Next(FirstNames.Length)] + " " + LastNames[rng.Next(LastNames.Length)];
        }
    }
}
