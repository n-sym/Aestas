namespace Aestas.Commands
open System
open System.Text
open System.Collections.Generic
open System.Linq
open Aestas
open AestasTypes
open Lexer
module rec Parser =
    let inline private eatSpace tokens =
        match tokens with
        | TokenSpace::r -> r
        | _ -> tokens
    let inline private eatSpaceAndNewLine tokens =
        match tokens with
        | TokenSpace::r -> r
        | TokenNewLine::r -> r
        | _ -> tokens
    let inline private eatSpaceOfTuple (t, tokens, errors) =
        match tokens with
        | TokenSpace::r -> t, r, errors
        | _ -> t, tokens, errors
    let parseAbstractTuple seperator multiLine makeTuple spSingleItem parseItem failMsg failValue tokens errors = 
        let rec innerRec tokens errors result =
            match eatSpace tokens with
            | TokenNewLine::r when multiLine ->
                match r with
                | TokenRightCurly::_ | TokenRightSquare::_ | TokenRightRound::_ -> 
                    result |> List.rev, tokens, errors
                | _ ->
                    let item, tokens, errors = parseItem (eatSpaceAndNewLine r) errors
                    innerRec tokens errors (item::result)
            | x::r when x = seperator ->
                let item, tokens, errors = parseItem r errors
                innerRec tokens errors (item::result)
            | _ -> result |> List.rev, tokens, errors
        match parseItem ((if multiLine then eatSpaceAndNewLine else eatSpace) tokens) errors with
        | item, tokens, [] ->
            let items, tokens, errors = innerRec tokens errors [item]
            match errors with 
            | [] -> (match items with | [e] when spSingleItem -> e | _ -> makeTuple items), tokens, errors
            | _ -> failValue, tokens, $"{failMsg} item, but found {tokens[0..2]}"::errors
        | _, _, errors -> failValue, tokens, $"{failMsg} tuple, but found {tokens[0..2]}"::errors
    /// tuple = tupleItem {"," tupleItem}
    let parse tokens errors = 
        parseAbstractTuple TokenComma false Tuple true parseTupleItem "Expected expression" (Atom Unit) tokens errors
    let rec parseTupleItem (tokens: Token list) (errors: string list) =
        let rec go tokens acc errors =
            match tokens with
            | [] -> acc |> List.rev, [], errors
            | TokenSpace::TokenLeftRound::xs
            | TokenLeftRound::xs ->
                match parse xs errors |> eatSpaceOfTuple with
                | ast, TokenRightRound::rest, errors -> go rest (ast::acc) errors
                | _, x::_, _ -> [], tokens, $"Expected \")\", but found \"{x}\""::errors
                | _ -> [], tokens, "Unexpected end of input"::errors
            | TokenSpace::xs ->
                let ast, rest, errors = parseAtom xs errors
                go rest ((Atom ast)::acc) errors
            | _ -> acc |> List.rev, tokens, errors
        match tokens |> eatSpace with
        | TokenLeftRound::xs ->
            match parse xs errors |> eatSpaceOfTuple with
            | ast, TokenRightRound::rest, errors -> 
                let func = ast
                let args, tokens, errors = go rest [] errors
                Call (func::args), tokens, errors
            | _, x::_, _ -> Atom Unit, tokens, $"Expected \")\", but found \"{x}\""::errors
            | _ -> Atom Unit, tokens, "Unexpected end of input"::errors
        | _ ->
            let func, rest, errors = parseAtom tokens errors
            let args, tokens, errors = go rest [] errors
            Call (Atom func::args), tokens, errors
        //go tokens [] errors
    let rec parseAtom tokens errors =
        match tokens |> eatSpace with
        | TokenFloat x::xs -> Number x, xs, errors
        | TokenString x::xs -> String x, xs, errors
        | TokenIdentifier x::xs -> Identifier x, xs, errors
        | x -> Unit, x, $"Unexpected token \"{x}\""::errors