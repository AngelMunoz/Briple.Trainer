module Briple.Trainer.Core.Tests.Program

open Expecto

[<EntryPoint>]
let main args =
  runTestsWithCLIArgs
    []
    args
    (testList "Briple.Trainer.Core" [
      Parser.Tests.tests
      Projection.Tests.tests
      Atletas.Tests.tests
      Memorias.Tests.tests
      Parametros.Tests.tests
      Solicitudes.Tests.tests
      Instrucciones.Tests.tests
      Wizard.Tests.tests
      Escritor.Tests.tests
      Datos.Tests.tests
      Validador.Tests.tests
      Prompts.Tests.tests
      Agente.Tests.tests
    ])
