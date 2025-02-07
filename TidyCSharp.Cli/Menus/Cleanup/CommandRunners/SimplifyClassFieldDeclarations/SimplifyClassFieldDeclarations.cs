using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using TidyCSharp.Cli.Menus.Cleanup.CommandRunners._Infra;
using TidyCSharp.Cli.Menus.Cleanup.CommandRunners.SimplifyClassFieldDeclarations.Option;
using TidyCSharp.Cli.Menus.Cleanup.SyntaxNodeExtractors;
using TidyCSharp.Cli.Menus.Cleanup.Utils;

namespace TidyCSharp.Cli.Menus.Cleanup.CommandRunners.SimplifyClassFieldDeclarations;

public class SimplifyClassFieldDeclarations : CodeCleanerCommandRunnerBase
{
    public override async Task<SyntaxNode> CleanUpAsync(SyntaxNode initialSourceNode)
    {
        return SimplifyClassFieldDeclarationsHelper(initialSourceNode, IsReportOnlyMode, Options);
    }

    public SyntaxNode SimplifyClassFieldDeclarationsHelper(SyntaxNode initialSourceNode, bool isReportOnlyMode, ICleanupOption options)
    {
        var rewriter = new Rewriter(isReportOnlyMode, options);
        var modifiedSourceNode = rewriter.Visit(initialSourceNode);

        if (isReportOnlyMode)
        {
            CollectMessages(rewriter.GetReport());
            return initialSourceNode;
        }

        return modifiedSourceNode;
    }

    private class Rewriter : CleanupCSharpSyntaxRewriter
    {
        public Rewriter(bool isReportOnlyMode, ICleanupOption options) :
            base(isReportOnlyMode, options)
        {
        }

        public override SyntaxNode VisitClassDeclaration(ClassDeclarationSyntax node)
        {
            if (CheckOption((int)CleanupTypes.GroupAndMergeClassFields))
            {
                node = Apply(node) as ClassDeclarationSyntax;
                return node;
            }

            return base.VisitClassDeclaration(node);
        }

        public override SyntaxNode VisitVariableDeclarator(VariableDeclaratorSyntax node)
        {
            if (node.Initializer == null) return base.VisitVariableDeclarator(node);
            if (node.Parent is VariableDeclarationSyntax == false) return base.VisitVariableDeclarator(node);
            if (node.Parent.Parent is FieldDeclarationSyntax == false) return base.VisitVariableDeclarator(node);
            if ((node.Parent.Parent as FieldDeclarationSyntax).Modifiers.Any(x => x.ValueText == "const")) return base.VisitVariableDeclarator(node);

            var value = node.Initializer.Value;

            if
            (
                !CheckOption((int)CleanupTypes.RemoveClassFieldsInitializerNull) &&
                !CheckOption((int)CleanupTypes.RemoveClassFieldsInitializerLiteral)
            )
                return base.VisitVariableDeclarator(node);

            if (value is LiteralExpressionSyntax)
            {
                var variableTypeNode = GetSystemTypeOfTypeNode((node.Parent as VariableDeclarationSyntax));
                var valueObj = (value as LiteralExpressionSyntax).Token.Value;

                if (TypesMapItem.GetAllPredefinedTypesDic().ContainsKey(variableTypeNode))
                {
                    if (CheckOption((int)CleanupTypes.RemoveClassFieldsInitializerLiteral) == false) return base.VisitVariableDeclarator(node);

                    var typeItem = TypesMapItem.GetAllPredefinedTypesDic()[variableTypeNode];

                    if ((typeItem.DefaultValue == null && valueObj != null) || (typeItem.DefaultValue != null && !typeItem.DefaultValue.Equals(valueObj)))
                        return base.VisitVariableDeclarator(node);
                }
                else
                {
                    if (CheckOption((int)CleanupTypes.RemoveClassFieldsInitializerNull) == false) return base.VisitVariableDeclarator(node);
                    if (valueObj != null) return base.VisitVariableDeclarator(node);
                }

                if (IsReportOnlyMode)
                {
                    var lineSpan = node.GetFileLinePosSpan();

                    AddReport(new ChangesReport(node)
                    {
                        LineNumber = lineSpan.StartLinePosition.Line,
                        Column = lineSpan.StartLinePosition.Character,
                        Message = "Field initialize with \"= null;\" or \"= 0;\" can be removed",
                        Generator = nameof(SimplifyClassFieldDeclarations)
                    });
                }

                node = node.WithInitializer(null).WithoutTrailingTrivia();
            }

            return base.VisitVariableDeclarator(node);
        }

        private SyntaxTrivia _spaceTrivia = SyntaxFactory.Whitespace(" ");

        private SyntaxNode Apply(ClassDeclarationSyntax classDescriptionNode)
        {
            var newDeclarationDic = new Dictionary<NewFieldDeclarationDicKey, NewFieldDeclarationDicItem>();

            var fieldDeclarations =
                classDescriptionNode
                    .Members
                    .OfType<FieldDeclarationSyntax>()
                    .Where(fd => fd.AttributeLists.Any() == false)
                    .Where(fd => fd.HasStructuredTrivia == false)
                    .Where(fd => fd.DescendantTrivia().Any(t => t.IsKind(SyntaxKind.SingleLineCommentTrivia) || t.IsKind(SyntaxKind.MultiLineCommentTrivia)) == false)
                    .Where(fd => fd.Declaration.Variables.All(x => x.Initializer == null || x.Initializer.Value is LiteralExpressionSyntax))
                    .ToList();

            foreach (var fieldDeclarationItem in fieldDeclarations)
            {
                var variableType = GetSystemTypeOfTypeNode(fieldDeclarationItem.Declaration);

                var key = GetKey(fieldDeclarationItem);

                if (newDeclarationDic.ContainsKey(key) == false)
                {
                    newDeclarationDic
                        .Add
                        (
                            key,
                            new NewFieldDeclarationDicItem
                            {
                                VariablesWithoutInitializer = new List<VariableDeclaratorSyntax>(),
                                VariablesWithInitializer = new List<VariableDeclaratorSyntax>(),
                                OldFieldDeclarations = new List<FieldDeclarationSyntax>()
                            }
                        );
                }

                var currentItem = newDeclarationDic[key];

                currentItem.OldFieldDeclarations.Add(fieldDeclarationItem);

                var newDeclaration = VisitFieldDeclaration(fieldDeclarationItem) as FieldDeclarationSyntax;

                currentItem.VariablesWithoutInitializer
                    .AddRange(newDeclaration.Declaration.Variables.Where(v => v.Initializer == null));
                currentItem.VariablesWithInitializer
                    .AddRange(newDeclaration.Declaration.Variables.Where(v => v.Initializer != null));
            }

            var newDeclarationDicAllItems = newDeclarationDic.ToList();

            newDeclarationDic.Clear();

            foreach (var newDelarationItem in newDeclarationDicAllItems)
            {
                var finalList = newDelarationItem.Value.VariablesWithoutInitializer.Select(x => x.WithoutTrailingTrivia().WithLeadingTrivia(_spaceTrivia)).ToList();
                finalList.AddRange(newDelarationItem.Value.VariablesWithInitializer.Select(x => x.WithoutTrailingTrivia().WithLeadingTrivia(_spaceTrivia)));

                finalList[0] = finalList[0].WithoutLeadingTrivia();

                newDelarationItem.Value.NewFieldDeclaration =
                    newDelarationItem.Value.FirstOldFieldDeclarations
                        .WithDeclaration(
                            newDelarationItem.Value.FirstOldFieldDeclarations
                                .Declaration
                                .WithVariables(SyntaxFactory.SeparatedList(finalList))
                        );

                if (newDelarationItem.Value.NewFieldDeclaration.Span.Length <= Option.Options.MaxFieldDeclarationLength)
                {
                    newDeclarationDic.Add(newDelarationItem.Key, newDelarationItem.Value);
                }
                else
                {
                    foreach (var item in newDelarationItem.Value.OldFieldDeclarations)
                        fieldDeclarations.Remove(item);
                }
            }

            var replaceList = newDeclarationDic.Select(x => x.Value.FirstOldFieldDeclarations).ToList();

            var newClassDescriptionNode =
                classDescriptionNode
                    .ReplaceNodes
                    (
                        fieldDeclarations,
                        (node1, node2) =>
                        {
                            if (replaceList.Contains(node1))
                            {
                                var dicItem = newDeclarationDic[GetKey(node1 as FieldDeclarationSyntax)];

                                return
                                    dicItem
                                        .NewFieldDeclaration
                                        .WithLeadingTrivia(dicItem.FirstOldFieldDeclarations.GetLeadingTrivia())
                                        .WithTrailingTrivia(dicItem.FirstOldFieldDeclarations.GetTrailingTrivia());
                            }

                            return null;
                        }
                    );

            if (replaceList.Any() && IsReportOnlyMode)
            {
                var lineSpan = classDescriptionNode.GetFileLinePosSpan();

                AddReport(new ChangesReport(classDescriptionNode)
                {
                    LineNumber = lineSpan.StartLinePosition.Line,
                    Column = lineSpan.StartLinePosition.Character,
                    Message = "Field initialize can be in one line",
                    Generator = nameof(SimplifyClassFieldDeclarations)
                });
            }

            return newClassDescriptionNode;
        }

        private NewFieldDeclarationDicKey GetKey(FieldDeclarationSyntax fieldDeclarationItem)
        {
            var header = new NewFieldDeclarationDicKey
            {
                TypeName = GetSystemTypeOfTypeNode(fieldDeclarationItem.Declaration),
            };

            if (fieldDeclarationItem.Modifiers.Any())
            {
                header.Modifiers = fieldDeclarationItem.Modifiers.Select(x => x.ValueText).ToArray();
            }

            return header;
        }

        private string GetSystemTypeOfTypeNode(VariableDeclarationSyntax d)
        {
            if (d.Type is PredefinedTypeSyntax)
                return TypesMapItem.GetAllPredefinedTypesDic()[(d.Type as PredefinedTypeSyntax).Keyword.ValueText].BuiltInName.Trim();

            return (d.Type.ToFullString().Trim());
        }

        private struct NewFieldDeclarationDicKey : IEquatable<NewFieldDeclarationDicKey>
        {

            public string TypeName { get; set; }
            public string[] Modifiers { get; set; }

            public bool Equals(NewFieldDeclarationDicKey other)
            {
                return this == other;
            }

            public static bool operator ==(NewFieldDeclarationDicKey left, NewFieldDeclarationDicKey right)
            {
                if (string.Compare(left.TypeName, right.TypeName) != 0) return false;
                if (left.Modifiers == null && right.Modifiers == null) return true;
                if (left.Modifiers == null || right.Modifiers == null) return false;
                if (left.Modifiers.Length != right.Modifiers.Length) return false;

                foreach (var item in left.Modifiers)
                {
                    if (right.Modifiers.Any(m => string.Compare(m, item) == 0) == false) return false;
                }

                return true;
            }
            public static bool operator !=(NewFieldDeclarationDicKey left, NewFieldDeclarationDicKey right)
            {
                return !(left == right);
            }
        }

        private class NewFieldDeclarationDicItem
        {
            public List<VariableDeclaratorSyntax> VariablesWithoutInitializer { get; set; }
            public List<VariableDeclaratorSyntax> VariablesWithInitializer { get; set; }
            public FieldDeclarationSyntax FirstOldFieldDeclarations => OldFieldDeclarations.FirstOrDefault();
            public List<FieldDeclarationSyntax> OldFieldDeclarations { get; set; }
            public FieldDeclarationSyntax NewFieldDeclaration { get; set; }
        }
    }
}