namespace Aestas.ChatApi
open System
open System.IO
open System.Net.Http
open System.Net.Http.Headers
open System.Collections.Generic
open System.Text
open System.Text.Json
    
type MsTTS_Profile = {subscriptionKey: string; subscriptionRegion: string; voiceName: string; outputFormat: string;}

type MsTTS_Client(profile: string) =
    let chatInfo = 
        use file = File.OpenRead(profile)
        use reader = new StreamReader(file)
        let json = reader.ReadToEnd()
        JsonSerializer.Deserialize<MsTTS_Profile>(json)

    // let upload (file: byte[]) (upHost: string) =
    //     let url = upHost
    //     use web = new HttpClient()
    //     web.BaseAddress <- new Uri(url)
    //     let content = new MultipartFormDataContent()
    //     content.Add(new ByteArrayContent(file), "file", Guid.NewGuid().ToString())
    //     let response = web.PostAsync("", content).Result
    //     if response.IsSuccessStatusCode then
    //         printfn $"Upload tts audio success"
    //         response.Content.ReadAsStringAsync().Result
    //     else
    //         printfn "Error: %s" response.ReasonPhrase
    //         raise <| new Exception(response.ReasonPhrase)
    let receive (dialog: string) (style: string) =
        let url = $"https://{chatInfo.subscriptionRegion}.tts.speech.microsoft.com/cognitiveservices/v1"
        use web = new HttpClient()
        web.BaseAddress <- new Uri(url)
        web.DefaultRequestHeaders.Add("Ocp-Apim-Subscription-Key", chatInfo.subscriptionKey)
        web.DefaultRequestHeaders.Add("X-Microsoft-OutputFormat", chatInfo.outputFormat)
        web.DefaultRequestHeaders.Add("User-Agent", "HttpClient")
        let ssml = $"""
<speak version="1.0" xmlns="http://www.w3.org/2001/10/synthesis" xmlns:mstts="https://www.w3.org/2001/mstts" xml:lang="zh-CN">
    <voice name='{chatInfo.voiceName}'>
        <mstts:express-as style='{style}'>
            {dialog}
        </mstts:express-as>
    </voice>
</speak>
"""
        let content = 
            new StringContent(ssml, Encoding.UTF8, "application/ssml+xml")
        content.Headers.ContentType <- MediaTypeHeaderValue("application/ssml+xml")
        let response = web.PostAsync("", content).Result
        if response.IsSuccessStatusCode then
            response.Content.ReadAsByteArrayAsync().Result
        else
            printfn "Error: %s" response.ReasonPhrase
            raise <| new Exception(response.ReasonPhrase)
    member _.Receive(dialog: string, style: string) = receive dialog style