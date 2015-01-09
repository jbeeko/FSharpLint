﻿(*
    FSharpLint, a linter for F#.
    Copyright (C) 2014 Matthew Mcveigh

    This program is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with this program.  If not, see <http://www.gnu.org/licenses/>.
*)

namespace FSharpLint.Framework

open FParsec

module HintParser =

    type Constant =
        | Byte of byte
        | Bytes of byte[]
        | Char of char
        | Decimal of decimal
        | Double of double
        | Int16 of int16
        | Int32 of int32
        | Int64 of int64
        | IntPtr of nativeint
        | SByte of sbyte
        | Single of single
        | UInt16 of uint16
        | UInt32 of uint32
        | UInt64 of uint64
        | UIntPtr of unativeint
        | UserNum of bigint * char
        | String of string
        | Unit
        | Bool of bool
        
    [<RequireQualifiedAccess>]
    type Argument = 
        | Wildcard
        | Variable of char

    type Lambda<'t> =
        {
            Arguments: Argument list
            Body: 't
        }

    [<RequireQualifiedAccess>]
    type Expression =
        | InfixOperator of string * Expression * Expression
        | PrefixOperator of string * Expression
        | FunctionApplication of Expression list
        | Wildcard
        | Variable of char
        | Identifier of string list
        | Constant of Constant
        | Parentheses of Expression
        | Lambda of Lambda<Expression>
        | Tuple of Expression list
        | List of Expression list
        | Array of Expression list
        | If of Expression * Expression * Expression option

    type Hint =
        {
            Match: Expression
            Suggestion: Expression
        }

    let charListToString charList =
        Seq.fold (fun x y -> x + y.ToString()) "" charList

    let pischar chars : Parser<char, 'a> =
        satisfy (fun x -> List.exists ((=) x) chars)

    let pnotchar chars : Parser<char, 'a> =
        satisfy (fun x -> not <| List.exists ((=) x) chars)

    module Operators =
        let pfirstopchar: Parser<char, unit> =
            pischar ['!';'%';'&';'*';'+';'-';'.';'/';'<';'=';'>';'@';'^';'|';'~']

        let opchars =
                [
                    '>';'<';'+';'-';'*';'=';'~';'%';'&';'|';'@'
                    '#';'^';'!';'?';'/';'.';':';',';//'(';')';'[';']'
                ]

        let poperator: Parser<char list, unit> =
            pfirstopchar .>>. many (pischar opchars)
                |>> fun (x, rest) -> x::rest

    /// Need to change isLetter so that it's using unicode character classes.
    module Identifiers =
        let private pidentstartchar: Parser<char, unit> =
            pchar '_' <|> satisfy isLetter

        let private pidentchar: Parser<char, unit> = 
            choice
                [
                    satisfy isLetter
                    satisfy isDigit
                    pchar '\''
                    pchar '_'
                ]

        let private pidenttext: Parser<char list, unit> = 
            pidentstartchar .>>. many pidentchar
                |>> fun (start, rest) -> start::rest

        let private pident: (CharStream<unit> -> Reply<char list>) = 
            let chars = ['`'; '\n'; '\r'; '\t']

            choice
                [
                    pidenttext
                    skipString "``" 
                        >>. many1
                                (choice
                                    [
                                        attempt (pnotchar chars)
                                        attempt (pchar '`' >>. pnotchar chars)
                                    ])
                        .>> skipString "``"
                ]

        let private plongident: (CharStream<unit> -> Reply<char list list>) = 
            choice
                [
                    attempt (sepBy1 pident (skipChar '.'))
                    pident |>> fun x -> [x]
                ]

        let private pidentorop: (CharStream<unit> -> Reply<char list>) =
            choice
                [
                    attempt pident
                    skipChar '(' 
                        .>> spaces
                        >>. Operators.poperator 
                        .>> spaces
                        .>> skipChar ')'
                ]

        let plongidentorop: Parser<string list, unit> = 
            choice
                [
                    attempt pident 
                        .>>. many (attempt (skipChar '.' >>. pident))
                        .>>. opt (skipChar '.' >>. pidentorop)
                        |>> fun ((startIdent, idents), operator) -> 
                                let identifiers = startIdent::idents
                                match operator with
                                    | Some(operator) ->
                                        identifiers@[operator]
                                    | None -> identifiers
                    attempt (pidentorop |>> fun x -> [x])
                    plongident
                ]
                    |>> List.map charListToString

    module StringAndCharacterLiterals =
        let private hexToCharacter hex =
            char(System.Convert.ToInt32(hex, 16))

        let private decimalToCharacter dec =
            char(System.Convert.ToInt32(dec, 10))

        let private escapeMap =
            [ 
                ('"', '\"')
                ('\\', '\\')
                ('\'', '\'')
                ('n', '\n')
                ('t', '\t')
                ('b', '\b')
                ('r', '\r')
                ('a', '\a')
                ('f', '\f')
                ('v', '\v')
            ] |> Map.ofList

        let private pescapechar: Parser<char, unit> = 
            skipChar '\\'
                >>. pischar ['"';'\\';'\'';'n';'t';'b';'r';'a';'f';'v']
                |>> fun x -> Map.find x escapeMap

        let private pnonescapechars: Parser<char, unit> =
            skipChar '\\'
                >>. pnotchar ['"';'\\';'\'';'n';'t';'b';'r';'a';'f';'v']

        let private psimplecharchar: Parser<char, unit> =
            pnotchar ['\n';'\t';'\r';'\b';'\a';'\f';'\v';'\\';'\'']

        let private psimplestringchar: Parser<char, unit> =
            pnotchar ['"';'\n';'\t';'\r';'\b';'\a';'\f';'\v';'\\';'\'']

        let private punicodegraphshort: Parser<char, unit> = 
            skipString "\\u"
                >>. many1 hex
                >>= fun x ->
                    if x.Length <> 4 then
                        fail "Unicode graph short must be 4 hex characters long"
                    else
                        preturn (x |> charListToString |> hexToCharacter)

        let private punicodegraphlong: Parser<char, unit> =
            skipString "\\U"
                >>. many1 hex
                >>= fun x ->
                    if x.Length <> 8 then
                        fail "Unicode graph long must be 8 hex characters long"
                    else
                        preturn (x |> charListToString |> hexToCharacter)

        let private ptrigraph: Parser<char, unit> =
            skipChar '\\'
                >>. many1 digit
                >>= fun x ->
                    if x.Length <> 3 then
                        fail "Trigraph must be 3 characters long"
                    else
                        preturn (x |> charListToString |> decimalToCharacter)

        let private pnewline: Parser<char, unit> = 
            pchar '\n' <|> (skipChar '\r' >>. skipChar '\n' >>% '\n')

        let private pcharchar: (CharStream<unit> -> Reply<char>) =
            choice 
                [
                    attempt psimplecharchar
                    attempt pescapechar
                    attempt ptrigraph
                    punicodegraphshort
                ]

        let private pstringchar: (CharStream<unit> -> Reply<char>) =
            choice
                [
                    attempt psimplestringchar
                    attempt ptrigraph
                    attempt punicodegraphlong
                    attempt punicodegraphshort
                    attempt pescapechar
                    attempt pnonescapechars
                    pnewline
                ]

        let private pstringelem, private pstringelemImpl = createParserForwardedToRef()
        do pstringelemImpl :=
            choice
                [
                    attempt pstringchar
                    skipChar '\\' >>. pnewline >>. many spaces >>. pstringelem
                ]

        let pcharacter: Parser<Constant, unit> =
            skipChar '\'' >>. pcharchar .>> pchar '\''
                |>> Char

        let pliteralstring: Parser<Constant, unit> =
            skipChar '"' >>. many pstringchar .>> skipChar '"'
                |>> fun x -> String(charListToString x)

        let private pverbatimstringchar: (CharStream<unit> -> Reply<char>) =
            choice
                [
                    pstringelem
                    pnonescapechars
                    pnewline
                    pchar '\\'
                    pstring "\"\"" >>% '"'
                ]

        let pverbatimstring: Parser<Constant, unit> =
            pstring "@\"" >>. many pverbatimstringchar .>> pchar '"'
                |>> fun x -> String(charListToString x)

        let private psimplechar: Parser<char, unit> =
            pnotchar ['\n';'\t';'\r';'\b';'\'';'\\';'"']

        let private psimpleorescapechar: Parser<char, unit> =
            pescapechar <|> psimplechar

        let pbytechar: Parser<Constant, unit> =
            skipChar '\'' >>. psimpleorescapechar .>> skipString "'B"
                |>> fun x -> Byte(byte x)

        let pbytearray: Parser<Constant, unit> = 
            skipChar '"' >>. many pstringchar .>> skipString "\"B"
                |>> fun x -> Bytes(System.Text.Encoding.Default.GetBytes(charListToString x))

        let pverbatimbytearray: Parser<Constant, unit> = 
            skipString "@\"" >>. many pverbatimstringchar .>> skipString "\"B"
                |>> fun x -> Bytes(System.Text.Encoding.Default.GetBytes(charListToString x))

        let ptriplequotedstring: Parser<Constant, unit> =
            skipString "\"\"\"" >>. many psimpleorescapechar .>> skipString "\"\"\""
                |>> fun x -> String(charListToString x)

    /// Not supporting hex single and hex float right now.
    /// Decimal float currently will lose precision.
    module NumericLiterals =
        let private pminus: Parser<char, unit> = pchar '-'

        let private minusString (minus:char option, charList) =
            if minus.IsSome then '-' :: charList else charList
                |> charListToString

        let private phexint: Parser<char list, unit> =
            skipChar '0' 
                >>. (skipChar 'x' <|> skipChar 'X')
                >>. many1 hex
                |>> fun x -> '0'::'x'::x

        let private poctalint: Parser<char list, unit> =
            skipChar '0'
                >>. (skipChar 'o' <|> skipChar 'O')
                >>. many1 octal
                |>> fun x -> '0'::'o'::x

        let private pbinaryint: Parser<char list, unit> =
            skipChar '0'
                >>. (skipChar 'b' <|> skipChar 'B')
                >>. many1 (pchar '0' <|> pchar '1')
                |>> fun x -> '0'::'b'::x

        let private pint: (CharStream<unit> -> Reply<char list>) =
            choice
                [
                    attempt phexint
                    attempt poctalint
                    attempt pbinaryint
                    many1 digit
                ]

        let psbyte: Parser<Constant, unit> = 
            (opt pminus) .>>. pint .>> skipChar 'y'
                |>> fun x -> SByte(sbyte(minusString x))

        let pbyte: Parser<Constant, unit> = 
            pint .>> skipString "uy"
                |>> fun x -> Byte(byte(charListToString x))

        let pint16: Parser<Constant, unit> = 
            (opt pminus) .>>. pint .>> skipChar 's'
                |>> fun x -> Int16(int16(minusString x))

        let puint16: Parser<Constant, unit> = 
            pint .>> skipString "us"
                |>> fun x -> UInt16(uint16(charListToString x))

        let puint32: Parser<Constant, unit> = 
            pint .>> (skipString "u" <|> skipString "ul")
                |>> fun x -> UInt32(uint32(charListToString x))

        let pnativeint: Parser<Constant, unit> = 
            (opt pminus) .>>. pint .>> skipChar 'n'
                |>> fun x -> IntPtr(nativeint(int64(minusString x)))

        let punativeint: Parser<Constant, unit> = 
            pint .>> pstring "un"
                |>> fun x -> UIntPtr(unativeint(uint64(charListToString x)))

        let pint64: Parser<Constant, unit> = 
            (opt pminus) .>>. pint .>> skipChar 'L'
                |>> fun x -> Int64(int64(minusString x))

        let puint64: Parser<Constant, unit> = 
            pint .>> (skipString "UL" <|> skipString "uL")
                >>= fun x -> preturn (UInt64(uint64(charListToString x)))

        let psingle: Parser<Constant, unit> =
            (opt pminus) .>>. 
                pfloat .>> (skipChar 'F' <|> skipChar 'f')
                    |>> fun (minus, x) -> Single(if minus.IsSome then -float32(x) else float32(x))

        let private numberFormat = 
            NumberLiteralOptions.AllowMinusSign
            ||| NumberLiteralOptions.AllowFraction
            ||| NumberLiteralOptions.AllowExponent

        let private pnumber: Parser<Constant, unit> =
            numberLiteral numberFormat "number"
            |>> fun nl ->
                    if nl.IsInteger then Int32(int32 nl.String)
                    else Double(double nl.String)

        let pint32: Parser<Constant, unit> = pnumber .>> optional (skipChar 'l')

        let pdouble: Parser<Constant, unit> = pnumber

        let pbignum: Parser<Constant, unit> =
            (opt pminus) .>>. pint .>>. anyOf ['Q'; 'R'; 'Z'; 'I'; 'N'; 'G']
                |>> fun (x, t) -> UserNum(bigint.Parse(minusString x), t)

        let pdecimal: Parser<Constant, unit> =
            let pdecimalint: Parser<Constant, unit> =
                (opt pminus) .>>. pint .>> (skipChar 'M' <|> skipChar 'm')
                    |>> fun x -> Decimal(decimal (minusString x))

            let pdecimalfloat: Parser<Constant, unit> =
                (opt pminus) .>>. pfloat .>> (skipChar 'M' <|> skipChar 'm')
                    |>> fun (minus, x) -> Decimal(decimal (if minus.IsSome then -x else x))

            choice 
                [
                    attempt pdecimalint
                    pdecimalfloat
                ]

    module Constants =
        let private pbool: (CharStream<unit> -> Reply<Constant>) = 
            choice
                [
                    skipString "true" >>% Bool(true)
                    skipString "false" >>% Bool(false)
                ]

        let private punit: Parser<Constant, unit> = 
            skipString "(" 
                >>. ((spaces >>. skipString ")") <|> skipString ")")
                >>% Unit

        let pconstant: Parser<Expression, unit> = 
            choice
                [
                    attempt pbool
                    attempt punit
                    attempt StringAndCharacterLiterals.pcharacter
                    attempt StringAndCharacterLiterals.pliteralstring
                    attempt StringAndCharacterLiterals.pverbatimstring
                    attempt StringAndCharacterLiterals.pbytechar
                    attempt StringAndCharacterLiterals.pbytearray
                    attempt StringAndCharacterLiterals.pverbatimbytearray
                    attempt StringAndCharacterLiterals.ptriplequotedstring
                    attempt NumericLiterals.psbyte
                    attempt NumericLiterals.pbyte
                    attempt NumericLiterals.pint16
                    attempt NumericLiterals.puint16
                    attempt NumericLiterals.puint32
                    attempt NumericLiterals.pnativeint
                    attempt NumericLiterals.punativeint
                    attempt NumericLiterals.pint64
                    attempt NumericLiterals.puint64
                    attempt NumericLiterals.psingle
                    attempt NumericLiterals.pbignum
                    attempt NumericLiterals.pdecimal
                    attempt NumericLiterals.pdouble
                    NumericLiterals.pint32
                ] |>> Expression.Constant

    module Expressions =

        let pwildcard: Parser<Expression, unit> = skipString "_" >>% Expression.Wildcard

        let pargumentwildcard: Parser<Argument, unit> = skipString "_" >>% Argument.Wildcard

        let pvariable: Parser<Expression, unit> = 
            satisfy isLetter 
                .>> notFollowedBy (satisfy isLetter)
                |>> Expression.Variable

        let pargumentvariable: Parser<Argument, unit> = satisfy isLetter |>> Argument.Variable

        let plambdaarguments: Parser<Argument list, unit> = sepEndBy1 (pargumentvariable <|> pargumentwildcard) spaces1

        let pexpression, private pexpressionImpl = createParserForwardedToRef()

        let pparentheses: Parser<Expression, unit> = skipChar '(' >>. pexpression .>> skipChar ')' |>> Expression.Parentheses

        let pif: Parser<Expression, unit> =
            skipString "if"
                >>. spaces
                >>. pexpression
                .>> spaces
                .>> skipString "then"
                .>>. pexpression
                .>>. opt (skipString "else" >>. pexpression)
                |>> fun ((condition, expr), elseExpr) -> Expression.If(condition, expr, elseExpr)

        let ptuple: Parser<Expression, unit> = 
            skipChar '(' 
                >>. pexpression
                .>> skipChar ',' 
                .>>. sepEndBy1 pexpression (skipChar ',')
                .>> skipChar ')' 
                |>> fun (func, rest) -> Expression.Tuple(func::rest)

        let plist: Parser<Expression, unit> = 
            skipChar '['
                >>. spaces
                >>. sepEndBy pexpression (skipChar ';')
                .>> spaces
                .>> skipChar ']'
                |>> Expression.List

        let parray: Parser<Expression, unit> = 
            skipString "[|"
                >>. spaces
                >>. sepEndBy pexpression (skipChar ';')
                .>> spaces
                .>> skipString "|]"
                |>> Expression.Array

        let private plambdastart: Parser<Argument list, unit> = 
            skipString "fun"
                >>. spaces1
                >>. plambdaarguments

        let private plambdaend: Parser<Expression, unit> =
            skipString "->" 
                >>. spaces
                >>. pexpression

        let plambda: Parser<Expression, unit> = 
            let plambdastart: Parser<Argument list, unit> = 
                skipString "fun"
                    >>. spaces1
                    >>. plambdaarguments

            let plambdaend: Parser<Expression, unit> =
                skipString "->" 
                    >>. spaces
                    >>. pexpression

            parse {
                let! arguments = plambdastart

                let! body = plambdaend

                return Expression.Lambda({ Arguments = arguments; Body = body })
            }

        let papplication =
            choice 
                [
                    attempt Constants.pconstant
                    attempt pvariable
                    attempt pwildcard
                    attempt Identifiers.plongidentorop |>> Expression.Identifier
                    attempt ptuple
                    attempt plist
                    attempt parray
                    attempt pparentheses
                ]

        let pfunctionapplication: Parser<Expression, unit> =
            Identifiers.plongidentorop 
                |>> Expression.Identifier
                .>> spaces
                .>>. sepEndBy1 papplication spaces
                |>> fun (func, rest) -> Expression.FunctionApplication(func::rest)

        let opp = OperatorPrecedenceParser<Expression, string, unit>()

        let prefixoperatorterm: Parser<Expression, unit> = 
            followedBy (pischar ['+';'-';'%';'&';'!';'~']) >>. opp.ExpressionParser

        opp.TermParser <- 
            spaces >>.
            choice 
                [
                    attempt pif
                    attempt Constants.pconstant
                    attempt plambda
                    attempt pvariable
                    attempt pwildcard
                    attempt pfunctionapplication
                    attempt Identifiers.plongidentorop |>> Expression.Identifier
                    attempt ptuple
                    attempt plist
                    attempt parray
                    attempt pparentheses
                    prefixoperatorterm
                ] .>> spaces

        // a helper function for adding infix operators to opp
        let addInfixOperator prefix precedence associativity =
            let remainingOpChars_ws = 
                if prefix = "=" then
                    notFollowedBy (pstring "==>") |>> fun _ -> ""
                else if prefix = "|" then
                    notFollowedBy (pstring "]") |>> fun _ -> ""
                else
                    manySatisfy (isAnyOf Operators.opchars)

            let op = InfixOperator(prefix, remainingOpChars_ws,
                                   precedence, associativity, (),
                                   fun remOpChars expr1 expr2 ->
                                        Expression.InfixOperator(prefix + remOpChars, expr1, expr2))
            opp.AddOperator(op)

        let addPrefixOperator op precedence =
            opp.AddOperator(PrefixOperator(op, spaces >>. preturn "", precedence, true, 
                                                fun expr ->
                                                    Expression.PrefixOperator(op, expr)))

        do
            addInfixOperator ":="  3 Associativity.Right

            addInfixOperator "or"  4 Associativity.Left
            addInfixOperator "||"  4 Associativity.Left

            addInfixOperator "|"  5 Associativity.Left

            addInfixOperator "&"  6 Associativity.Left
            addInfixOperator "&&"  6 Associativity.Left

            addInfixOperator "<"  7 Associativity.Left
            addInfixOperator ">"  7 Associativity.Left
            addInfixOperator "="  7 Associativity.Left
            for i in Operators.opchars do
                if i <> '&' then
                    addInfixOperator ("&" + i.ToString())  7 Associativity.Left

            addInfixOperator "&&&"  8 Associativity.Left
            addInfixOperator "|||"  8 Associativity.Left
            addInfixOperator "^^^"  8 Associativity.Left
            addInfixOperator "~~~"  8 Associativity.Left
            addInfixOperator "<<<"  8 Associativity.Left
            addInfixOperator ">>>"  8 Associativity.Left

            addInfixOperator "^"  9 Associativity.Right

            addInfixOperator "::"  10 Associativity.Right
            addInfixOperator "@"  10 Associativity.Right

            addInfixOperator ":?>"  11 Associativity.None
            addInfixOperator ":?"  11 Associativity.None

            addInfixOperator "-"  12 Associativity.Left
            addInfixOperator "+"  12 Associativity.Left

            addInfixOperator "*"  13 Associativity.Left
            addInfixOperator "/"  13 Associativity.Left
            addInfixOperator "%"  13 Associativity.Left

            addInfixOperator "**" 14 Associativity.Right
            
            addPrefixOperator "+" 15
            addPrefixOperator "-" 15
            addPrefixOperator "%" 15
            addPrefixOperator "%%" 15
            addPrefixOperator "&" 15
            addPrefixOperator "&&" 15
            addPrefixOperator "!" 15
            addPrefixOperator "~" 15

            pexpressionImpl := opp.ExpressionParser

    let phint: Parser<Hint, unit> = 
        let phintcenter: Parser<unit, unit> = 
            spaces 
                .>> skipString "===>"
                .>> spaces

        parse {
            let! m = Expressions.pexpression 

            do! phintcenter

            let! s = Expressions.pexpression

            return { Match = m; Suggestion = s }
        }