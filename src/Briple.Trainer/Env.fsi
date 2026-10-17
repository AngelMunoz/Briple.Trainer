module Briple.Trainer.Env

// The environment: one value carrying the capabilities the views name.
// A capability is an accessor interface (IProvideX) over the worker it
// exposes; route factories declare only the capabilities they use through
// flexible types and tests build envs implementing just those.

open System
open System.Globalization
open System.Threading
open System.Threading.Tasks
open Avalonia.Controls
open Microsoft.Extensions.AI
open Microsoft.Extensions.Logging

open Briple.Trainer.Core.Agent
open Briple.Trainer.Core.Memories
open Briple.Trainer.Core.Requests
open Briple.Trainer.Persistence
open Briple.Trainer.Secrets
open Briple.Trainer.Stores
open Briple.Trainer.Strings

/// The provider settings. Every field but `Key` comes from
/// `defaultSettings` today: the user only supplies the token; other
/// sources may fill the rest later. `Language` and `Theme` are the
/// persisted UI preferences.
type AppSettings = {
  ProviderName: string
  Endpoint: Uri
  Model: string
  /// The token; the plain-field disk copy exists only for the insecure
  /// fallback, empty when the OS vault holds the token.
  Key: string
  /// The persisted UI language token ("es"/"en").
  Language: string
  /// The persisted theme token ("system"/"light"/"dark").
  Theme: string
}

/// The built-in provider: DeepSeek's Responses API over deepseek-flash,
/// the combination the spike measured for the interactive wizard.
val defaultSettings: AppSettings

/// The user's theme preference: the OS theme or a fixed variant.
[<Struct>]
type ThemeChoice =
  | System
  | Light
  | Dark

module ThemeChoice =
  /// Reads a settings token; anything unknown reads as System.
  val ofToken: text: string -> ThemeChoice

  /// Writes the settings token of a choice.
  val toToken: theme: ThemeChoice -> string

/// Applies a theme choice to the running application, when there is one.
val applyTheme: theme: ThemeChoice -> unit

/// What saving the token produced; the Insecure case carries the reason
/// the OS vault failed, phrased for the user.
type CredentialSave =
  | Secured
  | Insecure of reason: string

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

  /// Emits the culture on every language switch, starting with the
  /// current one. Pages rebuild their text from it.
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
  /// True when the working token came from the plain settings field, not
  /// the OS vault; the UI shows the insecure warning for this case.
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
type AgentEnv =
  interface IProvideChat
  interface IProvideLog

  new: client: IChatClient * log: ILogger -> AgentEnv

/// The production environment. The language, the theme and the host
/// window change after construction, hence the setters.
type AppEnv =
  interface IProvideStore
  interface IProvideStores
  interface IProvideLanguage
  interface IProvideCulture
  interface IProvideTheme
  interface IProvideAgent
  interface IProvideCredentials
  interface IProvideHost
  interface IProvideLogging

  new:
    stores: DataStores * vault: ISecureStorage * settings: AppSettings voption ->
      AppEnv

  member SetHost: window: Window -> unit

  member Settings: AppSettings voption

  /// The live data layer, reachable without a cast.
  member Stores: DataStores

  /// The Serilog bridge: typed `ILogger<T>` categories for views and
  /// services, over the logger `Program` configured.
  member LoggerFactory: ILoggerFactory

  /// The culture of the current language, reachable without a cast.
  member Culture: CultureInfo

  /// The culture switches, reachable without a cast.
  member CultureChanged: IObservable<CultureInfo>

  /// The language switches, reachable without a cast.
  member LanguageChanged: IObservable<Language>

/// Where settings.json (the insecure fallback) is expected.
val settingsPath: unit -> string

/// The one logging factory of the process: every service that accepts a
/// logger takes one made here, all bridged to the Serilog pipeline
/// Program configured.
val loggerFactory: ILoggerFactory

/// Reads settings.json; no file is the ValueNone case, a malformed file
/// is an Error naming the cause. Absent provider fields fall back to the
/// defaults, so a file holding only the key is enough.
val loadSettings: unit -> Result<AppSettings voption, exn>

/// Builds the production env: migrates the database and resolves the
/// token — OS vault first, plain field as the insecure fallback.
val create: dbPath: string -> Result<AppEnv, exn>
