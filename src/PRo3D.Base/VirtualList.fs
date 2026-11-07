namespace PRo3D.Base

open System
open System.Globalization
open FSharp.Data.Adaptive
open Aardvark.UI

/// Windowed ("virtual") list (#900): a scroll box that puts only the rows in and around its
/// visible part into the DOM, with two spacer divs standing in for the rows above and below.
/// Thousands of rows cost the browser a screenful. The recipe is aardvark.media's
/// Scratch/19 - VirtualTree; unlike there, the scroll window is view-local state (two cvals
/// written from the scroll handler), so no model or message has to carry it.
///
/// Media builds the DOM once for all clients, so every open copy of a list shares one window:
/// with two visible copies, the one scrolled last decides which rows both get (as with
/// upstream's model-held window). A hidden copy (height 0) never moves the window.
///
/// Rows must all be exactly `rowHeight` pixels high: give each row `VirtualList.rowStyle`.
module VirtualList =

    /// rows rendered beyond the visible part on either side
    let private overscan = 10

    /// fixed row height; nothing may wrap or the spacers no longer add up
    let rowStyle (rowHeight : float) =
        sprintf "height: %.0fpx; overflow: hidden; white-space: nowrap; box-sizing: border-box" rowHeight

    /// The rows to render for a box scrolled to `top` showing `height` pixels of `n` rows of
    /// `rowHeight`: first row and row count, `overscan` rows beyond the visible part each side.
    let window (rowHeight : float) (top : float) (height : float) (n : int) : struct (int * int) =
        let first = max 0 (int (floor (top / rowHeight)) - overscan) |> min n
        let last  = min n (int (ceil ((top + height) / rowHeight)) + overscan) // exclusive
        struct (first, max 0 (last - first))

    // Reports scrollTop and clientHeight at most once per frame, on scroll and on resize
    // (a collapsed or hidden list has clientHeight 0 and reports again when it shows).
    // scrollCh carries "<key>|<row>" for the item to bring into view (row -1: not in this list).
    // Only a new key scrolls, so rows moving under an unchanged selection leave the view alone;
    // a request that arrives while the box is hidden waits until it shows. The outer page
    // scrolls only if the box had to move or is off screen, never for a row already in view.
    let private bootJs (rowHeight : float) =
        String.concat "\n" [
            "const self = document.getElementById('__ID__');"
            sprintf "const rowHeight = %s;" (rowHeight.ToString(CultureInfo.InvariantCulture))
            "let lastKey = null, lastRow = -1, pendingRow = -1;"
            "const reveal = (row) => {"
            "    if (self.clientHeight === 0) { pendingRow = row; return; }"
            "    pendingRow = -1;"
            "    const spacer = self.firstElementChild;"
            "    const origin = spacer ? spacer.getBoundingClientRect().top - self.getBoundingClientRect().top + self.scrollTop : 0;"
            "    const top = origin + row * rowHeight;"
            "    let moved = false;"
            "    if (top < self.scrollTop || top + rowHeight > self.scrollTop + self.clientHeight) {"
            "        self.scrollTop = top - (self.clientHeight - rowHeight) / 2;"
            "        moved = true;"
            "    }"
            "    const r = self.getBoundingClientRect();"
            "    if (moved || r.bottom <= 0 || r.top >= window.innerHeight) self.scrollIntoView({ block: 'nearest' });"
            "};"
            "let pending = false;"
            "const send = () => {"
            "    pending = false;"
            "    if (pendingRow >= 0 && self.clientHeight > 0) reveal(pendingRow);"
            "    aardvark.processEvent('__ID__', 'onvirtualwindow', self.scrollTop, self.clientHeight);"
            "};"
            "const schedule = () => { if (!pending) { pending = true; requestAnimationFrame(send); } };"
            "self.addEventListener('scroll', schedule, { passive: true });"
            "new ResizeObserver(schedule).observe(self);"
            "schedule();"
            "scrollCh.onmessage = (m) => {"
            "    const bar = m.lastIndexOf('|');"
            "    const key = m.substring(0, bar), row = parseInt(m.substring(bar + 1));"
            "    const fresh = key !== lastKey || (lastRow < 0 && row >= 0);"
            "    lastKey = key; lastRow = row;"
            "    if (fresh && row >= 0) reveal(row);"
            "};"
        ]

    let private parse (s : string) =
        match Double.TryParse(s.Trim('"'), NumberStyles.Float, CultureInfo.InvariantCulture) with
        | true, v when not (Double.IsNaN v || Double.IsInfinity v) -> Some v
        | _ -> None

    /// `cssClass` goes on the scroll box (e.g. "list" inside a semantic ui list), `maxHeight`
    /// is its CSS max-height (e.g. "60vh"); `row` must produce rows of `rowHeight` pixels.
    /// Whenever `scrollTo` names another item (by `key`), the box scrolls it into view.
    let view (cssClass : string) (maxHeight : string) (rowHeight : float)
             (key : 'a -> 'k) (scrollTo : aval<Option<'k>>)
             (row : 'a -> DomNode<'msg>) (items : alist<'a>) : DomNode<'msg> =

        let scrollTop    = cval 0.0
        let clientHeight = cval 800.0 // until the client reports: a generous first screen

        let onWindow =
            onEvent' "onvirtualwindow" [] (fun args ->
                match args with
                | [top; height] ->
                    match parse top, parse height with
                    // a hidden copy (height 0) must not shrink the window of a visible one
                    | Some top, Some height when height > 0.0 ->
                        if top <> scrollTop.Value || height <> clientHeight.Value then
                            transact (fun () ->
                                scrollTop.Value    <- top
                                clientHeight.Value <- height)
                    | _ -> ()
                | _ -> ()
                Seq.empty)

        let all = items |> AList.toAVal

        // first row, row count, total
        let rowWindow =
            adaptive {
                let! top    = scrollTop
                let! height = clientHeight
                let! all    = all
                let n       = all.Count
                let struct (first, count) = window rowHeight top height n
                return struct (first, count, n)
            }

        // IndexList.sub keeps the original indices, so moving the window by one row is one
        // remove and one add, and an edit outside the window changes nothing in the DOM
        let rows =
            AVal.map2 (fun all (struct (first, count, _)) -> IndexList.sub first count all) all rowWindow
            |> AList.ofAVal
            |> AList.map row

        // position of the `scrollTo` item, -1 when it is not in this list
        let position (k : 'k) (all : IndexList<'a>) =
            let arr = IndexList.toArray all
            let mutable found = -1
            let mutable i = 0
            while found < 0 && i < arr.Length do
                if key arr.[i] = k then found <- i
                i <- i + 1
            found

        // "<key>|<row>"; the client scrolls only when the key changes (see bootJs)
        let target =
            adaptive {
                match! scrollTo with
                | None -> return "|-1"
                | Some k ->
                    let! all = all
                    return sprintf "%s|%d" (string k) (position k all)
            }

        let spacer (height : struct (int * int * int) -> int) =
            let attributes =
                amap {
                    let! w = rowWindow
                    yield style (sprintf "height: %.0fpx" (float (height w) * rowHeight))
                } |> AttributeMap.ofAMap
            Incremental.div attributes AList.empty

        let content =
            alist {
                yield spacer (fun (struct (first, _, _)) -> first)
                yield! rows
                yield spacer (fun (struct (first, count, n)) -> n - first - count)
            }

        let attributes =
            AttributeMap.ofList [
                clazz cssClass
                // no scroll anchoring: the browser would shift scrollTop by the overscan
                // whenever the top spacer grows, and the rows would drift from the scroll bar
                style (sprintf "max-height: %s; overflow-y: auto; overflow-x: hidden; overflow-anchor: none" maxHeight)
                onWindow
            ]

        onBoot' [ "scrollCh", AVal.channel target ] (bootJs rowHeight) (Incremental.div attributes content)
