module Plan.Parser

// Parser for the line-based training plan format (#start/#end blocks, #meta,
// #ej rows, #x extras). Line bodies go through XParsec; block nesting is
// assembled by recursive descent so every diagnostic carries a line number.
//
// Recoverable problems (unknown blocks, unrecognized day names, malformed
// #ej rows, unknown meta keys, duplicates, empty days, wrong versions) come
// back as warnings in ParsedPlan; the raw file is stored alongside so
// skipping anything stays lossless. Structural breakage (#end mismatches,
// unterminated blocks, a missing #start plan header) is a hard ParseError.

open System
open XParsec
open XParsec.Combinators
open XParsec.Parsers
open XParsec.CharParsers
open XParsec.ErrorFormatting
open Plan.Types

let runLine parser (line: string) : Result<'parsed, string> =
  match parser(Reader.ofString line ()) with
  | Ok parsed -> Ok parsed
  | Error failure -> Error(formatStringError line failure)

type Line = { Number: int; Text: string }

type LineKind =
  | Blank
  | StartLine of kind: string * arg: string
  | EndLine of kind: string
  | MetaLine of key: string * value: string
  | EjLine of fields: string list
  | BadEj
  | ExtraLine of text: string
  | Unknown

let pword = many1Chars(asciiLetter <|> digit <|> pchar '-')

let pfield = manyChars(noneOf "|")

let pstartLine = parser {
  do! pstring "#start" >>. spaces1
  let! kind = pword
  do! spaces
  let! arg = manyChars anyChar
  return (kind, arg.Trim())
}

let pendLine = parser {
  do! pstring "#end" >>. spaces1
  let! kind = pword
  do! eof
  return kind
}

let pmetaLine = parser {
  do! pstring "#meta" >>. spaces1
  let! key = pword
  do! spaces1
  let! value = manyChars anyChar
  return (key, value.Trim())
}

let pejRow = parser {
  do! pstring "#ej" >>. spaces1
  let! circuit = pfield
  do! skipChar '|'
  let! vueltas = pfield
  do! skipChar '|'
  let! orden = pfield
  do! skipChar '|'
  let! nombre = pfield
  do! skipChar '|'
  let! reps = pfield
  do! skipChar '|'
  let! rir = pfield
  do! skipChar '|'
  let! descanso = pfield
  do! skipChar '|'
  let! notas = pfield
  do! eof
  return [ circuit; vueltas; orden; nombre; reps; rir; descanso; notas ]
}

let pextraLine =
  pstring "#x" >>. spaces1 >>. manyChars anyChar
  |>> (fun rest -> rest.TrimEnd())

let classify(raw: string) : LineKind =
  let text = raw.Trim()

  if text = "" then
    Blank
  elif text.StartsWith "#start " then
    match runLine pstartLine text with
    | Ok(kind, arg) -> StartLine(kind, arg)
    | Error _ -> Unknown
  elif text.StartsWith "#end " then
    match runLine pendLine text with
    | Ok kind -> EndLine kind
    | Error _ -> Unknown
  elif text.StartsWith "#meta " then
    match runLine pmetaLine text with
    | Ok(key, value) -> MetaLine(key, value)
    | Error _ -> Unknown
  elif text.StartsWith "#ej " then
    match runLine pejRow text with
    | Ok fields -> EjLine fields
    | Error _ -> BadEj
  elif text.StartsWith "#x " then
    match runLine pextraLine text with
    | Ok rest -> ExtraLine rest
    | Error _ -> Unknown
  else
    Unknown

let normalizeName(value: string) =
  value
    .Trim()
    .ToLower()
    .Replace("á", "a")
    .Replace("é", "e")
    .Replace("í", "i")
    .Replace("ó", "o")
    .Replace("ú", "u")

let dayTable =
  Map.ofList [
    "lunes", DayOfWeek.Monday
    "monday", DayOfWeek.Monday
    "martes", DayOfWeek.Tuesday
    "tuesday", DayOfWeek.Tuesday
    "miercoles", DayOfWeek.Wednesday
    "wednesday", DayOfWeek.Wednesday
    "jueves", DayOfWeek.Thursday
    "thursday", DayOfWeek.Thursday
    "viernes", DayOfWeek.Friday
    "friday", DayOfWeek.Friday
    "sabado", DayOfWeek.Saturday
    "saturday", DayOfWeek.Saturday
    "domingo", DayOfWeek.Sunday
    "sunday", DayOfWeek.Sunday
  ]

// Day names recognized by the es/en table; unknown names stay in the model
// but the calendar projection skips them.
let tryDayOfWeek(name: string) : DayOfWeek option =
  dayTable |> Map.tryFind(normalizeName name)

let parsePlan(input: string) : Result<ParsedPlan, ParseError> =
  let lines =
    input.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n')
    |> Array.mapi(fun index text -> { Number = index + 1; Text = text })

  let warnings = ResizeArray<ParseWarning>()

  let warn line kind message =
    warnings.Add {
      Line = line
      Kind = kind
      Message = message
    }

  let lastNumber = lines.[lines.Length - 1].Number
  let index = ref 0

  let advance() =
    if index.Value < lines.Length then
      let line = lines.[index.Value]
      index.Value <- index.Value + 1
      Some line
    else
      None

  let unterminated(block: string) : ParseError = {
    Line = lastNumber
    Message = $"unterminated #start {block}"
    Detail = None
  }

  let errAt (line: Line) (message: string) : Result<'a, ParseError> =
    Error {
      Line = line.Number
      Message = message
      Detail = None
    }

  // Consumes lines until the unknown block's matching #end; the caller has
  // already consumed the #start line. Unterminated unknown blocks stop at
  // EOF and the enclosing block reports the structural error.
  let skipBalanced() =
    let mutable depth = 1

    while depth > 0 do
      match advance() with
      | None -> depth <- 0
      | Some line ->
        match classify line.Text with
        | StartLine _ -> depth <- depth + 1
        | EndLine _ -> depth <- depth - 1
        | _ -> ()

  // The block assembler is a linear pipeline: each level only recurses into
  // itself or calls the level below it (dia <- semana <- opcion <- bloque
  // <- planBody <- header), so no mutual recursion is needed.
  let rec parseDia(dia: Dia) : Result<Dia, ParseError> =
    match advance() with
    | None -> Error(unterminated $"dia {dia.Id}")
    | Some line ->
      match classify line.Text with
      | EndLine "dia" ->
        if List.isEmpty dia.Ejercicios then
          warn line.Number EmptyDia $"day '{dia.Id}' has no #ej rows"

        Ok dia
      | EndLine kind ->
        errAt line $"#end {kind} does not match #start dia {dia.Id}"
      | StartLine(kind, arg) ->
        warn
          line.Number
          UnknownBlockSkipped
          $"skipping unknown block '#start {kind} {arg}' inside dia"

        skipBalanced()
        parseDia dia
      | MetaLine(key, value) ->
        if key = "titulo" then
          parseDia { dia with Titulo = Some value }
        else
          warn line.Number UnknownMetaKey $"unknown meta key '{key}' inside dia"
          parseDia dia
      | EjLine fields ->
        match fields with
        | [ circuit; vueltas; ordenText; nombre; reps; rir; descanso; notas ] ->
          let ordenOk, orden = Int32.TryParse ordenText

          if nombre.Trim().Length = 0 || not ordenOk then
            warn
              line.Number
              BadEjField
              "invalid #ej row (empty name or non-integer order)"

            parseDia dia
          else
            let ejercicio = {
              Circuito = circuit.Trim()
              Vueltas = vueltas.Trim()
              Orden = orden
              Nombre = nombre.Trim()
              Reps = reps.Trim()
              Rir = rir.Trim()
              Descanso = descanso.Trim()
              Notas = notas.Trim()
            }

            parseDia {
              dia with
                  Ejercicios = dia.Ejercicios @ [ ejercicio ]
            }
        | _ ->
          warn line.Number BadEjField "invalid #ej row (wrong field count)"
          parseDia dia
      | BadEj ->
        warn line.Number BadEjField "malformed #ej row"
        parseDia dia
      | ExtraLine _ ->
        warn
          line.Number
          UnknownLine
          "extras (#x) are only allowed at plan level"

        parseDia dia
      | Unknown ->
        warn line.Number UnknownLine "unrecognized line"
        parseDia dia
      | Blank -> parseDia dia

  let rec parseSemana(semana: Semana) : Result<Semana, ParseError> =
    match advance() with
    | None -> Error(unterminated $"semana {semana.Numero}")
    | Some line ->
      match classify line.Text with
      | EndLine "semana" -> Ok semana
      | EndLine kind ->
        errAt line $"#end {kind} does not match #start semana {semana.Numero}"
      | StartLine("dia", id) ->
        let dia = {
          Id = id
          Titulo = None
          Notas = []
          Ejercicios = []
        }

        if semana.Dias |> List.exists(fun existing -> existing.Id = id) then
          warn
            line.Number
            DuplicateEntry
            $"duplicate day '{id}' in semana {semana.Numero}"

        if tryDayOfWeek id |> Option.isNone then
          warn line.Number UnknownDayName $"day name '{id}' is not recognized"

        parseDia dia
        |> Result.map(fun parsed -> {
          semana with
              Dias = semana.Dias @ [ parsed ]
        })
        |> Result.bind parseSemana
      | StartLine(kind, arg) ->
        warn
          line.Number
          UnknownBlockSkipped
          $"skipping unknown block '#start {kind} {arg}' inside semana"

        skipBalanced()
        parseSemana semana
      | MetaLine(key, _) ->
        warn
          line.Number
          UnknownMetaKey
          $"meta '{key}' not allowed inside semana"

        parseSemana semana
      | EjLine _
      | BadEj ->
        warn line.Number UnknownLine "#ej row outside a dia"
        parseSemana semana
      | ExtraLine _
      | Unknown ->
        warn line.Number UnknownLine "unrecognized line"
        parseSemana semana
      | Blank -> parseSemana semana

  let rec parseOpcion(opcion: Opcion) : Result<Opcion, ParseError> =
    match advance() with
    | None -> Error(unterminated $"opcion {opcion.Id}")
    | Some line ->
      match classify line.Text with
      | EndLine "opcion" -> Ok opcion
      | EndLine kind ->
        errAt line $"#end {kind} does not match #start opcion {opcion.Id}"
      | StartLine("semana", numeroText) ->
        match Int32.TryParse numeroText with
        | true, numero ->
          if
            opcion.Semanas
            |> List.exists(fun existing -> existing.Numero = numero)
          then
            warn
              line.Number
              DuplicateEntry
              $"duplicate semana {numero} in opcion {opcion.Id}"

          parseSemana { Numero = numero; Dias = [] }
          |> Result.map(fun parsed -> {
            opcion with
                Semanas = opcion.Semanas @ [ parsed ]
          })
          |> Result.bind parseOpcion
        | _ ->
          errAt line $"#start semana requires an integer, got '{numeroText}'"
      | StartLine(kind, arg) ->
        warn
          line.Number
          UnknownBlockSkipped
          $"skipping unknown block '#start {kind} {arg}' inside opcion"

        skipBalanced()
        parseOpcion opcion
      | MetaLine("titulo", value) ->
        parseOpcion { opcion with Titulo = Some value }
      | MetaLine(key, _) ->
        warn
          line.Number
          UnknownMetaKey
          $"unknown meta key '{key}' inside opcion"

        parseOpcion opcion
      | EjLine _
      | BadEj ->
        warn line.Number UnknownLine "#ej row outside a dia"
        parseOpcion opcion
      | ExtraLine _
      | Unknown ->
        warn line.Number UnknownLine "unrecognized line"
        parseOpcion opcion
      | Blank -> parseOpcion opcion

  let rec parseBloque(bloque: Bloque) : Result<Bloque, ParseError> =
    match advance() with
    | None -> Error(unterminated $"genero {Genero.toString bloque.Genero}")
    | Some line ->
      match classify line.Text with
      | EndLine "genero" -> Ok bloque
      | EndLine kind ->
        errAt
          line
          $"#end {kind} does not match #start genero {Genero.toString bloque.Genero}"
      | StartLine("opcion", id) ->
        if bloque.Opciones |> List.exists(fun existing -> existing.Id = id) then
          warn line.Number DuplicateEntry $"duplicate opcion '{id}'"

        parseOpcion { Id = id; Titulo = None; Semanas = [] }
        |> Result.map(fun parsed -> {
          bloque with
              Opciones = bloque.Opciones @ [ parsed ]
        })
        |> Result.bind parseBloque
      | StartLine(kind, arg) ->
        warn
          line.Number
          UnknownBlockSkipped
          $"skipping unknown block '#start {kind} {arg}' inside genero"

        skipBalanced()
        parseBloque bloque
      | MetaLine(key, _) ->
        warn
          line.Number
          UnknownMetaKey
          $"unknown meta key '{key}' inside genero"

        parseBloque bloque
      | EjLine _
      | BadEj ->
        warn line.Number UnknownLine "#ej row outside a dia"
        parseBloque bloque
      | ExtraLine _
      | Unknown ->
        warn line.Number UnknownLine "unrecognized line"
        parseBloque bloque
      | Blank -> parseBloque bloque

  let rec parsePlanBody
    (plan: Plan)
    : Result<Plan * ParseWarning list, ParseError> =
    match advance() with
    | None -> Error(unterminated "plan")
    | Some line ->
      match classify line.Text with
      | EndLine "plan" ->
        if plan.Titulo = "" then
          warn line.Number MissingMeta "missing #meta titulo"

        if plan.Semanas = 0 then
          warn line.Number MissingMeta "missing #meta semanas"

        Ok(plan, List.ofSeq warnings)
      | EndLine kind -> errAt line $"#end {kind} does not match #start plan"
      | StartLine("genero", name) ->
        (match Genero.tryParse name with
         | Some genero ->
           if
             plan.Bloques
             |> List.exists(fun existing -> existing.Genero = genero)
           then
             warn line.Number DuplicateEntry $"duplicate genero '{name}'"

           parseBloque { Genero = genero; Opciones = [] }
           |> Result.map(fun parsed -> {
             plan with
                 Bloques = plan.Bloques @ [ parsed ]
           })
           |> Result.bind parsePlanBody
         | None ->
           warn
             line.Number
             UnknownBlockSkipped
             $"unknown genero '{name}' — skipping block"

           skipBalanced()
           parsePlanBody plan)
      | StartLine(kind, arg) ->
        warn
          line.Number
          UnknownBlockSkipped
          $"skipping unknown block '#start {kind} {arg}'"

        skipBalanced()
        parsePlanBody plan
      | MetaLine("titulo", value) -> parsePlanBody { plan with Titulo = value }
      | MetaLine("semanas", value) ->
        match Int32.TryParse value with
        | true, semanas -> parsePlanBody { plan with Semanas = semanas }
        | _ ->
          warn
            line.Number
            UnknownMetaKey
            $"#meta semanas is not an integer: '{value}'"

          parsePlanBody plan
      | MetaLine("generado", value) ->
        parsePlanBody { plan with Generado = value }
      | MetaLine("origen", value) -> parsePlanBody { plan with Origen = value }
      | MetaLine(key, _) ->
        warn line.Number UnknownMetaKey $"unknown meta key '{key}'"
        parsePlanBody plan
      | ExtraLine text ->
        parsePlanBody {
          plan with
              Extras = plan.Extras @ [ text ]
        }
      | EjLine _
      | BadEj ->
        warn line.Number UnknownLine "#ej row outside a dia"
        parsePlanBody plan
      | Unknown ->
        warn line.Number UnknownLine "unrecognized line"
        parsePlanBody plan
      | Blank -> parsePlanBody plan

  let rec parseHeader() : Result<Plan * ParseWarning list, ParseError> =
    match advance() with
    | None -> Error(unterminated "plan")
    | Some line ->
      match classify line.Text with
      | Blank -> parseHeader()
      | StartLine("plan", versionText) ->
        let versionOk, version =
          if versionText.StartsWith("v") then
            Int32.TryParse(versionText.Substring(1))
          else
            false, 0

        if not versionOk then
          errAt line $"invalid plan version '{versionText}'"
        else
          if version <> 1 then
            warn
              line.Number
              UnsupportedVersion
              $"unsupported plan version {version}"

          let plan = {
            Version = version
            Titulo = ""
            Semanas = 0
            Generado = ""
            Origen = ""
            Bloques = []
            Extras = []
          }

          parsePlanBody plan
      | _ -> errAt line "expected '#start plan v1' as the first line"

  parseHeader()
  |> Result.map(fun (plan, allWarnings) -> {
    Plan = plan
    Warnings = allWarnings
  })
