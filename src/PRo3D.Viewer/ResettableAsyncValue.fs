namespace Aardvark.Base

open System.Threading
open System.Threading.Tasks


/// A one-shot async signal that delivers a value to a single waiter and resets automatically.
type ConsumableAsyncValue<'T>() =
    let mutable tcs = TaskCompletionSource<'T>(TaskCreationOptions.RunContinuationsAsynchronously)

    /// Asynchronously waits for the next value. Consumes it.
    member _.WaitAsync() : Task<'T> =
        Volatile.Read(&tcs).Task

    /// Sets the value and resets the signal.
    member _.SetValue(value: 'T) =
        let current = Interlocked.Exchange(&tcs, TaskCompletionSource<'T>(TaskCreationOptions.RunContinuationsAsynchronously))
        current.TrySetResult(value) |> ignore

    /// Indicates whether a waiter is currently pending.
    member _.IsWaiting =
        not (Volatile.Read(&tcs).Task.IsCompleted)
