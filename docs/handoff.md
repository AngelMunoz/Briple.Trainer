# Handoff — Briple.Trainer

Fecha: 2026-10-14. Estado tras commitear la fase del agente.

## Dónde estamos

- F1 (puerto del dominio), F2 (atletas/memorias/parámetros), F3 (solicitudes/
  instrucciones/wizard) cerradas y commiteadas.
- F4 construida y commiteada: el pipeline del agente completo funciona contra
  un proveedor real (opencode) con salida VALIDO a la primera.
- 70/70 tests, build sin warnings, fantomas limpio.

## Qué existe

- `Briple.Trainer.Core` (net10.0, namespaces `Briple.Trainer.Core.*`):
  - `Plan.Types/Parser/Projection`: puerto verbatim de `E:\Briple\src\Plan`
    con XParsec. Sin política de sync con Briple (decisión del usuario).
  - `Atletas`, `Memorias`, `Parametros`, `Solicitudes`, `Instrucciones`,
    `Wizard`: dominio de F2/F3. Dos atletas reales en los fixtures (hombre con
    3dias+5dias, mujer con 5dias); conteos mes 1 = 72/72/108, mes 2 = 68/104/136.
  - `Plan.Escritor`: Plan → texto (inverso del parser, round-trip testeado).
  - `Plan.Datos`: frontera de datos del agente. JDeck (decoders del CE
    `decode` + `Optional.Property`), tolerante a campos ausentes/nulos y a la
    envoltura `{"data": ...}` (objeto o string). Sin FSharp.SystemTextJson:
    fue un parche innecesario, se eliminó.
  - `Validador`: `revisarParametros` sobre el Plan (género único, opciones
    exactas, semanas, semanas-opción, conteos, RIR) y `validarNuevo/Ajuste`
    sobre texto (parse + warnings + parámetros). `Veredicto = Valido of Plan
    | Rechazado of Fallo list`.
  - `Prompts`: `sistema`/`sistemaConsolidar` (rol, esquema JSON, reglas
    EVIDENCE-BASED TRAINING, cláusula TRAINER EXCEPTIONS) y mensajes de
    usuario por solicitud. Instrucciones en inglés, contenido y contrato de
    datos en español. Bloques estáticos como literales `"""`.
  - `Agente`: `Proveedor` (endpoint/modelo/AgenteUsuario/Encabezados),
    `crearCliente` (OpenAI-compatible; endpoint que termina en `/responses`
    usa la API Responses; encabezados vía DelegatingHandler), `generarNuevo`/
    `generarAjuste` (system+user → JSON → deJson → conMesociclo → revisar →
    Escritor), `consolidarMarcas` (protocolo de línea
    `objetivo | etiqueta | resumen | delta[;delta...]`).
- `spike/probar.fsx`: prueba manual con clave propia (`BRIPLE_KEY`).
  Modos: `nuevo N [prov]`, `ajuste N [prov]`, `ab [prov]`, `consolidar [prov]`,
  `excepcion [prov]`, `prompt [nuevo|ajuste|consolidar|excepcion]`.
  Cada corrida guarda prompt y salidas en `spike/` (ignorados por git).
- Proveedores: glm = coding plan z.ai (`https://api.z.ai/api/coding/paas/v4`),
  deepseek, opencode = `https://opencode.ai/inference/go/v1/responses` con
  `muse-spark-1.3-contributor`, User-Agent propio y `x-opencode-session`.
  `BRIPLE_MODELO` sobreescribe el modelo.

## Decisiones nuevas (no están en plan-evaluacion.md antes de esta sesión)

- El agente devuelve datos (JSON), el formato lo escribe la Core → decisión 11.
- Evidencia + excepciones del entrenador → decisión 12. Las excepciones nacen
  confirmadas (`memoriaDeExcepcion`), el entrenador no justifica, y las notas
  del ejercicio llevan el indicativo de por qué no se sugiere normalmente.
- Sin `private` en archivos con `.fsi` (regla 9 de AGENTS.md).
- Variables de entorno `BRIPLE_KEY` / `BRIPLE_MODELO` (Briple con B).

## Qué falta (ordenado)

1. Medir los gates del agente: 20 corridas por flujo × 2 perfiles × 3
   proveedores, ≥80% válidas a la primera; A/B con/sin memoria; consolidación
   ≥80% bien formada. `dotnet fsi spike/probar.fsx -- ajuste 20 opencode` etc.
2. Completar el pedido del spike: `nuevo` viaja sin conteos objetivo y sin RIR
   de semana 3; considerar llevar conteos también en plan nuevo.
3. F5: consumer desktop Avalonia (Metro estricto sobre SimpleTheme, NXUI,
  SQLite/Donald, export `.briple` que la PWA importa). El wizard ya está
   tipado en `Wizard.fs`.
4. F6: Agent.NET durable sobre el wizard (`awaitEvent` por confirmación).

## Comandos

- `dotnet build Briple.Trainer.slnx` (cero errores, cero warnings).
- `dotnet run --project tests/Briple.Trainer.Core.Tests` (Expecto, 70 tests).
- `dotnet fantomas .` antes de commitear (fantomas 8.0.0, mismo estilo que
  Briple vía .editorconfig).

## Reglas vigentes (AGENTS.md)

Nunca push sin permiso, nunca force push, `.fsi` por archivo nuevo de app/
librería, comentarios en las firmas, sin `private` con `.fsi`, fantomas antes
del commit. Reportes en ASD-STE100 o español plano.
