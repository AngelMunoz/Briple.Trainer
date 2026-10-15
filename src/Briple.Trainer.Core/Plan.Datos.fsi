module Briple.Trainer.Core.Plan.Datos

// La frontera de datos del agente: el modelo responde JSON, JDeck lo
// decodifica de forma tolerante (campos ausentes o nulos, respuesta
// envuelta en {"data": ...}) y este modulo valida lo decodificado
// antes de construir el dominio. El modelo nunca escribe el formato
// del archivo; el formato lo genera Escritor de forma determinista.

open Briple.Trainer.Core.Plan.Types

/// Convierte el JSON del modelo en un Plan del dominio. Cada campo
/// invalido acumula un fallo con su ruta; el dia debe tener nombre
/// reconocido y el genero debe ser hombre o mujer.
val fromJson: json: string -> Result<Plan, string list>

/// Serializa un Plan como el JSON que el modelo recibe de referencia
/// en el flujo de ajuste.
val toJson: plan: Plan -> string
