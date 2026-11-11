using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Penales.EditorTools
{
    /// <summary>
    /// Recibe en Unity lo que se exportó del creador de MetaHuman: ajusta cómo se importan las
    /// texturas y los modelos, y deja un informe de qué trae cada malla (huesos, piezas,
    /// tamaño) para poder armar los materiales sin abrir el editor.
    ///   Unity -batchmode -quit -projectPath . -executeMethod Penales.EditorTools.MetaHumanImport.Inspect
    /// </summary>
    public static class MetaHumanImport
    {
        public const string Root = "Assets/Penales/Art/MetaHuman";

        [MenuItem("Penales/MetaHuman/Ajustar importación e informar")]
        public static void Inspect()
        {
            ConfigureTextures();
            ConfigureModels();
            AssetDatabase.Refresh();

            StringBuilder sb = new StringBuilder();
            foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { Root }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                GameObject go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                sb.AppendLine("== " + path);
                foreach (SkinnedMeshRenderer r in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    Mesh m = r.sharedMesh;
                    sb.AppendLine("  malla " + r.name + ": " + m.vertexCount + " vértices, " + m.subMeshCount
                                  + " piezas, " + r.bones.Length + " huesos, " + m.blendShapeCount + " gestos, tamaño "
                                  + m.bounds.size.ToString("F2") + " centro " + m.bounds.center.ToString("F2"));
                    List<string> mats = new List<string>();
                    foreach (Material mat in r.sharedMaterials) mats.Add(mat != null ? mat.name : "(vacío)");
                    sb.AppendLine("    materiales: " + string.Join(", ", mats));
                }
                Transform[] all = go.GetComponentsInChildren<Transform>(true);
                sb.AppendLine("  nodos: " + all.Length + " · raíz rot " + go.transform.localEulerAngles.ToString("F0")
                              + " escala " + go.transform.localScale.ToString("F2"));
                List<string> key = new List<string>();
                foreach (Transform t in all)
                {
                    string n = t.name.ToLowerInvariant();
                    if (n == "root" || n == "pelvis" || n.StartsWith("spine_0") || n == "head" || n == "neck_01"
                        || n == "upperarm_l" || n == "hand_l" || n == "thigh_l" || n == "foot_l" || n == "ball_l")
                        key.Add(t.name + "@" + t.position.ToString("F2"));
                }
                sb.AppendLine("  huesos clave: " + string.Join("  ", key));
            }
            string report = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "metahuman_informe.txt");
            File.WriteAllText(report, sb.ToString());
            Debug.Log("[Penales] Informe de MetaHuman en " + report + "\n" + sb);
        }

        private static void ConfigureTextures()
        {
            foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { Root }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                TextureImporter ti = AssetImporter.GetAtPath(path) as TextureImporter;
                if (ti == null) continue;
                string name = Path.GetFileNameWithoutExtension(path);
                bool normal = name.Contains("Normal");
                bool data = normal || name.EndsWith("SRMF");
                bool changed = false;
                TextureImporterType type = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
                if (ti.textureType != type) { ti.textureType = type; changed = true; }
                // Unreal guarda el relieve con el canal verde al revés que Unity.
                if (normal && !ti.flipGreenChannel) { ti.flipGreenChannel = true; changed = true; }
                if (ti.sRGBTexture == data) { ti.sRGBTexture = !data; changed = true; }
                if (ti.maxTextureSize != 2048) { ti.maxTextureSize = 2048; changed = true; }
                if (!ti.mipmapEnabled) { ti.mipmapEnabled = true; changed = true; }
                // La piel del cuerpo y su mapa de uniforme se combinan en tiempo de juego.
                bool readable = name == "Body_Basecolor" || name == "Body_KitMask";
                if (ti.isReadable != readable) { ti.isReadable = readable; changed = true; }
                if (readable && ti.textureCompression != TextureImporterCompression.Uncompressed) { ti.textureCompression = TextureImporterCompression.Uncompressed; changed = true; }
                if (changed) ti.SaveAndReimport();
            }
        }

        private static void ConfigureModels()
        {
            foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { Root }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                ModelImporter mi = AssetImporter.GetAtPath(path) as ModelImporter;
                if (mi == null) continue;
                bool changed = false;
                // Los materiales se importan solo para conocer el nombre de cada pieza; después se sustituyen.
                if (mi.materialImportMode != ModelImporterMaterialImportMode.ImportStandard) { mi.materialImportMode = ModelImporterMaterialImportMode.ImportStandard; changed = true; }
                if (mi.materialLocation != ModelImporterMaterialLocation.InPrefab) { mi.materialLocation = ModelImporterMaterialLocation.InPrefab; changed = true; }
                if (mi.importBlendShapes != true) { mi.importBlendShapes = true; changed = true; }
                if (mi.importNormals != ModelImporterNormals.Import) { mi.importNormals = ModelImporterNormals.Import; changed = true; }
                if (mi.animationType != ModelImporterAnimationType.Generic) { mi.animationType = ModelImporterAnimationType.Generic; changed = true; }
                if (mi.importAnimation) { mi.importAnimation = false; changed = true; }
                // Se necesita leer la malla para pintar el uniforme por zonas del cuerpo.
                if (!mi.isReadable) { mi.isReadable = true; changed = true; }
                if (changed) mi.SaveAndReimport();
            }
        }
    }
}
