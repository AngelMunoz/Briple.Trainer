module Briple.Trainer.Core.Agent

// the IChatClient adapter of the Responses API is experimental
#nowarn "57"

open System
open IcedTasks
open System.ClientModel
open System.ClientModel.Primitives
open System.Net.Http
open System.Text
open System.Text.Json
open System.Threading
open System.Threading.Tasks
open JDeck
open Microsoft.Extensions.AI
open OpenAI
open Briple.Trainer.Core.Memories
open Briple.Trainer.Core.Plan.Data
open Briple.Trainer.Core.Plan.Writer
open Briple.Trainer.Core.Plan.Types
open Briple.Trainer.Core.Prompts
open Briple.Trainer.Core.Requests
open Briple.Trainer.Core.Validator

/// Adds the provider headers to every request.
type HeadersHandler(headers: (string * string) list) =
  inherit DelegatingHandler(new SocketsHttpHandler())

  override this.SendAsync
    (request: HttpRequestMessage, cancellation: CancellationToken)
    : Task<HttpResponseMessage> =
    for name, value in headers do
      request.Headers.Add(name, value) |> ignore

    base.SendAsync(request, cancellation)

/// A BYOK OpenAI-compatible provider.
type Provider = {
  Name: string
  Endpoint: Uri
  Model: string
  UserAgent: string
  Headers: (string * string) list
}

/// One run of the generation flow: the raw model response, the plan
/// rendered to the format and the verdict.
type Run = {
  Response: string
  Output: string
  Verdict: Verdict
}

/// The provider's chat client with its key, user agent and headers
/// applied. An endpoint ending in /responses uses the Responses API
/// client with that base.
let createClient (key: string) (provider: Provider) : IChatClient =
  let uri = provider.Endpoint.AbsoluteUri.TrimEnd('/')

  let usesResponses = uri.EndsWith("/responses")

  let baseUri =
    if usesResponses then
      Uri(uri.Substring(0, uri.Length - "/responses".Length))
    else
      provider.Endpoint

  let options =
    OpenAIClientOptions(
      Endpoint = baseUri,
      Transport =
        new HttpClientPipelineTransport(
          new HttpClient(new HeadersHandler(provider.Headers))
        )
    )

  if provider.UserAgent <> "" then
    options.UserAgentApplicationId <- provider.UserAgent

  let client = OpenAIClient(ApiKeyCredential(key), options)

  if usesResponses then
    client.GetResponsesClient().AsIChatClient(provider.Model)
  else
    client.GetChatClient(provider.Model).AsIChatClient()

let ask (client: IChatClient) (system: string) (user: string) : Task<string> =
  let messages = [|
    ChatMessage(ChatRole.System, system)
    ChatMessage(ChatRole.User, user)
  |]

  // the stream travels cold with its cancellation token and returns the
  // accumulated text when it closes
  let transmit = cancellableTask {
    let accumulated = Text.StringBuilder()

    for update in client.GetStreamingResponseAsync messages do
      accumulated.Append update.Text |> ignore

    return accumulated.ToString()
  }

  transmit CancellationToken.None

// mesocycle lines are a request parameter: they are added
// deterministically, the model never writes them
let appendMesocycle (rirPerWeek: (int * string) list) (plan: Plan) =
  let lines =
    rirPerWeek
    |> List.map(fun (week, value) ->
      $"mesociclo | Semana {week}: objetivo | RIR {value}")

  {
    plan with
        Extras = plan.Extras @ lines
  }

let run
  (rirPerWeek: (int * string) list)
  (check: Plan -> Violation list)
  (response: string)
  : Run =
  match fromJson response with
  | Error dataFailures -> {
      Response = response
      Output = response
      Verdict =
        Rejected(
          dataFailures
          |> List.map(fun detail -> { Rule = "data"; Detail = detail })
        )
    }
  | Ok rawPlan ->
    let plan = appendMesocycle rirPerWeek rawPlan
    let violations = check plan

    let verdict =
      match violations with
      | [] -> Valid(plan, checkPrescription plan)
      | _ -> Rejected violations

    {
      Response = response
      Output = write plan
      Verdict = verdict
    }

/// Generates a new plan: asks for the data, converts it, checks it and
/// writes the format.
let generateNew (client: IChatClient) (request: NewPlanRequest) : Task<Run> =
  task {
    let! response =
      ask client (system request.Brief.Goal) (newPlanPrompt request)

    return
      run
        (derivedRirPerWeek request.Weeks)
        (checkParameters request.Athlete request.Weeks request.Brief)
        response
  }

/// Generates an adjustment: the same with the previous plan as reference.
let generateAdjustment
  (client: IChatClient)
  (request: AdjustmentRequest)
  : Task<Run> = task {
  let! response =
    ask client (system request.Brief.Goal) (adjustmentPrompt request)

  return
    run
      (derivedRirPerWeek request.Weeks)
      (checkParameters request.Athlete request.Weeks request.Brief)
      response
}

// --- Mark consolidation ------------------------------------------------------

// the model decorates numbers with sign and percentage (load +5%): the
// protocol already declares load as percentage, the adornment is dropped
let cleanNumber(text: string) = text.Trim().TrimStart('+').TrimEnd('%')

let deltaOf(text: string) : Delta option =
  let parts = text.Trim().Split(' ', 2)

  match parts.[0].ToLowerInvariant(), parts |> Array.tryItem 1 with
  | "variant", Some name -> Some(PatternVariant(name.Trim()))
  | kind, Some numberText ->
    match numberText |> cleanNumber |> Int32.TryParse with
    | true, number ->
      match kind with
      | "rir" -> Some(Delta.Rir number)
      | "reps" -> Some(Reps number)
      | "sets" -> Some(Sets number)
      | "load" -> Some(Load number)
      | _ -> None
    | _ -> None
  | _ -> None

let labelOf(text: string) =
  match text.Trim().ToLowerInvariant() with
  | "light" -> Some Light
  | "heavy" -> Some Heavy
  | "pain" -> Some Pain
  | "no-equipment" -> Some NoEquipment
  | _ -> None

let targetMatches (text: string) (target: Target) =
  let normalized = text.Trim().ToLowerInvariant()

  match target with
  | Exercise name ->
    normalized = name.ToLowerInvariant()
    || normalized.EndsWith(name.ToLowerInvariant())
  | Day id -> normalized.Contains(id.ToLowerInvariant())

let memoryOf (marks: FeedbackMark list) (line: string) =
  match line.Split('|') |> Array.map(fun part -> part.Trim()) with
  | [| targetText; labelText; summary; deltasText |] ->
    marks
    |> List.tryFind(fun mark ->
      targetMatches targetText mark.Target
      && labelOf labelText = Some mark.Label)
    |> Option.bind(fun mark ->
      let deltas =
        deltasText.Split(';') |> Array.choose deltaOf |> Array.toList

      if List.isEmpty deltas then
        None
      else
        Some {
          Summary = summary
          Deltas = deltas
          Origin = Mark mark
          Status = Proposed
          Scope = Plan
        })
  | _ -> None

/// The agent consolidates the marks into proposed memories. Lines that
/// do not follow the protocol are discarded.
let consolidateMarks
  (client: IChatClient)
  (marks: FeedbackMark list)
  : Task<Memory list> = task {
  let! response = ask client consolidationSystem (consolidationPrompt marks)

  return
    response.Replace("\r\n", "\n").Split('\n')
    |> Array.choose(fun line -> memoryOf marks line)
    |> Array.toList
}

// --- note interpretation ------------------------------------------------------

/// The closed target vocabulary of a plan: its exercise names and day
/// ids.
let vocabularyOfPlan(plan: Plan) : Target list = [
  for block in plan.Blocks do
    for variant in block.Variants do
      for week in variant.Weeks do
        for day in week.Days do
          Day day.Id

          for exercise in day.Exercises do
            Exercise exercise.Name
]

// the tolerant decode mirrors Plan.Data: optional fields, a {"data":...}
// wrapper unwrapped before reading

/// Why a part of a trainer's note did not become a memory: structured
/// data only, the UI maps each case to localized prose.
type Rejection =
  | UnknownLabel of token: string
  | UnknownTarget of text: string
  | OutsideProtocol of detail: string

/// The reading of one trainer's note: the memories that mapped to the
/// closed surface, and the parts that did not (with the reason).
type NoteInterpretation = {
  Memories: Memory list
  Rejections: Rejection list
}

type MemoryData = {
  target: string option
  label: string option
  scope: string option
  summary: string option
  deltas: string list option
}

let inline readString (key: string) (element: JsonElement) =
  element
  |> Optional.Property.get(key, Optional.string)
  |> Result.map Option.flatten

let memoryDecoder: Decoder<MemoryData> =
  fun element -> decode {
    let! target = readString "target" element
    let! label = readString "label" element
    let! scope = readString "scope" element
    let! summary = readString "summary" element

    let! deltaItems =
      element |> Optional.Property.list("deltas", Optional.string)

    let deltas = deltaItems |> Option.map(List.choose id)

    return {
      target = target
      label = label
      scope = scope
      summary = summary
      deltas = deltas
    }
  }

let decodeMemories(json: string) : Result<MemoryData list, string> =
  try
    use document = JsonDocument.Parse json

    let root = document.RootElement

    let plain element =
      element
      |> Optional.Property.list("memories", memoryDecoder)
      |> Result.map(fun items -> items |> Option.defaultValue [])
      |> Result.mapError(fun error -> error.message)

    match root.ValueKind with
    | JsonValueKind.Object ->
      match root.TryGetProperty("data") with
      | true, data when data.ValueKind = JsonValueKind.Object ->
        plain(data.Clone())
      | true, data when data.ValueKind = JsonValueKind.String ->
        match data.GetString() with
        | null -> plain root
        | text ->
          use inner = JsonDocument.Parse text

          plain(inner.RootElement.Clone())
      | _ -> plain root
    | _ -> plain root
  with ex ->
    Error $"unreadable json: {ex.Message}"

let inline scopeOf(text: string option) =
  match text with
  | Some "athlete" -> Athlete
  | _ -> Plan

let inline findTarget (targets: Target list) (text: string) =
  targets |> List.tryFind(fun target -> targetMatches text target)

/// The pure reading of an interpretation response.
let interpretationOf
  (targets: Target list, date: DateOnly, response: string)
  : NoteInterpretation =
  match decodeMemories response with
  | Error detail -> {
      Memories = []
      Rejections = [ OutsideProtocol detail ]
    }
  | Ok items ->
    let read(item: MemoryData) =
      let targetText =
        item.target |> Option.defaultValue "" |> fun t -> t.Trim()

      let labelText = item.label |> Option.defaultValue "" |> fun t -> t.Trim()

      match findTarget targets targetText, labelOf labelText with
      | Some target, Some label ->
        let deltas =
          item.deltas |> Option.defaultValue [] |> List.choose deltaOf

        let summary =
          item.summary
          |> Option.defaultValue ""
          |> fun text ->
              if text.Length > 200 then text.Substring(0, 200) else text

        Ok {
          Summary = summary
          Deltas = deltas
          Origin =
            Mark {
              Target = target
              Label = label
              Date = date
            }
          Status = Proposed
          Scope = scopeOf item.scope
        }
      | None, _ -> Error(UnknownTarget targetText)
      | _, None -> Error(UnknownLabel labelText)

    let results = items |> List.map read

    {
      // the surface cap: the first ten mapped memories survive
      Memories =
        results
        |> List.choose(function
          | Ok memory -> Some memory
          | Error _ -> None)
        |> List.truncate 10
      Rejections =
        results
        |> List.choose(function
          | Error rejection -> Some rejection
          | Ok _ -> None)
    }

/// The agent reads the trainer's free-text note and answers with the
/// memories it maps to the closed surface.
let interpretNote(client: IChatClient, targets: Target list, note: string) =
  task {
    let! response =
      ask client (interpretationSystem targets) (interpretationNote note)

    return
      interpretationOf(targets, DateOnly.FromDateTime(DateTime.Today), response)
  }
