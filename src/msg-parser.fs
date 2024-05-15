namespace Aestas
open System
open System.IO
open System.Text
open System.Collections.Generic
open System.Linq
open System.Net.Http
open Prim
open Aestas.ChatApi
open AestasTypes
open Lagrange.Core.Message
open Lagrange.Core.Message.Entity
module rec MessageParser =
    let parseBotOut (notes: Notes) (media: MultiMediaParser) (botOut: string) =
        let cache = StringBuilder()
        let result = arrList<IMessageEntity>()
        let rec innerRec i =
            let checkCache() = 
                if cache.Length > 0 then
                    new TextEntity(cache.ToString()) |> result.Add
                    cache.Clear() |> ignore
            let sticker e =
                if media.stickers.ContainsKey e then
                    (match media.stickers[e] with
                    | ImageSticker b -> new ImageEntity(b) :> IMessageEntity
                    | MarketSticker s -> 
                        new MarketFaceEntity(s.faceid, s.tabid, s.summary, s.key) :> IMessageEntity)
                    |> result.Add
                    true
                else false
            if i = botOut.Length then
                checkCache()
            else
            match botOut[i] with
            | '(' | '（' ->
                let paren = botOut[i]
                checkCache()
                let i = scanParen cache botOut (i+1)
                let e = cache.ToString()
                if sticker e |> not then
                    if paren = '(' then new TextEntity($"({e})") |> result.Add
                    else new TextEntity($"（{e}）") |> result.Add
                cache.Clear() |> ignore
                innerRec i
            | '[' ->
                checkCache()
                let i = scanSqParen cache botOut (i+1)
                let e = cache.ToString()
                //[voice@emotion:content]
                if e.StartsWith "voice" then
                    match media.text2speech with
                    | Some tts ->
                        let colon = e.IndexOf(':')
                        let emotion = e[6..colon-1]
                        let text = e[colon+1..]
                        let data = tts text emotion
                        //no need to upload
                        printfn $"Parsed voice of text: {text}, emotion: {emotion}"
                        new RecordEntity(data, 2) |> result.Add
                    | _ -> new TextEntity($"[{e}]") |> result.Add
                elif e.StartsWith "sticker" then
                    let colon = e.IndexOf(':')
                    if sticker e[colon+1..] |> not then
                        new TextEntity($"[{e}]") |> result.Add
                elif e.StartsWith "note" then
                    let colon = e.IndexOf(':')
                    let note = e[colon+1..]
                    notes.Add note
                else 
                    if sticker e |> not then
                        new TextEntity($"[{e}]") |> result.Add
                cache.Clear() |> ignore
                innerRec i
            | _ -> 
                cache.Append(botOut[i]) |> ignore
                innerRec (i+1)
        innerRec 0
        result
    let scanParen cache s i =
        if s[i] = ')' || s[i] = '）' then (i+1)
        else 
            cache.Append(s[i]) |> ignore
            if i+1 = s.Length then i+1 else scanParen cache s (i+1)
    let scanSqParen cache s i =
        if s[i] = ']' then (i+1)
        else 
            cache.Append(s[i]) |> ignore
            if i+1 = s.Length then i+1 else scanSqParen cache s (i+1)
    let bytesFromUrl (url: string) =
        try
        use web = new HttpClient()
        let bytesTask = web.GetByteArrayAsync(url)
        bytesTask.Wait()
        bytesTask.Result
        with
        | e -> 
            printfn "%A,imgurl = %s" e url
            raise e
    let checkAmrLength (file: byte[]) =
        let amrSize = [|12; 13; 15; 17; 19; 20; 26; 31; 5; 0; 0; 0; 0; 0; 0; 0|]
        let rec innerRec pos result =
            if pos >= file.Length then result else
            let toc = file[pos] &&& 0x0Fuy |> int
            innerRec (pos+amrSize[toc]) (result+20)
        (innerRec 6 0 + 500) / 1000
    let parseElements doI2T (getMsg: uint32 -> string) (media: MultiMediaParser) (es: IEnumerable<IMessageEntity>) =
        let result = StringBuilder()
        let rec go (etor: IEnumerator<IMessageEntity>) =
            let e = etor.Current
            (match e with
            | null -> ""
            | :? TextEntity as t -> t.Text
            | :? ImageEntity as i -> 
                if doI2T then
                    match media.image2text with
                    | Some i2t ->
                        let data = bytesFromUrl i.ImageUrl
                        let s = data |> i2t
                        $"[image:{s}]"
                    | _ -> "[image:not support]"
                //else "[image]"
                else ""
            | :? FaceEntity as f ->
                $"""({f.ToPreviewString().Replace("[", "").Replace("]","")})"""
            | :? MarketFaceEntity as f ->
                etor.MoveNext() |> ignore
                $"""[sticker:{f.Summary.Replace("[", "").Replace("]","")}]"""
            | :? RecordEntity -> "[audio:not support]"
            | :? MentionEntity as m -> 
                m.ToPreviewText()
            | :? ForwardEntity as f ->
                if doI2T then $"[quote:{f.Sequence |> getMsg}]\n"
                //else "[quote]"
                else ""
            | _ -> "[not supported]")
            |> result.Append |> ignore
            if etor.MoveNext() then go etor else ()
        use etor = es.GetEnumerator() in go etor
        result.ToString()