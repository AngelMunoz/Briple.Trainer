module Briple.Trainer.Views.Generate

// The wizard page as one model: the hub section, the run state, the
// decided proposals and the brief draft. The two input sections are hub
// columns, each with its own action at the end; there is no step-back
// button and no second "Atras". The page tracks its derived phase.

open System
open System.Globalization
open System.Threading

open Avalonia.Controls

open Briple.Trainer.Components.Page
open Briple.Trainer.Core.Memories
open Briple.Trainer.Core.Plan.Types
open Briple.Trainer.Env
open Briple.Trainer.Navigation
open Briple.Trainer.Persistence
open Briple.Trainer.Stores

/// The services the wizard page needs, built once at the route.
[<Struct>]
type GenerateEnv = {
  Store: IStore
  Memories: MemoryStore
  Plans: PlanStore
  Agent: IProvideAgent
  Logger: Microsoft.Extensions.Logging.ILogger
  Dialogs: IDialogs
  Toasts: IToasts
  Culture: CultureInfo
  CultureChanged: IObservable<CultureInfo>
}

/// The data of the page.
[<Struct>]
type WizardPageProps = {
  /// The shell's back control, placed in the page header.
  Lead: (unit -> Control) voption
  Athlete: AthleteEntity
  Confirmed: Memory list
  Proposals: MemoryEntity list
  Previous: PlanEntity voption
  /// The exit gate: the page tracks its derived phase and listens for
  /// the shell's cancel command.
  Gate: PageGate
  /// The route lifetime: the page program stops when the route leaves.
  Lifetime: Navs.IDisposableBag
}

/// What the page reports outward.
type Outcome =
  | PlanFinished of Guid
  | WizardCancelled

/// The app writes the generation date; the model's value is a
/// placeholder. The format is ISO, independent of the culture.
val withGenerationDate: date: DateOnly -> plan: Plan -> Plan

/// The wizard page.
type WizardPage =
  inherit UserControl

  new:
    env: GenerateEnv *
    props: WizardPageProps *
    [<InlineIfLambda>] onOutcome: (Outcome -> unit) ->
      WizardPage
