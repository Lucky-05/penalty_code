using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using Penales.Core;

namespace Penales.Game
{
    /// <summary>
    /// Arma la cancha provisional completa al arrancar: pasto, área, arco, balón, tirador,
    /// portero, luz, cámara y marcador. Todo con figuras simples, para afinar cómo se siente
    /// jugar antes de invertir en arte. Basta con una escena vacía que tenga este componente.
    /// </summary>
    public class GrayboxBootstrap : MonoBehaviour
    {
        public Difficulty Difficulty = Difficulty.Normal;
        /// <summary>0 = distinto cada vez.</summary>
        public int Seed;
        /// <summary>Los jugadores de verdad. Si faltan, se juega con figuras provisionales.</summary>
        public GameObject ShooterPrefab;
        public GameObject KeeperPrefab;
        /// <summary>La foto en 360° del estadio que hace de cielo y de tribunas. Sin ella, cielo liso.</summary>
        public Cubemap StadiumSky;
        /// <summary>Giro del estadio, en grados, para dejar una tribuna detrás del arco.</summary>
        public float SkyRotation = 0f;

        private const float SunLux = 30000f;
        private const float FixedExposure = 12.6f;
        /// <summary>Cuánto hay que subir la foto del estadio para que empate con la luz del sol de la escena.</summary>
        private const float StadiumExposure = 12.4f;

        private void Awake()
        {
            Application.targetFrameRate = 120;
            Transform root = transform;

            BuildPitch(root, StadiumSky == null);
            BuildGoal(root);
            BuildLight(root, StadiumSky, SkyRotation);
            GrassField.Build(root);
            Camera cam = BuildCamera(root);

            GameObject ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            ball.name = "Balón";
            Destroy(ball.GetComponent<Collider>());
            ball.transform.SetParent(root, false);
            ball.transform.localScale = Vector3.one * (Field.BallRadius * 2f);
            ball.transform.position = Conv.ToUnity(Field.PenaltySpot);
            ball.GetComponent<Renderer>().sharedMaterial = Paint.Lit(Color.white, 0.5f);

            ShooterView shooter = ShooterPrefab != null
                ? (ShooterView)HumanShooterView.Create(ShooterPrefab, root)
                : CapsuleShooterView.Create(root);
            KeeperView keeper = KeeperPrefab != null
                ? HumanKeeperView.Create(KeeperPrefab, root)
                : KeeperView.Create(root);
            ReticleView reticle = ReticleView.Create(root);

            GameObject logic = new GameObject("Partido");
            logic.transform.SetParent(root, false);
            InputRouter controls = logic.AddComponent<InputRouter>();
            Hud hud = logic.AddComponent<Hud>();
            MenuScreen menu = logic.AddComponent<MenuScreen>();
            AudioDirector sound = logic.AddComponent<AudioDirector>();
            MatchDirector director = logic.AddComponent<MatchDirector>();
            director.Difficulty = Difficulty;
            director.Seed = Seed;
            director.Controls = controls;
            director.Emg = logic.AddComponent<EmgLink>();
            director.EmgSetup = logic.AddComponent<EmgSetupScreen>();
            director.EmgSetup.Emg = director.Emg;
            director.Ball = ball.transform;
            director.Shooter = shooter;
            director.Keeper = keeper;
            director.Reticle = reticle;
            director.Hud = hud;
            director.Menu = menu;
            director.Sound = sound;
            director.View = cam;
        }

        private static GameObject Block(string name, Transform parent, Vector3 pos, Vector3 scale, Material mat, PrimitiveType type = PrimitiveType.Cube)
        {
            GameObject g = GameObject.CreatePrimitive(type);
            g.name = name;
            Destroy(g.GetComponent<Collider>());
            g.transform.SetParent(parent, false);
            g.transform.position = pos;
            g.transform.localScale = scale;
            g.GetComponent<Renderer>().sharedMaterial = mat;
            return g;
        }

        private static void BuildPitch(Transform root, bool backWall)
        {
            // La tierra bajo las hojas, en los mismos tonos de las franjas del corte.
            Material grassA = Paint.Lit(new Color(0.070f, 0.250f, 0.055f), 0.05f);
            Material grassB = Paint.Lit(new Color(0.115f, 0.350f, 0.080f), 0.05f);
            Material line = Paint.Lit(new Color(0.93f, 0.93f, 0.90f), 0.05f);

            // Franjas de pasto, como las del corte de una cancha.
            for (int i = -12; i < 9; i++)
                Block("Suelo", root, new Vector3(0f, -0.05f, i * GrassField.StripeWidth + GrassField.StripeWidth * 0.5f), new Vector3(140f, 0.1f, GrassField.StripeWidth), (i & 1) == 0 ? grassA : grassB);

            const float w = 0.12f, y = 0.004f;
            Block("Línea de gol", root, new Vector3(0f, y, 0f), new Vector3(70f, 0.01f, w), line);
            // Área chica (5.5 m) y área grande (16.5 m)
            Block("Área chica", root, new Vector3(0f, y, -5.5f), new Vector3(18.32f, 0.01f, w), line);
            Block("Área chica izq", root, new Vector3(-9.16f, y, -2.75f), new Vector3(w, 0.01f, 5.5f), line);
            Block("Área chica der", root, new Vector3(9.16f, y, -2.75f), new Vector3(w, 0.01f, 5.5f), line);
            Block("Área grande", root, new Vector3(0f, y, -16.5f), new Vector3(40.32f, 0.01f, w), line);
            Block("Área grande izq", root, new Vector3(-20.16f, y, -8.25f), new Vector3(w, 0.01f, 16.5f), line);
            Block("Área grande der", root, new Vector3(20.16f, y, -8.25f), new Vector3(w, 0.01f, 16.5f), line);
            Block("Punto penal", root, new Vector3(0f, y, -Field.PenaltyDistance), new Vector3(0.24f, 0.01f, 0.24f), line, PrimitiveType.Cylinder);

            // Un muro de fondo para que la portería no flote en el vacío.
            if (backWall) Block("Fondo", root, new Vector3(0f, 4f, 9f), new Vector3(80f, 8f, 0.5f), Paint.Lit(new Color(0.10f, 0.12f, 0.17f), 0.1f));
        }

        private static void BuildGoal(Transform root)
        {
            Material white = Paint.Lit(Color.white, 0.6f);
            Material net = Paint.Lit(new Color(0.82f, 0.84f, 0.88f), 0.1f);
            float r = Field.PostRadius * 2f;
            float half = Field.HalfGoal + Field.PostRadius;
            float h = Field.GoalHeight + Field.PostRadius;
            const float depth = 2f;

            Block("Poste izq", root, new Vector3(-half, h / 2f, 0f), new Vector3(r, h / 2f, r), white, PrimitiveType.Cylinder);
            Block("Poste der", root, new Vector3(half, h / 2f, 0f), new Vector3(r, h / 2f, r), white, PrimitiveType.Cylinder);
            GameObject bar = Block("Travesaño", root, new Vector3(0f, h, 0f), new Vector3(r, half, r), white, PrimitiveType.Cylinder);
            bar.transform.rotation = Quaternion.Euler(0f, 0f, 90f);

            // La red, hecha de hilos: se ve a través y no necesita materiales transparentes.
            const float t = 0.015f;
            for (float x = -Field.HalfGoal; x <= Field.HalfGoal + 0.01f; x += 0.305f)
            {
                Block("Red", root, new Vector3(x, Field.GoalHeight / 2f, depth), new Vector3(t, Field.GoalHeight, t), net);
                Block("Red", root, new Vector3(x, Field.GoalHeight, depth / 2f), new Vector3(t, t, depth), net);
            }
            for (float yy = 0.15f; yy <= Field.GoalHeight + 0.01f; yy += 0.305f)
            {
                Block("Red", root, new Vector3(0f, yy, depth), new Vector3(Field.GoalWidth, t, t), net);
                Block("Red", root, new Vector3(-Field.HalfGoal, yy, depth / 2f), new Vector3(t, t, depth), net);
                Block("Red", root, new Vector3(Field.HalfGoal, yy, depth / 2f), new Vector3(t, t, depth), net);
            }
            for (float z = 0.3f; z <= depth; z += 0.305f)
            {
                Block("Red", root, new Vector3(0f, Field.GoalHeight, z), new Vector3(Field.GoalWidth, t, t), net);
                Block("Red", root, new Vector3(-Field.HalfGoal, Field.GoalHeight / 2f, z), new Vector3(t, Field.GoalHeight, t), net);
                Block("Red", root, new Vector3(Field.HalfGoal, Field.GoalHeight / 2f, z), new Vector3(t, Field.GoalHeight, t), net);
            }
        }

        /// <summary>Sol, exposición fija y cielo. Lo reutilizan las escenas de prueba.</summary>
        public static void BuildLight(Transform root, Cubemap stadium = null, float skyRotation = 0f)
        {
            GameObject sun = new GameObject("Luz");
            sun.transform.SetParent(root, false);
            sun.transform.rotation = Quaternion.Euler(48f, 28f, 0f);
            Light l = sun.AddComponent<Light>();
            l.type = LightType.Directional;
            l.color = new Color(1f, 0.97f, 0.92f);
            l.shadows = LightShadows.Soft;
            l.lightUnit = LightUnit.Lux;
            l.intensity = SunLux;

            // Exposición fija y un cielo sencillo: sin esto la imagen cambia de brillo sola
            // según lo que mire la cámara, y el fondo queda sin color.
            GameObject vol = new GameObject("Ambiente");
            vol.transform.SetParent(root, false);
            Volume volume = vol.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 10f;
            VolumeProfile profile = ScriptableObject.CreateInstance<VolumeProfile>();
            volume.sharedProfile = profile;

            Exposure exposure = profile.Add<Exposure>(true);
            exposure.mode.value = ExposureMode.Fixed;
            exposure.fixedExposure.value = FixedExposure;

            VisualEnvironment env = profile.Add<VisualEnvironment>(true);
            env.skyType.value = (int)(stadium != null ? SkyType.HDRI : SkyType.Gradient);
            env.skyAmbientMode.value = SkyAmbientMode.Dynamic;

            if (stadium != null)
            {
                HDRISky photo = profile.Add<HDRISky>(true);
                photo.hdriSky.value = stadium;
                photo.rotation.value = skyRotation;
                photo.skyIntensityMode.value = SkyIntensityMode.Exposure;
                photo.exposure.value = StadiumExposure;
                return;
            }
            GradientSky sky = profile.Add<GradientSky>(true);
            sky.top.value = new Color(0.10f, 0.22f, 0.48f);
            sky.middle.value = new Color(0.42f, 0.60f, 0.86f);
            sky.bottom.value = new Color(0.62f, 0.72f, 0.80f);
            sky.gradientDiffusion.value = 2.2f;
            sky.skyIntensityMode.value = SkyIntensityMode.Lux;
            sky.desiredLuxValue.value = SunLux * 0.25f;
        }

        private static Camera BuildCamera(Transform root)
        {
            GameObject go = new GameObject("Cámara");
            go.tag = "MainCamera";
            go.transform.SetParent(root, false);
            // Detrás y un poco arriba del tirador, con todo el arco a la vista.
            go.transform.position = new Vector3(0f, 3.3f, -21.5f);
            go.transform.LookAt(new Vector3(0f, 0.9f, 0f));
            Camera cam = go.AddComponent<Camera>();
            cam.fieldOfView = 29f;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 300f;
            // Suavizado temporal: sin él, las hojas del pasto parpadean al moverse la imagen.
            HDAdditionalCameraData extra = go.AddComponent<HDAdditionalCameraData>();
            extra.antialiasing = HDAdditionalCameraData.AntialiasingMode.TemporalAntialiasing;
            go.AddComponent<AudioListener>();
            return cam;
        }
    }
}
