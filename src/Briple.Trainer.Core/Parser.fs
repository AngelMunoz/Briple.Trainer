module Briple.Trainer.Core.Plan.Parser

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
open Briple.Trainer.Core.Plan.Types

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
  let! laps = pfield
  do! skipChar '|'
  let! order = pfield
  do! skipChar '|'
  let! name = pfield
  do! skipChar '|'
  let! reps = pfield
  do! skipChar '|'
  let! rir = pfield
  do! skipChar '|'
  let! rest = pfield
  do! skipChar '|'
  let! notes = pfield
  do! eof
  return [ circuit; laps; order; name; reps; rir; rest; notes ]
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
  // itself or calls the level below it (day <- week <- variant <- block
  // <- planBody <- header), so no mutual recursion is needed.
  let rec parseDay(day: Day) : Result<Day, ParseError> =
    match advance() with
    | None -> Error(unterminated $"dia {day.Id}")
    | Some line ->
      match classify line.Text with
      | EndLine "dia" ->
        if List.isEmpty day.Exercises then
          warn line.Number EmptyDay $"day '{day.Id}' has no #ej rows"

        Ok day
      | EndLine kind ->
        errAt line $"#end {kind} does not match #start dia {day.Id}"
      | StartLine(kind, arg) ->
        warn
          line.Number
          UnknownBlockSkipped
          $"skipping unknown block '#start {kind} {arg}' inside dia"

        skipBalanced()
        parseDay day
      | MetaLine(key, value) ->
        if key = "titulo" then
          parseDay { day with Title = Some value }
        else
          warn line.Number UnknownMetaKey $"unknown meta key '{key}' inside dia"
          parseDay day
      | EjLine fields ->
        match fields with
        | [ circuit; laps; orderText; name; reps; rir; rest; notes ] ->
          let orderOk, order = Int32.TryParse orderText

          if name.Trim().Length = 0 || not orderOk then
            warn
              line.Number
              BadEjField
              "invalid #ej row (empty name or non-integer order)"

            parseDay day
          else
            let exercise = {
              Circuit = circuit.Trim()
              Laps = laps.Trim()
              Order = order
              Name = name.Trim()
              Reps = reps.Trim()
              Rir = rir.Trim()
              Rest = rest.Trim()
              Notes = notes.Trim()
            }

            parseDay {
              day with
                  Exercises = day.Exercises @ [ exercise ]
            }
        | _ ->
          warn line.Number BadEjField "invalid #ej row (wrong field count)"
          parseDay day
      | BadEj ->
        warn line.Number BadEjField "malformed #ej row"
        parseDay day
      | ExtraLine _ ->
        warn
          line.Number
          UnknownLine
          "extras (#x) are only allowed at plan level"

        parseDay day
      | Unknown ->
        warn line.Number UnknownLine "unrecognized line"
        parseDay day
      | Blank -> parseDay day

  let rec parseWeek(week: Week) : Result<Week, ParseError> =
    match advance() with
    | None -> Error(unterminated $"semana {week.Number}")
    | Some line ->
      match classify line.Text with
      | EndLine "semana" -> Ok week
      | EndLine kind ->
        errAt line $"#end {kind} does not match #start semana {week.Number}"
      | StartLine("dia", id) ->
        let day = {
          Id = id
          Title = None
          Notes = []
          Exercises = []
        }

        if week.Days |> List.exists(fun existing -> existing.Id = id) then
          warn
            line.Number
            DuplicateEntry
            $"duplicate day '{id}' in semana {week.Number}"

        if tryDayOfWeek id |> Option.isNone then
          warn line.Number UnknownDayName $"day name '{id}' is not recognized"

        parseDay day
        |> Result.map(fun parsed -> {
          week with
              Days = week.Days @ [ parsed ]
        })
        |> Result.bind parseWeek
      | StartLine(kind, arg) ->
        warn
          line.Number
          UnknownBlockSkipped
          $"skipping unknown block '#start {kind} {arg}' inside semana"

        skipBalanced()
        parseWeek week
      | MetaLine(key, _) ->
        warn
          line.Number
          UnknownMetaKey
          $"meta '{key}' not allowed inside semana"

        parseWeek week
      | EjLine _
      | BadEj ->
        warn line.Number UnknownLine "#ej row outside a dia"
        parseWeek week
      | ExtraLine _
      | Unknown ->
        warn line.Number UnknownLine "unrecognized line"
        parseWeek week
      | Blank -> parseWeek week

  let rec parseVariant(variant: Variant) : Result<Variant, ParseError> =
    match advance() with
    | None -> Error(unterminated $"opcion {variant.Id}")
    | Some line ->
      match classify line.Text with
      | EndLine "opcion" -> Ok variant
      | EndLine kind ->
        errAt line $"#end {kind} does not match #start opcion {variant.Id}"
      | StartLine("semana", numberText) ->
        match Int32.TryParse numberText with
        | true, number ->
          if
            variant.Weeks
            |> List.exists(fun existing -> existing.Number = number)
          then
            warn
              line.Number
              DuplicateEntry
              $"duplicate semana {number} in opcion {variant.Id}"

          parseWeek { Number = number; Days = [] }
          |> Result.map(fun parsed -> {
            variant with
                Weeks = variant.Weeks @ [ parsed ]
          })
          |> Result.bind parseVariant
        | _ ->
          errAt line $"#start semana requires an integer, got '{numberText}'"
      | StartLine(kind, arg) ->
        warn
          line.Number
          UnknownBlockSkipped
          $"skipping unknown block '#start {kind} {arg}' inside opcion"

        skipBalanced()
        parseVariant variant
      | MetaLine("titulo", value) ->
        parseVariant { variant with Title = Some value }
      | MetaLine(key, _) ->
        warn
          line.Number
          UnknownMetaKey
          $"unknown meta key '{key}' inside opcion"

        parseVariant variant
      | EjLine _
      | BadEj ->
        warn line.Number UnknownLine "#ej row outside a dia"
        parseVariant variant
      | ExtraLine _
      | Unknown ->
        warn line.Number UnknownLine "unrecognized line"
        parseVariant variant
      | Blank -> parseVariant variant

  let rec parseBlock(block: Block) : Result<Block, ParseError> =
    match advance() with
    | None -> Error(unterminated $"genero {Gender.toFileToken block.Gender}")
    | Some line ->
      match classify line.Text with
      | EndLine "genero" -> Ok block
      | EndLine kind ->
        errAt
          line
          $"#end {kind} does not match #start genero {Gender.toFileToken block.Gender}"
      | StartLine("opcion", id) ->
        if block.Variants |> List.exists(fun existing -> existing.Id = id) then
          warn line.Number DuplicateEntry $"duplicate opcion '{id}'"

        parseVariant { Id = id; Title = None; Weeks = [] }
        |> Result.map(fun parsed -> {
          block with
              Variants = block.Variants @ [ parsed ]
        })
        |> Result.bind parseBlock
      | StartLine(kind, arg) ->
        warn
          line.Number
          UnknownBlockSkipped
          $"skipping unknown block '#start {kind} {arg}' inside genero"

        skipBalanced()
        parseBlock block
      | MetaLine(key, _) ->
        warn
          line.Number
          UnknownMetaKey
          $"unknown meta key '{key}' inside genero"

        parseBlock block
      | EjLine _
      | BadEj ->
        warn line.Number UnknownLine "#ej row outside a dia"
        parseBlock block
      | ExtraLine _
      | Unknown ->
        warn line.Number UnknownLine "unrecognized line"
        parseBlock block
      | Blank -> parseBlock block

  let rec parsePlanBody
    (plan: Plan)
    : Result<Plan * ParseWarning list, ParseError> =
    match advance() with
    | None -> Error(unterminated "plan")
    | Some line ->
      match classify line.Text with
      | EndLine "plan" ->
        if plan.Title = "" then
          warn line.Number MissingMeta "missing #meta titulo"

        if plan.Weeks = 0 then
          warn line.Number MissingMeta "missing #meta semanas"

        Ok(plan, List.ofSeq warnings)
      | EndLine kind -> errAt line $"#end {kind} does not match #start plan"
      | StartLine("genero", name) ->
        (match Gender.tryFileToken name with
         | Some gender ->
           if
             plan.Blocks
             |> List.exists(fun existing -> existing.Gender = gender)
           then
             warn line.Number DuplicateEntry $"duplicate genero '{name}'"

           parseBlock { Gender = gender; Variants = [] }
           |> Result.map(fun parsed -> {
             plan with
                 Blocks = plan.Blocks @ [ parsed ]
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
      | MetaLine("titulo", value) -> parsePlanBody { plan with Title = value }
      | MetaLine("semanas", value) ->
        match Int32.TryParse value with
        | true, weeks -> parsePlanBody { plan with Weeks = weeks }
        | _ ->
          warn
            line.Number
            UnknownMetaKey
            $"#meta semanas is not an integer: '{value}'"

          parsePlanBody plan
      | MetaLine("generado", value) ->
        parsePlanBody { plan with Generated = value }
      | MetaLine("origen", value) -> parsePlanBody { plan with Origin = value }
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
            Title = ""
            Weeks = 0
            Generated = ""
            Origin = ""
            Blocks = []
            Extras = []
          }

          parsePlanBody plan
      | _ -> errAt line "expected '#start plan v1' as the first line"

  parseHeader()
  |> Result.map(fun (plan, allWarnings) -> {
    Plan = plan
    Warnings = allWarnings
  })
