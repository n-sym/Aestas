namespace Aestas.ChatApi
open System
open System.IO
open System.Net.Http
open System.Net.Http.Headers
open System.Collections.Generic
open Newtonsoft.Json
open System.Text
open Newtonsoft.Json.Linq

type Message = {role: string; content: string}
type Messages = {messages: ResizeArray<Message>; system: string}
type IChatClient =
    interface
        abstract member Messages: ResizeArray<Message>
        abstract member DataBase: ResizeArray<Dictionary<string, string>>
        abstract member Turn: string -> (string -> Async<unit>) -> unit
        abstract member TurnAsync: string-> (string -> Async<unit>) -> Async<unit>
    end