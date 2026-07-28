namespace PRo3D

open System.IO
open Aardvark.Base
open Aardvark.Application
open Newtonsoft.Json

/// Per-computer user preferences. Persisted to
/// `%APPDATA%/Pro3D/userPreferences.json` (`Config.configPath` + the file
/// name below). These settings live OUTSIDE the scene file and outside any
/// bookmark — they belong to the local install, not to the data being viewed.
///
/// The type is immutable and lives on the runtime Model (as a TreatAsValue
/// field — the model holds the authoritative in-memory copy and the UI
/// reads it via `aval<UserPreferences>`). `load`/`save` move it between
/// disk and a fresh record.
type UserPreferences = {
    /// MapView WASD: swap forward/backward (W <-> S).
    mapInvertForward : bool
    /// MapView WASD: swap strafe (A <-> D).
    mapInvertStrafe  : bool
}

module UserPreferences =

    let initial : UserPreferences = {
        mapInvertForward = false
        mapInvertStrafe  = false
    }

    let private fileName = "userPreferences.json"

    let private filePath () = Path.Combine(Config.configPath, fileName)

    /// Load prefs from disk. Missing or malformed files fall back to
    /// `initial` — never throws.
    let load () : UserPreferences =
        let p = filePath ()
        try
            if File.Exists p then
                let json = File.ReadAllText p
                let parsed = JsonConvert.DeserializeObject<UserPreferences>(json)
                Log.line "[UserPreferences] loaded %s" p
                parsed
            else
                Log.line "[UserPreferences] no file at %s; using defaults" p
                initial
        with e ->
            Log.warn "[UserPreferences] failed to load %s: %s" p e.Message
            initial

    /// Persist `prefs` to disk. Errors are logged and swallowed.
    let save (prefs : UserPreferences) =
        let p = filePath ()
        try
            let json = JsonConvert.SerializeObject(prefs, Formatting.Indented)
            File.WriteAllText(p, json)
        with e ->
            Log.warn "[UserPreferences] failed to save %s: %s" p e.Message

    /// Remap a MapView WASD key according to `prefs`. Returns the key
    /// unchanged for non-WASD keys or when no invert flag is set.
    let remapMapViewKey (prefs : UserPreferences) (k : Keys) : Keys =
        match k with
        | Keys.W when prefs.mapInvertForward -> Keys.S
        | Keys.S when prefs.mapInvertForward -> Keys.W
        | Keys.A when prefs.mapInvertStrafe  -> Keys.D
        | Keys.D when prefs.mapInvertStrafe  -> Keys.A
        | _ -> k
