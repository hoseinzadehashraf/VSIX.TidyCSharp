using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace TidyCSharp.Cli.Menus.Cleanup.Utils;

public static class SyntaxTokenExtensions
{
    public static SyntaxNode RemovePrivateTokens(this SyntaxNode root, List<SyntaxToken> tokens)
    {
        if (!tokens.Any()) return root;

        return root.ReplaceTokens(tokens, MakeReplacementToken(tokens));
    }

    private static Func<SyntaxToken, SyntaxToken, SyntaxToken> MakeReplacementToken(List<SyntaxToken> tokens)
    {
        // replace with the LeadingTrivia so that the comments (if any) will not be lost also the private keyword is replaced at the same time
        return (oldToken, newToken) => SyntaxFactory.ParseToken(oldToken.LeadingTrivia.ToFullString());
    }

    public static FileLinePositionSpan GetFileLinePosSpan(this SyntaxToken node)
    {
        return node.SyntaxTree.GetLineSpan(new TextSpan(node.Span.Start, node.Span.Length));
    }

    private static object _lockFileWrite = new();
    public static void WriteSourceTo(this SyntaxNode sourceCode, string filePath)
    {
        lock (_lockFileWrite)
        {
            var encoding = DetectFileEncoding(filePath);

            var source = sourceCode.ToFullString().Trim(new[] { '\r', '\n' });
            var fileText = File.ReadAllText(filePath).Trim(new[] { '\r', '\n' });

            var bEqual = string.Compare(source, fileText, StringComparison.Ordinal) == 0;

            if (!bEqual)
            {
                using (var write = new StreamWriter(filePath, false, encoding))
                    write.Write(sourceCode.ToFullString());
            }
        }
    }

    private static Encoding DetectFileEncoding(string filePath)
    {
        Encoding encoding = null;

        using (var reader = new StreamReader(filePath))
            encoding = reader.CurrentEncoding;

        using (var reader = new FileStream(filePath, FileMode.Open, FileAccess.Read))
        {
            var bom = new byte[4];
            reader.Read(bom, 0, 4);

            if (bom[0] == 0xef && bom[1] == 0xbb && bom[2] == 0xbf)
            {
                encoding = new UTF8Encoding(true);
            }
            else if (bom[0] == 0x2b && bom[1] == 0x2f && bom[2] == 0x76)
            {
                encoding = new UTF7Encoding(true);
            }
            else if (bom[0] == 0xff && bom[1] == 0xfe)
            {
                encoding = new UnicodeEncoding(false, true);
            }
            else if (bom[0] == 0xfe && bom[1] == 0xff)
            {
                encoding = new UTF8Encoding(false);
                // encoding = new BigEndianUnicode(true);
            }
            else if (bom[0] == 0 && bom[1] == 0 && bom[2] == 0xfe && bom[3] == 0xff)
            {
                encoding = new UTF32Encoding(false, true);
            }
            else
            {
                encoding = new UTF8Encoding(false);
            }
        }

        return encoding;
    }

    public static SyntaxNode ToSyntaxNode(this Document item)
    {
        lock (_lockFileWrite)
        {
            return CSharpSyntaxTree.ParseText(File.ReadAllText(item.ToFullPathPropertyValue())).GetRoot();
        }
    }

    public static string ToFullPathPropertyValue(this Document item)
    {
        if (item == null) return null;
        return item.FilePath;
    }

    public static SyntaxToken WithoutTrivia(this SyntaxToken token)
    {
        return token.WithLeadingTrivia().WithTrailingTrivia();
    }

    public static SyntaxTriviaList WithoutWhiteSpaceTrivia(this SyntaxTriviaList triviaList)
    {
        return new SyntaxTriviaList().AddRange(triviaList.Where(t => !t.IsWhiteSpaceTrivia()));
    }

    public static SyntaxToken WithoutWhiteSpaceTrivia(this SyntaxToken token)
    {
        return
            token
                .WithLeadingTrivia(token.LeadingTrivia.Where(t => !t.IsWhiteSpaceTrivia()))
                .WithTrailingTrivia(token.TrailingTrivia.Where(t => !t.IsWhiteSpaceTrivia()));
    }

    public static T WithoutWhiteSpaceTrivia<T>(this T token)
        where T : SyntaxNode
    {
        return
            token
                .WithLeadingTrivia(token.GetLeadingTrivia().Where(t => !t.IsWhiteSpaceTrivia()))
                .WithTrailingTrivia(token.GetTrailingTrivia().Where(t => !t.IsWhiteSpaceTrivia()));
    }

    public static bool HasNoneWhiteSpaceTrivia(this IEnumerable<SyntaxTrivia> triviaList, SyntaxKind[] exceptionList = null)
    {
        if (exceptionList == null)
            return triviaList.Any(t => !t.IsWhiteSpaceTrivia());

        return triviaList.Any(t => !t.IsWhiteSpaceTrivia() && exceptionList.Any(e => t.IsKind(e)) == false);
    }

    public static bool IsWhiteSpaceTrivia(this SyntaxTrivia trivia)
    {
        return trivia.IsKind(SyntaxKind.EndOfLineTrivia) || trivia.IsKind(SyntaxKind.WhitespaceTrivia);
    }

    public static bool HasNoneWhiteSpaceTrivia(this SyntaxNode node, SyntaxKind[] exceptionList = null)
    {
        return (node.ContainsDirectives || node.HasStructuredTrivia || node.DescendantTrivia(descendIntoTrivia: true).HasNoneWhiteSpaceTrivia(exceptionList));
    }

    public static bool HasNoneWhiteSpaceTrivia(this SyntaxToken token, SyntaxKind[] exceptionList = null)
    {
        return (token.ContainsDirectives || token.HasStructuredTrivia || token.GetAllTrivia().HasNoneWhiteSpaceTrivia());
    }

    public static bool IsPrivate(this FieldDeclarationSyntax field) => IsPrivate(field.Modifiers);

    public static bool IsPrivate(this PropertyDeclarationSyntax field)
    {
        return IsPrivate(field.Modifiers);
    }

    public static bool IsPublic(this FieldDeclarationSyntax field)
    {
        return field.Modifiers.Any(m => m.IsKind(SyntaxKind.PublicKeyword));
    }

    public static bool IsPublic(this PropertyDeclarationSyntax field)
    {
        return field.Modifiers.Any(m => m.IsKind(SyntaxKind.PublicKeyword));
    }

    public static bool IsProtected(this FieldDeclarationSyntax field)
    {
        return field.Modifiers.Any(m => m.IsKind(SyntaxKind.ProtectedKeyword));
    }

    public static bool IsProtected(this PropertyDeclarationSyntax field)
    {
        return field.Modifiers.Any(m => m.IsKind(SyntaxKind.ProtectedKeyword));
    }

    public static bool IsInternal(this FieldDeclarationSyntax field)
    {
        return field.Modifiers.Any(m => m.IsKind(SyntaxKind.InternalKeyword));
    }

    public static bool IsInternal(this PropertyDeclarationSyntax field)
    {
        return field.Modifiers.Any(m => m.IsKind(SyntaxKind.InternalKeyword));
    }

    public static bool IsPrivate(this LocalDeclarationStatementSyntax local)
    {
        return IsPrivate(local.Modifiers);
    }

    private static bool IsPrivate(SyntaxTokenList modifiers)
    {
        return
            modifiers.Any(m => m.IsKind(SyntaxKind.PrivateKeyword)) ||
            modifiers
                .Any(
                    m =>
                        m.IsKind(SyntaxKind.PublicKeyword) ||
                        m.IsKind(SyntaxKind.ProtectedKeyword) ||
                        m.IsKind(SyntaxKind.InternalKeyword)
                ) == false;
    }
}