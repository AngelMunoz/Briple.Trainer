module Briple.Trainer.Views.Login

// The provider login page: the DeepSeek token and nothing else — the
// provider fields come from Env.defaultSettings. Saved through the env
// credentials capability; a failed vault triggers the huge insecure
// warning with the reason.

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
