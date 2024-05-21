namespace Aestas.Commands
open System.Collections.Generic
open System
open System.Reflection
open Lagrange.Core
open Lagrange.Core.Message
open Aestas
open AestasTypes
open Lexer
open Parser
module Command =
    type AestasCommandDomain = None = 0 | Private = 1 | Group = 2 | Console = 4 | Chat = 3 | All = 7
    type AestasCommandAttribute =
        inherit System.Attribute
        val Name: string
        val Domain: AestasCommandDomain
        new(name, domain) = { Name = name; Domain = domain }
    let getCommands filter =
        let ret = Dictionary<string, ICommand>()
        (fun (t: Type) ->
            t.GetCustomAttributes(typeof<AestasCommandAttribute>, false)
            |> Array.iter (fun attr ->
                if attr :?> AestasCommandAttribute |> filter then
                    let command = Activator.CreateInstance(t) :?> ICommand
                    let name = (attr :?> AestasCommandAttribute).Name
                    ret.Add(name, command)
            )
        )|> Array.iter <| Assembly.GetExecutingAssembly().GetTypes()
        ret
    let keywords = readOnlyDict [
        "print", TokenPrint
    ]
    let symbols = readOnlyDict [
        "<|", TokenLeftPipe
        "|>", TokenRightPipe
        "<-", TokenLeftArrow
        "->", TokenRightArrow
        ":", TokenColon
        ";", TokenSemicolon
        "(", TokenLeftRound
        ")", TokenRightRound
        "[", TokenLeftSquare
        "]", TokenRightSquare
        "{", TokenLeftCurly
        "}", TokenRightCurly
        ".", TokenDot
        ",", TokenComma
        "|", TokenPipe
    ]
    let newLine = [|'\n';'\r';'`'|]
    let rec private excecuteAst (env: CommandEnvironment) (ast: Ast) =
        match ast with
        | Tuple items ->
            let rec go acc = function
            | Call h::t ->
                go (excecuteAst env (Call h)::acc) t
            | Tuple h::t ->
                go (excecuteAst env (Tuple h)::acc) t
            | Atom h::t ->
                go (h::acc) t
            | [] -> acc |> List.rev |> AtomTuple
            go [] items
        | Call args ->
            let func = args.Head
            match excecuteAst env func with
            | Identifier "conslog" ->
                if args.Tail.Tail.IsEmpty |> not then env.log "To much arguments, try use tuple."; Unit else
                let conslog = printfn "At %d %s" (if env.chain.GroupUin.HasValue then env.chain.GroupUin.Value else env.chain.FriendUin)
                let result = excecuteAst {aestas = env.aestas; context = env.context; chain = env.chain; commands = env.commands; log = conslog; model = env.model } args.Tail.Head
                conslog $"returns {result}"
                Unit
            | Identifier name ->
                if env.commands.ContainsKey name then
                    let args = List.map (fun x -> excecuteAst env x) args.Tail
                    env.commands[name].Execute(env, args)
                else
                    env.log <| $"Command not found: {name}"
                    Unit
            | x -> 
                env.log <| $"Expected identifier, but found {x}"
                Unit
        | Atom x -> x
    let LanguagePack = makeLanguagePack keywords symbols newLine
    let excecute (env: CommandEnvironment) (cmd: string) =
        let tokens = scanWithoutMacro LanguagePack cmd
        let ast, _, errors = parse tokens []
        printfn "%A,%A,%A" tokens ast errors
        match errors with
        | [] -> 
            match excecuteAst env ast with
            | Unit -> ()
            | x -> env.log <| x.ToString()
        | _ -> env.log <| String.Join("\n", "Error occured:"::errors)