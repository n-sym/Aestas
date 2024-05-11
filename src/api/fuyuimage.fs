namespace Aestas.ChatApi
open System
open System.IO
open System.Net.Http
open System.Net.Http.Headers
open System.Collections.Generic
open Newtonsoft.Json
open System.Text
open Newtonsoft.Json.Linq

type FuyuProfile = {api_key:string; secret_key: string}
type FuyuRequest = {prompt: string; image: string}
type FuyuResponse = {id: string; object: string; created: int; sentence_id: int;
                is_end: bool; is_truncated: bool; result: string;
                need_clear_history: bool; ban_round: int; usang: string}
type FuyuImageClient(profile: string) =
    let chatInfo = 
        use file = File.OpenRead(profile)
        use reader = new StreamReader(file)
        let json = reader.ReadToEnd()
        JsonConvert.DeserializeObject<FuyuProfile>(json)
    let accessToken =
        let url = $"https://aip.baidubce.com/oauth/2.0/token?grant_type=client_credentials&client_id={chatInfo.api_key}&client_secret={chatInfo.secret_key}"
        use web = new HttpClient()
        web.BaseAddress <- new Uri(url)
        web.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"))
        let content = new StringContent("{}", Encoding.UTF8, "application/json")
        let response = web.PostAsync("", content).Result
        let result = response.Content.ReadAsStringAsync().Result
        (JsonConvert.DeserializeObject<JObject>(result)["access_token"]).ToString()

    let receive p (data: byte[]) =
        try
        let url = $"https://aip.baidubce.com/rpc/2.0/ai_custom/v1/wenxinworkshop/image2text/fuyu_8b?access_token={accessToken}"
        use web = new HttpClient()
        web.BaseAddress <- new Uri(url)
        let content = 
            new StringContent(JsonConvert.SerializeObject({prompt = p; image = Convert.ToBase64String(data)})
            , Encoding.UTF8, "application/json")
        let response = web.PostAsync("", content).Result
        let result = response.Content.ReadAsStringAsync().Result
        printfn "FuyuResult:%s" result
        let response = JsonConvert.DeserializeObject<FuyuResponse>(result)
        if response.result |> String.IsNullOrEmpty then "load image failed"
        else response.result
        with
        | _ -> "load image failed"
    member _.Receive(prompt, image) = receive prompt image