namespace Aestas
open System
open System.Text
open System.Collections.Generic
open Newtonsoft.Json
open Newtonsoft.Json.Linq
open Microsoft.FSharp.Reflection
module Prim = 
    type FSharpOptionConverter() =
        inherit JsonConverter()
            override _.CanConvert(objectType: Type) =
                objectType.IsGenericType && objectType.GetGenericTypeDefinition() = typedefof<option<_>>
            override _.ReadJson(reader: JsonReader, objectType: Type, existingValue: obj, serializer: JsonSerializer) =
                if reader.TokenType = JsonToken.Null then
                    None
                else
                    let valueType = objectType.GetGenericArguments()[0]
                    let value = serializer.Deserialize(reader, valueType)
                    Activator.CreateInstance((typedefof<option<_>>).MakeGenericType([|valueType|]), [|value|])
            override _.WriteJson(writer: JsonWriter, value: obj, serializer: JsonSerializer) =
                match value with
                | null -> serializer.Serialize(writer, null)
                | _ ->
                    let innerValue = FSharpValue.GetUnionFields(value, value.GetType())
                    serializer.Serialize(writer, innerValue)
    let JsonDeserializeObjectWithOption<'t> x = 
        let s = JsonSerializerSettings()
        s.Converters.Add(FSharpOptionConverter())
        JsonConvert.DeserializeObject<'t>(x, s)
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
        sb.Append("\nFollowing is a dictionary for you.\n") |> ignore
        for dic in db do
            for p in dic do
                sb.Append($"{p.Key}: {p.Value}\n") |> ignore
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