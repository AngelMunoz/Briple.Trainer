module Briple.Trainer.Program

// The boot: configure logging, build the production environment, and
// start the desktop lifetime. The app root and the shell live in their
// own modules.

open Serilog

open Briple.Trainer.Env
open Briple.Trainer.Persistence
open Briple.Trainer.Startup

[<EntryPoint>]
let main argv =
  configureLogging()

  try
    match Env.create(defaultDbPath()) with
    | Error e ->
      Log.Fatal(e, "boot failed")
      start(argv, fun () -> errorWindow e.Message)
    | Ok env -> start(argv, fun () -> Shell.create env)
  finally
    Log.CloseAndFlush()
