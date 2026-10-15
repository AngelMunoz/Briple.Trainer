module Briple.Trainer.Core.Plan.Data

open System
open System.Text.Json
open System.Text.Json.Nodes
open JDeck
open Briple.Trainer.Core.Plan.Parser
open Briple.Trainer.Core.Plan.Types

// --- tolerantly decoded data -------------------------------------------------

type ExerciseData = {
  circuit: string option
  laps: string option
  order: int option
  name: string option
  reps: string option
  rir: string option
  rest: string option
  notes: string option
}

type DayData = {
  id: string option
  title: string option
  exercises: ExerciseData list option
}

type WeekData = {
  number: int option
  days: DayData list option
}

type VariantData = {
  id: string option
  title: string option
  weeks: WeekData list option
}

type PlanData = {
  title: string option
  weeks: int option
  generated: string option
  origin: string option
  gender: string option
  variants: VariantData list option
  extras: string list option
}

// --- decoders ------------------------------------------------------------------

// tolerant integer: number or numeric text, null when missing
let integer(element: JsonElement) : Result<int option, DecodeError> =
  match element.ValueKind with
  | JsonValueKind.Number ->
    match element.TryGetInt32() with
    | true, value -> Ok(Some value)
    | _ -> Error(DecodeError.ofError(element.Clone(), "not an integer"))
  | JsonValueKind.String ->
    match element.GetString() with
    | null -> Error(DecodeError.ofError(element.Clone(), "not an integer"))
    | numberText ->
      match Int32.TryParse numberText with
      | true, value -> Ok(Some value)
      | _ -> Error(DecodeError.ofError(element.Clone(), "not an integer"))
  | JsonValueKind.Null
  | JsonValueKind.Undefined -> Ok None
  | _ -> Error(DecodeError.ofError(element.Clone(), "not an integer"))

let readString (key: string) (element: JsonElement) =
  element
  |> Optional.Property.get(key, Optional.string)
  |> Result.map Option.flatten

let exerciseDecoder: Decoder<ExerciseData> =
  fun element -> decode {
    let! circuit = readString "circuit" element
    let! laps = readString "laps" element

    let! order =
      element
      |> Optional.Property.get("order", integer)
      |> Result.map Option.flatten

    let! name = readString "name" element
    let! reps = readString "reps" element
    let! rir = readString "rir" element
    let! rest = readString "rest" element
    let! notes = readString "notes" element

    return {
      circuit = circuit
      laps = laps
      order = order
      name = name
      reps = reps
      rir = rir
      rest = rest
      notes = notes
    }
  }

let dayDecoder: Decoder<DayData> =
  fun element -> decode {
    let! id = readString "id" element
    let! title = readString "title" element

    let! exercises =
      element |> Optional.Property.list("exercises", exerciseDecoder)

    return {
      id = id
      title = title
      exercises = exercises
    }
  }

let weekDecoder: Decoder<WeekData> =
  fun element -> decode {
    let! number =
      element
      |> Optional.Property.get("number", integer)
      |> Result.map Option.flatten

    let! days = element |> Optional.Property.list("days", dayDecoder)

    return { number = number; days = days }
  }

let variantDecoder: Decoder<VariantData> =
  fun element -> decode {
    let! id = readString "id" element
    let! title = readString "title" element

    let! weeks = element |> Optional.Property.list("weeks", weekDecoder)

    return {
      id = id
      title = title
      weeks = weeks
    }
  }

let extrasDecoder(element: JsonElement) =
  element
  |> Optional.Property.list("extras", Optional.string)
  |> Result.map(Option.map(List.choose id))

let planDecoder: Decoder<PlanData> =
  fun element -> decode {
    let! title = readString "title" element

    let! weeks =
      element
      |> Optional.Property.get("weeks", integer)
      |> Result.map Option.flatten

    let! generated = readString "generated" element
    let! origin = readString "origin" element
    let! gender = readString "gender" element

    let! variants =
      element |> Optional.Property.list("variants", variantDecoder)

    let! extras = extrasDecoder element

    return {
      title = title
      weeks = weeks
      generated = generated
      origin = origin
      gender = gender
      variants = variants
      extras = extras
    }
  }

// some models wrap the answer in {"data": ...} with an object or with
// text; both shapes unwrap before decoding
let decode(json: string) : Result<PlanData, string> =
  use document = JsonDocument.Parse(json)

  let root = document.RootElement

  let plain element =
    element |> planDecoder |> Result.mapError(fun error -> error.message)

  match root.ValueKind with
  | JsonValueKind.Object ->
    match root.TryGetProperty("data") with
    | true, data when data.ValueKind = JsonValueKind.Object ->
      plain(data.Clone())
    | true, data when data.ValueKind = JsonValueKind.String ->
      match data.GetString() with
      | null -> plain root
      | text ->
        use inner = JsonDocument.Parse(text)

        plain(inner.RootElement.Clone())
    | _ -> plain root
  | _ -> plain root

// --- validation and construction ---------------------------------------------

let orDash(value: string option) =
  value
  |> Option.bind(fun text ->
    if String.IsNullOrWhiteSpace text then
      None
    else
      Some(text.Trim()))
  |> Option.defaultValue "-"

let convert(data: PlanData) : Result<Plan, string list> =
  let failures = ResizeArray<string>()

  let gender =
    match data.gender |> Option.bind Gender.tryProtocol with
    | Some gender -> Some gender
    | None ->
      let received = Option.defaultValue "" data.gender

      failures.Add $"gender: '{received}' is neither male nor female"

      None

  let title =
    match orDash data.title with
    | "-" ->
      failures.Add "title: empty"

      ""
    | title -> title

  let weeks = Option.defaultValue 0 data.weeks

  if weeks <= 0 then
    failures.Add $"weeks: {weeks} is not positive"

  let variants =
    data.variants
    |> Option.defaultValue []
    |> List.mapi(fun variantIndex variant ->
      let variantPath = $"variants[{variantIndex}]"

      let id = orDash variant.id

      if id = "-" then
        failures.Add $"{variantPath}: empty id"

      let weeks =
        variant.weeks
        |> Option.defaultValue []
        |> List.mapi(fun weekIndex week ->
          let weekPath = $"{variantPath}.weeks[{weekIndex}]"

          let days =
            week.days
            |> Option.defaultValue []
            |> List.mapi(fun dayIndex day ->
              let dayPath = $"{weekPath}.days[{dayIndex}]"

              let dayId = orDash day.id

              if tryDayOfWeek dayId |> Option.isNone then
                failures.Add $"{dayPath}: day '{dayId}' not recognized"

              day.exercises
              |> Option.defaultValue []
              |> List.iteri(fun exerciseIndex exercise ->
                if
                  exercise.name |> Option.forall(String.IsNullOrWhiteSpace)
                then
                  failures.Add
                    $"{dayPath}.exercises[{exerciseIndex}]: empty name")

              let exercises =
                day.exercises
                |> Option.defaultValue []
                |> List.map(fun exercise -> {
                  Circuit = orDash exercise.circuit
                  Laps = orDash exercise.laps
                  Order = Option.defaultValue 0 exercise.order
                  Name = orDash exercise.name
                  Reps = orDash exercise.reps
                  Rir = orDash exercise.rir
                  Rest = orDash exercise.rest
                  Notes = orDash exercise.notes
                })

              {
                Id = dayId
                Title = day.title
                Notes = []
                Exercises = exercises
              })

          {
            Number = Option.defaultValue 0 week.number
            Days = days
          })

      {
        Id = (if id = "-" then "" else id)
        Title = variant.title
        Weeks = weeks
      })

  match gender with
  | Some gender when failures.Count = 0 ->
    Ok {
      Version = 1
      Title = title
      Weeks = weeks
      Generated = orDash data.generated
      Origin = orDash data.origin
      Blocks = [ { Gender = gender; Variants = variants } ]
      Extras =
        data.extras
        |> Option.defaultValue []
        |> List.map(fun extra -> extra.Trim())
    }
  | _ -> Error(List.ofSeq failures)

/// Converts the model's JSON into a domain Plan. Every invalid field
/// accumulates a failure with its path; the day must carry a recognized
/// name and the gender must be male or female.
let fromJson(json: string) : Result<Plan, string list> =
  try
    match decode json with
    | Error reason -> Error [ reason ]
    | Ok data -> convert data
  with ex ->
    Error [ $"unreadable json: {ex.Message}" ]

/// Serializes a Plan as the JSON the model receives as reference in the
/// adjustment flow.
let toJson(plan: Plan) : string =
  let block = List.head plan.Blocks

  // reflection serialization is disabled under Native AOT; the DOM writer
  // carries an optional field only when it exists, never as json null
  let field(name: string, value: string option) : (string * JsonNode) list =
    match value with
    | Some value -> [ name, Encode.string value ]
    | None -> []

  let exerciseEntity(exercise: Exercise) : JsonNode =
    Json.object [
      "circuit", Encode.string exercise.Circuit
      "laps", Encode.string exercise.Laps
      "order", Encode.int exercise.Order
      "name", Encode.string exercise.Name
      "reps", Encode.string exercise.Reps
      "rir", Encode.string exercise.Rir
      "rest", Encode.string exercise.Rest
      "notes", Encode.string exercise.Notes
    ]

  let dayEntity(day: Day) : JsonNode =
    Json.object [
      "id", Encode.string day.Id
      yield! field("title", day.Title)
      "exercises", Json.sequence(day.Exercises, exerciseEntity)
    ]

  let weekEntity(week: Week) : JsonNode =
    Json.object [
      "number", Encode.int week.Number
      "days", Json.sequence(week.Days, dayEntity)
    ]

  let variantEntity(variant: Variant) : JsonNode =
    Json.object [
      "id", Encode.string variant.Id
      yield! field("title", variant.Title)
      "weeks", Json.sequence(variant.Weeks, weekEntity)
    ]

  let data =
    Json.object [
      "title", Encode.string plan.Title
      "weeks", Encode.int plan.Weeks
      "generated", Encode.string plan.Generated
      "origin", Encode.string plan.Origin
      "gender", Encode.string(Gender.toProtocol block.Gender)
      "variants", Json.sequence(block.Variants, variantEntity)
      "extras", Json.sequence(plan.Extras, Encode.string)
    ]

  data.ToJsonString(JsonSerializerOptions(WriteIndented = true))
