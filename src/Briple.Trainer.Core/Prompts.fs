module Briple.Trainer.Core.Prompts

open System
open Briple.Trainer.Core.Atletas
open Briple.Trainer.Core.Instrucciones
open Briple.Trainer.Core.Memorias
open Briple.Trainer.Core.Parametros
open Briple.Trainer.Core.Plan.Datos
open Briple.Trainer.Core.Plan.Types
open Briple.Trainer.Core.Solicitudes

// Instructions are written in English; the data contract (schema
// keys, day ids, labels, deltas) and the plan content stay in
// Spanish because the decoder and the athlete-facing app expect them.

let separar (separador: string) (partes: string list) =
  String.concat separador partes

let objetivoTexto objetivo =
  match objetivo with
  | Ejercicio nombre -> nombre
  | Dia id -> $"day {id}"

let deltaTexto delta =
  match delta with
  | Delta.Rir n -> $"rir {n}"
  | Repeticiones n -> $"reps {n}"
  | Series n -> $"series {n}"
  | Carga p -> $"carga {p}"
  | VariantePatron nombre -> $"variante {nombre}"

let parametroTexto parametro =
  match parametro with
  | SemanasPlan(semanas, _) -> $"- the plan covers {semanas} weeks"
  | VariantesAtleta(variantes, _) ->
    let lista = separar ", " variantes

    $"- the plan has one option per variant: {lista}"
  | ConteoOpcion(opcion, total, _) ->
    $"- option {opcion} has exactly {total} exercises in total"
  | RirSemana(semana, objetivo, _) ->
    $"- week {semana} targets RIR {objetivo} (added automatically, do not write it)"
  | Ajuste(objetivo, deltas, MemoriaConfirmada memoria) ->
    let ajustes = deltas |> List.map deltaTexto |> separar "; "

    match memoria.Origen with
    | Excepcion _ ->
      $"- on {objetivoTexto objetivo}: {ajustes} — trainer exception: the trainer prescribed this technique. Follow it, and write in that exercise's notas that it is trainer-prescribed plus the evidence-based reason why it is not generally suggested."
    | Marca _ -> $"- on {objetivoTexto objetivo}: {ajustes}"
  | Ajuste(objetivo, deltas, _) ->
    let ajustes = deltas |> List.map deltaTexto |> separar "; "

    $"- on {objetivoTexto objetivo}: {ajustes}"

let prohibicionTexto prohibicion =
  match prohibicion with
  | CambiarConteos -> "- do not change the exercise counts per option"
  | OpcionesFueraDeVariantes ->
    "- do not add options or genders other than the requested ones"
  | SalirseDelFormato -> "- do not invent fields outside the schema"

let campoTexto campo =
  match campo with
  | Titulo -> "the plan title"
  | Semanas -> "the week count"
  | Generado -> "the generation date"
  | OrigenArchivo -> "the origin line"
  | Opciones -> "the options"
  | TituloOpcion -> "each option title"
  | SemanasDeOpcion -> "how many weeks each option spans"
  | Dias -> "which days of the week are training days"
  | TituloDia -> "each day title"
  | NotasDia -> "day notes"
  | Circuito -> "each exercise circuit"
  | Vueltas -> "each circuit rounds"
  | Orden -> "the exercise order"
  | Nombre -> "each exercise name"
  | Reps -> "the rep ranges (text like 6-8 or 30-45 s)"
  | Campo.Rir -> "the RIR (text like 1-2, or - when it does not apply)"
  | Descanso -> "the rest periods (like 3 min)"
  | Notas -> "each exercise notes (or -)"
  | Extras ->
    "the extras: guidance lines (no mesociclo; that is added automatically)"

// the schema has no place for day notes: they are not requested
let camposPedidos(instrucciones: Instrucciones) =
  instrucciones.Pedir
  |> List.filter(fun campo -> campo <> NotasDia)
  |> List.map campoTexto

let esquemaTexto =
  """RESPONSE SCHEMA: respond only with valid JSON in this shape:
{
  "titulo": "texto",
  "semanas": 4,
  "generado": "2026-10-14",
  "origen": "texto",
  "genero": "hombre" o "mujer",
  "opciones": [
    {
      "id": "3dias",
      "titulo": "texto (opcional)",
      "semanas": [
        { "numero": 1, "dias": [
          { "id": "lunes", "titulo": "texto (opcional)", "ejercicios": [
            { "circuito": "A", "vueltas": "4", "orden": 1, "nombre": "texto", "reps": "6-8", "rir": "1-2", "descanso": "3 min", "notas": "-" }
          ] }
        ] }
      ]
    }
  ],
  "extras": ["texto libre"]
}
Day ids are Spanish: lunes, martes, miercoles, jueves, viernes, sabado, domingo.
The notas field uses "-" when it does not apply.
Do not write mesociclo RIR lines in extras: they are added automatically.
Write all content (titulo, titles, exercise names, reps, descanso, notas, extras) in Spanish."""

let evidenciaTexto =
  """EVIDENCE-BASED TRAINING:
- Every exercise must serve a clear purpose for this athlete, backed by current strength and conditioning evidence: specificity, progressive overload, volume landmarks, fatigue management.
- Educated guesses are acceptable where evidence is thin. Uninformed choices are not.
- Do not force high-skill or high-risk techniques the athlete has not earned. Prefer joint-friendly, coachable progressions with a good risk/benefit ratio for this athlete.
- If you cannot state why an exercise helps this athlete, replace it with one you can justify.
TRAINER EXCEPTIONS: the trainer is the health expert for this athlete and does not need to justify a prescription. When a trainer exception prescribes a technique, follow it. In that exercise's notas, record that it is trainer-prescribed and state the evidence-based reason why it is not generally suggested."""

let atletaTexto(atleta: Atleta) =
  let genero =
    match atleta.Genero with
    | Hombre -> "male"
    | Mujer -> "female"

  let metas = separar ", " atleta.Metas
  let enfasis = separar ", " atleta.Enfasis

  $"Athlete: {atleta.Nombre} ({genero}). Goals: {metas}. Focus: {enfasis}."

/// System message for the generation flows: role, schema, evidence
/// rules and output discipline.
let sistema =
  "You are a strength and conditioning coach who writes training plans.\n\n"
  + esquemaTexto
  + "\n\n"
  + evidenciaTexto
  + "\n\nRespond with the JSON only: no markdown, no extra text."

/// System message for feedback consolidation.
let sistemaConsolidar =
  """You are a strength and conditioning coach who summarizes athlete feedback into training memories.
Each memory is a persistent fact with a proposed parametric adjustment.
Valid deltas: rir N, reps N, series N, carga P (percent), variante NOMBRE.
Respond with one line per mark, in the same order, in this format:
objetivo | etiqueta | resumen | delta[;delta...]
Echo objetivo and etiqueta exactly as given in the marks. Write the resumen in Spanish, as one short sentence stating the fact.
The proposed delta corrects the mark: leve raises load, pesado lowers it, dolor and sin-equipo propose a variante.
No explanations, no markdown."""

/// User message for the new plan flow.
let promptNuevo(solicitud: SolicitudNuevoPlan) =
  let instrucciones = delNuevo solicitud

  // perfil and pedido fix the same variants: say it once
  let fijado =
    instrucciones.Fijar
    |> List.map parametroTexto
    |> List.distinct
    |> separar "\n"

  let pedido = camposPedidos instrucciones |> separar ", "

  let prohibido =
    instrucciones.Prohibir |> List.map prohibicionTexto |> separar "\n"

  $"ATHLETE\n{atletaTexto solicitud.Atleta}\n\nMANDATORY IN THE RESPONSE:\n{fijado}\n\nWHAT YOU DECIDE:\n{pedido}\n\nFORBIDDEN:\n{prohibido}"

/// User message for the adjustment flow: the common part plus the
/// previous plan as reference in the same schema.
let promptAjuste(solicitud: SolicitudAjuste) =
  let instrucciones = delAjuste solicitud

  let fijado =
    instrucciones.Fijar
    |> List.map parametroTexto
    |> List.distinct
    |> separar "\n"

  let pedido = camposPedidos instrucciones |> separar ", "

  let prohibido =
    instrucciones.Prohibir |> List.map prohibicionTexto |> separar "\n"

  $"ATHLETE\n{atletaTexto solicitud.Atleta}\n\nMANDATORY IN THE RESPONSE:\n{fijado}\n\nWHAT YOU DECIDE:\n{pedido}\n\nFORBIDDEN:\n{prohibido}\n\nPREVIOUS PLAN (adjustment reference, same schema):\n{aJson solicitud.PlanAnterior}"

/// User message for consolidation: the recorded marks.
let promptConsolidar(marcas: MarcaFeedback list) =
  let etiquetaTexto etiqueta =
    match etiqueta with
    | Leve -> "leve"
    | Pesado -> "pesado"
    | Dolor -> "dolor"
    | SinEquipo -> "sin-equipo"

  let marcaTexto(marca: MarcaFeedback) =
    let fecha = marca.Fecha.ToString("yyyy-MM-dd")

    $"- {objetivoTexto marca.Objetivo} | {etiquetaTexto marca.Etiqueta} | {fecha}"

  let marcasTexto = marcas |> List.map marcaTexto |> separar "\n"

  $"RECORDED MARKS:\n{marcasTexto}"
