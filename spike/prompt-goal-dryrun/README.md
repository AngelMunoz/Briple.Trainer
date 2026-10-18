# Dry run — prompts por objetivo

Prueba ejecutable del plan `docs/plan-prompts-objetivo-implementacion.md`.
No toca `src/`: prototipa el `GoalProfile` en el script y compara el prompt
actual con el propuesto.

## Cómo correrlo

Desde la raíz del repositorio:

```powershell
dotnet build Briple.Trainer.slnx
dotnet fsi spike/prompt-goal-dryrun.fsx -- prompts
dotnet fsi spike/prompt-goal-dryrun.fsx -- live
```

- `prompts`: renderiza los cinco prompts y los guarda. No llama al modelo.
- `live`: hace una generación por objetivo (Ramiro, 2 semanas, 3 días),
  guarda el plan y corre las comprobaciones.

La clave sale de `BRIPLE_KEY` o del `settings.json` del app
(campo `key`, el respaldo inseguro). No se imprime.

## Qué escribe

`spike/prompt-goal-dryrun/artifacts/<stamp>/`:

| Archivo | Contenido |
|---|---|
| `prompts/<goal>.system.txt` | Mensaje de sistema propuesto |
| `prompts/<goal>.user.txt` | Mensaje de usuario propuesto |
| `plans/<goal>.response.txt` | Respuesta cruda del modelo |
| `plans/<goal>.briple` | Plan escrito en el formato del app |
| `plans/<goal>.checks.txt` | Hallazgos por regla |
| `report.md` | Tabla comparativa y hallazgos |

## Qué comprueba

Las reglas existentes del Core (`checkParameters`, `checkPrescription`) y las
nuevas del perfil:

- filas por día contra `MaxRowsPerDay`;
- bloque de tiempo de 5 min o más sobre un ejercicio que no es cardio;
- RIR en una fila por tiempo;
- minutos de cardio por sesión;
- vueltas distintas dentro de un circuito;
- orden reiniciado en cada circuito;
- línea de mesociclo contra `RirWeeks`.

La línea de mesociclo la agrega el script, como la agregaría la app.

## Cómo leer el reporte

- **Hard**: la regla que el plan debería rechazar o corregir.
- **Soft**: una señal para revisar; no bloquea.
- **existing RIR**: hallazgos del validador actual que el perfil nuevo cambia
  a propósito. En cardio y activación se esperan.
- **prescription advisories**: las reglas tolerantes actuales (`slash`,
  `timed-rir`, `reps-without-effort`).

## Límites

- Una corrida por objetivo. No mide la tasa de pase a la primera; eso es el
  gate de F3 del plan.
- No implementa los deltas de memoria ni el énfasis estructural.
- Los planes son de 2 semanas para que la corrida sea corta. La progresión
  semanal se ve en dos puntos, no en cuatro.
