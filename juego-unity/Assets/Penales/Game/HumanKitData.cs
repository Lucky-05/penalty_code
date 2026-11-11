using UnityEngine;

namespace Penales.Game
{
    /// <summary>
    /// Lo que el uniforme necesita saber de un jugador: qué pieza es el cuerpo, su textura de
    /// piel y el mapa de zonas. Lo llena el armado del prefab.
    /// </summary>
    public class HumanKitData : MonoBehaviour
    {
        public Renderer Body;
        public Texture2D BaseColor;
        public Texture2D Mask;
        /// <summary>La malla de la ropa (camiseta, short, medias y botines).</summary>
        public Renderer Garment;
        /// <summary>La parte alta de la camiseta (hombros), que va con el esqueleto de la cabeza.</summary>
        public Renderer Yoke;
    }
}
