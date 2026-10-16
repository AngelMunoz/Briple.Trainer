module Briple.Trainer.Views.Routes

// The route table: pivots with Navs. Route factories load what they need
// from the env and build the pages; `go` is the one imperative navigation
// helper. Navs keeps no history, so "back" is a plain Navigate.

open Avalonia.Controls
open Navs
open Navs.Avalonia

open Briple.Trainer.Env

/// Navigates and reports a failure through the log; the failure is
/// visible, not swallowed.
val inline go: nav: INavigable<Control> * url: string -> unit

/// The route table over the environment; the shell opens on the login
/// when there is no working token yet.
val build:
  env:
    #IProvideStore & #IProvideStores & #IProvideCulture & #IProvideLanguage & #IProvideHost & #IProvideAgent & #IProvideCredentials ->
    Routes
