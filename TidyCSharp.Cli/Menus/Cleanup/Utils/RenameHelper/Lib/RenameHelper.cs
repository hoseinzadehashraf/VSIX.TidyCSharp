﻿using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace TidyCSharp.Cli.Menus.Cleanup.Utils.RenameHelper.Lib;

internal static partial class RenameHelper
{
    public static async Task<Solution> RenameSymbolAsync(Document document, SyntaxNode root, SyntaxToken declarationToken, string newName, CancellationToken cancellationToken = default(CancellationToken))
    {
        var annotatedRoot = root.ReplaceToken(declarationToken, declarationToken.WithAdditionalAnnotations(RenameAnnotation.Create()));
        var annotatedSolution = document.Project.Solution.WithDocumentSyntaxRoot(document.Id, annotatedRoot);
        var annotatedDocument = annotatedSolution.GetDocument(document.Id);

        annotatedRoot = await annotatedDocument.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        var annotatedToken = annotatedRoot.FindToken(declarationToken.SpanStart);

        var semanticModel = await annotatedDocument.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        var symbol = semanticModel.GetDeclaredSymbol(annotatedToken.Parent, cancellationToken);

        var newSolution = await Microsoft.CodeAnalysis.Rename.Renamer.RenameSymbolAsync(annotatedSolution, symbol, newName, null, cancellationToken).ConfigureAwait(false);

        // TODO: return annotatedSolution instead of newSolution if newSolution contains any new errors (for any project)
        return newSolution;
    }

    public static async Task<bool> IsValidNewMemberNameAsync(SemanticModel semanticModel, ISymbol symbol, string name, CancellationToken cancellationToken = default(CancellationToken))
    {
        if (symbol.Kind == SymbolKind.NamedType)
        {
            var typeKind = ((INamedTypeSymbol)symbol).TypeKind;

            // If the symbol is a class or struct, the name can't be the same as any of its members.
            if (typeKind == TypeKind.Class || typeKind == TypeKind.Struct)
            {
                var members = (symbol as INamedTypeSymbol)?.GetMembers(name);

                if (members.HasValue && !members.Value.IsDefaultOrEmpty)
                {
                    return false;
                }
            }
        }

        var containingSymbol = symbol.ContainingSymbol;

        if (symbol.Kind == SymbolKind.TypeParameter)
        {
            // If the symbol is a type parameter, the name can't be the same as any type parameters of the containing type.
            var parentSymbol = containingSymbol?.ContainingSymbol as INamedTypeSymbol;

            if (parentSymbol != null
                && parentSymbol.TypeParameters.Any(t => t.Name == name))
            {
                return false;
            }

            // Move up one level for the next validation step.
            containingSymbol = containingSymbol?.ContainingSymbol;
        }

        var containingNamespaceOrTypeSymbol = containingSymbol as INamespaceOrTypeSymbol;

        if (containingNamespaceOrTypeSymbol != null)
        {
            if (containingNamespaceOrTypeSymbol.Kind == SymbolKind.Namespace)
            {
                // Make sure to use the compilation namespace so interfaces in referenced assemblies are considered
                containingNamespaceOrTypeSymbol = semanticModel.Compilation.GetCompilationNamespace((INamespaceSymbol)containingNamespaceOrTypeSymbol);
            }
            else if (containingNamespaceOrTypeSymbol.Kind == SymbolKind.NamedType)
            {
                var typeKind = ((INamedTypeSymbol)containingNamespaceOrTypeSymbol).TypeKind;

                // If the containing type is a class or struct, the name can't be the same as the name of the containing
                // type.
                if ((typeKind == TypeKind.Class || typeKind == TypeKind.Struct)
                    && containingNamespaceOrTypeSymbol.Name == name)
                {
                    return false;
                }
            }

            // The name can't be the same as the name of an other member of the same type. At this point no special
            // consideration is given to overloaded methods.
            return containingNamespaceOrTypeSymbol.GetMembers(name).IsDefaultOrEmpty;
        }
        else if (containingSymbol.Kind == SymbolKind.Method)
        {
            var methodSymbol = (IMethodSymbol)containingSymbol;

            if (methodSymbol.Parameters.Any(i => i.Name == name)
                || methodSymbol.TypeParameters.Any(i => i.Name == name))
            {
                return false;
            }

            var outermostMethod = methodSymbol;

            while (outermostMethod.ContainingSymbol.Kind == SymbolKind.Method)
            {
                outermostMethod = (IMethodSymbol)outermostMethod.ContainingSymbol;

                if (outermostMethod.Parameters.Any(i => i.Name == name)
                    || outermostMethod.TypeParameters.Any(i => i.Name == name))
                {
                    return false;
                }
            }

            foreach (var syntaxReference in outermostMethod.DeclaringSyntaxReferences)
            {
                var syntaxNode = await syntaxReference.GetSyntaxAsync(cancellationToken).ConfigureAwait(false);
                var localNameFinder = new LocalNameFinder(name);
                localNameFinder.Visit(syntaxNode);
                if (localNameFinder.Found) return false;
            }

            return true;
        }
        else
        {
            return true;
        }
    }

    //public static SyntaxNode GetParentDeclaration(SyntaxToken token)
    //{
    //    var parent = token.Parent;

    //    while (parent != null)
    //    {
    //        switch ((SyntaxKind)parent.RawKind)
    //        {
    //            case SyntaxKind.VariableDeclarator:
    //            case SyntaxKind.Parameter:
    //            case SyntaxKind.TypeParameter:
    //            case SyntaxKind.CatchDeclaration:
    //            case SyntaxKind.ExternAliasDirective:
    //            case SyntaxKind.QueryContinuation:
    //            case SyntaxKind.FromClause:
    //            case SyntaxKind.LetClause:
    //            case SyntaxKind.JoinClause:
    //            case SyntaxKind.JoinIntoClause:
    //            case SyntaxKind.ForEachStatement:
    //            case SyntaxKind.UsingDirective:
    //            case SyntaxKind.LabeledStatement:
    //            case SyntaxKind.AnonymousObjectMemberDeclarator:
    //            case SyntaxKindEx.LocalFunctionStatement:
    //            case SyntaxKindEx.SingleVariableDesignation:
    //                return parent;

    //            default:
    //                var declarationParent = parent as MemberDeclarationSyntax;
    //                if (declarationParent != null)
    //                {
    //                    return declarationParent;
    //                }

    //                break;
    //        }

    //        parent = parent.Parent;
    //    }

    //    return null;
    //}

    private class LocalNameFinder : CSharpSyntaxWalker
    {
        private readonly string _name;
        public LocalNameFinder(string name) => _name = name;

        public bool Found
        {
            get;
            private set;
        }

        public override void Visit(SyntaxNode node)
        {
            switch ((SyntaxKind)node.RawKind)
            {
                case SyntaxKindEx.LocalFunctionStatement:
                    Found |= ((LocalFunctionStatementSyntaxWrapper)node).Identifier.ValueText == _name;
                    break;

                case SyntaxKindEx.SingleVariableDesignation:
                    Found |= ((SingleVariableDesignationSyntaxWrapper)node).Identifier.ValueText == _name;
                    break;

                default:
                    break;
            }

            base.Visit(node);
        }

        public override void VisitVariableDeclarator(VariableDeclaratorSyntax node)
        {
            Found |= node.Identifier.ValueText == _name;
            base.VisitVariableDeclarator(node);
        }

        public override void VisitParameter(ParameterSyntax node)
        {
            Found |= node.Identifier.ValueText == _name;
            base.VisitParameter(node);
        }

        public override void VisitTypeParameter(TypeParameterSyntax node)
        {
            Found |= node.Identifier.ValueText == _name;
            base.VisitTypeParameter(node);
        }

        public override void VisitCatchDeclaration(CatchDeclarationSyntax node)
        {
            Found |= node.Identifier.ValueText == _name;
            base.VisitCatchDeclaration(node);
        }

        public override void VisitQueryContinuation(QueryContinuationSyntax node)
        {
            Found |= node.Identifier.ValueText == _name;
            base.VisitQueryContinuation(node);
        }

        public override void VisitFromClause(FromClauseSyntax node)
        {
            Found |= node.Identifier.ValueText == _name;
            base.VisitFromClause(node);
        }

        public override void VisitLetClause(LetClauseSyntax node)
        {
            Found |= node.Identifier.ValueText == _name;
            base.VisitLetClause(node);
        }

        public override void VisitJoinClause(JoinClauseSyntax node)
        {
            Found |= node.Identifier.ValueText == _name;
            base.VisitJoinClause(node);
        }

        public override void VisitJoinIntoClause(JoinIntoClauseSyntax node)
        {
            Found |= node.Identifier.ValueText == _name;
            base.VisitJoinIntoClause(node);
        }

        public override void VisitForEachStatement(ForEachStatementSyntax node)
        {
            Found |= node.Identifier.ValueText == _name;
            base.VisitForEachStatement(node);
        }

        public override void VisitLabeledStatement(LabeledStatementSyntax node)
        {
            Found |= node.Identifier.ValueText == _name;
            base.VisitLabeledStatement(node);
        }

        public override void VisitAnonymousObjectMemberDeclarator(AnonymousObjectMemberDeclaratorSyntax node)
        {
            Found |= node.NameEquals?.Name?.Identifier.ValueText == _name;
            base.VisitAnonymousObjectMemberDeclarator(node);
        }
    }
}