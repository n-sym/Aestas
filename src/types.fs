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
    
    type IChatClient with
        static member Create<'t when 't :> IChatClient> (p: obj[]) =
            try
            (typeof<'t>).GetConstructor(p |> Array.map(fun a -> a.GetType())).Invoke(p) :?> IChatClient
            with | ex -> 
                printfn "Error: %A\nwhen create %A" ex (typeof<'t>)
                UnitClient() :> IChatClient