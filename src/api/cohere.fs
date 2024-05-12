namespace Aestas.ChatApi
open System
open System.IO
open System.Net.Http
open System.Net.Http.Headers
open System.Collections.Generic
open Newtonsoft.Json
open System.Text
open Newtonsoft.Json.Linq
open Aestas

type CMessage = {role: string; message: string}
type CConnectors = {id: string}
type CRequest = {message: string; model: string; preamble: string;
            chat_history: ResizeArray<CMessage>; connectors: CConnectors[];
            documents: ResizeArray<Dictionary<string, string>>; search_queries_only: bool}
type CProfile = {api_key:string; system: string; connectors: CConnectors[] option; documents: ResizeArray<Dictionary<string, string>> option; max_length: int}
type CCitations = {start: int; [<CompiledName("end")>]end': int; text: string; document_ids: string[]}
type CResponse = {text: string; generation_id: string; citations: CCitations[]; 
            is_search_required: bool; finish_reason: string; }
type CohereClient(profile: string) =
    let chatInfo = 
        use file = File.OpenRead(profile)
        use reader = new StreamReader(file)
        let json = reader.ReadToEnd()
        Prim.JsonDeserializeObjectWithOption<CProfile>(json)
    let messages = {message = ""; model = "command-r-plus"; preamble = chatInfo.system; 
                                chat_history = ResizeArray();
                                connectors = (match chatInfo.connectors with | Some c -> c | None -> [||]);
                                documents = (match chatInfo.documents with | Some d -> d | None -> ResizeArray());
                                search_queries_only = false}
    
    let checkDialogLength () =
        let rec trim (messages: ResizeArray<CMessage>) sum =
            if sum > chatInfo.max_length && messages.Count <> 0 then
                let temp = messages[0].message.Length+messages[1].message.Length
                messages.RemoveRange(0, 2)
                trim messages (sum-temp)
            else ()
        let sum = (messages.chat_history |> Seq.sumBy (fun m -> m.message.Length))+messages.preamble.Length
        trim messages.chat_history sum
    let receive input send =
        async {
        let url = $"https://api.cohere.ai/v1/chat"
        printfn "Use Cohere.."
        use web = new HttpClient()
        web.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"))
        web.DefaultRequestHeaders.Add("Authorization", $"Bearer {chatInfo.api_key}")
        web.BaseAddress <- Uri url
        let getResponse content = 
            async {
            let content = new StringContent(JsonConvert.SerializeObject(content), Encoding.UTF8, "application/json")
            let! response = web.PostAsync("", content) |> Async.AwaitTask
            let result = response.Content.ReadAsStringAsync().Result
            if response.IsSuccessStatusCode then
                return JsonConvert.DeserializeObject<CResponse>(result) |> Ok
            else return Error result
            }
        let! response = 
            match chatInfo.connectors with
            | Some connectors -> 
                if connectors.Length > 0 then
                    if messages.message.Contains("search") ||
                        messages.message.Contains("搜索") 
                    then
                        printfn "Cohere: SearchRequired"
                        let temp = {message = input; model = messages.model; preamble = messages.preamble;
                                chat_history = messages.chat_history; connectors = connectors;
                                documents = ResizeArray() ; search_queries_only = false}
                        getResponse temp
                    else 
                        printfn "Cohere: SearchNotRequired"
                        let temp = {message = input; model = messages.model; preamble = messages.preamble;
                                chat_history = messages.chat_history; connectors = [||];
                                documents = messages.documents ; search_queries_only = false}
                        getResponse temp
                else 
                    let temp = {message = input; model = messages.model; preamble = messages.preamble;
                                chat_history = messages.chat_history; connectors = connectors;
                                documents = ResizeArray() ; search_queries_only = false}
                    getResponse temp
            | _ -> 
                let temp = {message = input; model = messages.model; preamble = messages.preamble;
                                chat_history = messages.chat_history; connectors = messages.connectors;
                                documents = ResizeArray() ; search_queries_only = false}
                getResponse temp
        match response with
        | Error msg -> printfn "Cohere request failed: %A" msg
        | Ok response -> 
            do! send response.text
            printfn "refs%A" response.citations
            messages.chat_history.Add({role = "USER"; message = input})
            messages.chat_history.Add({role = "CHATBOT"; message = response.text})
            checkDialogLength()
        }
    interface IChatClient with
        member _.Messages = 
            let r = 
                messages.chat_history |> Seq.map (fun m -> {role = m.role; content = m.message})
                |> ResizeArray
            r.Add {role = "USER"; content = messages.message}
            r
        member _.DataBase = messages.documents
        member _.Turn input send = receive input send |> Async.RunSynchronously
        member _.TurnAsync input send = receive input send