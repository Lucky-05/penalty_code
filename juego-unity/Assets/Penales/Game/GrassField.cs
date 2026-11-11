using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Penales.Core;

namespace Penales.Game
{
    /// <summary>
    /// El pasto en relieve: cientos de miles de hojas sembradas sobre la zona que ve la cámara.
    /// Cada hoja toma su color de dónde cae — franja clara u oscura del corte, o blanco si está
    /// sobre una línea —, así que las líneas de cal y las franjas quedan pintadas en el propio
    /// pasto, como en una cancha de verdad.
    /// </summary>
    public static class GrassField
    {
        public const float StripeWidth = 5.5f;
        private const float Cell = 3f;
        private const int Shades = 4;

        /// <summary>Hojas por metro cuadrado según qué tan cerca queda de la cámara.</summary>
        private static float Density(float z)
        {
            if (z < -15.5f) return 620f;
            if (z < -8f) return 430f;
            return 300f;
        }

        public static void Build(Transform parent, int seed = 12345)
        {
            GameObject root = new GameObject("Pasto");
            root.transform.SetParent(parent, false);
            Material mat = Paint.Lit(Color.white, 0.12f);
            mat.SetTexture("_BaseColorMap", Palette());
            mat.SetFloat("_Smoothness", 0.18f);

            System.Random rng = new System.Random(seed);
            for (float z0 = -22f; z0 < 2.5f; z0 += Cell)
                for (float x0 = -10.5f; x0 < 10.5f; x0 += Cell)
                    Chunk(root.transform, mat, rng, x0, z0);
        }

        /// <summary>Una tira de colores: tonos de franja oscura, tonos de franja clara y blanco de cal.</summary>
        private static Texture2D Palette()
        {
            Color[] dark = { new Color(0.060f, 0.230f, 0.052f), new Color(0.075f, 0.265f, 0.060f), new Color(0.090f, 0.300f, 0.066f), new Color(0.105f, 0.320f, 0.078f) };
            Color[] light = { new Color(0.105f, 0.330f, 0.075f), new Color(0.125f, 0.370f, 0.085f), new Color(0.145f, 0.410f, 0.092f), new Color(0.165f, 0.430f, 0.105f) };
            Texture2D t = new Texture2D(Shades * 2 + 2, 1, TextureFormat.RGBA32, false, false);
            t.filterMode = FilterMode.Point;
            t.wrapMode = TextureWrapMode.Clamp;
            for (int i = 0; i < Shades; i++)
            {
                t.SetPixel(i, 0, dark[i]);
                t.SetPixel(Shades + i, 0, light[i]);
            }
            t.SetPixel(Shades * 2, 0, new Color(0.86f, 0.87f, 0.84f));
            t.SetPixel(Shades * 2 + 1, 0, new Color(0.93f, 0.94f, 0.90f));
            t.Apply();
            return t;
        }

        /// <summary>¿Este punto de la cancha cae sobre una línea de cal?</summary>
        public static bool OnLine(float x, float z)
        {
            const float w = 0.06f;                           // media anchura de la línea
            float ax = Mathf.Abs(x);
            if (Mathf.Abs(z) < w) return true;                                         // línea de gol
            if (Mathf.Abs(z + 5.5f) < w && ax < 9.16f + w) return true;                // área chica
            if (Mathf.Abs(ax - 9.16f) < w && z > -5.5f && z < 0f) return true;
            if (Mathf.Abs(z + 16.5f) < w && ax < 20.16f + w) return true;              // área grande
            if (Mathf.Abs(ax - 20.16f) < w && z > -16.5f && z < 0f) return true;
            float dz = z + Field.PenaltyDistance;
            if (x * x + dz * dz < 0.115f * 0.115f) return true;                        // punto penal
            float r = Mathf.Sqrt(x * x + dz * dz);
            if (Mathf.Abs(r - 9.15f) < w && z < -16.5f) return true;                   // media luna
            return false;
        }

        private static void Chunk(Transform parent, Material mat, System.Random rng, float x0, float z0)
        {
            int count = Mathf.RoundToInt(Density(z0 + Cell * 0.5f) * Cell * Cell);
            List<Vector3> v = new List<Vector3>(count * 3);
            List<Vector3> n = new List<Vector3>(count * 3);
            List<Vector2> uv = new List<Vector2>(count * 3);
            List<int> tri = new List<int>(count * 6);
            float texel = 1f / (Shades * 2 + 2);

            for (int i = 0; i < count; i++)
            {
                float x = x0 + (float)rng.NextDouble() * Cell;
                float z = z0 + (float)rng.NextDouble() * Cell;
                float angle = (float)rng.NextDouble() * Mathf.PI;
                float height = 0.045f + (float)rng.NextDouble() * 0.03f;
                float half = 0.011f + (float)rng.NextDouble() * 0.007f;
                Vector3 side = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * half;
                Vector3 lean = new Vector3((float)rng.NextDouble() - 0.5f, 0f, (float)rng.NextDouble() - 0.5f) * 0.03f;

                bool stripe = (Mathf.FloorToInt((z + 44f) / StripeWidth) & 1) == 0;
                int shade = rng.Next(Shades);
                float u = OnLine(x, z) ? (Shades * 2 + rng.Next(2) + 0.5f) * texel
                                       : ((stripe ? 0 : Shades) + shade + 0.5f) * texel;

                Vector3 p = new Vector3(x, 0f, z);
                int b = v.Count;
                v.Add(p - side); v.Add(p + side); v.Add(p + lean + Vector3.up * height);
                // La luz se calcula como si todas miraran arriba: así el pasto no parpadea.
                n.Add(Vector3.up); n.Add(Vector3.up); n.Add(Vector3.up);
                Vector2 t = new Vector2(u, 0.5f);
                uv.Add(t); uv.Add(t); uv.Add(t);
                tri.Add(b); tri.Add(b + 1); tri.Add(b + 2);
                tri.Add(b); tri.Add(b + 2); tri.Add(b + 1);  // la otra cara
            }

            Mesh mesh = new Mesh { name = "Pasto", indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(v);
            mesh.SetNormals(n);
            mesh.SetUVs(0, uv);
            mesh.SetTriangles(tri, 0, false);
            mesh.bounds = new Bounds(new Vector3(x0 + Cell * 0.5f, 0.04f, z0 + Cell * 0.5f), new Vector3(Cell + 0.2f, 0.2f, Cell + 0.2f));
            mesh.UploadMeshData(true);

            GameObject g = new GameObject("Pasto " + x0.ToString("F0") + "," + z0.ToString("F0"));
            g.transform.SetParent(parent, false);
            g.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer r = g.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = true;
        }
    }
}
