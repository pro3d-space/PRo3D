namespace PRo3D.Core

open Aardvark.Base
open Aardvark.UI
open Aardvark.UI.Primitives

open System
open System.IO
open PRo3D.Core.SequencedBookmarks

open Aether
open Aether.Operators

module SequencedBookmark =
    let filenameIsSequencedBookmark (filename : string) =
        let nameNoPath = Path.GetFileName filename 
        let startsWithPrefix = 
            String.startsWith 
                SequencedBookmarkDefaults.filenamePrefix 
                nameNoPath
        let ending = 
            SequencedBookmarkDefaults.filenamePostfix 
                + SequencedBookmarkDefaults.filenameExtension
        let containsPostfix = 
            String.contains ending nameNoPath
        startsWithPrefix && containsPostfix

    /// Extracts the guid from the filename of a sequenced bookmark.
    let guidFromFilename (filename : string) =
        let isProperName = filenameIsSequencedBookmark filename
        if isProperName then
            let nameNoPathNoExt = Path.GetFileNameWithoutExtension filename
            let guidString =
                nameNoPathNoExt 
                |> String.replace SequencedBookmarkDefaults.filenamePrefix ""
                |> String.replace SequencedBookmarkDefaults.filenamePostfix ""
            let (success, guid) = System.Guid.TryParse guidString
            if success 
            then Some guid
            else None
        else None

    let update (m : SequencedBookmarkModel) (msg : SequencedBookmarkAction) =
        match msg with
        | SetName name ->
            {m with bookmark = {m.bookmark with name = name}}
        | SetDelay msg ->
            {m with delay = Numeric.update m.delay msg}
        | SetDuration msg ->
            {m with duration = Numeric.update m.duration msg}