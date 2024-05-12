namespace Aestas.ChatApi
type UnitClient() =
    interface IChatClient with
        member val Messages = ResizeArray([{role = ""; content = ""}])
        member val DataBase = ResizeArray()
        member _.Turn input _ = 
            printfn "UnitClientTurn:%s" input
        member _.TurnAsync input _ = 
            async {
                printfn "UnitClientTurnAsync:%s" input
            }
        //member _.PostOrAppend s = printfn "UnitClientPost:%s" s
    end