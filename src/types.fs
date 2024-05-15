namespace Aestas
open System.Collections.Generic
open Lagrange.Core
open Lagrange.Core.Message
open Aestas
open Aestas.ChatApi
open Prim
module rec AestasTypes =
    type private _print = string -> unit
    type private _chatHook = IChatClient -> unit
    type Aestas = {
        privateChats: Dictionary<uint32, IChatClient>
        groupChats: Dictionary<uint32, IChatClient*arrList<struct(string*string)>>
        prePrivateChat: _chatHook
        preGroupChat: _chatHook
        postPrivateChat: _chatHook
        postGroupChat: _chatHook
        media: MultiMediaParser
        privateCommands: Dictionary<string, ICommand>
        groupCommands: Dictionary<string, ICommand>
        awakeMe: Dictionary<string, float32> 
        notes: Notes
    }
    type MultiMediaParser = {
        image2text: (byte[] -> string) option
        text2speech: (string -> string -> byte[]) option
        text2image: (string -> byte[]) option
        speech2text: (byte[] -> string) option
        stickers: Dictionary<string, Sticker>
    }
    type _MarketSticker = {
        faceid: string
        tabid: int
        key: string
        summary: string
    }
    type Sticker = ImageSticker of byte[] | MarketSticker of _MarketSticker
    type ICommand =
        interface
            abstract member Execute: CommandEnvironment * Atom list -> Atom
            abstract member Help: string
        end
    type CommandEnvironment = {
        aestas: Aestas
        context: BotContext
        chain: MessageChain
        commands: Dictionary<string, ICommand>
        log: string -> unit
        model: IChatClient ref
    }
    type Ast =
    | Call of Ast list
    | Atom of Atom
    and Atom =
    | Number of float32
    | String of string
    | Identifier of string
    | Unit
    type Notes() =
        let data = Array.zeroCreate<string> 30
        let mutable cursor = 0
        let mutable readPos = -1
        member _.Add s =
            data[cursor] <- s
            cursor <- (cursor+1) % 30
        member _.Clear() =
            for i = 0 to 29 do data[i] <- null
            cursor <- 0
        interface IEnumerable<string> with
            member this.GetEnumerator() = this :> IEnumerator<string>
        interface IEnumerator<string> with
            member _.Current = data[readPos]
            member _.Dispose() = readPos <- -1
        interface System.Collections.IEnumerable with
            member this.GetEnumerator() = this :> System.Collections.IEnumerator
        interface System.Collections.IEnumerator with
            member _.Current = data[readPos] :> obj
            member _.MoveNext() =
                if readPos >= 29  || data[readPos+1] |> System.String.IsNullOrEmpty then false
                else
                    readPos <- readPos + 1; true
            member _.Reset() = readPos <- -1
                
    type IChatClient with
        static member Create<'t when 't :> IChatClient> (p: obj[]) =
            try
            (typeof<'t>).GetConstructor(p |> Array.map(fun a -> a.GetType())).Invoke(p) :?> IChatClient
            with | ex -> 
                printfn "Error: %A\nwhen create %A" ex (typeof<'t>)
                UnitClient() :> IChatClient