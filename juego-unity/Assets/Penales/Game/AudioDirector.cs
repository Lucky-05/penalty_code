using UnityEngine;

namespace Penales.Game
{
    /// <summary>
    /// El sonido del estadio. No hay grabaciones: cada sonido se sintetiza al arrancar —el
    /// murmullo de la tribuna, el silbato, el golpe al balón, la red, el grito de gol y el
    /// lamento—, así que el juego no depende de ningún archivo de audio.
    /// </summary>
    public class AudioDirector : MonoBehaviour
    {
        private const int Rate = 44100;
        private AudioSource _crowd, _fx, _swell;
        private AudioClip _whistle, _kick, _net, _roar, _groan, _post;
        private float _crowdTarget = 0.32f;

        private void Awake()
        {
            _crowd = Source(true);
            _fx = Source(false);
            _swell = Source(false);

            _crowd.clip = Crowd(8f, 41);
            _crowd.volume = 0f;
            _crowd.Play();

            _whistle = Whistle(0.55f);
            _kick = Kick();
            _net = Noise(0.45f, 5200f, 9f, 77);
            _post = PostClip();
            _roar = Roar(3.2f, true, 5);
            _groan = Roar(2.2f, false, 9);
        }

        private AudioSource Source(bool loop)
        {
            AudioSource s = gameObject.AddComponent<AudioSource>();
            s.loop = loop;
            s.playOnAwake = false;
            s.spatialBlend = 0f;
            return s;
        }

        private void Update()
        {
            _crowd.volume = Mathf.MoveTowards(_crowd.volume, _crowdTarget, Time.deltaTime * 0.35f);
        }

        /// <summary>La tribuna contiene el aliento antes del tiro.</summary>
        public void Hush() { _crowdTarget = 0.16f; }
        public void Murmur() { _crowdTarget = 0.32f; }

        public void Whistle() { _fx.PlayOneShot(_whistle, 0.5f); }
        public void Kick(float power) { _fx.PlayOneShot(_kick, Mathf.Lerp(0.55f, 1f, power)); }
        public void Net() { _fx.PlayOneShot(_net, 0.5f); }
        public void Post() { _fx.PlayOneShot(_post, 0.8f); }
        public void Roar() { _crowdTarget = 0.5f; _swell.PlayOneShot(_roar, 0.9f); }
        public void Groan() { _crowdTarget = 0.26f; _swell.PlayOneShot(_groan, 0.7f); }

        // ── Síntesis ─────────────────────────────────────────────────────────────────────────

        private static AudioClip Make(string name, float[] data)
        {
            AudioClip c = AudioClip.Create(name, data.Length, 1, Rate, false);
            c.SetData(data, 0);
            return c;
        }

        /// <summary>Ruido filtrado: la base de casi todo (tribuna, red, roce).</summary>
        private static float[] Filtered(int n, float cutoff, System.Random rng)
        {
            float[] d = new float[n];
            float a = Mathf.Clamp01(2f * Mathf.PI * cutoff / Rate);
            float y = 0f;
            for (int i = 0; i < n; i++)
            {
                float x = (float)rng.NextDouble() * 2f - 1f;
                y += a * (x - y);
                d[i] = y;
            }
            return d;
        }

        /// <summary>Murmullo de miles de personas: ruido grave que sube y baja despacio, y se repite sin costura.</summary>
        private static AudioClip Crowd(float seconds, int seed)
        {
            int n = (int)(seconds * Rate);
            System.Random rng = new System.Random(seed);
            float[] low = Filtered(n, 420f, rng);
            float[] mid = Filtered(n, 1500f, rng);
            float[] d = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)n;
                // Ondas lentas con un número entero de ciclos, para que el final empate con el inicio.
                float swell = 0.72f + 0.16f * Mathf.Sin(2f * Mathf.PI * 2f * t) + 0.12f * Mathf.Sin(2f * Mathf.PI * 5f * t + 1.3f);
                d[i] = (low[i] * 2.6f + mid[i] * 0.9f) * swell;
            }
            int fade = Rate / 4;                                // cruce entre el final y el inicio
            for (int i = 0; i < fade; i++)
            {
                float k = i / (float)fade;
                d[i] = d[i] * k + d[n - fade + i] * (1f - k);
            }
            float[] looped = new float[n - fade];
            System.Array.Copy(d, looped, looped.Length);
            return Make("Tribuna", Normalize(looped, 0.6f));
        }

        /// <summary>Silbato de árbitro: dos tonos muy cercanos que vibran entre sí.</summary>
        private static AudioClip Whistle(float seconds)
        {
            int n = (int)(seconds * Rate);
            float[] d = new float[n];
            System.Random rng = new System.Random(3);
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float env = Mathf.Clamp01(t / 0.012f) * Mathf.Clamp01((seconds - t) / 0.05f);
                float trill = 1f + 0.35f * Mathf.Sin(2f * Mathf.PI * 34f * t);
                float tone = Mathf.Sin(2f * Mathf.PI * 2950f * t) + 0.6f * Mathf.Sin(2f * Mathf.PI * 3120f * t);
                d[i] = (tone * 0.5f * trill + ((float)rng.NextDouble() - 0.5f) * 0.15f) * env;
            }
            return Make("Silbato", Normalize(d, 0.7f));
        }

        /// <summary>El golpe al balón: un bombo corto que cae de tono, más el chasquido del cuero.</summary>
        private static AudioClip Kick()
        {
            int n = (int)(0.22f * Rate);
            float[] d = new float[n];
            System.Random rng = new System.Random(8);
            float phase = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float freq = Mathf.Lerp(190f, 62f, Mathf.Clamp01(t / 0.07f));
                phase += 2f * Mathf.PI * freq / Rate;
                float thump = Mathf.Sin(phase) * Mathf.Exp(-t * 26f);
                float slap = ((float)rng.NextDouble() * 2f - 1f) * Mathf.Exp(-t * 130f) * 0.7f;
                d[i] = thump + slap;
            }
            return Make("Golpeo", Normalize(d, 0.95f));
        }

        private static AudioClip PostClip()
        {
            int n = (int)(0.5f * Rate);
            float[] d = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                d[i] = (Mathf.Sin(2f * Mathf.PI * 410f * t) + 0.5f * Mathf.Sin(2f * Mathf.PI * 1130f * t)
                        + 0.3f * Mathf.Sin(2f * Mathf.PI * 2260f * t)) * Mathf.Exp(-t * 11f);
            }
            return Make("Poste", Normalize(d, 0.9f));
        }

        private static AudioClip Noise(float seconds, float cutoff, float decay, int seed)
        {
            int n = (int)(seconds * Rate);
            float[] d = Filtered(n, cutoff, new System.Random(seed));
            for (int i = 0; i < n; i++) d[i] *= Mathf.Exp(-(i / (float)Rate) * decay);
            return Make("Roce", Normalize(d, 0.8f));
        }

        /// <summary>
        /// La reacción de la tribuna. El grito de gol sube rápido, se abre a agudos y se
        /// sostiene; el lamento es más corto, más grave y cae.
        /// </summary>
        private static AudioClip Roar(float seconds, bool joy, int seed)
        {
            int n = (int)(seconds * Rate);
            System.Random rng = new System.Random(seed);
            float[] low = Filtered(n, joy ? 700f : 380f, rng);
            float[] high = Filtered(n, joy ? 3200f : 900f, rng);
            float[] d = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float attack = Mathf.Clamp01(t / (joy ? 0.18f : 0.35f));
                float release = Mathf.Clamp01((seconds - t) / (seconds * (joy ? 0.55f : 0.7f)));
                float body = joy ? 1f + 0.12f * Mathf.Sin(2f * Mathf.PI * 3.1f * t) : Mathf.Exp(-t * 0.9f);
                d[i] = (low[i] * 2.2f + high[i] * (joy ? 1.6f : 0.6f)) * attack * release * body;
            }
            return Make(joy ? "Gol" : "Lamento", Normalize(d, 0.9f));
        }

        private static float[] Normalize(float[] d, float peak)
        {
            float max = 1e-6f;
            for (int i = 0; i < d.Length; i++) max = Mathf.Max(max, Mathf.Abs(d[i]));
            float k = peak / max;
            for (int i = 0; i < d.Length; i++) d[i] *= k;
            return d;
        }
    }
}
