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
    let inline private eatSpaceOfTuple (t, tokens, errors) =
        match tokens with
        | TokenSpace::r -> t, r, errors
        | _ -> t, tokens, errors
    let rec parse (tokens: Token list) (errors: string list) =
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