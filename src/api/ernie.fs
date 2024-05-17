namespace Aestas.ChatApi
open System
open System.IO
open System.Net.Http
open System.Net.Http.Headers
open System.Collections.Generic
open System.Text
open System.Text.Json
open Aestas

type EProfile = {api_key:string; secret_key: string; mutable system: string; max_length: int; database: Dictionary<string, string>[] option}
type EResponse = {id: string; object: string; created: int; sentence_id: int;
                is_end: bool; is_truncated: bool; result: string;
                need_clear_history: bool; ban_round: int; usang: string}
type EModel =
| Ernie_Chara
| Ernie_35
| Ernie_40
| Ernie_35P
| Ernie_40P
type ErnieClient(profile: string, model: EModel) =
    let getModelApiUrl model = 
        match model with
        | Ernie_Chara -> "https://aip.baidubce.com/rpc/2.0/ai_custom/v1/wenxinworkshop/chat/ernie-char-8k?access_token="
        | Ernie_35 -> "https://aip.baidubce.com/rpc/2.0/ai_custom/v1/wenxinworkshop/chat/ernie-3.5-8k-preview?access_token="
        | Ernie_40 -> "https://aip.baidubce.com/rpc/2.0/ai_custom/v1/wenxinworkshop/chat/ernie-4.0-8k-preview?access_token="
        | Ernie_35P -> "https://aip.baidubce.com/rpc/2.0/ai_custom/v1/wenxinworkshop/chat/completions_preemptible?access_token="
        | Ernie_40P -> "https://aip.baidubce.com/rpc/2.0/ai_custom/v1/wenxinworkshop/chat/completions_pro_preemptible?access_token="
    let chatInfo = 
        use file = File.OpenRead(profile)
        use reader = new StreamReader(file)
        let json = reader.ReadToEnd()
        JsonSerializer.Deserialize<EProfile>(json)
    let messages = {messages = ResizeArray(); system = chatInfo.system}
    let database = 
        match chatInfo.database with
        | Some db -> ResizeArray(db)
        | None -> ResizeArray()
    let accessToken =
        let url = $"https://aip.baidubce.com/oauth/2.0/token?grant_type=client_credentials&client_id={chatInfo.api_key}&client_secret={chatInfo.secret_key}"
        use web = new HttpClient()
        web.BaseAddress <- new Uri(url)
        web.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"))
        let content = new StringContent("{}", Encoding.UTF8, "application/json")
        let response = web.PostAsync("", content).Result
        let result = response.Content.ReadAsStringAsync().Result
        (JsonSerializer.Deserialize<Nodes.JsonObject>(result)["access_token"]).ToString()
    
    let checkDialogLength () =
        let rec trim (messages: ResizeArray<Message>) sum =
            if sum > chatInfo.max_length && messages.Count <> 0 then
                let temp = messages[0].content.Length+messages[1].content.Length
                messages.RemoveRange(0, 2)
                trim messages (sum-temp)
            else ()
        let sum = (messages.messages |> Seq.sumBy (fun m -> m.content.Length))+messages.system.Length
        trim messages.messages sum
    let receive input send =
        async {
        let url = $"{getModelApiUrl model}{accessToken}"
        use web = new HttpClient()
        web.BaseAddress <- new Uri(url)
        let temp = ResizeArray(messages.messages)
        temp.Add {role = "user"; content = input}
        let messages' = {messages = temp; system = Prim.buildDatabasePrompt chatInfo.system database}
        let content = new StringContent(JsonSerializer.Serialize(messages'), Encoding.UTF8, "application/json")
        let! response = web.PostAsync("", content) |> Async.AwaitTask
        let result = response.Content.ReadAsStringAsync().Result
        if response.IsSuccessStatusCode then
            let response = JsonSerializer.Deserialize<EResponse>(result)
            do! send response.result
            messages.messages.Add {role = "user"; content = input}
            messages.messages.Add {role = "assistant"; content = response.result}
            checkDialogLength()
        else printfn "Ernie request failed: %A" result
        }
    
    interface IChatClient with
        member _.Messages = messages.messages
        member _.DataBase = database
        member _.Turn input send = receive input send |> Async.RunSynchronously
        member _.TurnAsync input send = receive input send