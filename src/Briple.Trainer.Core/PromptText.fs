module Briple.Trainer.Core.PromptText

open System
open System.Text

let inline cap (maxLength: int) (text: string) =
  let limit = max 0 maxLength

  if text.Length <= limit then
    text
  elif limit = 0 then
    ""
  elif Char.IsHighSurrogate text.[limit - 1] then
    text.Substring(0, limit - 1).TrimEnd()
  else
    text.Substring(0, limit).TrimEnd()

let inline flatten(text: string) =
  let builder = StringBuilder(text.Length)
  let mutable pendingSpace = false

  for c in text do
    let isSpace = Char.IsWhiteSpace c || Char.IsControl c || c = '|'

    if isSpace then
      pendingSpace <- builder.Length > 0
    else
      if pendingSpace then
        builder.Append ' ' |> ignore

      pendingSpace <- false
      builder.Append c |> ignore

  builder.ToString()

let line (maxLength: int) (text: string) =
  if String.IsNullOrEmpty text then
    ""
  else
    text |> flatten |> cap maxLength

let block (maxLength: int) (text: string) =
  if String.IsNullOrEmpty text then
    ""
  else
    let normalized = text.Replace("\r\n", "\n").Replace('\r', '\n')

    let builder = StringBuilder(normalized.Length)
    let mutable pendingSpace = false

    for c in normalized do
      if c = '\n' then
        builder.Append '\n' |> ignore
        pendingSpace <- false
      elif c = '\t' || (Char.IsControl c && c <> '\n') then
        pendingSpace <- builder.Length > 0
      elif Char.IsWhiteSpace c then
        pendingSpace <- builder.Length > 0
      else
        if pendingSpace then
          builder.Append ' ' |> ignore

        pendingSpace <- false
        builder.Append c |> ignore

    builder.ToString().Trim() |> cap maxLength

let token (maxLength: int) (text: string) =
  if String.IsNullOrEmpty text then
    ""
  else
    let normalized = text.Replace(',', ' ').Replace(';', ' ') |> flatten

    normalized |> cap maxLength

let flat (maxLength: int) (text: string) =
  if String.IsNullOrEmpty text then
    ""
  else
    text.Replace("\r\n", " ").Replace('\r', ' ').Replace('\n', ' ')
    |> block maxLength
