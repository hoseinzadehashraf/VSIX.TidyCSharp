using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using TidyCSharp.Cli.Menus.Cleanup.SyntaxNodeExtractors;
using TidyCSharp.Cli.Menus.Cleanup.Utils;

namespace TidyCSharp.Cli.Menus.Cleanup.TokenRemovers;

public class NestedClassTokenRemover : CleanupCSharpSyntaxRewriter, IPrivateModiferTokenRemover
{
    public NestedClassTokenRemover(bool isReportOnlyMode)
        : base(isReportOnlyMode, null)
    { }
    public SyntaxNode Remove(SyntaxNode root)
    {
        var nestedClasses = new NestedClassExtractor().Extraxt(root, SyntaxKind.PrivateKeyword);

        if (IsReportOnlyMode)
        {
            foreach (var nestedClass in nestedClasses)
            {
                var lineSpan = nestedClass.GetFileLinePosSpan();

                AddReport(new ChangesReport(root)
                {
                    LineNumber = lineSpan.StartLinePosition.Line,
                    Column = lineSpan.StartLinePosition.Character,
                    Message = "private nested class --> private can be removed",
                    Generator = nameof(NestedClassTokenRemover)
                });
            }
        }

        // TODO: 1. Fix the issue with touching the namespaces
        // TODO: 2. Remove the conditional operator 
        return nestedClasses.Count == 0 ? null : root.RemovePrivateTokens(nestedClasses);
    }
}