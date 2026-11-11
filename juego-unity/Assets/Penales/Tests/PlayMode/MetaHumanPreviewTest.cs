#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using Penales.Core;
using Penales.Game;

namespace Penales.PlayTests
{
    public class MetaHumanPreviewTest
    {
        private const int Cell = 560;

        /// <summary>
        /// Pone al tirador y al portero en sus poses clave y guarda una hoja de seis capturas
        /// (parado, carrera, impacto, guardia, lance alto y lance raso) para revisar cuerpo,
        /// uniforme y animación sin abrir el editor.
        /// </summary>
        [UnityTest]
        public IEnumerator LosJugadoresSeVenYSeMueven()
        {
            GameObject shooterPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Penales/Art/MetaHuman/Kelvin/Kelvin.prefab");
            GameObject keeperPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Penales/Art/MetaHuman/Victor/Victor.prefab");
            Assert.That(shooterPrefab, Is.Not.Null, "no existe el prefab del Tirador; corre MetaHumanBuilder.BuildAll");
            if (keeperPrefab == null) keeperPrefab = shooterPrefab;

            // Si antes corrió otra prueba, su cancha sigue viva: se despeja para no encimar dos soles.
            foreach (GrayboxBootstrap old in Object.FindObjectsByType<GrayboxBootstrap>(FindObjectsSortMode.None)) Object.Destroy(old.gameObject);
            yield return null;

            GameObject root = new GameObject("Vista previa");
            GrayboxBootstrap.BuildLight(root.transform);
            GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.transform.position = new Vector3(0f, -0.05f, -6f);
            floor.transform.localScale = new Vector3(60f, 0.1f, 60f);
            floor.GetComponent<Renderer>().sharedMaterial = Paint.Lit(new Color(0.16f, 0.45f, 0.19f), 0.05f);

            List<Team> teams = Teams.CreateDefault();
            HumanShooterView shooter = HumanShooterView.Create(shooterPrefab, root.transform);
            HumanKeeperView keeper = HumanKeeperView.Create(keeperPrefab, root.transform);
            shooter.SetKit(teams[0]);
            keeper.SetKit(teams[1]);

            GameObject camGo = new GameObject("Cámara de prueba");
            Camera cam = camGo.AddComponent<Camera>();
            cam.nearClipPlane = 0.05f;
            cam.targetTexture = new RenderTexture(Cell, Cell, 24, RenderTextureFormat.ARGB32);
            Texture2D sheet = new Texture2D(Cell * 3, Cell * 2, TextureFormat.RGB24, false);

            // Fila de arriba: el tirador, visto de frente desde el arco.
            System.Action<Vector3> lookAtShooter = at =>
            {
                cam.fieldOfView = 34f;
                cam.transform.position = at + new Vector3(0.9f, 1.25f, 3.4f);
                cam.transform.LookAt(at + new Vector3(0f, 0.95f, 0f));
            };
            shooter.Ready();
            lookAtShooter(ShooterView.StartSpot);
            yield return Settle(30);
            Grab(cam, sheet, 0, 1);

            shooter.RunUp(0.42f);
            lookAtShooter(Vector3.Lerp(ShooterView.StartSpot, ShooterView.BallSpot, 0.4f));
            yield return Settle(8);
            Grab(cam, sheet, 1, 1);

            shooter.RunUp(1f);
            cam.fieldOfView = 34f;
            cam.transform.position = ShooterView.BallSpot + new Vector3(3.2f, 1.0f, 1.2f);     // de lado: se ve la pierna
            cam.transform.LookAt(ShooterView.BallSpot + new Vector3(0f, 0.85f, 0f));
            yield return Settle(8);
            Grab(cam, sheet, 2, 1);

            // Fila de abajo: el portero, visto desde el punto penal.
            cam.fieldOfView = 44f;
            cam.transform.position = new Vector3(0f, 1.3f, -7.5f);
            cam.transform.LookAt(new Vector3(0f, 1.15f, 0f));
            keeper.Pose(KeeperModel.Ready, 0, 0f);
            yield return Settle(8);
            Grab(cam, sheet, 0, 0);

            KeeperDive high = new KeeperDive(1, 2, 0f);
            keeper.Pose(KeeperModel.HandTarget(high), high.Column, 1f);
            yield return Settle(8);
            Grab(cam, sheet, 1, 0);

            KeeperDive low = new KeeperDive(-1, 0, 0f);
            keeper.Pose(KeeperModel.HandTarget(low), low.Column, 1f);
            yield return Settle(8);
            Grab(cam, sheet, 2, 0);

            sheet.Apply();
            File.WriteAllBytes(Path.Combine(Directory.GetCurrentDirectory(), "Logs", "jugadores.png"), sheet.EncodeToPNG());
            Assert.That(shooter.GetComponentInChildren<HumanRig>().Ready, Is.True, "el esqueleto del tirador no quedó listo");
        }

        private static IEnumerator Settle(int frames)
        {
            for (int i = 0; i < frames; i++) yield return null;
        }

        private static void Grab(Camera cam, Texture2D sheet, int col, int row)
        {
            RenderTexture.active = cam.targetTexture;
            sheet.ReadPixels(new Rect(0, 0, Cell, Cell), col * Cell, row * Cell);
            RenderTexture.active = null;
        }
    }
}
#endif
