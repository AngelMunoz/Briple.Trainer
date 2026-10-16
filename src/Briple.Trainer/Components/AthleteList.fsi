module Briple.Trainer.Components.AthleteList

// The athletes page: the registered athletes and the entry to the
// registration form. The list is plain data in; clicks go out.

open System
open System.Globalization

open Avalonia.Controls

open Briple.Trainer.Persistence

/// The data and callbacks of the page.
[<Struct>]
type AthleteListProps = {
  Culture: CultureInfo
  Athletes: AthleteEntity list
  OnNew: unit -> unit
  OnOpen: Guid -> unit
}

/// The athletes page: one row per athlete, plus the new-athlete entry.
val view: props: AthleteListProps -> Control
