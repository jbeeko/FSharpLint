module FSharpLint.Rules.UselessBinding

open System
open FSharpLint.Framework
open FSharpLint.Framework.Suggestion
open FSharp.Compiler.SyntaxTree
open FSharp.Compiler.SourceCodeServices
open FSharpLint.Framework.Ast
open FSharpLint.Framework.Rules

let private checkForUselessBinding (checkInfo:FSharpCheckFileResults option) pattern expr range =
    match checkInfo with
    | Some checkInfo ->
        let rec findBindingIdentifier = function
            | SynPat.Paren(pattern, _) -> findBindingIdentifier pattern
            | SynPat.Named(_, ident, _, _, _) -> Some(ident)
            | _ -> None

        let checkNotMutable (ident:Ident) =
            async {
                let! symbol =
                    checkInfo.GetSymbolUseAtLocation(
                        ident.idRange.StartLine, ident.idRange.EndColumn, "", [ident.idText])

                let isNotMutable (symbol:FSharpSymbolUse) =
                    match symbol.Symbol with
                    | :? FSharpMemberOrFunctionOrValue as v -> not v.IsMutable
                    | _ -> true

                return
                    match symbol with
                    | Some(symbol) -> isNotMutable symbol
                    | None -> false }

        let rec matchingIdentifier (bindingIdent:Ident) = function
            | SynExpr.Paren(expr, _, _, _) ->
                matchingIdentifier bindingIdent expr
            | SynExpr.Ident(ident) when ident.idText = bindingIdent.idText -> Some ident
            | _ -> None

        findBindingIdentifier pattern
        |> Option.bind (fun bindingIdent -> matchingIdentifier bindingIdent expr)
        |> Option.map (fun ident ->
            { Range = range
              Message = Resources.GetString("RulesUselessBindingError")
              SuggestedFix = None
              TypeChecks = [checkNotMutable ident] })
        |> Option.toArray
    | _ -> Array.empty

let private runner (args:AstNodeRuleParams) =
    match args.AstNode with
    | AstNode.Binding(SynBinding.Binding(_, _, _, isMutable, _, _, _, pattern, _, expr, range, _))
            when Helper.Binding.isLetBinding args.NodeIndex args.SyntaxArray args.SkipArray
            && not isMutable ->
        checkForUselessBinding args.CheckInfo pattern expr range
    | _ ->
        Array.empty

let rule =
    { Name = "UselessBinding"
      Identifier = Identifiers.UselessBinding
      RuleConfig = { AstNodeRuleConfig.Runner = runner; Cleanup = ignore } }
    |> AstNodeRule

