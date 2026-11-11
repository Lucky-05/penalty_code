using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Penales.Game;

namespace Penales.EditorTools
{
    /// <summary>
    /// Deja el proyecto listo sin tocar el editor a mano: pipeline de alta fidelidad, espacio de
    /// color lineal, la escena provisional y la lista de escenas del juego. Se puede correr las
    /// veces que haga falta, desde el menú o por línea de comandos:
    ///   Unity -batchmode -quit -projectPath . -executeMethod Penales.EditorTools.ProjectSetup.Run
    /// </summary>
    public static class ProjectSetup
    {
        private const string SettingsDir = "Assets/Penales/Settings";
        private const string PipelinePath = SettingsDir + "/PenalesHDRP.asset";
        private const string ScenePath = "Assets/Penales/Scenes/Graybox.unity";
        /// <summary>Qué personaje de MetaHuman hace de tirador y cuál de portero.</summary>
        public const string ShooterCharacter = "Kelvin";
        public const string KeeperCharacter = "Victor";

        [MenuItem("Penales/Preparar proyecto")]
        public static void Run()
        {
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.companyName = "Penales";
            PlayerSettings.productName = "Penales";
            PlayerSettings.SetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.Standalone, "mx.penales.juego");

            EnsurePipeline();
            BuildGrayboxScene();
            AssetDatabase.SaveAssets();
            Debug.Log("[Penales] Proyecto preparado. Pipeline: " + (GraphicsSettings.defaultRenderPipeline != null
                ? GraphicsSettings.defaultRenderPipeline.name : "ninguno") + " · escena: " + ScenePath);
        }

        private static void EnsurePipeline()
        {
            if (!Directory.Exists(SettingsDir)) Directory.CreateDirectory(SettingsDir);
            RenderPipelineAsset asset = AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>(PipelinePath);
            if (asset == null)
            {
                // Se crea por nombre de tipo para que este archivo compile aunque el paquete de
                // alta fidelidad todavía no esté instalado.
                System.Type type = System.Type.GetType(
                    "UnityEngine.Rendering.HighDefinition.HDRenderPipelineAsset, Unity.RenderPipelines.HighDefinition.Runtime");
                if (type == null)
                {
                    Debug.LogError("[Penales] No está el paquete com.unity.render-pipelines.high-definition.");
                    return;
                }
                asset = (RenderPipelineAsset)ScriptableObject.CreateInstance(type);
                AssetDatabase.CreateAsset(asset, PipelinePath);
            }
            GraphicsSettings.defaultRenderPipeline = asset;
            for (int i = 0; i < QualitySettings.names.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                QualitySettings.renderPipeline = asset;
            }
        }

        /// <summary>Compila la aplicación de Mac en Builds/Penales.app.</summary>
        [MenuItem("Penales/Compilar para Mac")]
        public static void BuildMac()
        {
            Run();
            BuildPlayerOptions o = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = "Builds/Penales.app",
                target = BuildTarget.StandaloneOSX,
                options = BuildOptions.None
            };
            UnityEditor.Build.Reporting.BuildReport report = BuildPipeline.BuildPlayer(o);
            Debug.Log("[Penales] Compilación: " + report.summary.result + " · " + report.summary.totalErrors
                      + " errores · " + (report.summary.totalSize / (1024 * 1024)) + " MB · " + report.summary.outputPath);
            if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded && Application.isBatchMode)
                EditorApplication.Exit(1);
        }

        /// <summary>Importa la foto en 360° del estadio como cielo cúbico.</summary>
        private static Cubemap ImportStadium()
        {
            const string path = "Assets/Penales/Art/Stadium/stadium_01_4k.hdr";
            if (!File.Exists(path)) return null;
            TextureImporter ti = AssetImporter.GetAtPath(path) as TextureImporter;
            if (ti != null && ti.textureShape != TextureImporterShape.TextureCube)
            {
                ti.textureShape = TextureImporterShape.TextureCube;
                ti.generateCubemap = TextureImporterGenerateCubemap.Cylindrical;
                ti.sRGBTexture = false;
                ti.maxTextureSize = 4096;
                ti.mipmapEnabled = true;
                ti.textureCompression = TextureImporterCompression.CompressedHQ;
                ti.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Cubemap>(path);
        }

        [MenuItem("Penales/Armar escena en gris")]
        public static void BuildGrayboxScene()
        {
            string dir = Path.GetDirectoryName(ScenePath);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            GameObject root = new GameObject("Cancha provisional");
            GrayboxBootstrap boot = root.AddComponent<GrayboxBootstrap>();
            boot.ShooterPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(MetaHumanImport.Root + "/" + ShooterCharacter + "/" + ShooterCharacter + ".prefab");
            boot.KeeperPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(MetaHumanImport.Root + "/" + KeeperCharacter + "/" + KeeperCharacter + ".prefab");
            if (boot.KeeperPrefab == null) boot.KeeperPrefab = boot.ShooterPrefab;
            boot.StadiumSky = ImportStadium();
            boot.SkyRotation = 90f;
            Debug.Log("[Penales] Jugadores en la escena: tirador=" + (boot.ShooterPrefab != null) + " portero=" + (boot.KeeperPrefab != null));
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        }
    }
}
