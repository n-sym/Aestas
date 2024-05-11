namespace Aestas.Commands
open System
open System.Text
open System.Collections.Generic
open System.Linq
open Aestas
open Prim
module ErrorMessage =
    let stringEof = "String literal arrives the end of file"
    let commnetEof = "Comment literal arrives the end of file"
    let inline unkownSymbol t = $"Could not recognize this symbol \"{t}\""
module rec Lexer =
    type Token =
    | TokenSpace
    | TokenPrint
    | TokenFloat of single
    | TokenString of string
    /// a, bar, 变量 etc.
    | TokenIdentifier of string
    /// '<|'
    | TokenLeftPipe
    /// '|>'
    | TokenRightPipe
    /// '('
    | TokenLeftRound
    /// ')'
    | TokenRightRound
    /// '['
    | TokenLeftSquare
    /// ']'
    | TokenRightSquare
    /// '{'
    | TokenLeftCurly
    /// '}'
    | TokenRightCurly
    /// '<-'
    | TokenLeftArrow
    /// '->'
    | TokenRightArrow
    /// '|'
    | TokenPipe
    | TokenNewLine
    /// '.'
    | TokenDot
    /// ','
    | TokenComma
    /// ';'
    | TokenSemicolon
    /// ':'
    | TokenColon
    /// string: Error message
    | TokenError of string
    type private _stDict = IReadOnlyDictionary<string, Token>
    type Macros = IReadOnlyDictionary<string, string>
    type MaybeDict = 
    | AValue of Token
    | ADict of Dictionary<char, MaybeDict> 
    /// A pack of language primitive informations
    type LanguagePack = {keywords: _stDict; operatorChars: char array; operators: Dictionary<char, MaybeDict>; newLine: char array}
    /// Scan the whole source code
    let scan (lp: LanguagePack) (source: string) (macros: Macros) =
        let rec checkBound (k: string) (s: string) =
            let refs = s.Split(' ') |> Array.filter (fun s -> s.StartsWith '$')
            let mutable flag = false
            for p in macros do
                flag <- flag || (refs.Contains p.Key && p.Value.Contains k)
            flag
        let mutable flag = false
        for p in macros do
            flag <- flag || checkBound p.Key p.Value
        if flag then [TokenError(ErrorMessage.unkownSymbol "Macro looped reference detected")]
        else scan_ (arrList()) lp source macros |> List.ofSeq
    let scanWithoutMacro (lp: LanguagePack) (source: string)=
        scan_ (arrList()) lp source (readOnlyDict []) |> List.ofSeq
    let rec private scan_ (tokens: Token arrList) (lp: LanguagePack) (source: string) (macros: Macros) = 
        let cache = StringBuilder()
        let rec innerRec (tokens: Token arrList) cursor =
            if cursor >= source.Length then tokens else
            let lastToken = if tokens.Count = 0 then TokenNewLine else tokens[^0]
            cache.Clear() |> ignore
            match source[cursor] with
            | ' ' -> 
                if (lastToken = TokenNewLine || lastToken = TokenSpace) |> not then tokens.Add(TokenSpace)
                innerRec tokens (cursor+1)
            | '\"' ->
                let (cursor, eof) = scanString lp (cursor+1) source cache
                if eof then tokens.Add (TokenError ErrorMessage.stringEof)
                else tokens.Add (TokenString (cache.ToString()))
                innerRec tokens cursor
            | c when Array.contains c lp.newLine ->
                if lastToken = TokenNewLine |> not then 
                    if lastToken = Token.TokenSpace then tokens[^0] <- TokenNewLine
                    else tokens.Add(TokenNewLine)
                innerRec tokens (cursor+1)
            | c when Array.contains c lp.operatorChars ->
                if c = '(' && source.Length-cursor >= 2 && source[cursor+1] = '*' then
                    let (cursor, eof) = scanComment lp (cursor+2) source cache
                    if eof then tokens.Add (TokenError ErrorMessage.stringEof)
                    innerRec tokens cursor
                else
                    let cursor = scanSymbol lp cursor source cache
                    let rec splitOp (d: Dictionary<char, MaybeDict>) i =
                        if i = cache.Length |> not && d.ContainsKey(cache[i]) then
                            match d[cache[i]] with
                            | ADict d' -> splitOp d' (i+1)
                            | AValue v -> 
                                tokens.Add v
                                splitOp lp.operators (i+1)
                        else if d.ContainsKey('\000') then
                            let (AValue t) = d['\000'] in tokens.Add t
                            splitOp lp.operators i
                        else if i = cache.Length then ()
                        else tokens.Add (TokenError(ErrorMessage.unkownSymbol cache[if i = cache.Length then i-1 else i]))
                    splitOp lp.operators 0
                    innerRec tokens cursor
            | c when isNumber c ->
                let (cursor, isFloat) = scanNumber lp cursor source cache false
                let s = cache.ToString()
                tokens.Add(TokenFloat (Single.Parse s))
                innerRec tokens cursor
            | '$' ->
                let cursor = scanIdentifier lp (cursor+1) source cache
                let s = cache.ToString()
                if s = "" || macros.ContainsKey s |> not then tokens.Add (TokenError(ErrorMessage.unkownSymbol "$"))
                else 
                    scan_  tokens lp macros[s] macros |> ignore
                innerRec tokens cursor
            | _ -> 
                let cursor = scanIdentifier lp cursor source cache
                let s = cache.ToString()
                match s with
                | s when lp.keywords.ContainsKey s -> tokens.Add(lp.keywords[s])
                | _ -> tokens.Add(TokenIdentifier s)
                innerRec tokens cursor
        innerRec tokens 0
    let rec private scanIdentifier lp cursor source cache =
        let current = source[cursor]
        if current = ' ' || current = '\"' || Array.contains current lp.newLine || Array.contains current lp.operatorChars then
            cursor
        else 
            cache.Append current |> ignore
            if cursor = source.Length-1 then cursor+1 else scanIdentifier lp (cursor+1) source cache
    let rec private scanNumber lp cursor source cache isFloat =
        let current = source[cursor]
        if isNumber current then
            cache.Append current |> ignore
            if cursor = source.Length-1 then (cursor+1, isFloat) else scanNumber lp (cursor+1) source cache isFloat
        else if current = '.'  then 
            cache.Append current |> ignore
            if cursor = source.Length-1 then (cursor+1, true) else scanNumber lp (cursor+1) source cache true
        else if current = 'e' && source.Length - cursor >= 2 && (isNumber source[cursor+1] || source[cursor+1] = '-') then 
            cache.Append current |> ignore
            scanNumber lp (cursor+1) source cache isFloat
        else (cursor, isFloat)
    let rec private scanSymbol lp cursor source cache =
        let current = source[cursor]
        if Array.contains current lp.operatorChars then
            cache.Append current |> ignore
            if cursor = source.Length-1 then cursor+1 else scanSymbol lp (cursor+1) source cache
        else cursor
    let rec private scanString lp cursor source cache =
        let current = source[cursor]
        if current = '\"' then (cursor+1, false)
        else if source.Length-cursor >= 2 && current = '\\' then
            let next = source[cursor+1]
            (match next with
            | 'n' -> cache.Append '\n'
            | 'r' -> cache.Append '\r'
            | '\\' -> cache.Append '\\'
            | '\"' -> cache.Append '\"'
            | _ -> cache.Append '?') |> ignore
            if cursor+1 = source.Length-1 then (cursor+1, true) else scanString lp (cursor+2) source cache
        else
            cache.Append current |> ignore
            if cursor = source.Length-1 then (cursor, true) else scanString lp (cursor+1) source cache
    let rec private scanComment lp cursor source cache =
        if source[cursor] = '*' && source[cursor+1] = ')' then (cursor+2, false)
        else if cursor < source.Length-1 then scanComment lp (cursor+1) source cache
        else (cursor, true)
    let private isNumber c = c <= '9' && c >= '0'
    let makeLanguagePack (keywords: _stDict) (operators: _stDict) (newLine: char array) =
        let rec makeOpTree (operators: _stDict) =
            let dictdict = Dictionary<char, MaybeDict>()
            let rec addToDict (m: ReadOnlyMemory<char>) (t: Token) (d: Dictionary<char, MaybeDict>) =
                let s = m.Span
                if s.Length = 1 then
                    if d.ContainsKey(s[0]) then
                        let (ADict d) = d[s[0]]
                        d.Add('\000', AValue t)
                    else d.Add(s[0], AValue t)
                else
                    if d.ContainsKey(s[0]) then
                        match d[s[0]] with
                        | AValue v ->
                            let d' = Dictionary<char, MaybeDict>()
                            d'.Add('\000', AValue v)
                            d[s[0]] <- ADict (Dictionary<char, MaybeDict>(d'))
                        | _ -> ()
                    else d.Add(s[0], ADict (Dictionary<char, MaybeDict>()))
                    let (ADict d) = d[s[0]]
                    addToDict (m.Slice 1) t d
            let rec makeDict (dictdict: Dictionary<char, MaybeDict>) (p: KeyValuePair<string, Token>) =
                addToDict (p.Key.AsMemory()) p.Value dictdict
            Seq.iter (makeDict dictdict) operators
            dictdict
        let opChars = ResizeArray<char>()
        Seq.iter 
        <| (fun s -> Seq.iter (fun c -> if opChars.Contains c |> not then opChars.Add c) s)
        <| operators.Keys
        ()
        {keywords = keywords; operatorChars = opChars.ToArray(); operators = makeOpTree operators; newLine = newLine}
