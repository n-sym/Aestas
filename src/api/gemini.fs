namespace Aestas.ChatApi
open System
open System.IO
open System.Net.Http
open System.Net.Http.Headers
open System.Collections.Generic
open System.Text
open System.Text.Json
open Aestas
open Prim
type GText = {text: string}
type GContent = {role: string; parts: GText[]}
type GSafetySetting = {category: string; threshold: string}
type GSafetyRatting = {category: string; probability: string}
type GSafetyCategory =
| HARM_CATEGORY_HATE_SPEECH
| HARM_CATEGORY_SEXUALLY_EXPLICIT
| HARM_CATEGORY_DANGEROUS_CONTENT
| HARM_CATEGORY_HARASSMENT
type GSafetyThreshold = 
| HARM_BLOCK_THRESHOLD_UNSPECIFIED
| BLOCK_LOW_AND_ABOVE
| BLOCK_MEDIUM_AND_ABOVE
| BLOCK_ONLY_HIGH
| BLOCK_NONE
type GProfile = {api_key: string option; gcloudpath: string option; mutable system: string; safetySettings: GSafetySetting[]; max_length: int; database: Dictionary<string, string>[] option; generation_configs: Dictionary<string, GenerationConfig> option}
type GGenerationConfig = {maxOutputTokens: int; temperature: float; topP: float; topK: float}
type GRequest = {contents: ResizeArray<GContent>; safetySettings: GSafetySetting[]; systemInstruction: GContent; generationConfig: GGenerationConfig option}
type GCandidate = {content: GContent; finishReason: string; index: int; safetyRatings: GSafetyRatting[]}
type GResponse = {candidates: GCandidate[]}
module Gemini =
    open System.Diagnostics
    let postRequest (auth: GProfile) (url: string) (content: string) =
        async{
        printfn "Use Gemini.."
        let useOauth = match auth.api_key with | Some _ -> false | None -> true
        let url =
            if useOauth then 
                printfn "Use GAuth.."
                url
            else $"{url}?key={auth.api_key.Value}"
        use web = new HttpClient()
        web.BaseAddress <- new Uri(url)
        if useOauth then 
            let access_token = 
                let info = ProcessStartInfo(auth.gcloudpath.Value, "auth application-default print-access-token")
                info.RedirectStandardOutput <- true
                Process.Start(info).StandardOutput.ReadToEnd().Trim()
            printfn "Get access-token successful.."
            web.DefaultRequestHeaders.Authorization <- new AuthenticationHeaderValue("Bearer", access_token)
            web.DefaultRequestHeaders.Add("x-goog-user-project", "")
        let content = new StringContent(content, Encoding.UTF8, "application/json")
        let! response = web.PostAsync("", content) |> Async.AwaitTask
        let result = response.Content.ReadAsStringAsync().Result
        if response.IsSuccessStatusCode then
            return Ok result
        else return Error result
        }
type GeminiClient (profile: string, flash: bool) =
    let chatInfo = 
        use file = File.OpenRead(profile)
        use reader = new StreamReader(file)
        let json = reader.ReadToEnd()
        JsonDeserializeFs<GProfile>(json)
    let model = if flash then "gemini-1.5-flash-latest" else "gemini-1.5-pro-latest"
    let rec convertSaftySettings (l: (GSafetyCategory*GSafetyThreshold) list) =
        match l with
        | [] -> []
        | (c, t)::xs -> {category = c.ToString(); threshold = t.ToString()}::convertSaftySettings xs
    let safetySettings = convertSaftySettings [
        HARM_CATEGORY_HARASSMENT, BLOCK_NONE
        HARM_CATEGORY_HATE_SPEECH, BLOCK_MEDIUM_AND_ABOVE
        HARM_CATEGORY_SEXUALLY_EXPLICIT, BLOCK_NONE
        HARM_CATEGORY_DANGEROUS_CONTENT, BLOCK_MEDIUM_AND_ABOVE
    ]
    let messages = {
        contents = ResizeArray(); 
        safetySettings = chatInfo.safetySettings; systemInstruction = {role = "system"; 
        parts = [|{text = chatInfo.system}|]};
        generationConfig = 
            match chatInfo.generation_configs with
            | None -> None
            | Some gs -> Some {
                    maxOutputTokens = gs[model].max_length; 
                    temperature = gs[model].temperature;
                    topP = gs[model].top_p;
                    topK = gs[model].top_k
            }
        }
    let database = 
        match chatInfo.database with
        | Some db -> ResizeArray(db)
        | None -> ResizeArray()
    let checkDialogLength () =
        let rec trim (messages: ResizeArray<GContent>) sum =
            if sum > chatInfo.max_length && messages.Count <> 0 then
                    let temp = messages[0].parts[0].text.Length+messages[1].parts[0].text.Length
                    messages.RemoveRange(0, 2)
                    trim messages (sum-temp)
                else ()
        let sum = (messages.contents |> Seq.sumBy (fun m -> m.parts[0].text.Length))+messages.systemInstruction.parts[0].text.Length
        trim messages.contents sum
    let receive input send =
        async {
            let! response = 
                let apiLink = 
                    if flash then  "https://generativelanguage.googleapis.com/v1beta/models/gemini-1.5-flash-latest:generateContent"
                    else "https://generativelanguage.googleapis.com/v1beta/models/gemini-1.5-pro-latest:generateContent"
                let temp = arrList(messages.contents)
                temp.Add {role = "user"; parts = [|{text = input}|]}
                let system =
                    {role = "system"; parts = [|{text = buildDatabasePrompt messages.systemInstruction.parts[0].text database}|]}
                let messages = 
                    {contents = temp; safetySettings = chatInfo.safetySettings; systemInstruction = system; generationConfig = messages.generationConfig}
                Gemini.postRequest chatInfo apiLink (JsonSerializer.Serialize(messages))
            match response with
            | Ok result ->
                let response = JsonSerializer.Deserialize<GResponse>(result).candidates[0].content.parts[0].text
                // gemini often return line break which seems redundant, so we need to remove it
                // they will increase these line breaks when they learn from context, so must remove it
                // their website (aistudio.google.com) also do that....
                let response = if response.EndsWith('\n') then response.TrimEnd() else response
                // gemini 1.5 flash likes \n\n, but i dont like it
                let response = if flash then response.Replace("\n\n", "\n") else response
                do! send response
                messages.contents.Add {role = "user"; parts = [|{text = input}|]}
                messages.contents.Add {role = "model"; parts = [|{text = response}|]}
                checkDialogLength()
            | Error result -> printfn "Gemini request failed: %A" result
            }
    interface IChatClient with
        member _.Messages = 
            messages.contents |> Seq.map (fun m -> {role = m.role; content = m.parts[0].text})
            |> ResizeArray
        member _.DataBase = database
        member _.Turn input send = receive input send |> Async.RunSynchronously
        member _.TurnAsync input send = receive input send
type G10GenerationConfig = {maxOutputTokens: int; temperature: float; topP: float}
type G10Request = {contents: ResizeArray<GContent>; safetySettings: GSafetySetting[]; generationConfig: G10GenerationConfig option}
type Gemini10Client(profile: string, tunedName: string) =
    let apiLink = 
        if tunedName |> String.IsNullOrEmpty then "https://generativelanguage.googleapis.com/v1beta/models/gemini-1.0-pro:generateContent"
        else $"https://generativelanguage.googleapis.com/v1beta/tunedModels/{tunedName}:generateContent"
    let chatInfo = 
        use file = File.OpenRead(profile)
        use reader = new StreamReader(file)
        let json = reader.ReadToEnd()
        JsonDeserializeFs<GProfile>(json)
    let messages = {
            contents = ResizeArray(); 
            safetySettings = chatInfo.safetySettings; 
            generationConfig = 
                match chatInfo.generation_configs with
                | None -> None
                | Some gs -> Some {
                        maxOutputTokens = gs["gemini-1.0-pro"].max_length; 
                        temperature = gs["gemini-1.0-pro"].temperature;
                        topP = gs["gemini-1.0-pro"].top_p
                }
            }
    let _ = 
        messages.contents.Add({role = "user"; parts = [|{text = chatInfo.system}|]})
        messages.contents.Add({role = "model"; parts = [|{text = "Certainly!"}|]})
    let database = 
        match chatInfo.database with
        | Some db -> ResizeArray(db)
        | None -> ResizeArray()
    let checkDialogLength () =
        let rec trim (messages: ResizeArray<GContent>) sum =
            if sum > chatInfo.max_length && messages.Count <> 2 then
                let temp = messages[2].parts[0].text.Length+messages[3].parts[0].text.Length
                messages.RemoveRange(2, 2)
                trim messages (sum-temp)
            else ()
        let sum = (messages.contents |> Seq.sumBy (fun m -> m.parts[0].text.Length))
        trim messages.contents sum
    let receive (input: string) send =
        async {
        let! response = 
            let messages = 
                // gemini tuned model not supported multiturn chat yet, so we need do that manually
                if tunedName |> String.IsNullOrEmpty then
                    let temp = arrList(messages.contents)
                    temp[0] <- {role = "user"; parts = [|{text = buildDatabasePrompt temp[0].parts[0].text database}|]}
                    temp.Add {role = "user"; parts = [|{text = input}|]}
                    {contents = temp; safetySettings = chatInfo.safetySettings; generationConfig = messages.generationConfig}
                else 
                    // not support database because i am lazy
                    let sb = StringBuilder()
                    for c in messages.contents do
                        sb.Append '{' |> ignore
                        sb.Append c.role |> ignore
                        sb.Append '}' |> ignore
                        sb.Append ':' |> ignore
                        sb.Append(c.parts[0].text) |> ignore
                        sb.Append '\n' |> ignore
                    sb.Append "{user}:" |> ignore
                    sb.Append input |> ignore
                    sb.Append "\n{model}:" |> ignore
                    {contents = ResizeArray([{role = "user"; parts = [|{text = sb.ToString()}|]}]); safetySettings = chatInfo.safetySettings; generationConfig = messages.generationConfig}
            Gemini.postRequest chatInfo apiLink (JsonSerializer.Serialize(messages))
        match response with
        | Ok result ->
            let response = JsonSerializer.Deserialize<GResponse>(result).candidates[0].content.parts[0].text
            do! send response
            messages.contents.Add {role = "user"; parts = [|{text = input}|]}
            messages.contents.Add {role = "model"; parts = [|{text = response}|]}
            checkDialogLength()
        | Error result -> printfn "Gemini10 request failed: %A" result
        }
    interface IChatClient with
        member _.Messages = 
            let r = 
                messages.contents 
                |> Seq.map (fun m -> {role = m.role; content = m.parts[0].text}) 
                |> ResizeArray
            r.RemoveRange(0, 2)
            r
        member _.DataBase = database
        member _.Turn input send = receive input send |> Async.RunSynchronously
        member _.TurnAsync input send = receive input send