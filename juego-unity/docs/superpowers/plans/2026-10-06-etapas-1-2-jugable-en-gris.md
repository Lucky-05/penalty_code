# Penales — plan de las etapas 1 y 2: preparación y jugable en gris

**Objetivo:** una escena de Unity con figuras provisionales donde se puede tirar y atajar una
tanda completa contra la máquina, con toda la lógica del juego probada.

**Spec:** `docs/superpowers/specs/2026-10-06-penales-design.md`

**Ejecución:** nativa (se implementa de inmediato, a petición de Luciano de empezar ya).
Este plan lista tareas, interfaces y criterio de terminado; el código se escribe al ejecutar.

## Restricciones

- Unity 6.3 LTS (`6000.3.25f1`), Apple Silicon, HDRP, Input System, Cinemachine.
- C# 9 como máximo (lo que compila Unity 6): sin espacios de nombres de archivo, sin `init`.
- `Assets/Penales/Core/` no usa `UnityEngine`: vectores de `System.Numerics`. Así se prueba con
  `dotnet test` sin abrir Unity, y las mismas pruebas corren dentro de Unity (EditMode).
- Unidades en metros, segundos, kilogramos. Origen en el centro de la línea de gol, a ras de
  suelo. `x` a la derecha del tirador, `y` arriba, `z` hacia el tirador. Punto penal en `(0,0,11)`.
- Todo azar pasa por un `System.Random` inyectado, para que las pruebas usen semilla fija.
- La escena y los prefabs se arman con scripts de `Assets/Penales/Editor/`.

## Tareas

### 1. Lógica (`Core`) con pruebas — no depende de Unity
| Archivo | Responsabilidad |
|---|---|
| `Field.cs` | Medidas: arco 7.32 × 2.44, punto penal a 11 m, balón 0.11 m y 0.43 kg |
| `BallFlight.cs` | `BallState {Pos, Vel, Spin}`; `Step(state, dt)` con gravedad, arrastre y Magnus; `FlyToGoalPlane(state)` → punto y tiempo de cruce |
| `ShotSolver.cs` | `Solve(target, power, curve)` → `BallState` inicial que cruza el plano de gol en `target` |
| `Pressure.cs` | `ErrorRadius(power, target, clutch, composure)`; `Disturb(target, radius, rng)` |
| `Keeper.cs` | `KeeperDive {Column, Row, StartTime}`; `KeeperModel.Reaches(cruce, tiempo, dive, perfil)` |
| `ShotResolver.cs` | `Resolve(estado inicial, dive, perfil)` → `Goal`, `Saved`, `Post`, `Out` |
| `KeeperBrain.cs` | Decisión del portero máquina según dificultad y tendencias |
| `ShooterBrain.cs` | Decisión del tirador máquina según sus tendencias |
| `Shootout.cs` | Reglas: turnos, 5 tiros, corte anticipado, muerte súbita, ganador |
| `Tournament.cs` | Llave de 8 equipos |
| `Teams.cs` | 8 equipos genéricos y sus jugadores |

Terminado cuando: `dotnet test` pasa, incluyendo que el tiro sin desvío llega a ≤ 5 cm del
punto apuntado en toda la portería y a toda potencia; que el margen de error crece con potencia
y esquina; que la tasa de atajadas sube con la dificultad (10,000 tiros, semilla fija); y que
las reglas de la tanda cortan cuando deben.

### 2. Proyecto de Unity
Crear el proyecto HDRP en `~/penales` por línea de comandos, paquetes (HDRP, Input System,
Cinemachine, Test Framework), definiciones de ensamblado para `Core`, `Game`, `Editor`, `Tests`.
Requiere el editor instalado y la licencia activada por Luciano en Unity Hub.

Terminado cuando: Unity abre el proyecto en modo por lotes sin errores de compilación y corre
las pruebas de EditMode.

### 3. Escena en gris (`Game` + `Editor`)
`GrayboxBuilder` (menú `Penales/Armar escena en gris`) genera: cancha, arco con postes físicos,
balón, tirador y portero como cápsulas, luz y cámara. `MatchDirector`, `BallController`,
`KeeperController`, `InputRouter` y un marcador mínimo conectan `Core` con la escena.

Terminado cuando: Luciano abre la escena, tira y ataja una tanda completa contra la máquina.

### 4. Ajuste de sensación
Con lo que Luciano reporte tras jugar: potencias, márgenes de error, dificultad, cámara.

## Fuera de este plan
Personajes y animaciones (etapa 3), estadio, cámaras de transmisión, repetición y sonido
(etapa 4), modos y menús (etapa 5), empaquetado (etapa 6).
