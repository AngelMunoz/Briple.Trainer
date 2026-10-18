module Briple.Trainer.Views.Routes

// The route table: pivots with Navs. Route factories load what they need
// from the env and build the pages; `go` and `back` are the imperative
// navigation helpers. `leadOf` builds a fresh back-control factory for
// the navigable that renders the page; "back" uses router history.

open Avalonia.Controls
open Navs
open Navs.Avalonia

open Briple.Trainer.Env

type RouteDeps<'T
  when 'T :> IProvideStore
  and 'T :> IProvideStores
  and 'T :> IProvideCulture
  and 'T :> IProvideLanguage
  and 'T :> IProvideTheme
  and 'T :> IProvideHost
  and 'T :> IProvideAgent
  and 'T :> IProvideCredentials
  and 'T :> IProvideLogging> = 'T

/// Navigates and reports a failure through the log.
val inline go: nav: INavigable * url: string -> unit

/// Navigates back through the router history and reports a failure.
val inline back: nav: INavigable -> unit

/// The router plus the page-phase surface the shell needs.
[<Struct>]
type ShellRoutes = {
  Router: Navs.IRouter<Avalonia.Controls.Control>

  /// True when leaving the page at this URL is allowed.
  AskLeave: string -> Async<bool>
}

/// The route table over the environment. `leadOf` builds one back
/// control per page for the navigable that renders it.
val build:
  env: RouteDeps<'T> * leadOf: (INavigable -> (unit -> Control)) -> ShellRoutes
