module Briple.Trainer.Env

open System
open System.Globalization
open System.IO
open System.Reactive.Subjects
open System.Text.Json
open System.Threading
open System.Threading.Tasks
open Avalonia
open Avalonia.Controls
open Avalonia.Styling
open IcedTasks
open IcedTasks.Polyfill.Task.Tasks
open Microsoft.Extensions.AI
open Microsoft.Extensions.Logging

open Serilog

open Briple.Trainer.Core.Agent
open Briple.Trainer.Core.Memories
open Briple.Trainer.Core.Requests
open Briple.Trainer.Persistence
open Briple.Trainer.Secrets
open Briple.Trainer.Stores
open Briple.Trainer.Strings

type AppSettings = {
  ProviderName: string
  Endpoint: Uri
  Model: string
  Key: string
  /// The persisted UI language token ("es"/"en").
  Language: string
  /// The persisted theme token ("system"/"light"/"dark").
  Theme: string
}

let defaultSettings = {
  ProviderName = "deepseek"
  Endpoint = Uri "https://api.deepseek.com/responses"
  Model = "deepseek-flash"
  Key = ""
  Language = "es"
  Theme = "system"
}

/// The user's theme preference: the OS theme or a fixed variant.
[<Struct>]
type ThemeChoice =
  | System
  | Light
  | Dark

module ThemeChoice =

  let ofToken(text: string) =
    match text with
    | "light" -> Light
    | "dark" -> Dark
    | _ -> System

  let toToken(theme: ThemeChoice) =
    match theme with
    | System -> "system"
    | Light -> "light"
    | Dark -> "dark"

/// Applies a theme choice to the running application, when there is one.
let applyTheme(theme: ThemeChoice) =
  match Application.Current with
  | null -> ()
  | app ->
    app.RequestedThemeVariant <-
      match theme with
      | Light -> ThemeVariant.Light
      | Dark -> ThemeVariant.Dark
      | System -> ThemeVariant.Default

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

/// The live data layer: one observable store per aggregate over the raw
/// storage.
type IProvideStores =
  abstract Stores: DataStores

type IProvideLanguage =
  abstract Language: Language

  /// Emits on every language switch; the shell rebuilds its chrome and
  /// the pages re-render through it.
  abstract LanguageChanged: IObservable<Language>

  abstract SetLanguage: language: Language -> unit

type IProvideTheme =
  abstract Theme: ThemeChoice

  abstract SetTheme: theme: ThemeChoice -> unit

type IProvideCulture =
  abstract Culture: CultureInfo

  abstract CultureChanged: IObservable<CultureInfo>

type IProvideAgent =
  abstract AgentAvailable: bool

  abstract GenerateNew:
    request: NewPlanRequest * ct: CancellationToken -> Task<Result<Run, exn>>

  abstract GenerateAdjustment:
    request: AdjustmentRequest * ct: CancellationToken -> Task<Result<Run, exn>>

  abstract ConsolidateMarks:
    marks: FeedbackMark list * ct: CancellationToken ->
      Task<Result<Memory list, exn>>

  /// Reads the trainer's note over the plan's closed vocabulary.
  abstract InterpretNote:
    ask: NoteAsk * ct: CancellationToken ->
      Task<Result<NoteInterpretation, exn>>

type IProvideCredentials =
  abstract TokenInsecure: bool

  abstract SaveCredentials: token: string -> Result<CredentialSave, string>

type IProvideHost =
  abstract Host: Window voption

/// The logging capability: one factory bridged to Serilog, so every
/// service that accepts a logger lands in the same sink and level.
type IProvideLogging =
  abstract LoggerFactory: ILoggerFactory

/// The Core agent capabilities: one provider client plus the app's
/// logger. One value per call; Core never sees the concrete app env.
/// (The log type is qualified: `open Serilog` shadows `ILogger` here.)
type AgentEnv(client: IChatClient, log: Microsoft.Extensions.Logging.ILogger) =
  interface IProvideChat with
    member _.Chat = client

  interface IProvideLog with
    member _.Log = log

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

/// The one logging factory of the process: every service that accepts a
/// logger takes one made here, all bridged to the Serilog pipeline
/// Program configured. It exists before the env so the boot steps
/// (Migrondi among them) log through the app too.
let loggerFactory = (new LoggerFactory()).AddSerilog Log.Logger

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

      writer.WriteString("language", settings.Language)

      writer.WriteString("theme", settings.Theme)

      writer.WriteEndObject()

    Ok()
  with e ->
    Error $"settings.json: {e.Message}"

type AppEnv
  (stores: DataStores, vault: ISecureStorage, settings: AppSettings voption) =

  // the disk shape as loaded: preference writes re-write it with the
  // provider fields and the key untouched
  let disk = settings |> ValueOption.defaultValue defaultSettings

  let bootLanguage = Language.ofToken disk.Language

  let mutable currentLanguage = bootLanguage

  let languageChanged = new BehaviorSubject<Language>(bootLanguage)

  let mutable currentTheme = ThemeChoice.ofToken disk.Theme

  let mutable host = ValueNone

  let mutable workingSettings =
    settings |> ValueOption.filter(fun s -> s.Key <> "")

  do
    let culture = Language.toCulture bootLanguage

    CultureInfo.DefaultThreadCurrentUICulture <- culture
    CultureInfo.DefaultThreadCurrentCulture <- culture
    Thread.CurrentThread.CurrentUICulture <- culture
    Thread.CurrentThread.CurrentCulture <- culture

  // a preference write keeps the provider fields and the key as loaded
  let persistPrefs(language: string, theme: string) =
    match
      writeSettings {
        disk with
            Language = language
            Theme = theme
      }
    with
    | Ok() -> ()
    | Error reason -> Log.Error("settings write failed: {Razon}", reason)

  // the env is insecure only when the plain field carried the token
  let mutable tokenInsecure =
    settings |> ValueOption.exists(fun s -> s.Key <> "") && vault.Load().IsNone

  // callers check AgentAvailable first; reaching here without a token is
  // a broken invariant, not a reportable failure
  let privateClient() =
    match workingSettings with
    | ValueSome settings -> clientOf settings.Key
    | ValueNone -> invalidOp "the agent was requested without a token"

  // the Core agent capabilities over one provider client and the app's
  // Serilog-bridged logger
  let agentEnv() =
    AgentEnv(
      privateClient(),
      loggerFactory.CreateLogger "Briple.Trainer.Core.Agent"
    )

  let generate generateFn ct request =
    guarded(fun () -> task {
      let env = agentEnv()
      return! generateFn (env, request) ct
    })

  interface IProvideStore with
    member _.Store = stores.Raw

  interface IProvideStores with
    member _.Stores = stores

  interface IProvideLanguage with
    member _.Language = currentLanguage

    member _.LanguageChanged = languageChanged :> IObservable<Language>

    member _.SetLanguage(language: Language) =
      currentLanguage <- language

      languageChanged.OnNext language

      let culture = Language.toCulture language

      CultureInfo.DefaultThreadCurrentUICulture <- culture
      CultureInfo.DefaultThreadCurrentCulture <- culture
      Thread.CurrentThread.CurrentUICulture <- culture
      Thread.CurrentThread.CurrentCulture <- culture

      persistPrefs(Language.toToken language, ThemeChoice.toToken currentTheme)

  interface IProvideTheme with
    member _.Theme = currentTheme

    member _.SetTheme(theme: ThemeChoice) =
      currentTheme <- theme

      persistPrefs(Language.toToken currentLanguage, ThemeChoice.toToken theme)

  interface IProvideCulture with
    member _.Culture = Language.toCulture currentLanguage

    member _.CultureChanged =
      languageChanged
      |> Observable.map(fun language -> Language.toCulture language)

  interface IProvideHost with
    member _.Host = host

  interface IProvideLogging with
    member _.LoggerFactory = loggerFactory

  interface IProvideAgent with
    member _.AgentAvailable = workingSettings.IsSome

    member _.GenerateNew(request: NewPlanRequest, ct: CancellationToken) =
      generate generateNew ct request

    member _.GenerateAdjustment
      (request: AdjustmentRequest, ct: CancellationToken)
      =
      generate generateAdjustment ct request

    member _.ConsolidateMarks(marks: FeedbackMark list, ct: CancellationToken) =
      guarded(fun () -> task {
        let env = agentEnv()
        return! consolidateMarks (env, marks) ct
      })

    member _.InterpretNote(ask: NoteAsk, ct: CancellationToken) =
      guarded(fun () -> task {
        let env = agentEnv()
        return! interpretNote (env, ask) ct
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

  member _.SetHost(window: Window) = host <- ValueSome window

  member _.Settings = workingSettings

  /// The live data layer, reachable without a cast.
  member _.Stores = stores

  /// The Serilog bridge for typed `ILogger<T>` categories.
  member _.LoggerFactory = loggerFactory

  /// The culture of the current language, reachable without a cast.
  member this.Culture = (this :> IProvideCulture).Culture

  /// The culture switches, reachable without a cast.
  member _.CultureChanged = languageChanged |> Observable.map Language.toCulture

  /// The language switches, reachable without a cast.
  member _.LanguageChanged = languageChanged :> IObservable<Language>

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
          Language = read("language", defaultSettings.Language)
          Theme = read("theme", defaultSettings.Theme)
        }
      )
  with e ->
    Error e

let create(dbPath: string) : Result<AppEnv, exn> =
  migrate(loggerFactory.CreateLogger "Migrondi", dbPath)
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

      let stores =
        createStores(
          sqliteStore dbPath,
          fun (context, e) -> Log.Error(e, "{Contexto}", context)
        )

      AppEnv(stores, vault, ValueSome settings)))
