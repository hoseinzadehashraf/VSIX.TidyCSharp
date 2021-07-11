﻿using Geeks.VSIX.TidyCSharp.Menus.Cleanup.SyntaxNodeExtractors;
using Geeks.VSIX.TidyCSharp.Menus.Cleanup.Utils;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Geeks.VSIX.TidyCSharp.Cleanup.NormalizeWhitespace
{
	public class EndOFLineRewriter : CSharpSyntaxRewriterBase
	{
		public EndOFLineRewriter(SyntaxNode initialSource,
			bool isReadOnlyMode, Options options)
			: base(initialSource, isReadOnlyMode, options)
		{ }

		public override SyntaxTrivia VisitTrivia(SyntaxTrivia trivia)
		{
			if (trivia.IsKind(SyntaxKind.EndOfLineTrivia) && trivia.ToFullString() == "\r\n")
			{
				if (IsReportOnlyMode)
				{
					var lineSpan = trivia.GetFileLinePosSpan();
					AddReport(new ChangesReport(trivia)
					{
						LineNumber = lineSpan.StartLinePosition.Line,
						Column = lineSpan.StartLinePosition.Character,
						Message = "\\r\\n should be \\n",
						Generator = nameof(EndOFLineRewriter)
					});
				}
				return base.VisitTrivia(SyntaxFactory.EndOfLine("\n"));
			}
			return base.VisitTrivia(trivia);
		}

	}
}
