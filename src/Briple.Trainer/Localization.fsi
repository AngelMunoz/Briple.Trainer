module Briple.Trainer.Localization

// The app's one localization resource dictionary: every resx entry under
// a culture, installed into Application.Resources. Static text binds
// through the resource observable, the code form of {DynamicResource};
// a language switch swaps the dictionary and Avalonia updates the bound
// controls. Composed text (goal lists, plan lines, dates) still needs a
// culture-aware binding of its own.

open System
open System.Globalization

open Avalonia.Controls

/// Replaces the language dictionary with the entries of a culture.
val install: culture: CultureInfo -> unit

/// The resource text for a key, as an observable, for code bindings.
val text: key: string -> IObservable<string>

/// The resource text for a key on a given host; tests use a local host
/// instead of the app.
val textFor: host: IResourceHost * key: string -> IObservable<string>

/// A TextBlock whose text follows the app resource key.
val inline textBlock: key: string -> TextBlock

/// A TextBlock whose text follows the key on a given host.
val inline textBlockFor: host: IResourceHost * key: string -> TextBlock
