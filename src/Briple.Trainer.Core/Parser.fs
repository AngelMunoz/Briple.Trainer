module Briple.Trainer.Core.Plan.Parser

// Parser for the line-based training plan format (#start/#end blocks, #meta,
// #ej rows, #x extras). Line bodies go through XParsec; block nesting is
// assembled by recursive descent so every diagnostic carries a line number.
//
// Recoverable problems (unknown blocks, unrecognized day names, malformed
// #ej rows, unknown meta keys, duplicates, empty days, wrong versions) come
// back as warnings in ParsedPlan. Structural breakage (#end mismatches,
// unterminated blocks, a missing #start plan header) is the leaf's IWSAM
// error: the caller's edge type implements IPlanFormatError, so failures
// compose without mapError and the edge matches exhaustively.

#nowarn "3535"

open System
open XParsec
open XParsec.Combinators
open XParsec.Parsers
open XParsec.CharParsers
open XParsec.ErrorFormatting
open Briple.Trainer.Core.Plan.Types

// The IWSAM leaf interface (IPlanFormatError) and the display edge
// (PlanFailure) live in Plan.Types: that file has no signature file, so
// it can host the static-abstract implementations.

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

let parsePlan<'e when IPlanFormatError<'e>>
  (input: string)
  : Result<ParsedPlan, 'e> =
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

  let unterminated(block: string) : Result<'a, 'e> =
    Error('e.Unterminated(lastNumber, block))

  // Consumes lines until the unknown block's matching #end; the caller has
  // already consumed the #start line. Unterminated unknown blocks stop at
  // EOF and the enclosing block reports the structural error.
  let skipBalanced() =
    let rec loop depth =
      if depth <= 0 then
        ()
      else
        match advance() with
        | None -> ()
        | Some line ->
          match classify line.Text with
          | StartLine _ -> loop(depth + 1)
          | EndLine _ -> loop(depth - 1)
          | _ -> loop depth

    loop 1

  // The block assembler is a linear pipeline: each level only recurses into
  // itself or calls the level below it (day <- week <- variant <- block
  // <- planBody <- header), so no mutual recursion is needed. The inner
  // functions inherit 'e from parsePlan.
  let rec parseDay(day: Day) : Result<Day, 'e> =
    match advance() with
    | None -> unterminated $"dia {day.Id}"
    | Some line ->
      match classify line.Text with
      | EndLine "dia" ->
        // the rows accumulated in reverse; the file order returns here
        let day = {
          day with
              Exercises = List.rev day.Exercises
        }

        if List.isEmpty day.Exercises then
          warn line.Number EmptyDay $"day '{day.Id}' has no #ej rows"

        Ok day
      | EndLine kind ->
        Error('e.MismatchedEnd(line.Number, kind, $"dia {day.Id}"))
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
          match Int32.TryParse orderText with
          | true, order when not(String.IsNullOrWhiteSpace name) ->
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
                  Exercises = exercise :: day.Exercises
            }
          | _ ->
            warn
              line.Number
              BadEjField
              $"invalid #ej row (empty name or non-integer order): '{line.Text.Trim()}'"

            parseDay day
        | _ ->
          warn
            line.Number
            BadEjField
            $"invalid #ej row (wrong field count): '{line.Text.Trim()}'"

          parseDay day
      | BadEj ->
        warn line.Number BadEjField $"malformed #ej row: '{line.Text.Trim()}'"

        parseDay day
      | ExtraLine _ ->
        warn
          line.Number
          UnknownLine
          "extras (#x) are only allowed at plan level"

        parseDay day
      | Unknown ->
        warn line.Number UnknownLine $"unrecognized line: '{line.Text.Trim()}'"

        parseDay day
      | Blank -> parseDay day

  let rec parseWeek(week: Week) : Result<Week, 'e> =
    match advance() with
    | None -> unterminated $"semana {week.Number}"
    | Some line ->
      match classify line.Text with
      | EndLine "semana" -> Ok { week with Days = List.rev week.Days }
      | EndLine kind ->
        Error('e.MismatchedEnd(line.Number, kind, $"semana {week.Number}"))
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
        |> Result.map(fun parsed -> { week with Days = parsed :: week.Days })
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
        warn line.Number UnknownLine $"unrecognized line: '{line.Text.Trim()}'"

        parseWeek week
      | Blank -> parseWeek week

  let rec parseVariant(variant: Variant) : Result<Variant, 'e> =
    match advance() with
    | None -> unterminated $"opcion {variant.Id}"
    | Some line ->
      match classify line.Text with
      | EndLine "opcion" ->
        Ok {
          variant with
              Weeks = List.rev variant.Weeks
        }
      | EndLine kind ->
        Error('e.MismatchedEnd(line.Number, kind, $"opcion {variant.Id}"))
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
                Weeks = parsed :: variant.Weeks
          })
          |> Result.bind parseVariant
        | _ -> Error('e.BadWeekNumber(line.Number, numberText))
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
        warn line.Number UnknownLine $"unrecognized line: '{line.Text.Trim()}'"

        parseVariant variant
      | Blank -> parseVariant variant

  let rec parseBlock(block: Block) : Result<Block, 'e> =
    match advance() with
    | None -> unterminated $"genero {Gender.toFileToken block.Gender}"
    | Some line ->
      match classify line.Text with
      | EndLine "genero" ->
        Ok {
          block with
              Variants = List.rev block.Variants
        }
      | EndLine kind ->
        Error(
          'e
            .MismatchedEnd(
              line.Number,
              kind,
              $"genero {Gender.toFileToken block.Gender}"
            )
        )
      | StartLine("opcion", id) ->
        if block.Variants |> List.exists(fun existing -> existing.Id = id) then
          warn line.Number DuplicateEntry $"duplicate opcion '{id}'"

        parseVariant { Id = id; Title = None; Weeks = [] }
        |> Result.map(fun parsed -> {
          block with
              Variants = parsed :: block.Variants
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
        warn line.Number UnknownLine $"unrecognized line: '{line.Text.Trim()}'"

        parseBlock block
      | Blank -> parseBlock block

  let rec parsePlanBody(plan: Plan) : Result<Plan * ParseWarning list, 'e> =
    match advance() with
    | None -> unterminated "plan"
    | Some line ->
      match classify line.Text with
      | EndLine "plan" ->
        // blocks and extras accumulated in reverse; the file order
        // returns here
        let plan = {
          plan with
              Blocks = List.rev plan.Blocks
              Extras = List.rev plan.Extras
        }

        if plan.Title = "" then
          warn line.Number MissingMeta "missing #meta titulo"

        if plan.Weeks = 0 then
          warn line.Number MissingMeta "missing #meta semanas"

        Ok(plan, List.ofSeq warnings)
      | EndLine kind -> Error('e.MismatchedEnd(line.Number, kind, "plan"))
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
                 Blocks = parsed :: plan.Blocks
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
              Extras = text :: plan.Extras
        }
      | EjLine _
      | BadEj ->
        warn line.Number UnknownLine "#ej row outside a dia"
        parsePlanBody plan
      | Unknown ->
        warn line.Number UnknownLine $"unrecognized line: '{line.Text.Trim()}'"

        parsePlanBody plan
      | Blank -> parsePlanBody plan

  let rec parseHeader() : Result<Plan * ParseWarning list, 'e> =
    match advance() with
    | None -> unterminated "plan"
    | Some line ->
      match classify line.Text with
      | Blank -> parseHeader()
      | StartLine("plan", versionText) ->
        let version =
          if versionText.StartsWith("v") then
            match Int32.TryParse(versionText.Substring(1)) with
            | true, v -> Some v
            | _ -> None
          else
            None

        match version with
        | None -> Error('e.BadVersion(line.Number, versionText))
        | Some version ->
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
      | _ -> Error('e.MissingStart(line.Number))

  parseHeader()
  |> Result.map(fun (plan, allWarnings) -> {
    Plan = plan
    Warnings = allWarnings
  })

/// The fixed display edge of the parse leaf.
module Parse =

  /// Parses with `PlanFailure`; the annotation is what fixes `'e`.
  let parse(input: string) : Result<ParsedPlan, PlanFailure> = parsePlan input

  /// One diagnostic line per failure. The file format is the domain, so
  /// the description is technical and single-language, like the parse
  /// warnings themselves.
  let describe(failure: PlanFailure) : string =
    match failure with
    | Unterminated(lastLine, block) ->
      $"line {lastLine}: unterminated #start {block}"
    | MismatchedEnd(line, found, expected) ->
      $"line {line}: #end {found} does not match #start {expected}"
    | MissingStart line -> $"line {line}: expected '#start plan v1' first"
    | BadVersion(line, raw) -> $"line {line}: invalid plan version '{raw}'"
    | BadWeekNumber(line, raw) ->
      $"line {line}: #start semana requires an integer, got '{raw}'"
