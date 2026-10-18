module Briple.Trainer.Views.Generate

// The wizard page as one model: the hub section, the run state, the
// decided proposals and the brief draft. The two input sections are hub
// columns, each with its own action at the end; there is no step-back
// button and no second "Atras". The page tracks its derived phase.

open System
open System.Threading

open Avalonia.Controls

open Briple.Trainer.Components.Page
open Briple.Trainer.Core.Memories
open Briple.Trainer.Env
open Briple.Trainer.Navigation
open Briple.Trainer.Persistence
open Briple.Trainer.Stores

/// The data of the page.
[<Struct>]
type WizardPageProps = {
  Culture: PageCulture
  /// The shell's back control, placed in the page header.
  Lead: (unit -> Control) voption
  /// The raw storage: the audit line only.
  Store: IStore
  Memories: MemoryStore
  Plans: PlanStore
  Agent: IProvideAgent
  Athlete: AthleteEntity
  Confirmed: Memory list
  Proposals: MemoryEntity list
  Previous: PlanEntity voption
  /// The exit gate: the page tracks its derived phase and listens for
  /// the shell's cancel command.
  Gate: PageGate
}

/// What the page reports outward.
type Outcome =
  | PlanFinished of Guid
  | WizardCancelled

/// The wizard page.
type WizardPage =
  inherit UserControl

  new:
    props: WizardPageProps * [<InlineIfLambda>] onOutcome: (Outcome -> unit) ->
      WizardPage
