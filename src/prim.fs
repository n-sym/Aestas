namespace Aestas
open System
open System.Text
open System.Text.Json
open System.Text.Json.Serialization
open System.Collections.Generic
open System.Text.Encodings.Web
open System.Text.Unicode
module Prim = 
    let version = Version(0, 240517)
    let inline is<'t when 't: not struct> (a: obj) =
        match a with
        | :? 't -> true
        | _ -> false
    let inline await a = a |> Async.AwaitTask |> Async.RunSynchronously
    let inline isNotNull a = a |> isNull |> not
    let fsOptions = 
        JsonFSharpOptions.Default()
            .WithSkippableOptionFields(SkippableOptionFields.Always, true)
            .ToJsonSerializerOptions()
    let _ =
        fsOptions.Encoder <- JavaScriptEncoder.Create(UnicodeRanges.All)
    let inline jsonDeserialize<'t> (x: string) = 
        JsonSerializer.Deserialize<'t>(x, fsOptions)
    let inline jsonSerialize (x: 't) = 
        JsonSerializer.Serialize<'t>(x, fsOptions)
    let inline (|StrStartWith|_|) (s: string) (x: string) = 
        if x.StartsWith(s) then Some x else None
    type 't arrList = ResizeArray<'t>
    type System.Collections.Generic.List<'t> with
        member this.GetReverseIndex(_:int, offset) = this.Count-offset-1
        member this.GetSliceGetSlice(startIndex: int option, endIndex: int option) = 
            match startIndex, endIndex with
            | None, None -> this
            | Some i, None -> this.Slice(i, this.Count-i)
            | Some i, Some j -> this.Slice(i, j-i)
            | None, Some j -> this.Slice(0, j)
    let buildDatabasePrompt (system: string) (db: arrList<Dictionary<string, string>>) = 
        let sb = StringBuilder()
        sb.Append(system) |> ignore
        sb.Append("*Following is a dictionary for you:*") |> ignore
        for dic in db do
            for p in dic do
                sb.Append(';').Append(p.Key).Append(": ").Append(p.Value) |> ignore
        sb.ToString()
    let colorAt (arr: byte[]) w x y =
        let i = 4*(w*y+x)
        struct(arr[i], arr[i+1], arr[i+2], arr[i+3])
    let randomString (length: int) =
        let chars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789"
        let rand = Random()
        let sb = StringBuilder()
        for _ = 1 to length do
            sb.Append(chars[rand.Next(chars.Length)]) |> ignore
        sb.ToString()
    let bash (cmd: string) =
        let psi = Diagnostics.ProcessStartInfo("/bin/bash", $"-c \"{cmd}\"")
        psi.RedirectStandardOutput <- true
        psi.UseShellExecute <- false
        psi.CreateNoWindow <- true
        use p = new Diagnostics.Process()
        p.StartInfo <- psi
        p.Start() |> ignore
        let result = p.StandardOutput.ReadToEnd()
        p.WaitForExit() |> ignore
        p.Kill()
        result
    let cmd (cmd: string) =
        let psi = Diagnostics.ProcessStartInfo("cmd", $"/c \"{cmd}\"")
        psi.RedirectStandardOutput <- true
        psi.UseShellExecute <- false
        psi.CreateNoWindow <- true
        use p = new Diagnostics.Process()
        p.StartInfo <- psi
        p.Start() |> ignore
        let result = p.StandardOutput.ReadToEnd()
        p.WaitForExit() |> ignore
        p.Kill()
        result