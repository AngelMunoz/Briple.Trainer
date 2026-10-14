# Plan de evaluación — app de escritorio Briple.Trainer

Fecha: 2026-10-13 (rev 4).
Base: investigación 3 vs 5 días (sess_d95b0e28), evaluación .NET de agentes (sess_cc6ca46d), análisis del código de `E:\Briple`.

## Arquitectura

Dos piezas:

1. **Core Library** (`Briple.Trainer.Core`): genera, parametriza y expone las herramientas para consumo en apps. Sin dependencias de UI. Contiene el dominio portado de Briple, el modelo de atletas y memorias, los contratos de solicitud, el validador, la orquestación del agente y la export `.briple`.
2. **Consumer app**: desktop Avalonia para el entrenador. Un server puede consumir la misma Core después.

## Modelo conceptual

Cinco capas:

1. **Formato**: el único determinismo. Tipos F#, parser y validador, portados de Briple.
2. **Atleta**: perfil individual. Género, opción, metas, énfasis.
3. **Memorias**: hechos relevantes que persisten entre planes. Las genera el agente desde el feedback registrado. Ejemplo: "la última vez Pepe sintió el press muy leve". Se presentan como pregunta con ajustes predefinidos en la generación siguiente.
4. **Parámetros**: valores de la solicitud. Cada uno con origen trazable: perfil del atleta, memorias confirmadas, plan anterior, ajuste pedido.
5. **Contenido**: ejercicios, variantes, notas. Lo produce el agente.

El período lo decide el entrenador: semana, quincena, tres semanas o mes.
Solo cambia el número de semanas del plan; ningún otro parámetro se altera.

Las solicitudes son autocontenidas. No hay memoria de conversación:

- **Plan nuevo**: atleta + memorias confirmadas + pedido → agente → plan.
- **Ajuste**: atleta + memorias confirmadas + plan anterior (record completo) + delta → agente → plan.

El feedback se registra con marcas rápidas sobre ejercicios o días del plan activo:
leve, pesado, dolor, sin equipo. Texto libre solo por excepción.
El agente consolida esas marcas en memorias con un delta paramétrico propuesto.

## Qué se toma de Briple

Puerto a la Core:

- `src/Plan/Types.fs` y `src/Plan/Projection.fs`: .NET puro. Copia sin cambios.
- `src/Plan/Parser.fs`: usa XParsec. Se mantiene XParsec; el paquete entra al CPM y el parser se copia tal cual.
- Los tests de `tests/` (Parser, Projection) y sus fixtures, como línea base.
- El formato de archivo `.briple`: el atleta sigue usando la PWA. El desktop exporta y la PWA importa; los deep links `web+bripple` ya existen.

Lo que el atleta ve hoy define la superficie que el entrenador ajusta:

| El atleta ve en Briple | El entrenador ajusta |
|---|---|
| Card del día: ejercicio + scheme | Nombre, reps, RIR, descanso, notas |
| Grupos por circuito (A1/B1) y vueltas | Circuito, vueltas, orden |
| Badge de semana + RIR del mesociclo | Semanas, RIR por semana |
| Guía (descansos, regla-circuito) | Extras del plan |
| Anchor y cola de planes | Fecha de inicio, período, encadenamiento |
| Chips género × opción | Variantes por atleta |

## Flujo de generación con confirmación (tuning)

Pasos fijos antes de generar:

1. Seleccionar atleta.
2. Revisar memorias: cada una se muestra como resumen y pregunta ("¿qué ajuste aplicamos?"). Respuestas predefinidas en parámetros: RIR, repeticiones, series, carga, variante del patrón. El ajuste automático va primero; el entrenador acepta, elige o descarta. Sin texto.
3. Elegir período: 1, 2, 3 o 4 semanas.
4. Definir el pedido: ajustes, énfasis, RIR, variantes.
5. Generar. El validador chequea el formato y los parámetros de la solicitud.
6. Revisión del entrenador: ver el plan con vistas tipo Briple, editar campos, regenerar o descartar. Confirmar agrega el plan al historial del atleta.

## Reglas fijas

- Sin interfaz de chat. Wizard, formularios, cards, diffs.
- Solo el formato es determinista. Todo lo demás viaja como parámetro en la solicitud.
- El validador comprueba la salida contra el formato y contra los parámetros de la solicitud: conteos, RIR, pares género-opción.
- Las memorias las genera el agente desde el feedback. Se aplican solo con la confirmación del entrenador, como deltas paramétricos.
- Entrada de texto manual al mínimo. Chips y parámetros primero; texto libre solo por excepción.
- El plan confirmado se guarda como Raw verbatim, fuente de verdad, como en Briple.
- Nada específico de un atleta va hardcodeado en el motor ni en los prompts.
- BYOK: GLM, DeepSeek, Meta Muse. Los tres son OpenAI-compatible.

## Criterios

| Criterio | Peso | Medida |
|---|---|---|
| Cobertura paramétrica | Alto | Los 3 planes existentes se expresan sin campos implícitos |
| Flexibilidad individual | Alto | Un atleta nuevo se expresa solo con parámetros, sin cambios de código |
| Autocontención | Alto | La solicitud reproduce el plan sin historial de chat |
| Memoria efectiva | Alto | Un hecho confirmado cambia la salida del plan siguiente |
| Tasa de validación | Alto | % de salidas del agente que pasan a la primera |
| Esfuerzo | Medio | Sesiones de trabajo por fase |

## Fases

### F1 — Puerto del dominio a la Core

- Copiar Types, Projection y Parser tal cual (XParsec); portar los tests.
- Gate: mes 1 (252 `#ej`) y mes 2 (308 `#ej`) parsean con el mismo resultado que la PWA: mismos planes, mismos warnings.
- Esfuerzo: 1-2 sesiones.

### F2 — Atletas, memorias y parámetros

- Tipos `Atleta`, `MarcaFeedback` (objetivo, etiqueta, fecha) y `Memoria` (resumen generado, delta paramétrico propuesto, origen, estado).
- Inventario de cada campo del plan con su origen: perfil del atleta, memorias confirmadas, plan anterior, ajuste pedido, agente.
- Gate: los tres planes existentes se descomponen como dos atletas reales: el hombre con dos variantes (3d y 5d) y la mujer con una (5d). Las variantes son del atleta, no personas distintas.
- Gate: un tercer atleta de prueba se define solo con parámetros, sin tocar tipos.
- Gate: una marca de feedback ("muy leve" en un ejercicio) se consolida en una memoria con delta paramétrico bien formado.
- Gate: una memoria confirmada ("carga muy leve") se refleja en los parámetros de la solicitud siguiente.
- Esfuerzo: 2 sesiones.

### F3 — Contrato de solicitud y flujo de confirmación

- Tipos `SolicitudNuevoPlan` y `SolicitudAjuste`, con memorias confirmadas y período.
- Mapeo parámetros → instrucciones del agente: qué se fija, qué se pide, qué se prohíbe.
- Gate: "mes 2" se expresa como ajuste sobre mes 1. Los conteos 68/104/136 van en la solicitud y se cumplen en la salida.
- Gate: los 6 pasos del wizard quedan especificados con entradas y salidas.
- Esfuerzo: 1-2 sesiones.

### F4 — Spike agente (Microsoft.Extensions.AI)

- Dos flujos (plan nuevo, ajuste) por dos perfiles de atleta por tres proveedores BYOK.
- Gate: al menos 80% de salidas pasa el validador a la primera. 20 llamadas por flujo.
- Gate A/B: con la memoria confirmada y sin ella, la salida del plan cambia.
- Gate: el agente consolida un set de marcas de prueba en memorias con deltas paramétricos válidos. Al menos 80% bien formado.
- Toda salida rechazada se descarta. No hay reparo automático en esta fase.
- Esfuerzo: 2 sesiones.

### F5 — Consumer desktop

- Tema Metro estricto propio: SimpleTheme de base más diccionario de estilos planos. CornerRadius 0, un acento, tipografía Segoe UI, app bar inferior, pivots con Navs. Tokens e idiomas visuales se portan de Briple (`Theme.fs`, acentos Metro de Metrino.Ripple). `E:\metrino` es la fuente del lenguaje de diseño para la web: tokens y especificaciones visuales salen de ahí.
- Avalonia: lista de atletas, wizard de generación, revisión con edición de campos, vistas tipo Briple (grupos por circuito, badge de semana, guía), export `.briple`.
- Persistencia SQLite (Donald): atletas, memorias, planes (Raw verbatim), solicitudes.
- Gate: alta de atleta, generación con wizard, confirmación de memorias y export `.briple` que la PWA importa sin errores.
- Esfuerzo: 3-4 sesiones.

### F6 — Agent.NET

- Frontera tipada: `TypedAgent<'Solicitud, 'PlanResultado>` sobre el contrato de F3.
- El wizard como flujo multi-paso: `awaitEvent` en cada confirmación del entrenador.
- Restricciones: records en checkpoints (las DUs no hacen round-trip). Side effects idempotentes.
- Gate: el wizard corre in-process y durable desde una sola definición.
- Esfuerzo: 2-3 sesiones.

## Decisiones cerradas

1. **Determinismo**: solo el formato del texto. Conteos, RIR y vueltas son parámetros de la solicitud, validados en la salida.
2. **Memorias**: las genera el agente automáticamente desde el feedback. Captura con marcas rápidas; texto libre solo por excepción. En la generación siguiente se presentan como resumen y pregunta, con ajustes predefinidos en parámetros: RIR, repeticiones, series, carga, variante. Se aplican solo al confirmar.
3. **Período**: 1, 2, 3 o 4 semanas. Solo cambia `Semanas` del plan.
4. **Validador**: formato portado de Briple más chequeo contra los parámetros de la solicitud.
5. **PWA del atleta**: sigue vigente. El desktop exporta `.briple` y la PWA lo importa. Un server después consume la misma Core.
6. **Puerto**: Types, Projection y Parser directos, con XParsec como en Briple. Tests portados como línea base.
7. **Campos verbatim**: Reps, Rir, Descanso y Notas siguen como texto en el modelo. Valores tipados solo en la parametrización que consume el agente.
8. **Edición del plan generado**: por campos en la revisión, no texto libre. Toda edición pasa el validador.
9. **Persistencia**: SQLite. El Raw del plan confirmado es la fuente de verdad.
10. **UI**: sin SukiUI. Tema Metro estricto propio sobre Avalonia: SimpleTheme de base y estilos planos (CornerRadius 0, un acento, Segoe UI). Tokens e idiomas portados de Briple y de `E:\metrino`, la fuente del lenguaje de diseño Metro para la web. NXUI se mantiene para autoría de UI en código.

## Fuera de alcance

Chat libre, MCP, nube, sincronización multi-dispositivo, autenticación, multi-entrenador, publicación.
Server: fase posterior; reusará la Core sin cambios.
Bitácora manual de entrenamiento: RIR real por serie, cargas levantadas, marcas de series hechas.
