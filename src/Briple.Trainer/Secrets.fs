module Briple.Trainer.Secrets

open System
open System.IO
open System.Runtime.InteropServices
open System.Security.Cryptography

// The vault identity is fixed: same service/account everywhere, so a
// saved token is found again after any restart.
let service = "Briple.Trainer"

let account = "agent-token"

/// The platform-agnostic vault.
type ISecureStorage =

  /// Stores the token; Error carries the reason, phrased for the user.
  abstract Save: token: string -> Result<unit, string>

  /// Reads the token; None when absent.
  abstract Load: unit -> string option

  /// Removes the token.
  abstract Clear: unit -> Result<unit, string>

// --- Windows: DPAPI (CurrentUser) over a file next to the settings ---

module Win =

  let blobPath() =
    let dir =
      Path.Combine(
        Environment.GetFolderPath Environment.SpecialFolder.ApplicationData,
        "Briple.Trainer"
      )

    Directory.CreateDirectory dir |> ignore

    Path.Combine(dir, "token.secure")

  // DPAPI entropy: fixed app bytes, so other apps in the same profile
  // cannot unprotect the blob
  let entropy = Text.Encoding.UTF8.GetBytes "Briple.Trainer.agent-token"

  let save(token: string) : Result<unit, string> =
    try
      let blob =
        ProtectedData.Protect(
          Text.Encoding.UTF8.GetBytes token,
          entropy,
          DataProtectionScope.CurrentUser
        )

      File.WriteAllBytes(blobPath(), blob)
      Ok()
    with e ->
      Error $"DPAPI falló: {e.Message}"

  let load() : string option =
    try
      let path = blobPath()

      if not(File.Exists path) then
        None
      else
        ProtectedData.Unprotect(
          File.ReadAllBytes path,
          entropy,
          DataProtectionScope.CurrentUser
        )
        |> Text.Encoding.UTF8.GetString
        |> Some
    with _ ->
      None

  let clear() : Result<unit, string> =
    try
      let path = blobPath()

      if File.Exists path then
        File.Delete path

      Ok()
    with e ->
      Error $"no se pudo borrar el token: {e.Message}"

// --- macOS: Keychain generic passwords (Security.framework) ---

module Mac =

  [<Literal>]
  let Security = "/System/Library/Frameworks/Security.framework/Security"

  [<DllImport(Security, CharSet = CharSet.Ansi)>]
  extern int32 SecKeychainFindGenericPassword(
    nativeint keychain,
    uint32 serviceNameLength,
    string serviceName,
    uint32 accountNameLength,
    string accountName,
    uint32& passwordLength,
    nativeint& passwordData,
    nativeint& itemRef
  )

  [<DllImport(Security, CharSet = CharSet.Ansi)>]
  extern int32 SecKeychainAddGenericPassword(
    nativeint keychain,
    uint32 serviceNameLength,
    string serviceName,
    uint32 accountNameLength,
    string accountName,
    uint32 passwordLength,
    string passwordData,
    nativeint itemRef
  )

  [<DllImport(Security)>]
  extern int32 SecKeychainItemDelete(nativeint itemRef)

  [<DllImport(Security)>]
  extern int32 SecKeychainItemFreeContent(nativeint attr, nativeint data)

  let reason(status: int32) =
    $"Keychain devolvió el estado {status} (¿bloqueado?)"

  /// Removes the existing item, if any, so re-saving never duplicates.
  let deleteExisting() : unit =
    let mutable length = 0u
    let mutable data = 0n
    let mutable item = 0n

    let status =
      SecKeychainFindGenericPassword(
        0n,
        uint32 service.Length,
        service,
        uint32 account.Length,
        account,
        &length,
        &data,
        &item
      )

    if status = 0 then
      if data <> 0n then
        SecKeychainItemFreeContent(0n, data) |> ignore

      if item <> 0n then
        SecKeychainItemDelete item |> ignore

  let save(token: string) : Result<unit, string> =
    try
      deleteExisting()

      let status =
        SecKeychainAddGenericPassword(
          0n,
          uint32 service.Length,
          service,
          uint32 account.Length,
          account,
          uint32 token.Length,
          token,
          0n
        )

      if status = 0 then Ok() else Error(reason status)
    with e ->
      Error $"Keychain falló: {e.Message}"

  let load() : string option =
    try
      let mutable length = 0u
      let mutable data = 0n
      let mutable item = 0n

      let status =
        SecKeychainFindGenericPassword(
          0n,
          uint32 service.Length,
          service,
          uint32 account.Length,
          account,
          &length,
          &data,
          &item
        )

      if status <> 0 then
        None
      else
        let token = Marshal.PtrToStringAnsi(data, int length)

        if data <> 0n then
          SecKeychainItemFreeContent(0n, data) |> ignore

        if item <> 0n then
          SecKeychainItemFreeContent(0n, item) |> ignore

        Some token
    with _ ->
      None

  let clear() : Result<unit, string> =
    try
      deleteExisting()
      Ok()
    with e ->
      Error $"Keychain falló: {e.Message}"

// --- Linux: libsecret over the Secret Service D-Bus API ---

module Linux =

  [<Literal>]
  let Lib = "libsecret-1.so.0"

  [<Literal>]
  let Glib = "libglib-2.0.so.0"

  // secret_schema_attribute_type: SECRET_SCHEMA_ATTRIBUTE_STRING = 0
  [<Literal>]
  let attributeString = 0

  // secret_schema_flags: SECRET_SCHEMA_DONT_MATCH_NAME = 8
  [<Literal>]
  let dontMatchName = 8

  [<Struct; StructLayout(LayoutKind.Sequential)>]
  type SecretSchemaAttribute =
    val Name: nativeint
    val Type: int32

    new(name: nativeint, attributeType: int32) =
      { Name = name; Type = attributeType }

  [<Struct; StructLayout(LayoutKind.Sequential)>]
  type SecretSchema =
    val Name: nativeint
    val Flags: int32
    // the C schema reserves 32 attribute slots
    [<MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)>]
    val Attributes: SecretSchemaAttribute[]

    new(name: nativeint, flags: int32, attributes: SecretSchemaAttribute[]) =
      {
        Name = name
        Flags = flags
        Attributes = attributes
      }

  // glib: the hash/equal/free callbacks are function pointers, resolved
  // from the exports table rather than DllImport
  [<DllImport(Glib)>]
  extern nativeint g_hash_table_new_full(
    nativeint hash,
    nativeint equal,
    nativeint keyDestroy,
    nativeint valueDestroy
  )

  [<DllImport(Glib)>]
  extern nativeint g_hash_table_unref(nativeint table)

  [<DllImport(Glib)>]
  extern void g_hash_table_insert(
    nativeint table,
    nativeint key,
    nativeint value
  )

  [<DllImport(Glib)>]
  extern void g_error_free(nativeint error)

  [<DllImport(Lib)>]
  extern int32 secret_password_storev_sync(
    nativeint schema,
    nativeint attributes,
    [<MarshalAs(UnmanagedType.LPStr)>] string collection,
    [<MarshalAs(UnmanagedType.LPStr)>] string label,
    [<MarshalAs(UnmanagedType.LPStr)>] string password,
    nativeint cancellable,
    nativeint& error
  )

  [<DllImport(Lib)>]
  extern nativeint secret_password_lookupv_sync(
    nativeint schema,
    nativeint attributes,
    nativeint cancellable,
    nativeint& error
  )

  [<DllImport(Lib)>]
  extern int32 secret_password_clearv_sync(
    nativeint schema,
    nativeint attributes,
    nativeint cancellable,
    nativeint& error
  )

  [<DllImport(Lib)>]
  extern void secret_password_free(nativeint password)

  let glib = lazy NativeLibrary.Load Glib

  let inline symbol(name: string) : nativeint =
    NativeLibrary.GetExport(glib.Force(), name)

  /// The schema is copied to native memory (StructureToPtr handles the
  /// by-value attribute array); native reads it during the call only.
  let withSchema(run: nativeint -> 'T) : 'T =
    let name = Marshal.StringToHGlobalAnsi "briple.trainer"

    let serviceKey = Marshal.StringToHGlobalAnsi "service"

    let accountKey = Marshal.StringToHGlobalAnsi "account"

    let attributes = Array.create 32 (SecretSchemaAttribute(0n, 0))

    attributes[0] <- SecretSchemaAttribute(serviceKey, attributeString)

    attributes[1] <- SecretSchemaAttribute(accountKey, attributeString)

    let schema = SecretSchema(name, dontMatchName, attributes)

    let size = Marshal.SizeOf<SecretSchema>()

    let ptr = Marshal.AllocHGlobal size

    try
      Marshal.StructureToPtr(schema, ptr, false)
      run ptr
    finally
      Marshal.DestroyStructure<SecretSchema>(ptr)
      Marshal.FreeHGlobal ptr
      Marshal.FreeHGlobal name
      Marshal.FreeHGlobal serviceKey
      Marshal.FreeHGlobal accountKey

  let newTable() : nativeint =
    let free = symbol "g_free"

    g_hash_table_new_full(symbol "g_str_hash", symbol "g_str_equal", free, free)

  /// Keys and values become owned by the table (g_free destroys them), so
  /// they must be real native strings, not marshaled temporaries.
  let insert(table: nativeint, key: string, value: string) =
    g_hash_table_insert(
      table,
      Marshal.StringToHGlobalAnsi key,
      Marshal.StringToHGlobalAnsi value
    )

  let withTable(run: nativeint -> 'T) : 'T =
    let table = newTable()

    try
      insert(table, "service", service)
      insert(table, "account", account)
      run table
    finally
      g_hash_table_unref table |> ignore

  let available() : bool =
    let mutable handle = 0n
    NativeLibrary.TryLoad(Lib, &handle)

  let save(token: string) : Result<unit, string> =
    try
      withTable(fun table ->
        withSchema(fun schema ->
          let mutable err = 0n

          let stored =
            secret_password_storev_sync(
              schema,
              table,
              "default",
              service,
              token,
              0n,
              &err
            )

          if err <> 0n then
            g_error_free err

          if stored = 1 then
            Ok()
          else
            Error
              "el servicio de secretos rechazó la escritura (¿keyring bloqueado?)"))
    with e ->
      Error $"libsecret falló: {e.Message}"

  let load() : string option =
    try
      withTable(fun table ->
        withSchema(fun schema ->
          let mutable err = 0n

          let found = secret_password_lookupv_sync(schema, table, 0n, &err)

          if err <> 0n then
            g_error_free err

          if found = 0n then
            None
          else
            let token =
              Marshal.PtrToStringAnsi found
              |> Option.ofObj
              |> Option.defaultValue ""

            secret_password_free found

            Some token))
    with _ ->
      None

  let clear() : Result<unit, string> =
    try
      withTable(fun table ->
        withSchema(fun schema ->
          let mutable err = 0n

          secret_password_clearv_sync(schema, table, 0n, &err) |> ignore

          if err <> 0n then
            g_error_free err

          Ok()))
    with e ->
      Error $"libsecret falló: {e.Message}"

// --- the platform-agnostic surface ---

let unavailableReason() =
  "no hay un servicio de secretos disponible (en Linux: instala y desbloquea GNOME Keyring o KWallet)"

/// The platform is picked once here; every call afterwards goes through
/// the chosen implementation, so no availability probe runs twice.
let create() : ISecureStorage =
  if OperatingSystem.IsWindows() then
    {
      new ISecureStorage with
        member _.Save(token: string) = Win.save token

        member _.Load() = Win.load()

        member _.Clear() = Win.clear()
    }
  elif OperatingSystem.IsMacOS() then
    {
      new ISecureStorage with
        member _.Save(token: string) = Mac.save token

        member _.Load() = Mac.load()

        member _.Clear() = Mac.clear()
    }
  elif Linux.available() then
    {
      new ISecureStorage with
        member _.Save(token: string) = Linux.save token

        member _.Load() = Linux.load()

        member _.Clear() = Linux.clear()
    }
  else
    // no vault on this system: every call reports the reason so the
    // caller (Env) can take the insecure fallback and the UI can show why
    {
      new ISecureStorage with
        member _.Save(_: string) = Error(unavailableReason())

        member _.Load() = None

        member _.Clear() = Error(unavailableReason())
    }
