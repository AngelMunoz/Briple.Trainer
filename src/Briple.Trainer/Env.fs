module Briple.Trainer.Env

open System
open System.Globalization
open System.IO
open System.Text.Json
open System.Threading
open System.Threading.Tasks
open Avalonia.Controls
open Microsoft.Extensions.AI

open Briple.Trainer.Core.Agent
open Briple.Trainer.Core.Memories
open Briple.Trainer.Core.Requests
open Briple.Trainer.Persistence
open Briple.Trainer.Secrets
open Briple.Trainer.Strings

type AppSettings = {
  ProviderName: string
  Endpoint: Uri
  Model: string
  Key: string
}

let defaultSettings = {
  ProviderName = "deepseek"
  Endpoint = Uri "https://api.deepseek.com/responses"
  Model = "deepseek-flash"
  Key = ""
}

/// What saving the token produced; the Insecure case carries the reason
/// the OS vault failed, phrased for the user.
type CredentialSave =
  | Secured
  | Insecure of reason: string

  override this.ToString() =
    match this with
    | Secured -> "Secured"
    | Insecure reason -> $"Insecure: {reason}"

type IProvideStore =
  abstract Store: IStore

type IProvideLanguage =
  abstract Language: Language

type IProvideCulture =
  abstract Culture: CultureInfo

type IProvideAgent =
  abstract AgentAvailable: bool
  abstract GenerateNew: request: NewPlanRequest -> Task<Result<Run, exn>>

  abstract GenerateAdjustment:
    request: AdjustmentRequest -> Task<Result<Run, exn>>

  abstract ConsolidateMarks:
    marks: FeedbackMark list -> Task<Result<Memory list, exn>>

type IProvideCredentials =
  abstract TokenInsecure: bool

  abstract SaveCredentials: token: string -> Result<CredentialSave, string>

type IProvideHost =
  abstract Host: Window voption

let clientOf(key: string) : IChatClient =
  createClient key {
    Name = defaultSettings.ProviderName
    Endpoint = defaultSettings.Endpoint
    Model = defaultSettings.Model
    UserAgent = "Briple.Trainer"
    Headers = []
  }

let guarded(op: unit -> Task<'a>) : Task<Result<'a, exn>> = task {
  try
    let! result = op()
    return Ok result
  with e ->
    return Error e
}

let settingsPath() =
  Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
    "Briple.Trainer",
    "settings.json"
  )

// --- settings.json on disk: AOT-safe hand-written JSON; the key field is
// the insecure fallback, empty when the OS vault holds the token ---

let writeSettings(settings: AppSettings) : Result<unit, string> =
  try
    let path = settingsPath()

    let folder = Path.GetDirectoryName(path: string)

    (match folder with
     | null -> ()
     | folder -> Directory.CreateDirectory(folder) |> ignore)

    do
      use stream = File.Create(path)

      use writer =
        new Utf8JsonWriter(stream, JsonWriterOptions(Indented = true))

      writer.WriteStartObject()

      writer.WriteString("providerName", settings.ProviderName)

      writer.WriteString("endpoint", string settings.Endpoint)

      writer.WriteString("model", settings.Model)

      writer.WriteString("key", settings.Key)

      writer.WriteEndObject()

    Ok()
  with e ->
    Error $"settings.json: {e.Message}"

type AppEnv(store: IStore, vault: ISecureStorage, settings: AppSettings voption)
  =

  let mutable currentLanguage = Es
  let mutable host = ValueNone

  let mutable workingSettings =
    settings |> ValueOption.filter(fun s -> s.Key <> "")

  // the env is insecure only when the plain field carried the token
  let mutable tokenInsecure =
    settings |> ValueOption.exists(fun s -> s.Key <> "") && vault.Load().IsNone

  // callers check AgentAvailable first; reaching here without a token is
  // a broken invariant, not a reportable failure
  let privateClient() =
    match workingSettings with
    | ValueSome settings -> clientOf settings.Key
    | ValueNone -> invalidOp "the agent was requested without a token"

  let generate generateFn request =
    guarded(fun () -> task {
      let client = privateClient()
      return! generateFn client request
    })

  interface IProvideStore with
    member _.Store = store

  interface IProvideLanguage with
    member _.Language = currentLanguage

  interface IProvideCulture with
    member _.Culture = Language.toCulture currentLanguage

  interface IProvideHost with
    member _.Host = host

  interface IProvideAgent with
    member _.AgentAvailable = workingSettings.IsSome

    member _.GenerateNew(request: NewPlanRequest) = generate generateNew request

    member _.GenerateAdjustment(request: AdjustmentRequest) =
      generate generateAdjustment request

    member _.ConsolidateMarks(marks: FeedbackMark list) =
      guarded(fun () -> task {
        let client = privateClient()
        return! consolidateMarks client marks
      })

  interface IProvideCredentials with
    member _.TokenInsecure = tokenInsecure

    member _.SaveCredentials(token: string) : Result<CredentialSave, string> =
      let withToken = { defaultSettings with Key = token }

      match vault.Save token with
      | Ok() ->
        // the key field stays empty on disk: the vault owns the token
        match writeSettings { defaultSettings with Key = "" } with
        | Ok() ->
          workingSettings <- ValueSome withToken
          tokenInsecure <- false
          Ok Secured
        | Error reason -> Error reason
      | Error vaultReason ->
        // insecure fallback: the token survives in settings.json and the
        // UI shows why the vault failed
        match writeSettings withToken with
        | Ok() ->
          workingSettings <- ValueSome withToken
          tokenInsecure <- true
          Ok(Insecure vaultReason)
        | Error reason -> Error reason

  member _.SetLanguage(language: Language) =
    currentLanguage <- language

    let culture = Language.toCulture language

    CultureInfo.DefaultThreadCurrentUICulture <- culture
    Thread.CurrentThread.CurrentUICulture <- culture

  member _.SetHost(window: Window) = host <- ValueSome window

  member _.Settings = workingSettings

  /// The culture of the current language, reachable without a cast.
  member this.Culture = (this :> IProvideCulture).Culture

let loadSettings() : Result<AppSettings voption, exn> =
  try
    let path = settingsPath()

    if not(File.Exists path) then
      Ok ValueNone
    else
      use doc = JsonDocument.Parse(File.ReadAllText path)

      let read(name: string, fallback: string) : string =
        match doc.RootElement.TryGetProperty(name) with
        | true, element ->
          element.GetString() |> Option.ofObj |> Option.defaultValue fallback
        | false, _ -> fallback

      Ok(
        ValueSome {
          // absent provider fields read as the defaults, so a file with
          // only the key (or a future source) is enough
          ProviderName = read("providerName", defaultSettings.ProviderName)
          Endpoint = Uri(read("endpoint", string defaultSettings.Endpoint))
          Model = read("model", defaultSettings.Model)
          Key = read("key", "")
        }
      )
  with e ->
    Error e

let create(dbPath: string) : Result<AppEnv, exn> =
  migrate dbPath
  |> Result.bind(fun () ->
    loadSettings()
    |> Result.map(fun disk ->
      let vault = Briple.Trainer.Secrets.create()

      // OS vault first, plain field as the insecure fallback
      let settings =
        disk
        |> ValueOption.map _.Key
        |> ValueOption.defaultValue ""
        |> fun key ->
            let key =
              match vault.Load() with
              | Some token -> token
              | None -> key

            {
              (disk |> ValueOption.defaultValue defaultSettings) with
                  Key = key
            }

      AppEnv(sqliteStore dbPath, vault, ValueSome settings)))
