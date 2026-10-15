module Briple.Trainer.Core.Plan.Datos

open System
open System.Text.Json
open JDeck
open Briple.Trainer.Core.Plan.Parser
open Briple.Trainer.Core.Plan.Types

// --- datos decodificados de forma tolerante ---------------------------------

type EjercicioDato = {
  circuito: string option
  vueltas: string option
  orden: int option
  nombre: string option
  reps: string option
  rir: string option
  descanso: string option
  notas: string option
}

type DiaDato = {
  id: string option
  titulo: string option
  ejercicios: EjercicioDato list option
}

type SemanaDato = {
  numero: int option
  dias: DiaDato list option
}

type OpcionDato = {
  id: string option
  titulo: string option
  semanas: SemanaDato list option
}

type PlanDato = {
  titulo: string option
  semanas: int option
  generado: string option
  origen: string option
  genero: string option
  opciones: OpcionDato list option
  extras: string list option
}

// --- decoders ------------------------------------------------------------------

// entero tolerante: numero o texto numerico, nulo cuando falta
let entero(elemento: JsonElement) : Result<int option, DecodeError> =
  match elemento.ValueKind with
  | JsonValueKind.Number ->
    match elemento.TryGetInt32() with
    | true, valor -> Ok(Some valor)
    | _ -> Error(DecodeError.ofError(elemento.Clone(), "no es un entero"))
  | JsonValueKind.String ->
    match elemento.GetString() with
    | null -> Error(DecodeError.ofError(elemento.Clone(), "no es un entero"))
    | numeroText ->
      match Int32.TryParse numeroText with
      | true, valor -> Ok(Some valor)
      | _ -> Error(DecodeError.ofError(elemento.Clone(), "no es un entero"))
  | JsonValueKind.Null
  | JsonValueKind.Undefined -> Ok None
  | _ -> Error(DecodeError.ofError(elemento.Clone(), "no es un entero"))

let texto (nombre: string) (elemento: JsonElement) =
  elemento
  |> Optional.Property.get(nombre, Optional.string)
  |> Result.map Option.flatten

let ejercicioDecoder: Decoder<EjercicioDato> =
  fun elemento -> decode {
    let! circuito = texto "circuito" elemento
    let! vueltas = texto "vueltas" elemento

    let! orden =
      elemento
      |> Optional.Property.get("orden", entero)
      |> Result.map Option.flatten

    let! nombre = texto "nombre" elemento
    let! reps = texto "reps" elemento
    let! rir = texto "rir" elemento
    let! descanso = texto "descanso" elemento
    let! notas = texto "notas" elemento

    return {
      circuito = circuito
      vueltas = vueltas
      orden = orden
      nombre = nombre
      reps = reps
      rir = rir
      descanso = descanso
      notas = notas
    }
  }

let diaDecoder: Decoder<DiaDato> =
  fun elemento -> decode {
    let! id = texto "id" elemento
    let! titulo = texto "titulo" elemento

    let! ejercicios =
      elemento |> Optional.Property.list("ejercicios", ejercicioDecoder)

    return {
      id = id
      titulo = titulo
      ejercicios = ejercicios
    }
  }

let semanaDecoder: Decoder<SemanaDato> =
  fun elemento -> decode {
    let! numero =
      elemento
      |> Optional.Property.get("numero", entero)
      |> Result.map Option.flatten

    let! dias = elemento |> Optional.Property.list("dias", diaDecoder)

    return { numero = numero; dias = dias }
  }

let opcionDecoder: Decoder<OpcionDato> =
  fun elemento -> decode {
    let! id = texto "id" elemento
    let! titulo = texto "titulo" elemento

    let! semanas = elemento |> Optional.Property.list("semanas", semanaDecoder)

    return {
      id = id
      titulo = titulo
      semanas = semanas
    }
  }

let extrasDecoder(elemento: JsonElement) =
  elemento
  |> Optional.Property.list("extras", Optional.string)
  |> Result.map(Option.map(List.choose id))

let planDecoder: Decoder<PlanDato> =
  fun elemento -> decode {
    let! titulo = texto "titulo" elemento

    let! semanas =
      elemento
      |> Optional.Property.get("semanas", entero)
      |> Result.map Option.flatten

    let! generado = texto "generado" elemento
    let! origen = texto "origen" elemento
    let! genero = texto "genero" elemento

    let! opciones =
      elemento |> Optional.Property.list("opciones", opcionDecoder)

    let! extras = extrasDecoder elemento

    return {
      titulo = titulo
      semanas = semanas
      generado = generado
      origen = origen
      genero = genero
      opciones = opciones
      extras = extras
    }
  }

// algunos modelos envuelven la respuesta en {"data": ...} con objeto
// o con texto; ambas formas se desenvuelven antes de decodificar
let decodificar(json: string) : Result<PlanDato, string> =
  use documento = JsonDocument.Parse(json)

  let raiz = documento.RootElement

  let plano elemento =
    elemento |> planDecoder |> Result.mapError(fun error -> error.message)

  match raiz.ValueKind with
  | JsonValueKind.Object ->
    match raiz.TryGetProperty("data") with
    | true, dato when dato.ValueKind = JsonValueKind.Object ->
      plano(dato.Clone())
    | true, dato when dato.ValueKind = JsonValueKind.String ->
      match dato.GetString() with
      | null -> plano raiz
      | texto ->
        use interno = JsonDocument.Parse(texto)

        plano(interno.RootElement.Clone())
    | _ -> plano raiz
  | _ -> plano raiz

// --- validacion y construccion ----------------------------------------------

let campo(valor: string option) =
  valor
  |> Option.bind(fun texto ->
    if String.IsNullOrWhiteSpace texto then
      None
    else
      Some(texto.Trim()))
  |> Option.defaultValue "-"

let convertir(dato: PlanDato) : Result<Plan, string list> =
  let fallos = ResizeArray<string>()

  let genero =
    match dato.genero |> Option.bind Gender.tryFileToken with
    | Some genero -> Some genero
    | None ->
      let recibido = Option.defaultValue "" dato.genero

      fallos.Add $"genero: '{recibido}' no es hombre ni mujer"

      None

  let titulo =
    match campo dato.titulo with
    | "-" ->
      fallos.Add "titulo: vacio"

      ""
    | titulo -> titulo

  let semanas = Option.defaultValue 0 dato.semanas

  if semanas <= 0 then
    fallos.Add $"semanas: {semanas} no es positivo"

  let opciones =
    dato.opciones
    |> Option.defaultValue []
    |> List.mapi(fun indiceOpcion opcion ->
      let rutaOpcion = $"opciones[{indiceOpcion}]"

      let id = campo opcion.id

      if id = "-" then
        fallos.Add $"{rutaOpcion}: id vacia"

      let semanas =
        opcion.semanas
        |> Option.defaultValue []
        |> List.mapi(fun indiceSemana semana ->
          let rutaSemana = $"{rutaOpcion}.semanas[{indiceSemana}]"

          let dias =
            semana.dias
            |> Option.defaultValue []
            |> List.mapi(fun indiceDia dia ->
              let rutaDia = $"{rutaSemana}.dias[{indiceDia}]"

              let idDia = campo dia.id

              if tryDayOfWeek idDia |> Option.isNone then
                fallos.Add $"{rutaDia}: dia '{idDia}' no reconocido"

              dia.ejercicios
              |> Option.defaultValue []
              |> List.iteri(fun indiceEjercicio ejercicio ->
                if
                  ejercicio.nombre
                  |> Option.forall(String.IsNullOrWhiteSpace)
                then
                  fallos.Add
                    $"{rutaDia}.ejercicios[{indiceEjercicio}]: nombre vacio")

              let ejercicios =
                dia.ejercicios
                |> Option.defaultValue []
                |> List.map(fun ejercicio -> {
                  Circuit = campo ejercicio.circuito
                  Laps = campo ejercicio.vueltas
                  Order = Option.defaultValue 0 ejercicio.orden
                  Name = campo ejercicio.nombre
                  Reps = campo ejercicio.reps
                  Rir = campo ejercicio.rir
                  Rest = campo ejercicio.descanso
                  Notes = campo ejercicio.notas
                })

              {
                Id = idDia
                Title = dia.titulo
                Notes = []
                Exercises = ejercicios
              })

          {
            Number = Option.defaultValue 0 semana.numero
            Days = dias
          })

      {
        Id = (if id = "-" then "" else id)
        Title = opcion.titulo
        Weeks = semanas
      })

  match genero with
  | Some genero when fallos.Count = 0 ->
    Ok {
      Version = 1
      Title = titulo
      Weeks = semanas
      Generated = campo dato.generado
      Origin = campo dato.origen
      Blocks = [ { Gender = genero; Variants = opciones } ]
      Extras =
        dato.extras
        |> Option.defaultValue []
        |> List.map(fun extra -> extra.Trim())
    }
  | _ -> Error(List.ofSeq fallos)

/// Convierte el JSON del modelo en un Plan del dominio. Cada campo
/// invalido acumula un fallo con su ruta; el dia debe tener nombre
/// reconocido y el genero debe ser hombre o mujer.
let deJson(json: string) : Result<Plan, string list> =
  try
    match decodificar json with
    | Error motivo -> Error [ motivo ]
    | Ok dato -> convertir dato
  with ex ->
    Error [ $"json ilegible: {ex.Message}" ]

/// Serializa un Plan como el JSON que el modelo recibe de referencia
/// en el flujo de ajuste.
let aJson(plan: Plan) : string =
  let bloque = List.head plan.Blocks

  let dato = {|
    titulo = plan.Title
    semanas = plan.Weeks
    generado = plan.Generated
    origen = plan.Origin
    genero = Gender.toFileToken bloque.Gender
    opciones =
      bloque.Variants
      |> List.map(fun opcion -> {|
        id = opcion.Id
        titulo = opcion.Title
        semanas =
          opcion.Weeks
          |> List.map(fun semana -> {|
            numero = semana.Number
            dias =
              semana.Days
              |> List.map(fun dia -> {|
                id = dia.Id
                titulo = dia.Title
                ejercicios =
                  dia.Exercises
                  |> List.map(fun ejercicio -> {|
                    circuito = ejercicio.Circuit
                    vueltas = ejercicio.Laps
                    orden = ejercicio.Order
                    nombre = ejercicio.Name
                    reps = ejercicio.Reps
                    rir = ejercicio.Rir
                    descanso = ejercicio.Rest
                    notas = ejercicio.Notes
                  |})
              |})
          |})
      |})
    extras = plan.Extras
  |}

  JsonSerializer.Serialize(dato, JsonSerializerOptions(WriteIndented = true))
