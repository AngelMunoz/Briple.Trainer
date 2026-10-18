module Briple.Trainer.Core.Plan.Data

#nowarn "3535"

open System
open System.Text.Json
open System.Text.Json.Nodes
open FsToolkit.ErrorHandling
open JDeck
open Briple.Trainer.Core.Plan.Parser
open Briple.Trainer.Core.Plan.Types
open Briple.Trainer.Core.PromptText

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
//
// Every object decoder is a CollectErrorsDecoder composed with the
// validation CE: a bad field does not hide the others, every DecodeError
// comes back (the single-error readers lift into it directly).

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

/// An optional string field: absent, null and present all read.
let inline readString(key: string, element: JsonElement) =
  element
  |> Optional.Property.get(key, Optional.string)
  |> Result.map Option.flatten

let exerciseDecoder: CollectErrorsDecoder<ExerciseData> =
  fun element -> validation {
    let! circuit = readString("circuit", element)
    and! laps = readString("laps", element)

    and! order =
      element
      |> Optional.Property.get("order", integer)
      |> Result.map Option.flatten

    and! name = readString("name", element)
    and! reps = readString("reps", element)
    and! rir = readString("rir", element)
    and! rest = readString("rest", element)
    and! notes = readString("notes", element)

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

let dayDecoder: CollectErrorsDecoder<DayData> =
  fun element -> validation {
    let! id = readString("id", element)
    and! title = readString("title", element)

    and! exercises =
      element |> Optional.Property.list("exercises", exerciseDecoder)

    return {
      id = id
      title = title
      exercises = exercises
    }
  }

let weekDecoder: CollectErrorsDecoder<WeekData> =
  fun element -> validation {
    let! number =
      element
      |> Optional.Property.get("number", integer)
      |> Result.map Option.flatten

    and! days = element |> Optional.Property.list("days", dayDecoder)

    return { number = number; days = days }
  }

let variantDecoder: CollectErrorsDecoder<VariantData> =
  fun element -> validation {
    let! id = readString("id", element)
    and! title = readString("title", element)

    and! weeks = element |> Optional.Property.list("weeks", weekDecoder)

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

let planDecoder: CollectErrorsDecoder<PlanData> =
  fun element -> validation {
    let! title = readString("title", element)

    and! weeks =
      element
      |> Optional.Property.get("weeks", integer)
      |> Result.map Option.flatten

    and! generated = readString("generated", element)
    and! origin = readString("origin", element)
    and! gender = readString("gender", element)

    and! variants =
      element |> Optional.Property.list("variants", variantDecoder)

    and! extras = extrasDecoder element

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

/// The model's answer root: {"data": ...} wrappers (object or string)
/// unwrapped, the raw root otherwise. The element is cloned, so the
/// document it came from is closed.
let answerRoot(json: string) : Result<JsonElement, string> =
  use document = JsonDocument.Parse(json)

  let root = document.RootElement

  let plain(element: JsonElement) = Ok(element.Clone())

  match root.ValueKind with
  | JsonValueKind.Object ->
    match root.TryGetProperty("data") with
    | true, data when data.ValueKind = JsonValueKind.Object -> plain data
    | true, data when data.ValueKind = JsonValueKind.String ->
      match data.GetString() with
      | null -> plain root
      | text ->
        use inner = JsonDocument.Parse(text)

        Ok(inner.RootElement.Clone())
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

let convert(data: PlanData) : Result<Plan, DataFailure list> =
  let failures = ResizeArray<DataFailure>()

  // each level reviews its own fields (accumulating every failure) and
  // builds the domain value; a rejected build's defaults never survive
  let exerciseOf(path: string, exercise: ExerciseData) : Exercise =
    if exercise.name |> Option.forall(String.IsNullOrWhiteSpace) then
      failures.Add(DataFailure.EmptyExerciseName path)

    match exercise.order with
    | Some _ -> ()
    | None -> failures.Add(DataFailure.MissingOrder path)

    {
      Circuit = orDash exercise.circuit
      Laps = orDash exercise.laps
      Order = Option.defaultValue 0 exercise.order
      Name = orDash exercise.name
      Reps = orDash exercise.reps
      Rir = orDash exercise.rir
      Rest = orDash exercise.rest
      Notes = orDash exercise.notes
    }

  let dayOf(path: string, day: DayData) : Day =
    let dayId = orDash day.id

    if tryDayOfWeek dayId |> Option.isNone then
      failures.Add(DataFailure.UnknownDay(path, dayId))

    {
      Id = dayId
      Title = day.title
      Notes = []
      Exercises =
        day.exercises
        |> Option.defaultValue []
        |> List.mapi(fun index exercise ->
          exerciseOf($"{path}.exercises[{index}]", exercise))
    }

  let weekOf(path: string, week: WeekData) : Week =
    let number =
      match week.number with
      | Some number -> number
      | None ->
        failures.Add(DataFailure.MissingWeekNumber path)

        0

    {
      Number = number
      Days =
        week.days
        |> Option.defaultValue []
        |> List.mapi(fun index day -> dayOf($"{path}.days[{index}]", day))
    }

  let variantOf(path: string, variant: VariantData) : Variant =
    let id = orDash variant.id

    if id = "-" then
      failures.Add(DataFailure.EmptyVariantId path)

    {
      Id = (if id = "-" then "" else id)
      Title = variant.title
      Weeks =
        variant.weeks
        |> Option.defaultValue []
        |> List.mapi(fun index week -> weekOf($"{path}.weeks[{index}]", week))
    }

  let gender =
    match data.gender |> Option.bind Gender.tryProtocol with
    | Some gender -> Some gender
    | None ->
      failures.Add(DataFailure.BadGender(Option.defaultValue "" data.gender))

      None

  let title =
    match orDash data.title with
    | "-" ->
      failures.Add DataFailure.MissingTitle

      ""
    | title -> title

  let weeks = Option.defaultValue 0 data.weeks

  if weeks <= 0 then
    failures.Add(DataFailure.BadWeeks weeks)

  let variants =
    data.variants
    |> Option.defaultValue []
    |> List.mapi(fun index variant -> variantOf($"variants[{index}]", variant))

  match gender, List.ofSeq failures with
  | Some gender, [] ->
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
  | _, failures -> Error failures

/// Converts the model's JSON into a domain Plan. Every invalid field
/// accumulates a DataFailure; the day must carry a recognized name and
/// the gender must be male or female.
let fromJson<'e when IPlanDataError<'e>>(json: string) : Result<Plan, 'e> =
  try
    match answerRoot json with
    | Error reason ->
      Error('e.InvalidData [ DataFailure.UnreadableJson reason ])
    | Ok root ->
      match root |> planDecoder with
      | Error errors ->
        // the validation CE kept every bad field; each one becomes its
        // own failure
        Error(
          'e
            .InvalidData(
              errors
              |> List.map(fun error -> DataFailure.UnreadableJson error.message)
            )
        )
      | Ok data ->
        match convert data with
        | Ok plan -> Ok plan
        | Error failures -> Error('e.InvalidData failures)
  with :? JsonException as ex ->
    Error('e.InvalidData [ DataFailure.UnreadableJson ex.Message ])

/// Serializes the plan's block of the given gender as the JSON the model
/// receives as reference in the adjustment flow; None when the plan has
/// no block for it.
let toJson(gender: Gender, plan: Plan) : string option =
  // reflection serialization is disabled under Native AOT; the DOM writer
  // carries an optional field only when it exists, never as json null
  let field(name: string, value: string option) : (string * JsonNode) list =
    match value with
    | Some value -> [ name, Encode.string value ]
    | None -> []

  let exerciseEntity(exercise: Exercise) : JsonNode =
    Json.object [
      "circuit", Encode.string(line 8 exercise.Circuit)
      "laps", Encode.string(line 40 exercise.Laps)
      "order", Encode.int exercise.Order
      "name", Encode.string(line 120 exercise.Name)
      "reps", Encode.string(line 40 exercise.Reps)
      "rir", Encode.string(line 40 exercise.Rir)
      "rest", Encode.string(line 40 exercise.Rest)
      "notes", Encode.string(line 200 exercise.Notes)
    ]

  let dayEntity(day: Day) : JsonNode =
    Json.object [
      "id", Encode.string(line 40 day.Id)
      yield! field("title", day.Title |> Option.map(line 200))
      "exercises", Json.sequence(day.Exercises, exerciseEntity)
    ]

  let weekEntity(week: Week) : JsonNode =
    Json.object [
      "number", Encode.int week.Number
      "days", Json.sequence(week.Days, dayEntity)
    ]

  let variantEntity(variant: Variant) : JsonNode =
    Json.object [
      "id", Encode.string(line 40 variant.Id)
      yield! field("title", variant.Title |> Option.map(line 200))
      "weeks", Json.sequence(variant.Weeks, weekEntity)
    ]

  plan.Blocks
  |> List.tryFind(fun block -> block.Gender = gender)
  |> Option.map(fun block ->
    let data =
      Json.object [
        "title", Encode.string(line 200 plan.Title)
        "weeks", Encode.int plan.Weeks
        "generated", Encode.string(line 40 plan.Generated)
        "origin", Encode.string(line 500 plan.Origin)
        "gender", Encode.string(Gender.toProtocol block.Gender)
        "variants", Json.sequence(block.Variants, variantEntity)
        "extras",
        Json.sequence(
          plan.Extras |> List.map(Briple.Trainer.Core.PromptText.block 500),
          Encode.string
        )
      ]

    data.ToJsonString(JsonSerializerOptions(WriteIndented = true)))
