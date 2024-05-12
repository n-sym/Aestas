namespace Aestas
open System.Collections.Generic
open System.IO
open System.Text
open System.Text.RegularExpressions
open System.Linq
open System.Reflection
open Newtonsoft.Json
open System
open Lagrange.Core
open Lagrange.Core.Common
open Lagrange.Core.Common.Interface
open Lagrange.Core.Common.Interface.Api
open Lagrange.Core.Event
open Lagrange.Core.Message
open Lagrange.Core.Message.Entity
open Lagrange.Core.Utility
open StbImageSharp
open Aestas.ChatApi
open AestasTypes
open Prim
open Aestas.Commands.Command

module rec AestasBot =
    type private arrList<'t> = Prim.arrList<'t>
    type Config = {id: string; passwordMD5: string;}
    let inline await a = a |> Async.AwaitTask |> Async.RunSynchronously
    let inline isNotNull a = a |> isNull |> not
    let inline isEntity<'a when 'a :> IMessageEntity> (a: IMessageEntity) =
        match a with
        | :? 'a -> true
        | _ -> false
    let run() =
        let keyStore =
            try
            use file = File.OpenRead("keystore.json")
            use reader = new StreamReader(file)
            JsonConvert.DeserializeObject<BotKeystore>(reader.ReadToEnd())
            with _ -> new BotKeystore()
        let deviceInfo =
            try
            use file = File.OpenRead("deviceinfo.json")
            use reader = new StreamReader(file)
            JsonConvert.DeserializeObject<BotDeviceInfo>(reader.ReadToEnd())
            with _ -> 
                let d = BotDeviceInfo.GenerateInfo()
                d.DeviceName <- "Aestas@Lagrange-" + Prim.randomString 6
                d.SystemKernel <- Environment.OSVersion.VersionString
                d.KernelVersion <- Environment.OSVersion.Version.ToString()
                File.WriteAllText("deviceinfo.json", JsonConvert.SerializeObject(d))
                d
        use bot = BotFactory.Create(
            let c = new BotConfig() in
            c.UseIPv6Network <- false
            c.GetOptimumServer <- true
            c.AutoReconnect <- true
            c.Protocol <- Protocols.Linux
            c;
            , deviceInfo, keyStore)
        let preChat (model: IChatClient) =
            if model.DataBase.Count = 0 then model.DataBase.Add(Dictionary())
            if model.DataBase[0].ContainsKey("time") |> not then model.DataBase[0].Add("time", "")
            model.DataBase[0]["time"] <- DateTime.Now.ToString()
        let aestas = {
            privateChats = Dictionary()
            groupChats = Dictionary()
            prePrivateChat = preChat
            preGroupChat = preChat
            postPrivateChat = (fun _ -> ())
            postGroupChat = (fun _ -> ())
            media = {
                image2text = 
                    try
                    let _itt = FuyuImageClient("profiles/chat_info_fuyu.json")
                    (fun x -> _itt.Receive("introduce the image", x)) |> Some
                    with _ -> None
                text2speech = 
                    try
                    let _tts = MsTTS_Client("profiles/ms_tts.json")
                    (fun x y -> _tts.Receive(x, y)) |> Some
                    with _ -> None
                text2image = None
                speech2text = None
                stickers = loadStickers()
            }
            privateCommands = getCommands(fun a -> 
                a.Domain &&& AestasCommandDomain.Private <> AestasCommandDomain.None)
            groupCommands = getCommands(fun a -> 
                a.Domain &&& AestasCommandDomain.Group <> AestasCommandDomain.None)
            awakeMe = loadAwakeMe()
        }
        (fun context event -> 
            printfn "%A" event
        ) |> bot.Invoker.add_OnBotLogEvent
        (fun context event -> 
            printfn "Login Succeeded."
            bot.UpdateKeystore() |> saveKeyStore
            //bot.FetchCustomFace().Result |> printfn "%A"
        ) |> bot.Invoker.add_OnBotOnlineEvent
        privateChat aestas |> bot.Invoker.add_OnFriendMessageReceived
        groupChat aestas |> bot.Invoker.add_OnGroupMessageReceived
        login keyStore bot
        Console.ReadLine() |> ignore
    let saveKeyStore keyStore =
        File.WriteAllText("keystore.json", JsonConvert.SerializeObject(keyStore))
    let login keyStore bot =
        printfn "Try login.."
        if keyStore.Uid |> String.IsNullOrEmpty then
            let qrCode = bot.FetchQrCode() |> await
            if qrCode.HasValue then
                let struct(url, data) = qrCode.Value
                let image = ImageResult.FromMemory(data, ColorComponents.RedGreenBlueAlpha)
                // 1px' = 3px, padding = 12px
                let a = (image.Width)/3 
                let color x y = Prim.colorAt image.Data image.Width (x*3) (y*3)
                let cl2ch struct(r, g, b, a) =
                    if r = 0uy then "  "
                    else "■■"
                for i = 0 to (a-1) do
                    for j = 0 to (a-1) do
                        color i j |> cl2ch |> printf "%s"
                    printf "\n"
                printfn "Use this QR Code to login, or use this url else: %s" url
                bot.LoginByQrCode().Wait()
            else
                failwith "Fetch QR Code failed"
        else if bot.LoginByPassword() |> await |> not then failwith "Login failed"
    let privateChat aestas context event =
        async {
        let print s =
            MessageBuilder.Friend(event.Chain.FriendUin).Add(new TextEntity(s)).Build()
            |> context.SendMessage |> await |> ignore
        if event.Chain.FriendUin = context.BotUin then () else
        if tryProcessCommand aestas context event.Chain print true event.Chain.FriendUin then () else
        let dialog = event.Chain |> MessageParser.parseElements true (getMsgPrivte context event.Chain aestas true) aestas.media
        try
        if aestas.privateChats.ContainsKey(event.Chain.FriendUin) |> not then 
            aestas.privateChats.Add(event.Chain.FriendUin, 
            IChatClient.Create<ErnieClient> [|"profiles/chat_info_group_ernie.json"; Ernie_35P|])
        let chat = aestas.privateChats[event.Chain.FriendUin]
        aestas.prePrivateChat chat
        chat.Turn $"{{{event.Chain.FriendInfo.Nickname}}} {dialog}" (buildElement context aestas.media event.Chain.FriendUin true)
        aestas.postPrivateChat chat
        with e -> printfn "Error: %A" e
        } |> Async.Start
    let groupChat aestas context event =
        async {
        let print s =
            MessageBuilder.Group(event.Chain.GroupUin.Value).Add(new TextEntity(s)).Build()
            |> context.SendMessage |> await |> ignore
        if event.Chain.FriendUin = context.BotUin then () else
        let atMe =
            (fun (a: IMessageEntity) -> 
                match a with 
                | :? MentionEntity as m ->
                    m.Uin = context.BotUin
                | _ -> false
            ) |> event.Chain.Any || 
            (let msg = event.Chain.FirstOrDefault(fun t -> isEntity<TextEntity> t) in
            if isNull msg then false else
            let text = (msg :?> TextEntity).Text in 
                aestas.awakeMe 
                |> Seq.tryFind (fun p -> text.Contains(p.Key))
                |> function 
                    | Some p -> p.Value > Random.Shared.NextSingle()
                    | None -> false)
        let name = 
            if event.Chain.GroupMemberInfo.MemberCard |> String.IsNullOrEmpty then
                event.Chain.GroupMemberInfo.MemberName
            else event.Chain.GroupMemberInfo.MemberCard
        if atMe then 
            if tryProcessCommand aestas context event.Chain print false event.Chain.GroupUin.Value then () else
            let dialog = event.Chain |> MessageParser.parseElements true (getMsgGroup context event.Chain aestas true) aestas.media
            try
            if aestas.groupChats.ContainsKey(event.Chain.GroupUin.Value) |> not then 
                aestas.groupChats.Add(event.Chain.GroupUin.Value, 
                (IChatClient.Create<ErnieClient> [|"profiles/chat_info_group_ernie.json"; Ernie_35P|], arrList()))
            let chat, cache = aestas.groupChats[event.Chain.GroupUin.Value]
            let dialog = 
                if cache.Count = 0 then $"{{{name}}} {dialog}" else
                let sb = StringBuilder()
                for sender, msg in cache do
                    sb.Append('{').Append(sender).Append('}') |> ignore
                    sb.Append(' ').Append(msg) |> ignore
                    sb.Append(";\n") |> ignore
                cache.Clear()
                sb.Append('{').Append(name).Append('}') |> ignore
                sb.Append(' ').Append(dialog).ToString()
            aestas.preGroupChat chat
            chat.Turn dialog (buildElement context aestas.media event.Chain.GroupUin.Value false)
            aestas.postGroupChat chat
            with e -> printfn "Error: %A" e
        else
            try
            if aestas.groupChats.ContainsKey(event.Chain.GroupUin.Value) |> not then 
                aestas.groupChats.Add(event.Chain.GroupUin.Value, 
                (IChatClient.Create<ErnieClient> [|"profiles/chat_info_group_ernie.json"; Ernie_35P|], arrList()))
            let dialog = event.Chain |> MessageParser.parseElements false (getMsgGroup context event.Chain aestas false) aestas.media
            let _, cache = aestas.groupChats[event.Chain.GroupUin.Value]
            cache.Add (name, dialog)
            with e -> printfn "Error: %A" e
        } |> Async.Start
    
    let rec getMsgPrivte context chain aestas flag u =
            let msgs = context.GetRoamMessage(chain, 30u).Result
            let f = msgs.FirstOrDefault(fun a -> a.Sequence = u)
            if isNull f then "Message not found, maybe too far."
            else $"{{{chain.FriendInfo.Nickname}}} {f |> MessageParser.parseElements flag (getMsgPrivte context chain aestas flag) aestas.media}"
    let rec getMsgGroup context chain aestas flag u =
            let msgs = context.GetGroupMessage(chain.GroupUin.Value, chain.Sequence-30u, chain.Sequence).Result
            let f = msgs.FirstOrDefault(fun a -> a.Sequence = u)
            if isNull f then "Message not found, maybe too far."
            else
                let name = 
                    if f.GroupMemberInfo.MemberCard |> String.IsNullOrEmpty then
                        f.GroupMemberInfo.MemberName
                    else f.GroupMemberInfo.MemberCard
                $"{{{name}}} {f |> MessageParser.parseElements flag (getMsgGroup context chain aestas flag) aestas.media}"
    let buildElement context media id isPrivate (s: string) =
        async {
        let es = MessageParser.parseBotOut media s
        let newContent() = if isPrivate then MessageBuilder.Friend(id) else MessageBuilder.Group(id)
        let mutable content = newContent()
        let send (content: MessageBuilder) =
            if content.Build().Count <> 0 then
                let result = content.Build() |> context.SendMessage |> await
                printfn "sends:%d" result.Result
        for e in es do
            match e with
            | :? VideoEntity
            | :? ForwardEntity
            | :? RecordEntity ->
                send content
                content <- newContent()
                e |> newContent().Add |> send
            | :? MarketFaceEntity as m ->
                send content
                content <- newContent()
                newContent().Add(e).Add(new TextEntity(m.Summary)) |> send
            | _ -> content.Add e |> ignore
        send content
        }
    let tryProcessCommand aestas context msgs print isPrivate id = 
        let msg = msgs.FirstOrDefault(fun t -> isEntity<TextEntity> t)
        if isNull msg then false else
        let text = (msg :?> TextEntity).Text.Trim()
        printfn "%s" text
        if text.StartsWith '#' |> not then false else
        let source = text[1..]
        try
        match source with
        | x when x.StartsWith '#' ->
            let command = source.ToLower().Split(' ')
            match command[0] with
            | "#help" ->
                print "Commands: help, current, ernie, gemini, cohere, dumpcontext"
            | "#current" ->
                let tp = 
                    if isPrivate then 
                        if aestas.privateChats.ContainsKey id then 
                            aestas.privateChats[id].GetType().Name
                        else "UnitClient"
                    else 
                        if aestas.groupChats.ContainsKey id then 
                            let chat, _ = aestas.groupChats[id] in chat.GetType().Name
                        else "UnitClient"
                print $"Model is {tp}"
            | "#ernie" ->
                if command.Length < 2 then print "Usage: ernie [model=chara|35|40|35p|40p]"
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
                    if isPrivate then aestas.privateChats[id] <- IChatClient.Create<ErnieClient> [|"profiles/chat_info_private_ernie.json"; model|]
                    else aestas.groupChats[id] <- IChatClient.Create<ErnieClient> [|"profiles/chat_info_group_ernie.json"; model|], arrList()
                    print $"Model changed to ernie{command[1]}"
            | "#gemini" -> 
                if command.Length < 2 then print "Usage: gemini [model=15|10]"
                else
                    let profile = if isPrivate then "profiles/chat_info_private_gemini.json" else "profiles/chat_info_group_gemini.json"
                    let model = 
                        match command[1] with
                        | "15" -> IChatClient.Create<GeminiClient> [|profile|]
                        | "10" -> IChatClient.Create<Gemini10Client> [|profile; ""|]
                        | "vespera" -> IChatClient.Create<Gemini10Client> [|profile; "vespera-k7ejxi4vj84j"|]
                        | _ -> 
                            command[1] <- "default:10"
                            IChatClient.Create<Gemini10Client> [|profile; ""|]
                    if isPrivate then aestas.privateChats[id] <- model
                    else aestas.groupChats[id] <- model, arrList()
                    print $"Model changed to gemini {command[1]}"
            | "#cohere" -> 
                let model = IChatClient.Create<CohereClient> [|if isPrivate then "profiles/chat_info_private_cohere.json" else "profiles/chat_info_group_cohere.json"|]
                if isPrivate then aestas.privateChats[id] <- model
                else aestas.groupChats[id] <- model, arrList()
                print $"Model changed to cohere"
            | "#dumpcontext" ->
                let chat = if isPrivate then aestas.privateChats[id] else let chat, _ = aestas.groupChats[id] in chat
                let sb = StringBuilder()
                let msgs = chat.Messages
                let length = 
                    if command.Length < 2 then msgs.Count
                    else try command[1] |> Int32.Parse with | _ -> msgs.Count
                for i = 0 to length-1 do
                    let m = msgs[msgs.Count-length+i]
                    sb.Append($"{m.role}: {m.content}\n") |> ignore
                print (sb.ToString())
            | _ -> 
                print "Unknown command."
        | _ when isPrivate ->
            let model =
                if aestas.privateChats.ContainsKey id then ref aestas.privateChats[id]
                else let x = UnitClient() in aestas.privateChats.Add(id, x); ref x
            {aestas = aestas; context = context; chain = msgs; commands = aestas.privateCommands; log = print; model = model}
            |> excecute <| source
            aestas.privateChats[id] <- model.Value
        | _ ->
            let model =
                if aestas.groupChats.ContainsKey id then aestas.groupChats[id] |> fst |> ref
                else let x = UnitClient() in aestas.groupChats.Add(id, (x, arrList())); ref x
            {aestas = aestas; context = context; chain = msgs; commands = aestas.groupCommands; log = print; model = model}
            |> excecute <| source
            aestas.groupChats[id] <- model.Value, snd aestas.groupChats[id]
        true
        with | ex -> print $"Error: {ex}"; true
    
    type _Stickers = {
        from_file: Dictionary<string, string>
        from_url: Dictionary<string, string>
        from_market: Dictionary<string, _MarketSticker>
    }
    let loadStickers() =
        let result = Dictionary<string, Sticker>()
        try
        let json = 
            File.ReadAllText("profiles/stickers.json") |> JsonConvert.DeserializeObject<_Stickers>
        for p in json.from_file do
            let data = File.ReadAllBytes p.Value
            result.Add(p.Key, data |> ImageSticker)
        for s in json.from_market do
            result.Add(s.Key, s.Value |> MarketSticker)
        with _ -> ()
        result

    let loadAwakeMe() =
        try
        File.ReadAllText("profiles/awake_me.json") |> JsonConvert.DeserializeObject<Dictionary<string, float32>>
        with
        | _ -> Dictionary()