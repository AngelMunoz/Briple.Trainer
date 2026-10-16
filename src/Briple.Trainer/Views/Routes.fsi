module Briple.Trainer.Views.Routes

// The route table: pivots with Navs. Route factories load what they need
// from the env and build the pages; `go` is the one imperative navigation
// helper. Navs keeps no history, so "back" is a plain Navigate.

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

/// Navigates and reports a failure through the log; the failure is
/// visible, not swallowed.
val inline go: nav: INavigable<Control> * url: string -> unit

/// The route table over the environment, as the library's own router:
/// definitions built with the DSL, the agent guard on the provider
/// routes, and the first navigation already on the athletes list. The
/// shell mounts it in a RouterOutlet.
val build: env: RouteDeps<'T> -> Navs.IRouter<Avalonia.Controls.Control>
