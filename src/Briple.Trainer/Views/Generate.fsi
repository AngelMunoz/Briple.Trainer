module Briple.Trainer.Views.Generate

// The wizard page: memories, brief and generation in one place. The page
// owns the step state; the memory cards and the brief form are the MVUish
// components. The agent call runs in the background and its outcome lands
// back on the UI thread.

open System
open System.Globalization

open Avalonia.Controls

open Briple.Trainer.Env
open Briple.Trainer.Persistence

/// The data and callbacks of the page.
[<Struct>]
type WizardPageProps = {
  Culture: CultureInfo
  Store: IStore
  Agent: IProvideAgent
  Athlete: AthleteEntity
  Proposals: MemoryEntity list
  Previous: PlanEntity voption
  OnFinished: Guid -> unit
  OnCancelled: unit -> unit
}

/// The wizard page.
type WizardPage =
  inherit UserControl

  new: props: WizardPageProps -> WizardPage
