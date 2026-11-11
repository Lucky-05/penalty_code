using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Penales.Game;

namespace Penales.PlayTests
{
    public class GrayboxSmokeTest
    {
        /// <summary>
        /// La escena provisional arranca, arma todo lo que el partido necesita y la cámara
        /// dibuja algo. De paso deja una captura en Logs/ para revisar cómo se ve sin abrir
        /// el editor.
        /// </summary>
        [UnityTest]
        public IEnumerator LaEscenaArrancaYSeDibuja()
        {
            yield return SceneManager.LoadSceneAsync("Graybox", LoadSceneMode.Single);
            yield return null;

            MatchDirector director = Object.FindFirstObjectByType<MatchDirector>();
            Assert.That(director, Is.Not.Null, "no hay director del partido");
            Assert.That(director.Ball, Is.Not.Null);
            Assert.That(director.Keeper, Is.Not.Null);
            Assert.That(director.View, Is.Not.Null);

            Camera cam = director.View;
            RenderTexture rt = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt;
            for (int i = 0; i < 40; i++) yield return null;     // que la exposición se asiente

            RenderTexture.active = rt;
            Texture2D shot = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            shot.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            shot.Apply();
            RenderTexture.active = null;
            cam.targetTexture = null;

            string path = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "graybox.png");
            File.WriteAllBytes(path, shot.EncodeToPNG());

            // No todo del mismo color: algo se dibujó.
            Color a = shot.GetPixel(640, 100), b = shot.GetPixel(640, 600), c = shot.GetPixel(200, 360);
            float spread = Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - c.b) + Mathf.Abs(b.g - c.g);
            Assert.That(spread, Is.GreaterThan(0.02f), "la imagen salió plana: " + a + " " + b + " " + c);
        }
    }
}
