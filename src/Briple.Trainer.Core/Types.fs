module Plan.Types

// Domain model of the training plan format, mirroring the file one-to-one
// Reps/Rir/Descanso/Notas
// are stored verbatim from the file; "-" means "not present" for the UI.

type Ejercicio = {
  Circuito: string
  Vueltas: string
  Orden: int
  Nombre: string
  Reps: string
  Rir: string
  Descanso: string
  Notas: string
}

type Dia = {
  Id: string
  Titulo: string option
  Notas: string list
  Ejercicios: Ejercicio list
}

type Semana = { Numero: int; Dias: Dia list }

type Opcion = {
  Id: string
  Titulo: string option
  Semanas: Semana list
}

type Genero =
  | Hombre
  | Mujer

type Bloque = {
  Genero: Genero
  Opciones: Opcion list
}

type Plan = {
  Version: int
  Titulo: string
  Semanas: int
  Generado: string
  Origen: string
  Bloques: Bloque list
  Extras: string list
}

module Genero =

  let toString(genero: Genero) =
    match genero with
    | Hombre -> "hombre"
    | Mujer -> "mujer"

  let tryParse(value: string) =
    match value.Trim().ToLowerInvariant() with
    | "hombre" -> Some Hombre
    | "mujer" -> Some Mujer
    | _ -> None

type WarningKind =
  | UnknownDayName
  | UnknownBlockSkipped
  | UnknownMetaKey
  | UnknownLine
  | BadEjField
  | DuplicateEntry
  | EmptyDia
  | UnsupportedVersion
  | MissingMeta

type ParseWarning = {
  Line: int
  Kind: WarningKind
  Message: string
}

type ParseError = {
  Line: int
  Message: string
  Detail: string option
}

type ParsedPlan = {
  Plan: Plan
  Warnings: ParseWarning list
}
