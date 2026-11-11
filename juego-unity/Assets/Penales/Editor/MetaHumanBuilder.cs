using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;
using Penales.Game;

namespace Penales.EditorTools
{
    /// <summary>
    /// Arma un jugador a partir de lo exportado de MetaHuman: crea los materiales de piel, ojos,
    /// dientes y pestañas para el pipeline de alta fidelidad, y une la cabeza y el cuerpo —que
    /// llegan como dos modelos— sobre un solo esqueleto, listo para animar.
    ///   Unity -batchmode -quit -projectPath . -executeMethod Penales.EditorTools.MetaHumanBuilder.BuildAll
    /// </summary>
    public static class MetaHumanBuilder
    {
        // El orden de las piezas de la cabeza, tal como salen del creador de MetaHuman.
        private enum HeadSlot { Skin = 0, Teeth = 1, Saliva = 2, EyeLeft = 3, EyeRight = 4, EyeShell = 5, Eyelashes = 6, EyeEdge = 7, Cartilage = 8 }

        [MenuItem("Penales/MetaHuman/Armar jugadores")]
        public static void BuildAll()
        {
            MetaHumanImport.Inspect();
            foreach (string dir in Directory.GetDirectories(MetaHumanImport.Root))
            {
                string name = Path.GetFileName(dir);
                if (File.Exists(dir + "/" + name + "_Body.fbx")) Build(name);
            }
            AssetDatabase.SaveAssets();
        }

        public static void Build(string name)
        {
            string dir = MetaHumanImport.Root + "/" + name;
            string maps = dir + "/Maps";
            string matDir = dir + "/Materials";
            if (!Directory.Exists(matDir)) Directory.CreateDirectory(matDir);

            Texture2D headMask = BuildMask(maps + "/Head_SRMF.png", maps + "/Head_Mask.png");
            Texture2D bodyMask = BuildMask(maps + "/Body_SRMF.png", maps + "/Body_Mask.png");
            DiffusionProfileSettings skinProfile = FindSkinProfile();

            Material headSkin = Skin(matDir + "/Piel_Cabeza.mat", Tex(maps + "/Head_Basecolor.png"), Tex(maps + "/Head_Normal.png"), headMask, skinProfile);
            Material bodySkin = Skin(matDir + "/Piel_Cuerpo.mat", Tex(maps + "/Body_Basecolor.png"), Tex(maps + "/Body_Normal.png"), bodyMask, skinProfile);
            Material eyes = Lit(matDir + "/Ojos.mat", Tex(maps + "/Eyes_Color.png"), Tex(maps + "/Eyes_Normal.png"), 0.92f);
            Material teeth = Lit(matDir + "/Dientes.mat", Tex(maps + "/Teeth_Color.png"), Tex(maps + "/Teeth_Normal.png"), 0.7f);
            Material lashes = Cutout(matDir + "/Pestanas.mat", Tex(maps + "/Eyelashes_Color.png"), new Color(0.06f, 0.04f, 0.03f, 1f), 0.25f);
            Material hidden = Cutout(matDir + "/Oculto.mat", null, new Color(0f, 0f, 0f, 0f), 0.5f);

            GameObject bodySrc = AssetDatabase.LoadAssetAtPath<GameObject>(dir + "/" + name + "_Body.fbx");
            GameObject headSrc = AssetDatabase.LoadAssetAtPath<GameObject>(dir + "/" + name + "_Head.fbx");
            GameObject root = Object.Instantiate(bodySrc);
            root.name = name;
            GameObject head = Object.Instantiate(headSrc);

            SkinnedMeshRenderer bodyR = root.GetComponentInChildren<SkinnedMeshRenderer>(true);
            SkinnedMeshRenderer headR = head.GetComponentInChildren<SkinnedMeshRenderer>(true);
            bodyR.sharedMaterials = new[] { bodySkin };

            Material[] hm = new Material[headR.sharedMesh.subMeshCount];
            for (int i = 0; i < hm.Length; i++) hm[i] = hidden;
            Assign(hm, HeadSlot.Skin, headSkin);
            Assign(hm, HeadSlot.Teeth, teeth);
            Assign(hm, HeadSlot.EyeLeft, eyes);
            Assign(hm, HeadSlot.EyeRight, eyes);
            Assign(hm, HeadSlot.Eyelashes, lashes);
            headR.sharedMaterials = hm;

            int moved = Rebind(headR, bodyR, root.transform);
            headR.transform.SetParent(root.transform, true);
            headR.name = name + "_Cabeza";
            bodyR.name = name + "_Cuerpo";
            Object.DestroyImmediate(head);

            // El uniforme: un mapa de zonas calculado sobre el cuerpo en reposo.
            Texture2D kitMask = BuildKitMask(bodyR.sharedMesh, Tex(maps + "/Body_Basecolor.png"), maps + "/Body_KitMask.png");
            HumanKitData kit = root.AddComponent<HumanKitData>();
            kit.Body = bodyR;
            kit.BaseColor = Tex(maps + "/Body_Basecolor.png");
            kit.Mask = kitMask;

            // La ropa de verdad: una malla aparte, con volumen, que se mueve con el mismo esqueleto.
            Mesh garment = BuildGarment(bodyR.sharedMesh, dir + "/" + name + "_Uniforme.asset");
            GameObject garmentGo = new GameObject(name + "_Uniforme");
            garmentGo.transform.SetParent(bodyR.transform.parent, false);
            garmentGo.transform.localPosition = bodyR.transform.localPosition;
            garmentGo.transform.localRotation = bodyR.transform.localRotation;
            garmentGo.transform.localScale = bodyR.transform.localScale;
            SkinnedMeshRenderer garmentR = garmentGo.AddComponent<SkinnedMeshRenderer>();
            garmentR.sharedMesh = garment;
            garmentR.bones = bodyR.bones;
            garmentR.rootBone = bodyR.rootBone;
            garmentR.sharedMaterial = Cloth(matDir + "/Tela.mat");
            garmentR.updateWhenOffscreen = true;
            kit.Garment = garmentR;

            // Los hombros: misma tela, pero colgada del esqueleto de la cabeza.
            float bodyScale = (bodyR.sharedMesh.bounds.size.z > bodyR.sharedMesh.bounds.size.y
                ? bodyR.sharedMesh.bounds.max.z : bodyR.sharedMesh.bounds.max.y) / 1.43f;
            Mesh yoke = BuildGarment(headR.sharedMesh, dir + "/" + name + "_Hombros.asset", true, bodyScale);
            if (yoke.vertexCount > 0)
            {
                GameObject yokeGo = new GameObject(name + "_Hombros");
                yokeGo.transform.SetParent(headR.transform.parent, false);
                yokeGo.transform.localPosition = headR.transform.localPosition;
                yokeGo.transform.localRotation = headR.transform.localRotation;
                yokeGo.transform.localScale = headR.transform.localScale;
                SkinnedMeshRenderer yokeR = yokeGo.AddComponent<SkinnedMeshRenderer>();
                yokeR.sharedMesh = yoke;
                yokeR.bones = headR.bones;
                yokeR.rootBone = headR.rootBone;
                yokeR.sharedMaterial = garmentR.sharedMaterial;
                yokeR.updateWhenOffscreen = true;
                kit.Yoke = yokeR;
            }

            foreach (SkinnedMeshRenderer r in new[] { bodyR, headR })
            {
                r.updateWhenOffscreen = true;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            }

            string prefab = dir + "/" + name + ".prefab";
            PrefabUtility.SaveAsPrefabAsset(root, prefab);
            Object.DestroyImmediate(root);
            Debug.Log("[Penales] Jugador armado: " + prefab + " · huesos de la cara reubicados: " + moved
                      + " · perfil de piel: " + (skinProfile != null ? skinProfile.name : "ninguno"));
        }

        private static void Assign(Material[] slots, HeadSlot slot, Material m)
        {
            if ((int)slot < slots.Length) slots[(int)slot] = m;
        }

        /// <summary>
        /// La cabeza trae una copia del esqueleto del cuerpo más los huesos de la cara. Se
        /// apunta a los huesos del cuerpo donde coinciden los nombres y se cuelgan los de la
        /// cara del esqueleto del cuerpo, para que todo se mueva con una sola animación.
        /// </summary>
        private static int Rebind(SkinnedMeshRenderer headR, SkinnedMeshRenderer bodyR, Transform bodyRoot)
        {
            Dictionary<string, Transform> body = new Dictionary<string, Transform>();
            foreach (Transform t in bodyRoot.GetComponentsInChildren<Transform>(true))
                if (!body.ContainsKey(t.name)) body[t.name] = t;

            Transform[] bones = headR.bones;
            // Primero se reubican los huesos propios de la cara cuyo padre sí existe en el cuerpo.
            int moved = 0;
            foreach (Transform b in bones)
            {
                if (b == null || body.ContainsKey(b.name)) continue;
                Transform parent = b.parent;
                Transform target;
                if (parent != null && body.TryGetValue(parent.name, out target) && target != parent)
                {
                    b.SetParent(target, true);
                    moved++;
                }
            }
            for (int i = 0; i < bones.Length; i++)
            {
                Transform target;
                if (bones[i] != null && body.TryGetValue(bones[i].name, out target)) bones[i] = target;
            }
            headR.bones = bones;
            Transform rootBone;
            if (headR.rootBone != null && body.TryGetValue(headR.rootBone.name, out rootBone)) headR.rootBone = rootBone;
            else headR.rootBone = bodyR.rootBone;
            return moved;
        }

        private static Texture2D Tex(string path)
        {
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        private static DiffusionProfileSettings FindSkinProfile()
        {
            DiffusionProfileSettings any = null;
            foreach (string guid in AssetDatabase.FindAssets("t:DiffusionProfileSettings"))
            {
                DiffusionProfileSettings p = AssetDatabase.LoadAssetAtPath<DiffusionProfileSettings>(AssetDatabase.GUIDToAssetPath(guid));
                if (p == null) continue;
                if (p.name.ToLowerInvariant().Contains("skin")) return p;
                if (any == null) any = p;
            }
            return any;
        }

        /// <summary>
        /// MetaHuman empaqueta brillo y aspereza en una textura (verde = aspereza). El pipeline
        /// de Unity espera otra distribución: alfa = lisura, verde = oclusión.
        /// </summary>
        private static Texture2D BuildMask(string srmfPath, string outPath)
        {
            if (!File.Exists(srmfPath)) return null;
            if (!File.Exists(outPath))
            {
                Texture2D src = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
                src.LoadImage(File.ReadAllBytes(srmfPath));
                Color32[] px = src.GetPixels32();
                for (int i = 0; i < px.Length; i++)
                {
                    byte rough = px[i].g;
                    px[i] = new Color32(0, 255, 0, (byte)(255 - rough));
                }
                Texture2D dst = new Texture2D(src.width, src.height, TextureFormat.RGBA32, false, true);
                dst.SetPixels32(px);
                dst.Apply();
                File.WriteAllBytes(outPath, dst.EncodeToPNG());
                Object.DestroyImmediate(src);
                Object.DestroyImmediate(dst);
                AssetDatabase.ImportAsset(outPath);
            }
            TextureImporter ti = (TextureImporter)AssetImporter.GetAtPath(outPath);
            if (ti.sRGBTexture) { ti.sRGBTexture = false; ti.SaveAndReimport(); }
            return Tex(outPath);
        }

        /// <summary>
        /// Pinta, en el espacio de la textura del cuerpo, qué parte es camiseta (rojo), short
        /// (verde), medias (azul) y botines (alfa). Cada punto de la textura se clasifica por
        /// el lugar del cuerpo al que corresponde, así que sirve para cualquier complexión.
        /// </summary>
        private static Texture2D BuildKitMask(Mesh mesh, Texture2D skin, string outPath)
        {
            int size = skin != null ? skin.width : 2048;
            Vector3[] v = mesh.vertices;
            Vector2[] uv = mesh.uv;
            int[] tris = mesh.triangles;

            // En el modelo, la altura puede venir en Y o en Z según cómo se exportó.
            Bounds b = mesh.bounds;
            bool zUp = b.size.z > b.size.y;
            float top = zUp ? b.max.z : b.max.y;
            float k = top / 1.43f;                           // 1.43 m: hombros de un cuerpo de 1.72

            Color32[] px = new Color32[size * size];
            for (int t = 0; t < tris.Length; t += 3)
            {
                int ia = tris[t], ib = tris[t + 1], ic = tris[t + 2];
                // MetaHuman reparte sus texturas en cuadros contiguos (la del cuerpo no está en
                // el primero): se lleva cada triángulo al cuadro 0-1 antes de pintarlo.
                Vector2 tile = new Vector2(Mathf.Floor(Mathf.Min(uv[ia].x, Mathf.Min(uv[ib].x, uv[ic].x)) + 1e-4f),
                                           Mathf.Floor(Mathf.Min(uv[ia].y, Mathf.Min(uv[ib].y, uv[ic].y)) + 1e-4f));
                Vector2 a = (uv[ia] - tile) * size, bb = (uv[ib] - tile) * size, c = (uv[ic] - tile) * size;
                int x0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.x, Mathf.Min(bb.x, c.x))));
                int x1 = Mathf.Min(size - 1, Mathf.CeilToInt(Mathf.Max(a.x, Mathf.Max(bb.x, c.x))));
                int y0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.y, Mathf.Min(bb.y, c.y))));
                int y1 = Mathf.Min(size - 1, Mathf.CeilToInt(Mathf.Max(a.y, Mathf.Max(bb.y, c.y))));
                float area = (bb.x - a.x) * (c.y - a.y) - (c.x - a.x) * (bb.y - a.y);
                if (Mathf.Abs(area) < 1e-6f) continue;
                for (int y = y0; y <= y1; y++)
                    for (int x = x0; x <= x1; x++)
                    {
                        float pxx = x + 0.5f, pyy = y + 0.5f;
                        float w0 = ((bb.x - pxx) * (c.y - pyy) - (c.x - pxx) * (bb.y - pyy)) / area;
                        float w1 = ((c.x - pxx) * (a.y - pyy) - (a.x - pxx) * (c.y - pyy)) / area;
                        float w2 = 1f - w0 - w1;
                        const float e = -0.02f;              // un pelo de margen para no dejar costuras
                        if (w0 < e || w1 < e || w2 < e) continue;
                        Vector3 p = v[ia] * w0 + v[ib] * w1 + v[ic] * w2;
                        float h = (zUp ? p.z : p.y) / k;
                        float side = Mathf.Abs(p.x) / k;
                        px[y * size + x] = Zone(h, side);
                    }
            }
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false, true);
            tex.SetPixels32(px);
            tex.Apply();
            File.WriteAllBytes(outPath, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(outPath);
            TextureImporter ti = (TextureImporter)AssetImporter.GetAtPath(outPath);
            ti.sRGBTexture = false;
            ti.isReadable = true;
            ti.mipmapEnabled = false;
            ti.alphaIsTransparency = false;
            ti.textureCompression = TextureImporterCompression.Uncompressed;
            ti.SaveAndReimport();
            return Tex(outPath);
        }

        // ── La ropa con volumen ─────────────────────────────────────────────────────────────

        private enum Wear { Jersey = 0, Shorts = 1, Socks = 2, Boots = 3 }

        /// <summary>¿Este punto del cuerpo queda cubierto por la prenda?</summary>
        private static bool Covers(Wear w, float h, float side)
        {
            switch (w)
            {
                case Wear.Boots: return h < 0.135f;
                case Wear.Socks: return h >= 0.115f && h < 0.475f;
                case Wear.Shorts: return h >= 0.585f && h < 1.005f && side < 0.275f;
                default:
                    return (h >= 0.955f && h < 1.455f && side < 0.225f)
                           || (h >= 1.44f && h < 1.53f && side >= 0.062f && side < 0.225f)     // hombros, dejando el hueco del cuello
                           || (h >= 1.205f && h < 1.50f && side >= 0.2f && side < 0.35f);
            }
        }

        /// <summary>
        /// La parte alta de la camiseta. En MetaHuman los hombros y la base del cuello vienen en
        /// la malla de la cabeza, así que se cubren aparte, dejando libre el cuello.
        /// </summary>
        private static bool CoversShoulders(float h, float side)
        {
            if (h >= 1.535f) return false;
            float neck = Mathf.Lerp(0.058f, 0.078f, Mathf.InverseLerp(1.535f, 1.44f, h));     // el hueco se abre hacia abajo
            return side >= neck || h < 1.43f;
        }

        /// <summary>Qué tan separada del cuerpo va la tela en este punto, en metros.</summary>
        private static float Clearance(Wear w, float h, float side)
        {
            switch (w)
            {
                case Wear.Boots: return 0.010f;
                case Wear.Socks: return 0.006f;
                case Wear.Shorts: return Mathf.Lerp(0.050f, 0.020f, Mathf.InverseLerp(0.585f, 0.86f, h));   // vuelo en la bastilla
                default:
                    float waist = Mathf.Lerp(0.040f, 0.020f, Mathf.InverseLerp(0.955f, 1.10f, h));         // cae suelta sobre el short
                    float sleeve = Mathf.Lerp(0.018f, 0.034f, Mathf.InverseLerp(0.24f, 0.35f, side));      // manga abierta
                    return side >= 0.225f ? sleeve : waist;
            }
        }

        /// <summary>¿Es un vivo (cuello, puño, bastilla, resorte) que va en el color secundario?</summary>
        private static bool Trim(Wear w, float h, float side)
        {
            switch (w)
            {
                case Wear.Jersey: return (h > 1.44f && side < 0.105f) || side > 0.325f;
                case Wear.Shorts: return h < 0.615f;
                case Wear.Socks: return h > 0.44f;
                default: return false;
            }
        }

        private static int Smoothing(Wear w)
        {
            switch (w)
            {
                case Wear.Boots: return 14;          // funde los dedos en una sola punta
                case Wear.Socks: return 2;
                default: return 7;                   // borra el relieve de los músculos
            }
        }

        /// <summary>
        /// Genera el uniforme a partir del cuerpo: toma la piel de cada zona, la separa hacia
        /// afuera, la alisa para que caiga como tela en vez de marcar la anatomía, y la deja
        /// pegada al mismo esqueleto. Cada prenda toma su color de una tira de ocho casillas.
        /// </summary>
        private static Mesh BuildGarment(Mesh body, string assetPath, bool shoulders = false, float scale = 0f)
        {
            Vector3[] bv = body.vertices;
            Vector3[] bn = body.normals;
            BoneWeight[] bw = body.boneWeights;
            // En modo hombros solo interesa la piel (la primera pieza de la malla de la cabeza).
            int[] bt = shoulders ? body.GetTriangles(0) : body.triangles;
            Bounds b = body.bounds;
            bool zUp = b.size.z > b.size.y;
            float k = scale > 0f ? scale : (zUp ? b.max.z : b.max.y) / 1.43f;

            List<Vector3> verts = new List<Vector3>();
            List<Vector2> uvs = new List<Vector2>();
            List<BoneWeight> weights = new List<BoneWeight>();
            List<int> tris = new List<int>();

            for (int wi = 0; wi < (shoulders ? 1 : 4); wi++)
            {
                Wear w = (Wear)wi;
                Dictionary<int, int> map = new Dictionary<int, int>();      // vértice del cuerpo → vértice de la prenda
                List<int> src = new List<int>();
                List<int> local = new List<int>();
                for (int t = 0; t < bt.Length; t += 3)
                {
                    Vector3 c = (bv[bt[t]] + bv[bt[t + 1]] + bv[bt[t + 2]]) / 3f;
                    float ch = (zUp ? c.z : c.y) / k, cs = Mathf.Abs(c.x) / k;
                    if (shoulders ? !CoversShoulders(ch, cs) : !Covers(w, ch, cs)) continue;
                    for (int j = 0; j < 3; j++)
                    {
                        int vi = bt[t + j], li;
                        if (!map.TryGetValue(vi, out li)) { li = src.Count; map[vi] = li; src.Add(vi); }
                        local.Add(li);
                    }
                }
                if (src.Count == 0) continue;

                // Vértices repetidos en las costuras de la textura se tratan como uno solo.
                Dictionary<Vector3Int, int> weld = new Dictionary<Vector3Int, int>();
                int[] group = new int[src.Count];
                List<Vector3> gp = new List<Vector3>(), gn = new List<Vector3>();
                for (int i = 0; i < src.Count; i++)
                {
                    Vector3 p = bv[src[i]];
                    Vector3Int key = new Vector3Int(Mathf.RoundToInt(p.x * 20000f), Mathf.RoundToInt(p.y * 20000f), Mathf.RoundToInt(p.z * 20000f));
                    int g;
                    if (!weld.TryGetValue(key, out g)) { g = gp.Count; weld[key] = g; gp.Add(p); gn.Add(bn[src[i]]); }
                    else gn[g] += bn[src[i]];
                    group[i] = g;
                }
                int n = gp.Count;
                float[] gap = new float[n];
                Vector3[] pos = new Vector3[n];
                for (int g = 0; g < n; g++)
                {
                    gn[g] = gn[g].normalized;
                    float h = (zUp ? gp[g].z : gp[g].y) / k, side = Mathf.Abs(gp[g].x) / k;
                    gap[g] = (shoulders ? 0.020f : Clearance(w, h, side)) * k;
                    pos[g] = gp[g] + gn[g] * gap[g];
                }
                List<int>[] near = new List<int>[n];
                for (int g = 0; g < n; g++) near[g] = new List<int>(6);
                for (int t = 0; t < local.Count; t += 3)
                    for (int j = 0; j < 3; j++)
                    {
                        int a = group[local[t + j]], c2 = group[local[t + (j + 1) % 3]];
                        if (a == c2) continue;
                        if (!near[a].Contains(c2)) near[a].Add(c2);
                        if (!near[c2].Contains(a)) near[c2].Add(a);
                    }
                for (int it = 0; it < Smoothing(w); it++)
                {
                    Vector3[] next = new Vector3[n];
                    for (int g = 0; g < n; g++)
                    {
                        if (near[g].Count == 0) { next[g] = pos[g]; continue; }
                        Vector3 avg = Vector3.zero;
                        foreach (int o in near[g]) avg += pos[o];
                        next[g] = Vector3.Lerp(pos[g], avg / near[g].Count, 0.55f);
                        // La tela nunca se mete en el cuerpo: se respeta su separación mínima.
                        float out_ = Vector3.Dot(next[g] - gp[g], gn[g]);
                        if (out_ < gap[g]) next[g] += gn[g] * (gap[g] - out_);
                    }
                    pos = next;
                }

                int baseIndex = verts.Count;
                for (int i = 0; i < src.Count; i++)
                {
                    int g = group[i];
                    float h = (zUp ? gp[g].z : gp[g].y) / k, side = Mathf.Abs(gp[g].x) / k;
                    bool trim = shoulders ? (h > 1.475f && side < 0.105f) : Trim(w, h, side);
                    int slot = wi * 2 + (trim ? 1 : 0);
                    verts.Add(pos[g]);
                    uvs.Add(new Vector2((slot + 0.5f) / 8f, 0.5f));
                    weights.Add(bw[src[i]]);
                }
                foreach (int li in local) tris.Add(baseIndex + li);
            }

            Mesh mesh = new Mesh { name = shoulders ? "Hombros" : "Uniforme", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.boneWeights = weights.ToArray();
            mesh.bindposes = body.bindposes;
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            if (File.Exists(assetPath)) AssetDatabase.DeleteAsset(assetPath);
            AssetDatabase.CreateAsset(mesh, assetPath);
            Debug.Log("[Penales] Uniforme generado: " + verts.Count + " vértices, " + (tris.Count / 3) + " triángulos");
            return mesh;
        }

        private static Material Cloth(string path)
        {
            Material m = Fresh(path);
            m.SetColor("_BaseColor", Color.white);
            m.SetFloat("_Metallic", 0f);
            m.SetFloat("_Smoothness", 0.22f);
            m.SetFloat("_DoubleSidedEnable", 1f);       // se ve el interior de mangas y bastillas
            HDMaterial.ValidateMaterial(m);
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>A qué prenda pertenece un punto del cuerpo, por su altura y su distancia al eje (m).</summary>
        private static Color32 Zone(float h, float side)
        {
            if (h < 0.105f) return new Color32(0, 0, 0, 255);                              // botines
            if (h < 0.47f) return new Color32(0, 0, 255, 0);                               // medias
            if (h >= 0.60f && h < 0.985f && side < 0.27f) return new Color32(0, 255, 0, 0); // short
            bool torso = h >= 0.985f && h < 1.44f && side < 0.215f;
            bool sleeve = h >= 1.21f && h < 1.47f && side >= 0.215f && side < 0.335f;
            if (torso || sleeve) return new Color32(255, 0, 0, 0);                         // camiseta
            return new Color32(0, 0, 0, 0);
        }

        private static Material Fresh(string path)
        {
            Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(Shader.Find("HDRP/Lit"));
                AssetDatabase.CreateAsset(m, path);
            }
            return m;
        }

        private static Material Lit(string path, Texture2D color, Texture2D normal, float smoothness)
        {
            Material m = Fresh(path);
            m.SetColor("_BaseColor", Color.white);
            m.SetTexture("_BaseColorMap", color);
            m.SetTexture("_NormalMap", normal);
            m.SetFloat("_NormalScale", 1f);
            m.SetFloat("_Metallic", 0f);
            m.SetFloat("_Smoothness", smoothness);
            HDMaterial.ValidateMaterial(m);
            EditorUtility.SetDirty(m);
            return m;
        }

        private static Material Skin(string path, Texture2D color, Texture2D normal, Texture2D mask, DiffusionProfileSettings profile)
        {
            Material m = Lit(path, color, normal, 0.3f);
            if (mask != null)
            {
                // Rango corto de lisura: la piel no debe brillar como si estuviera mojada.
                m.SetTexture("_MaskMap", mask);
                m.SetFloat("_SmoothnessRemapMin", 0.12f);
                m.SetFloat("_SmoothnessRemapMax", 0.46f);
            }
            if (profile != null)
            {
                // La piel deja pasar algo de luz: sin esto se ve como plástico.
                m.SetFloat("_MaterialID", 0f);
                m.SetFloat("_SubsurfaceMask", 0.75f);
                m.SetFloat("_Thickness", 0.5f);
                HDMaterial.SetDiffusionProfile(m, profile);
            }
            HDMaterial.ValidateMaterial(m);
            EditorUtility.SetDirty(m);
            return m;
        }

        private static Material Cutout(string path, Texture2D color, Color tint, float cutoff)
        {
            Material m = Fresh(path);
            m.SetColor("_BaseColor", tint);
            m.SetTexture("_BaseColorMap", color);
            m.SetFloat("_AlphaCutoffEnable", 1f);
            m.SetFloat("_AlphaCutoff", cutoff);
            m.SetFloat("_DoubleSidedEnable", 1f);
            m.SetFloat("_Smoothness", 0.2f);
            HDMaterial.ValidateMaterial(m);
            EditorUtility.SetDirty(m);
            return m;
        }
    }
}
