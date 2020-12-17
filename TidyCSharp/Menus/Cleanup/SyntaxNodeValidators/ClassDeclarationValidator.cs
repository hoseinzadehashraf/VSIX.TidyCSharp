﻿using Geeks.VSIX.TidyCSharp.Menus.Cleanup.SyntaxNodeTypeConverter;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Geeks.VSIX.TidyCSharp.Menus.Cleanup.SyntaxNodeValidators
{
    public static class ClassDeclarationValidator
    {
        public static bool ClassShouldHaveBase(this ClassDeclarationSyntax node)
        {
            return node.BaseList != null;
        }

        public static bool ClassShouldHaveGenericBase(this ClassDeclarationSyntax node)
        {
            return node.BaseList.Types.Any(x => x.Type.IsKind(SyntaxKind.GenericName));
        }

        public static bool GenericClassShouldInheritFrom(this ClassDeclarationSyntax node, string baseClass)
        {
            return node.BaseList.Types.Any(x => x.As<SimpleBaseTypeSyntax>()?
            .Type.As<GenericNameSyntax>()?
                .Identifier.Text == baseClass);
        }
    }
}
