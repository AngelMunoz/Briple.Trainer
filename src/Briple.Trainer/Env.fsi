module Briple.Trainer.Env

// The environment: one value carrying the capabilities the views name.
// A capability is an accessor interface (IProvideX) over the worker it
// exposes; route factories declare only the capabilities they use through
// flexible types and tests build envs implementing just those.

open System
open System.Globalization
open System.Threading.Tasks
open Avalonia.Controls
open Microsoft.Extensions.AI

open Briple.Trainer.Core.Agent
open Briple.Trainer.Core.Memories
open Briple.Trainer.Core.Requests
open Briple.Trainer.Persistence
open Briple.Trainer.Secrets
open Briple.Trainer.Strings

/// The provider settings. Every field but `Key` comes from
/// `defaultSettings` today: the user only supplies the token; other
/// sources may fill the rest later.
type AppSettings = {
  ProviderName: string
  Endpoint: Uri
  Model: string
  /// The token; the plain-field disk copy exists only for the insecure
  /// fallback, empty when the OS vault holds it.
  Key: string
}

/// The built-in provider: DeepSeek's Responses API over deepseek-flash,
/// the combination the spike measured for the interactive wizard.
val defaultSettings: AppSettings

/// What saving the token produced; the Insecure case carries the reason
/// the OS vault failed, phrased for the user.
type CredentialSave =
  | Secured
  | Insecure of reason: string

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
  /// True when the working token came from the plain settings field, not
  /// the OS vault; the UI shows the insecure warning for this case.
  abstract TokenInsecure: bool

  abstract SaveCredentials: token: string -> Result<CredentialSave, string>

type IProvideHost =
  abstract Host: Window voption

/// The production environment. The language and the host window change
/// after construction (the toggle and the shell), hence the setters.
type AppEnv =
  interface IProvideStore
  interface IProvideLanguage
  interface IProvideCulture
  interface IProvideAgent
  interface IProvideCredentials
  interface IProvideHost

  new:
    store: IStore * vault: ISecureStorage * settings: AppSettings voption ->
      AppEnv

  /// Switches the language and sets the UI cultures for threads that
  /// queue work afterwards.
  member SetLanguage: language: Language -> unit

  member SetHost: window: Window -> unit

  member Settings: AppSettings voption

  /// The culture of the current language, reachable without a cast.
  member Culture: CultureInfo

/// Where settings.json (the insecure fallback) is expected.
val settingsPath: unit -> string

/// Reads settings.json; no file is the ValueNone case, a malformed file
/// is an Error naming the cause. Absent provider fields fall back to the
/// defaults, so a file holding only the key is enough.
val loadSettings: unit -> Result<AppSettings voption, exn>

/// Builds the production env: migrates the database and resolves the
/// token — OS vault first, plain field as the insecure fallback.
val create: dbPath: string -> Result<AppEnv, exn>
