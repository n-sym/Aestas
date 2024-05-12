namespace Aestas
open Aestas.ChatApi
open System
module Cli =
    [<EntryPoint>]
    let main(args) =
        let printHelp() =
            printfn "Usage: Aestas [command]"
            printfn "Commands:"
            printfn "listen [token] [selfId] [selfName] [host] - Listen to the server"
            printfn "run - Chat in the console directly"
        let run() =
            let introHelp = """----------------------------------------------
    Aestas - A Simple Chatbot Client
    Type #exit to exit
    Type #help to show this again
    Type #commands to show all commands
    Voice and Image are not supported in cli
----------------------------------------------
"""
            let mutable client = ErnieClient("profiles/chat_info_private_ernie.json", Ernie_35P) :> IChatClient
            printfn "%s" introHelp
            let rec mainLoop() =
                let rec printDialogs (messages: ResizeArray<Message>) i =
                    if i = messages.Count then () else
                    printfn "%s: %s" messages.[i].role messages.[i].content
                    printDialogs messages (i+1)
                printf "> "
                let s = Console.ReadLine()
                if s.StartsWith '#' then
                    let command = s[1..].ToLower().Split(' ')
                    match command[0] with
                    | "exit" -> ()
                    | "help" -> 
                        printfn "%s" introHelp
                        mainLoop()
                    | "commands" ->
                        printfn """Commands:
#exit - Exit the program
#help - Show the intro help
#commands - Show all commands
#current - Show current model
#ernie [model=chara|35|40|35p|40p] - Change model to ernie
#gemini [model=15|10] - Change model to gemini
#cohere - Change model to cohere"""
                        mainLoop()
                    | "current" ->
                        printfn $"Model is {client.GetType().Name}"
                        mainLoop()
                    | "ernie" ->
                        if command.Length < 2 then 
                            printfn "Usage: ernie [model=chara|35|40|35p|40p]"
                            mainLoop()
                        else
                            let model = 
                                match command[1] with
                                | "chara" -> Ernie_Chara
                                | "35" -> Ernie_35
                                | "40" -> Ernie_40
                                | "35p" -> Ernie_35P
                                | "40p" -> Ernie_40P
                                | _ -> 
                                    command[1] <- "default:chara"
                                    Ernie_Chara
                            client <- ErnieClient("profiles/chat_info_private_ernie.json", model)
                            printfn $"Model changed to ernie{command[1]}"
                            mainLoop()
                    | "gemini" -> 
                        if command.Length < 2 then 
                            printfn "Usage: gemini [model=15|10]"
                            mainLoop()
                        else
                            match command[1] with
                            | "15" -> client <- GeminiClient "profiles/chat_info_private_gemini.json" :> IChatClient
                            | "10" -> client <- Gemini10Client ("profiles/chat_info_private_gemini.json", "") :> IChatClient
                            | _ -> 
                                command[1] <- "default:10"
                                client <- Gemini10Client ("profiles/chat_info_private_gemini.json", "") :> IChatClient
                            printfn $"Model changed to gemini {command[1]}"
                            mainLoop()
                    | "cohere" ->
                        client <- CohereClient("profiles/chat_info_private_cohere.json")
                        printfn $"Model changed to cohere"
                        mainLoop()
                    | _ -> 
                        printfn "Unknown command."
                        mainLoop()
                elif s.Length > 400 then
                    printfn "Input should less than 400 characters."
                    mainLoop()
                elif s <> "" then
                    //client.PostOrAppend s
                    //client.CheckDialogLength()
                    //client.Receive(fun _ -> async {return ()})
                    client.Turn s (fun _ -> async {return ()})
                    Console.Clear()
                    printDialogs client.Messages 0
                    mainLoop()
                else
                    mainLoop()
            mainLoop()
        if args.Length = 0 then
            printHelp()
        else
            match args[0] with
            | "run" ->
                AestasBot.run()
            | "cli" ->
                run()
                ()
            | _ ->
                printHelp()
        0