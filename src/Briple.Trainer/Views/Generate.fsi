module Briple.Trainer.Views.Generate

// The wizard page: memories, brief and generation in one place. The page
// owns the step state; the memory cards and the brief form are the MVUish
// components. The agent call runs in the background and its outcome lands
// back on the UI thread.

open System
open System.Globalization

open Avalonia.Controls

open Briple.Trainer.Core.Memories
open Briple.Trainer.Env
open Briple.Trainer.Persistence
open Briple.Trainer.Stores

/// The data of the page.
[<Struct>]
type WizardPageProps = {
  Culture: CultureInfo
  /// The raw storage: the audit line only.
  Store: IStore
  Memories: MemoryStore
  Plans: PlanStore
  Agent: IProvideAgent
  Athlete: AthleteEntity
  /// The confirmed memories relevant to this generation: athlete-scoped
  /// ones plus the plan-scoped ones of the plan being adjusted.
  Confirmed: Memory list
  Proposals: MemoryEntity list
  Previous: PlanEntity voption
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
