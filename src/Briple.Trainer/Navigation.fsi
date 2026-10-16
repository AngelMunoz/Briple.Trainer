module Briple.Trainer.Navigation

// The shell's back stack over Navs (which keeps no history): the URL of
// every completed navigation lands in `Sync`, `Back` pops and answers
// where to navigate. URL-to-section mapping for the nav pane lives here.

open System

/// Pure back-stack operations over a most-recent-first URL list.
module History =

  /// Pushes unless the URL repeats the current top; depth caps at 100.
  val push: url: string -> stack: string list -> string list

  /// The URL a back navigation goes to.
  val back: stack: string list -> string option

  /// Drops the current entry.
  val pop: stack: string list -> string list

  /// Whether a back navigation is possible.
  val canBack: stack: string list -> bool

  /// The pane section a URL belongs to.
  val sectionOf: url: string -> string voption

/// The live back stack: the stack rides one behavior subject, the same
/// idiom the stores use.
type BackStack =
  new: unit -> BackStack

  /// Records a completed navigation.
  member Sync: url: string -> unit

  /// Pops the current entry and answers where to navigate.
  member Back: unit -> string option

  /// The URL of the most recent navigation.
  member Current: string option

  /// Whether a back navigation is possible, as a stream.
  member CanBack: IObservable<bool>

/// An ICommand over a plain action, for key bindings.
type ActionCommand =
  interface System.Windows.Input.ICommand

  new: [<InlineIfLambda>] action: (unit -> unit) -> ActionCommand
