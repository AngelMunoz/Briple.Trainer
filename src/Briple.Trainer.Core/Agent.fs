module Briple.Trainer.Core.Agent

// the IChatClient adapter of the Responses API is experimental
#nowarn "57"

open System
open IcedTasks
open System.Diagnostics
open System.ClientModel
open System.ClientModel.Primitives
open System.Net.Http
open System.Text
open System.Text.Json
open System.Threading
open System.Threading.Tasks
open FsToolkit.ErrorHandling
open JDeck
open Microsoft.Extensions.AI
open Microsoft.Extensions.Logging
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

/// Capability: the provider's chat client.
type IProvideChat =
  abstract Chat: IChatClient

/// Capability: the boundary log. The app injects its own provider;
/// Core never creates a logger.
type IProvideLog =
  abstract Log: ILogger

/// What a note reading needs: the closed vocabulary, the note verbatim,
/// and the day the fact was observed. The date travels here; Core never
/// reads the clock.
type NoteAsk = {
  Targets: Target list
  Note: string
  Date: DateOnly
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

// the ambient token of the cancellableTask reaches the provider stream;
// cancelling surfaces as OperationCanceledException, nothing swallows it
let ask
  (env: #IProvideChat & #IProvideLog, system: string, user: string)
  : CancellableTask<string> = cancellableTask {
  let messages = [|
    ChatMessage(ChatRole.System, system)
    ChatMessage(ChatRole.User, user)
  |]

  let! ct = CancellableTask.getCancellationToken()
  let watch = Stopwatch.StartNew()
  let accumulated = Text.StringBuilder()

  for update in
    env.Chat.GetStreamingResponseAsync(messages, cancellationToken = ct) do
    accumulated.Append update.Text |> ignore

  let text = accumulated.ToString()

  env.Log.LogInformation(
    "ask done {Chars} chars in {Ms} ms",
    text.Length,
    watch.ElapsedMilliseconds
  )

  return text
}

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
  match fromJson response: Result<_, Violation> with
  | Error failure -> {
      Response = response
      Output = response
      Verdict = Rejected [ failure ]
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
let generateNew
  (env: #IProvideChat & #IProvideLog, request: NewPlanRequest)
  : CancellableTask<Run> = cancellableTask {
  env.Log.LogInformation(
    "generateNew {Weeks} weeks for {Athlete}",
    request.Weeks,
    request.Athlete.Name
  )

  let! response = ask(env, system request.Brief.Goal, newPlanPrompt request)

  let result =
    run
      (derivedRirPerWeek request.Weeks)
      (checkParameters request.Athlete request.Weeks request.Brief)
      response

  match result.Verdict with
  | Valid _ -> env.Log.LogInformation("generateNew valid")
  | Rejected violations ->
    env.Log.LogWarning(
      "generateNew rejected: {Count} violations",
      violations.Length
    )

  return result
}

/// Generates an adjustment: the same with the previous plan as reference.
let generateAdjustment
  (env: #IProvideChat & #IProvideLog, request: AdjustmentRequest)
  : CancellableTask<Run> = cancellableTask {
  env.Log.LogInformation(
    "generateAdjustment {Weeks} weeks for {Athlete}",
    request.Weeks,
    request.Athlete.Name
  )

  let! response = ask(env, system request.Brief.Goal, adjustmentPrompt request)

  let result =
    run
      (derivedRirPerWeek request.Weeks)
      (checkParameters request.Athlete request.Weeks request.Brief)
      response

  match result.Verdict with
  | Valid _ -> env.Log.LogInformation("generateAdjustment valid")
  | Rejected violations ->
    env.Log.LogWarning(
      "generateAdjustment rejected: {Count} violations",
      violations.Length
    )

  return result
}

// --- Mark consolidation ------------------------------------------------------

// the model decorates numbers with sign and percentage (load +5%): the
// protocol already declares load as percentage, the adornment is dropped
let inline cleanNumber(text: string) = text.Trim().TrimStart('+').TrimEnd('%')

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
  (env: #IProvideChat & #IProvideLog, marks: FeedbackMark list)
  : CancellableTask<Memory list> = cancellableTask {
  let! response = ask(env, consolidationSystem, consolidationPrompt marks)

  return
    response.Replace("\r\n", "\n").Split('\n')
    |> Array.choose(fun line -> memoryOf marks line)
    |> Array.toList
}

// --- note interpretation ------------------------------------------------------

/// The closed target vocabulary of a plan: its exercise names and day
/// ids.
let vocabularyOfPlan(plan: Plan) : Target list = [
  for day in Tree.daysOf plan do
    Day day.Id

    for exercise in day.Exercises do
      Exercise exercise.Name
]

// the tolerant decode shares Plan.Data's answer unwrapping (answerRoot
// and readString): optional fields, a {"data":...} wrapper unwrapped
// before reading

/// Why a part of a trainer's note did not become a memory: structured
/// data only, the UI maps each case to localized prose.
type Rejection =
  | UnknownLabel of token: string
  | UnknownTarget of text: string
  | MissingField of field: string
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

let memoryDecoder: CollectErrorsDecoder<MemoryData> =
  fun element -> validation {
    let! target = readString("target", element)
    and! label = readString("label", element)
    and! scope = readString("scope", element)
    and! summary = readString("summary", element)

    and! deltaItems =
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

// only the expected exception is caught, and its message is the
// external string the rejection carries; the validation CE kept every
// bad field, so the joined detail names them all
let decodeMemories(json: string) : Result<MemoryData list, string> =
  try
    match answerRoot json with
    | Error reason -> Error reason
    | Ok root ->
      root
      |> Optional.Property.list("memories", memoryDecoder)
      |> Result.map(fun items -> items |> Option.defaultValue [])
      |> Result.mapError(fun errors ->
        errors |> List.map(fun error -> error.message) |> String.concat "; ")
  with :? JsonException as ex ->
    Error ex.Message

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
      // an absent field is absent; only a present-but-unknown one is
      // unknown
      match item.target, item.label with
      | None, _ -> Error(MissingField "target")
      | _, None -> Error(MissingField "label")
      | Some targetRaw, Some labelRaw ->
        let targetText = targetRaw.Trim()
        let labelText = labelRaw.Trim()

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
let interpretNote
  ((env: #IProvideChat & #IProvideLog), reading: NoteAsk)
  : CancellableTask<NoteInterpretation> = cancellableTask {
  let! response =
    ask(
      env,
      interpretationSystem reading.Targets,
      interpretationNote reading.Note
    )

  return interpretationOf(reading.Targets, reading.Date, response)
}
