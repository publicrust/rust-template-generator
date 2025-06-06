﻿using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Library.Extensions;
using Library.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Library
{
    public static partial class AssemblyDataSerializer
    {
        public class HookFinderVisitor : CSharpSyntaxWalker
        {
            private readonly SemanticModel semanticModel;
            public Dictionary<string, HookModel> Hooks = new Dictionary<string, HookModel>();
            private readonly HashSet<string> validOxideTypes = new HashSet<string>
            {
                "Oxide.Core.Interface",
                "Oxide.Core.OxideMod",
                "Oxide.Core.Libraries.Plugins",
                "Oxide.Core.Plugins.Plugin",
                "Oxide.Core.Plugins.PluginManager",
                "Oxide.Plugins.CSharpPlugin",
            };

            public HookFinderVisitor(SemanticModel semanticModel)
            {
                this.semanticModel = semanticModel;
            }

            public override void VisitInvocationExpression(InvocationExpressionSyntax invocation)
            {
                if (invocation == null)
                    return;

                try
                {
                    // Получаем информацию о содержащем методе
                    var containingMethod = invocation
                        .Ancestors()
                        .OfType<MethodDeclarationSyntax>()
                        .FirstOrDefault();

                    // Получаем информацию о выражении и его типе
                    var memberAccessExpr = invocation.Expression as MemberAccessExpressionSyntax;
                    var expression = memberAccessExpr?.Expression;
                    var expressionTypeInfo =
                        expression != null ? semanticModel.GetTypeInfo(expression) : default;
                    var expressionType = expressionTypeInfo.Type;

                    // Если это прямой вызов метода (без this/base), получаем тип из содержащего класса
                    if (expressionType == null && invocation.Expression != null)
                    {
                        var containingClass = invocation
                            .Ancestors()
                            .OfType<ClassDeclarationSyntax>()
                            .FirstOrDefault();
                        if (containingClass != null)
                        {
                            var classSymbol = semanticModel.GetDeclaredSymbol(containingClass);
                            if (classSymbol != null)
                            {
                                expressionType = classSymbol;
                            }
                        }
                    }

                    // Проверяем, является ли тип вызывающего объекта одним из допустимых типов Oxide или их наследником
                    bool isValidType = false;
                    if (expressionType != null)
                    {
                        var currentType = expressionType;
                        while (currentType != null)
                        {
                            if (validOxideTypes.Contains(currentType.ToString()))
                            {
                                isValidType = true;
                                break;
                            }
                            currentType = currentType.BaseType;
                        }
                    }

                    if (!isValidType)
                    {
                        return;
                    }

                    // Проверяем различные варианты вызова хуков
                    string methodName;
                    if (memberAccessExpr != null)
                    {
                        methodName = memberAccessExpr.Name.ToString();
                    }
                    else if (invocation.Expression is IdentifierNameSyntax identifierName)
                    {
                        methodName = identifierName.Identifier.Text;
                    }
                    else
                    {
                        methodName = string.Empty;
                    }

                    var isCallHook =
                        methodName == "CallHook"
                        || methodName == "DirectCallHook"
                        || methodName == "OnCallHook"
                        || methodName == "Call";

                    if (isCallHook)
                    {
                        // Проверяем, что первый аргумент - строковая константа
                        var args =
                            invocation.ArgumentList?.Arguments
                            ?? new SeparatedSyntaxList<ArgumentSyntax>();
                        var firstArg = args.FirstOrDefault();

                        // Проверяем, является ли первый аргумент строковым литералом
                        if (
                            firstArg?.Expression is not LiteralExpressionSyntax literal
                            || literal.Kind() != SyntaxKind.StringLiteralExpression
                        )
                        {
                            return;
                        }

                        // Получаем имя хука из строкового литерала
                        string hookName = literal.Token.ValueText;

                        if (string.IsNullOrEmpty(hookName))
                        {
                            return;
                        }

                        var parameters = new List<string>();

                        // Обрабатываем все аргументы кроме первого (имени хука)
                        foreach (var arg in args.Skip(1))
                        {
                            if (arg?.Expression == null)
                                continue;

                            // Пропускаем null параметры
                            if (
                                arg.Expression is LiteralExpressionSyntax nullLiteral
                                && nullLiteral.Kind() == SyntaxKind.NullLiteralExpression
                            )
                            {
                                continue;
                            }

                            // Если это массив с инициализатором
                            if (
                                arg.Expression is ArrayCreationExpressionSyntax arrayCreation
                                && arrayCreation.Initializer != null
                            )
                            {
                                foreach (var element in arrayCreation.Initializer.Expressions)
                                {
                                    if (element != null)
                                    {
                                        // Пропускаем null элементы массива
                                        if (
                                            element is LiteralExpressionSyntax nullElement
                                            && nullElement.Kind()
                                                == SyntaxKind.NullLiteralExpression
                                        )
                                        {
                                            continue;
                                        }

                                        var typeInfo = semanticModel.GetTypeInfo(element);
                                        var type = typeInfo.Type?.ToString() ?? "unknown";
                                        var parameterName = ProcessParameterName(
                                            element.ToString(),
                                            type
                                        );
                                        parameters.Add($"{type} {parameterName}");
                                    }
                                }
                            }
                            // Если это просто переменная или выражение
                            else
                            {
                                var typeInfo = semanticModel.GetTypeInfo(arg.Expression);
                                var type = typeInfo.Type?.ToString() ?? "unknown";
                                var parameterName = ProcessParameterName(
                                    arg.Expression.ToString(),
                                    type
                                );
                                parameters.Add($"{type} {parameterName}");
                            }
                        }

                        var hash = $"{hookName}({string.Join(", ", parameters)})";

                        if (!Hooks.ContainsKey(hash))
                        {
                            ProcessHook(invocation, hash, hookName, parameters);
                        }
                        else { }
                    }
                }
                catch (Exception ex) { }

                base.VisitInvocationExpression(invocation);
            }

            private string ProcessParameterName(string parameterName, string type)
            {
                if (string.IsNullOrEmpty(parameterName))
                {
                    return "param";
                }

                // Обработка случая с this
                if (parameterName == "this" && type != "unknown")
                {
                    return char.ToLower(type[0]) + type.Substring(1);
                }

                // Обработка вызовов методов
                while (parameterName.Contains("(") && parameterName.Contains(")"))
                {
                    var openBracket = parameterName.LastIndexOf('(');
                    var closeBracket = parameterName.IndexOf(')', openBracket);
                    if (openBracket > 0)
                    {
                        var methodStart = parameterName.LastIndexOf('.', openBracket);
                        methodStart = methodStart == -1 ? 0 : methodStart + 1;
                        var methodName = parameterName.Substring(
                            methodStart,
                            openBracket - methodStart
                        );
                        if (!string.IsNullOrEmpty(methodName))
                        {
                            methodName = char.ToLower(methodName[0]) + methodName.Substring(1);
                            parameterName =
                                parameterName.Substring(0, methodStart)
                                + methodName
                                + parameterName.Substring(closeBracket + 1);
                        }
                    }
                    else
                    {
                        break;
                    }
                }

                // Обработка точек
                if (parameterName.Contains('.'))
                {
                    var parts = parameterName.Split('.');
                    if (parts.Length > 0)
                    {
                        parameterName = parts[0];
                        for (int i = 1; i < parts.Length; i++)
                        {
                            if (!string.IsNullOrEmpty(parts[i]))
                            {
                                parameterName += char.ToUpper(parts[i][0]) + parts[i].Substring(1);
                            }
                        }
                    }
                }

                return string.IsNullOrEmpty(parameterName)
                    ? "param"
                    : parameterName.Replace("ToString", "");
            }

            private void ProcessHook(
                InvocationExpressionSyntax invocation,
                string hash,
                string hookName,
                List<string> parameters
            )
            {
                var syntaxTree = invocation.SyntaxTree;
                var root = syntaxTree.GetRoot();

                // Ищем все родительские узлы до корня
                var currentNode = invocation.Parent;
                MethodDeclarationSyntax containingMethod = null;
                LocalFunctionStatementSyntax localFunction = null;
                ClassDeclarationSyntax containingClass = null;

                while (currentNode != null)
                {
                    if (currentNode is LocalFunctionStatementSyntax localFunc)
                    {
                        localFunction = localFunc;
                    }
                    else if (currentNode is MethodDeclarationSyntax method)
                    {
                        containingMethod = method;
                    }
                    else if (currentNode is ClassDeclarationSyntax classDecl)
                    {
                        containingClass = classDecl;
                        break;
                    }

                    currentNode = currentNode.Parent;
                }

                // Если не нашли класс в родительских узлах, ищем в более широком контексте
                if (containingClass == null)
                {
                    containingClass = root.DescendantNodes()
                        .OfType<ClassDeclarationSyntax>()
                        .FirstOrDefault();
                    if (containingClass != null) { }
                }

                // Если нашли локальную функцию, используем её как метод
                var methodToUse =
                    localFunction != null ? (SyntaxNode)localFunction : containingMethod;

                if (methodToUse != null)
                {
                    var className = containingClass?.Identifier.Text ?? "UnknownClass";
                    var methodName =
                        localFunction != null
                            ? $"{containingMethod?.Identifier.Text ?? ""}{(containingMethod != null ? "." : "")}{localFunction.Identifier.Text}"
                            : containingMethod?.Identifier.Text;

                    // Получаем позицию начала метода/локальной функции
                    var methodStartLine = methodToUse
                        .GetLocation()
                        .GetLineSpan()
                        .StartLinePosition.Line;
                    var hookCallLine = invocation
                        .GetLocation()
                        .GetLineSpan()
                        .StartLinePosition.Line;
                    var lineNumber = hookCallLine - methodStartLine + 1;

                    // Получаем параметры из метода или локальной функции
                    var parameterList =
                        localFunction?.ParameterList ?? containingMethod?.ParameterList;
                    var methodParameters =
                        parameterList
                            ?.Parameters.Select(p => new ParameterModel
                            {
                                ParameterType = p.Type?.GetFriendlyTypeName() ?? "unknown",
                                ParameterName = p.Identifier.Text,
                            })
                            .Where(p => p.ParameterType != "unknown")
                            .ToList() ?? new List<ParameterModel>();

                    var hookModel = new HookModel
                    {
                        HookName = hookName,
                        HookParameters = $"({string.Join(", ", parameters)})",
                        MethodParameters = methodParameters,
                        ClassName = className,
                        MethodName = methodName ?? "UnknownMethod",
                        MethodCode = methodToUse.ToFullString(),
                        LineNumber = lineNumber,
                    };

                    Hooks.Add(hash, hookModel);
                }
                else
                {
                    // Дополнительная отладочная информация





                    // Выводим все родительские узлы
                    var parent = invocation.Parent;
                    var level = 0;
                    while (parent != null)
                    {
                        parent = parent.Parent;
                    }
                }
            }

            private MethodDeclarationSyntax? FindContainingMethod(
                InvocationExpressionSyntax invocation
            )
            {
                // Проверяем, находится ли вызов внутри локальной функции
                var localFunction = invocation
                    .Ancestors()
                    .OfType<LocalFunctionStatementSyntax>()
                    .FirstOrDefault();
                if (localFunction != null)
                {
                    // Ищем содержащий метод для локальной функции
                    var containingMethod = localFunction
                        .Ancestors()
                        .OfType<MethodDeclarationSyntax>()
                        .FirstOrDefault();
                    if (containingMethod != null)
                    {
                        return containingMethod;
                    }
                }

                // Пробуем найти обычный метод через Ancestors
                var method = invocation
                    .Ancestors()
                    .OfType<MethodDeclarationSyntax>()
                    .FirstOrDefault();
                if (method != null)
                {
                    return method;
                }

                return null;
            }
        }
    }
}
