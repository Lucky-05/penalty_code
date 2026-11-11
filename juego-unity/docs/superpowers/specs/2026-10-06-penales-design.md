# Penales — juego de tanda de penales para Mac

Fecha: 2026-10-06 · Estado: especificación para revisión de Luciano

## 1. Qué se quiere

Un juego de penales con el estilo y la sensación de FIFA, hecho en un motor real (no una página
web), que corra como aplicación nativa en la Mac de Luciano y se vea profesional.

Decisiones tomadas con él:

| Decisión | Elegido |
|---|---|
| Motor | Unity 6 LTS con HDRP (pipeline de alta fidelidad) |
| Plataforma | macOS (Apple Silicon), aplicación local `.app`. Sin tienda en esta versión |
| Arte | Solo assets gratuitos |
| Marca | Estilo FIFA, sin usar nombre, logos, equipos, uniformes ni jugadores reales |

Éxito de la primera versión: una `.app` que abre en la Mac, corre a 60 cuadros por segundo en
1440p en un M3 Pro, y permite jugar una tanda completa contra la máquina o entre dos personas,
con cámaras de transmisión, repetición y sonido de estadio.

Límite que se acepta: con assets gratuitos el juego no alcanza el realismo de FIFA en caras ni
en animaciones. El diseño separa el arte del juego para poder sustituirlo después sin reescribir.

## 2. Alcance de la primera versión

**Entra**
- Tanda de penales: 5 tiros por lado y muerte súbita.
- El jugador alterna entre tirador y portero.
- Modos: tanda rápida, torneo de eliminación de 8 equipos, práctica libre, dos jugadores en la
  misma Mac.
- 8 equipos genéricos con editor de nombre y colores; 3 niveles de dificultad.
- Teclado y ratón, y control (mando) con el sistema de entrada de Unity.
- Cámaras de transmisión, repetición desde 3 ángulos, marcador, público, sonido.
- Menús, pausa, ajustes de gráficos y audio, guardado local del progreso.

**No entra**
- Multijugador en línea, tienda, logros de plataforma.
- Partido completo, tiros libres, carrera, licencias reales.
- Windows, móvil o consola.

## 3. Cómo se juega

**Tirar.** Una retícula sobre el arco se mueve con el ratón o la palanca. Mantener el botón
carga la potencia; soltarlo dispara. Un segundo eje da efecto (curva). El **medidor de presión**
define el margen de error: crece con la potencia, con la cercanía a los postes y con el momento
de la tanda (tiro decisivo). El punto real de impacto es la retícula más un desvío aleatorio
dentro de ese margen. Tiro especial: picada ("panenka") con potencia baja y botón alterno.

**Atajar.** El jugador elige una de las zonas (9: tres alturas por tres lados) y el momento del
lance. Lanzarse antes de que el tirador golpee lo deja fijo; lanzarse tarde no llega. El alcance
depende de la zona y del tiempo de reacción.

**La máquina.**
- Portero: con una probabilidad que depende de la dificultad "lee" el lado correcto; si no, elige
  según las tendencias del tirador. Tiene tiempo de reacción y alcance propios.
- Tirador: cada jugador genérico tiene tendencias (lado preferido, altura, potencia, sangre
  fría) que sesgan su tiro y su margen de error.

**Reglas.** Orden alterno A-B, 5 tiros, se corta cuando el resultado ya no es alcanzable,
después muerte súbita. El torneo son cuartos, semifinal y final a una tanda cada uno.

## 4. Arquitectura

Unity 6 LTS, HDRP, Input System, Cinemachine, Timeline. Código en C# dividido en dos capas:

```
Assets/Penales/
  Core/          lógica pura, sin MonoBehaviour: se prueba sin abrir escenas
    Shootout.cs        estado y reglas de la tanda
    Tournament.cs      llave de 8 equipos
    ShotSolver.cs      de (retícula, potencia, efecto, presión) a velocidad y giro iniciales
    BallFlight.cs      integración del vuelo: gravedad, arrastre, efecto Magnus
    KeeperBrain.cs     decisión del portero máquina
    ShooterBrain.cs    decisión del tirador máquina
    Pressure.cs        margen de error
    Teams.cs           datos de equipos y jugadores
  Game/          MonoBehaviours que conectan Core con la escena
    MatchDirector.cs   máquina de estados de la presentación (entrada, tiro, repetición, marcador)
    BallController.cs  mueve el Rigidbody con BallFlight; detecta gol, poste, atajada, fuera
    ShooterController.cs / KeeperController.cs   animación y sincronía con el golpeo
    InputRouter.cs     Input System: teclado, ratón y mando, para 1 o 2 jugadores
    CameraDirector.cs  cámaras Cinemachine y cortes
    ReplaySystem.cs    graba posiciones y poses por cuadro y las reproduce
    CrowdSystem.cs     público instanciado y reacción al resultado
    AudioDirector.cs   ambiente, golpeo, red, silbato, reacción
  UI/            UI Toolkit: menús, marcador, medidores, ajustes
  Art/           modelos, animaciones, materiales, sonidos (sustituible)
  Editor/        scripts que arman escenas y prefabs desde código
  Tests/         EditMode (Core) y PlayMode (flujo de una tanda)
```

Reglas de diseño:
- `Core` no depende de `UnityEngine` salvo tipos matemáticos; toda regla y todo número de juego
  vive ahí y se prueba con pruebas de EditMode.
- La escena y los prefabs se generan con scripts de `Editor/` (reproducible, sin trabajo manual
  en el editor), y se pueden regenerar.
- Los parámetros de juego (potencias, márgenes, dificultad) están en `ScriptableObject`.
- `Art/` se puede reemplazar por assets mejores sin tocar `Core` ni `Game`.

## 5. Física del balón

Balón reglamentario: 0.43 kg, radio 0.11 m. Vuelo integrado a paso fijo con gravedad, arrastre
cuadrático y fuerza de Magnus proporcional a giro × velocidad. `ShotSolver` calcula la velocidad
inicial para pasar por el punto de impacto elegido a 11 m. Postes y travesaño con colisión
física real; la red es una malla con resortes simple que cede y frena el balón. La atajada se
resuelve por colisión con manos y cuerpo del portero más un volumen de alcance.

## 6. Arte gratuito

| Qué | Fuente |
|---|---|
| Jugadores y portero, animaciones de carrera, golpeo, lance, festejo | Mixamo (cuenta gratuita de Adobe; descarga manual) |
| Texturas de césped, materiales, cielos HDRI | Poly Haven (CC0) |
| Estadio | Armado por piezas en código: gradas, techo, túnel, vallas, torres de luz |
| Público | Figuras instanciadas con variación de color y animación simple |
| Sonido | Bibliotecas libres (CC0) |
| Balón, arco, red | Modelados por código y con texturas libres |

Uniformes: material con máscara para colores primario y secundario por equipo, número en la
espalda generado con texto.

## 7. Presentación

Secuencia por tiro: entrada del tirador → cámara detrás del tirador para apuntar → golpeo →
seguimiento del balón → reacción → repetición desde 3 ángulos (detrás del arco, lateral, cámara
alta) → marcador. Iluminación nocturna de estadio con reflectores, niebla volumétrica ligera,
profundidad de campo en las tomas cerradas y desenfoque de movimiento en repetición.

Objetivo de rendimiento: 60 cuadros por segundo a 2560×1440 en M3 Pro (Metal). Tres perfiles de
calidad en ajustes.

## 8. Pruebas

- EditMode sobre `Core`: reglas de la tanda (cortes, muerte súbita), llave del torneo, que
  `ShotSolver` + `BallFlight` lleguen al punto apuntado dentro de 5 cm sin desvío, que el margen
  de error crezca con potencia y esquina, que la dificultad cambie la tasa de acierto del portero
  (simulación de 10,000 tiros con semilla fija).
- PlayMode: una tanda completa máquina contra máquina termina con un ganador y sin errores.
- Manual: lista de comprobación de sensación de juego y de cámaras por etapa.

## 9. Etapas

1. **Preparación.** Unity Hub, Unity 6 LTS con soporte de Mac, proyecto HDRP, repositorio git.
2. **Jugable en gris.** `Core` completo con pruebas; escena con figuras provisionales; tirar y
   atajar contra la máquina; marcador mínimo.
3. **Personajes.** Modelos y animaciones de Mixamo, sincronía del golpeo y del lance.
4. **Estadio y cámaras.** Estadio, luz, público, cámaras, repetición, sonido.
5. **Modos y menús.** Torneo, práctica, dos jugadores, equipos, ajustes, guardado.
6. **Empaquetado.** `.app` para Mac, icono, prueba en limpio.

Cada etapa termina con algo que se puede abrir y jugar.

## 10. Lo que pone Luciano

- Cuenta de Unity (gratuita) e inicio de sesión en Unity Hub para activar la licencia personal.
- Cuenta de Adobe (gratuita) y descarga de los modelos y animaciones de Mixamo que se le listen.
- Probar cada etapa y decir qué no se siente bien.

## 11. Riesgos

- **Realismo.** El resultado visual depende de los assets; con gratuitos queda como un juego
  independiente cuidado, no como FIFA.
- **Animaciones.** Las de Mixamo son genéricas; ajustar el golpeo al punto de impacto y el lance
  a 9 zonas requiere mezcla y cinemática inversa, y es lo que más iteración pide.
- **Trabajo en el editor.** El código y los scripts que arman la escena se escriben fuera del editor, y Unity se
  corre por línea de comandos para compilar y probar, sin ver la pantalla del juego. La valoración
  visual y de sensación la hace Luciano en cada etapa.
- **Disco.** Tras limpiar Xcode hay 53 GB libres; Unity más el proyecto ocupan unos 25-35 GB.
