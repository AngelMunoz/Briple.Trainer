module Briple.Trainer.Views.Login

// The provider login page: the DeepSeek token and nothing else — the
// provider fields come from Env.defaultSettings. The token goes through
// the env credentials capability. A vault save continues straight to
// the app; the insecure fallback stays on the page with the warning and
// an explicit continue button, so the user decides with the facts.

open System
open System.Globalization

open Avalonia.Controls

open Briple.Trainer.Env

/// The data and callbacks of the page.
[<Struct>]
type LoginPageProps = {
  Culture: CultureInfo
  Save: string -> Result<CredentialSave, string>
  OnDone: unit -> unit
}

/// The login page.
type LoginPage =
  inherit UserControl

  new: props: LoginPageProps -> LoginPage
