﻿using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;

namespace ShaderGen
{
    internal static class Utilities
    {
        public static string GetFullTypeName(this SemanticModel model, ExpressionSyntax type)
        {
            bool _; return GetFullTypeName(model, type, out _);
        }

        public static string GetFullTypeName(this SemanticModel model, ExpressionSyntax type, out bool isArray)
        {
            if (model == null)
            {
                throw new ArgumentNullException(nameof(model));
            }
            if (type == null)
            {
                throw new ArgumentNullException(nameof(type));
            }
            if (type.SyntaxTree != model.SyntaxTree)
            {
                model = GetSemanticModel(model.Compilation, type.SyntaxTree);
            }

            TypeInfo typeInfo = model.GetTypeInfo(type);
            if (typeInfo.Type == null)
            {
                typeInfo = model.GetSpeculativeTypeInfo(0, type, SpeculativeBindingOption.BindAsTypeOrNamespace);
                if (typeInfo.Type == null || typeInfo.Type is IErrorTypeSymbol)
                {
                    throw new InvalidOperationException("Unable to resolve type: " + type + " at " + type.GetLocation());
                }
            }

            return GetFullTypeName(typeInfo.Type, out isArray);
        }

        public static string GetFullTypeName(ITypeSymbol type, out bool isArray)
        {
            if (type is IArrayTypeSymbol ats)
            {
                isArray = true;
                return GetFullMetadataName(ats.ElementType);
            }
            else
            {
                isArray = false;
                return GetFullMetadataName(type);
            }
        }

        public static string GetFullMetadataName(this ISymbol s)
        {
            if (s == null || IsRootNamespace(s))
            {
                return string.Empty;
            }

            if (s.Kind == SymbolKind.ArrayType)
            {
                return GetFullMetadataName(((IArrayTypeSymbol)s).ElementType) + "[]";
            }

            StringBuilder sb = new StringBuilder(s.MetadataName);
            ISymbol last = s;

            s = s.ContainingSymbol;

            while (!IsRootNamespace(s))
            {
                if (s is ITypeSymbol && last is ITypeSymbol)
                {
                    sb.Insert(0, '+');
                }
                else
                {
                    sb.Insert(0, '.');
                }

                sb.Insert(0, s.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat));
                //sb.Insert(0, s.MetadataName);
                s = s.ContainingSymbol;
            }

            return sb.ToString();
        }

        private static bool IsRootNamespace(ISymbol symbol)
        {
            INamespaceSymbol s = null;
            return ((s = symbol as INamespaceSymbol) != null) && s.IsGlobalNamespace;
        }

        private static SemanticModel GetSemanticModel(Compilation compilation, SyntaxTree syntaxTree)
        {
            return compilation.GetSemanticModel(syntaxTree);
        }

        public static string GetFullName(INamespaceSymbol ns)
        {
            Debug.Assert(ns != null);
            string currentNamespace = ns.Name;
            if (ns.ContainingNamespace != null && !ns.ContainingNamespace.IsGlobalNamespace)
            {
                return GetFullName(ns.ContainingNamespace) + "." + currentNamespace;
            }
            else
            {
                return currentNamespace;
            }
        }

        public static string GetFullName(INamedTypeSymbol symbol)
        {
            Debug.Assert(symbol != null);
            string name = symbol.Name;
            if (symbol.ContainingNamespace != null && !symbol.ContainingNamespace.IsGlobalNamespace)
            {
                return GetFullName(symbol.ContainingNamespace) + "." + name;
            }
            else
            {
                return name;
            }
        }

        public static string GetFullNamespace(SyntaxNode node)
        {
            var fileScopedNamespace = node.SyntaxTree.GetRoot()
                .DescendantNodes()
                .OfType<FileScopedNamespaceDeclarationSyntax>()
                .FirstOrDefault();

            // not allowed to be combined with regular namespaces
            if (fileScopedNamespace != null)
            {
                return fileScopedNamespace.Name.ToString();
            }

            var namespaces = new List<string>();
            while (SyntaxNodeHelper.TryGetParentSyntax(node, out NamespaceDeclarationSyntax namespaceDeclarationSyntax))
            {
                node = namespaceDeclarationSyntax;
                namespaces.Add(namespaceDeclarationSyntax.Name.ToString());
            }

            return string.Join('.', ((IEnumerable<string>)namespaces).Reverse());
        }

        public static string GetFullNestedTypePrefix(SyntaxNode node, out bool nested)
        {
            string ns = GetFullNamespace(node);
            List<string> nestedTypeParts = new List<string>();
            while (SyntaxNodeHelper.TryGetParentSyntax(node, out TypeDeclarationSyntax tds))
            {
                nestedTypeParts.Add(tds.Identifier.ToFullString().Trim());
                node = tds;
            }

            string nestedTypeStr = string.Join("+", nestedTypeParts);
            if (string.IsNullOrEmpty(ns))
            {
                nested = true;
                return nestedTypeStr;
            }
            else
            {
                if (string.IsNullOrEmpty(nestedTypeStr))
                {
                    nested = false;
                    return ns;
                }
                else
                {
                    nested = true;
                    return ns + "." + nestedTypeStr;
                }
            }
        }

        private static readonly HashSet<string> s_basicNumericTypes = new HashSet<string>()
        {
            "System.Numerics.Vector2",
            "System.Numerics.Vector3",
            "System.Numerics.Vector4",
            "System.Numerics.Matrix4x4",
        };

        public static bool IsBasicNumericType(string fullName)
        {
            return s_basicNumericTypes.Contains(fullName);
        }

        public static AttributeSyntax[] GetMemberAttributes(CSharpSyntaxNode cssn, string name)
        {
            return (cssn is ParameterSyntax ps
                    ? ps.AttributeLists.SelectMany(x => x.Attributes)
                    : cssn.Parent.Parent.DescendantNodes().OfType<AttributeSyntax>()
                ).Where(attrSyntax => attrSyntax.Name.ToString().Contains(name)).ToArray();
        }

        public static AttributeSyntax[] GetMethodAttributes(BaseMethodDeclarationSyntax mds, string name)
        {
            return mds.DescendantNodes().OfType<AttributeSyntax>()
            .Where(attrSyntax => attrSyntax.Name.ToString().Contains(name)).ToArray();
        }

        /// <summary>
        /// Gets the full namespace + name for the given SymbolInfo.
        /// </summary>
        public static string GetFullName(SymbolInfo symbolInfo)
        {
            Debug.Assert(symbolInfo.Symbol != null);
            string fullName = symbolInfo.Symbol.Name;
            string ns = GetFullName(symbolInfo.Symbol.ContainingNamespace);
            if (!string.IsNullOrEmpty(ns))
            {
                fullName = ns + "." + fullName;
            }

            return fullName;
        }

        public static bool IsAutoProperty(IPropertySymbol propertySymbol)
            => propertySymbol.ContainingType.GetMembers()
                   .OfType<IFieldSymbol>()
                   .Any(x => SymbolEqualityComparer.Default.Equals(x.AssociatedSymbol, propertySymbol))
               || propertySymbol.GetAttributes().Any(x => x.AttributeClass?.Name == nameof(TreatAsAutoPropertyAttribute));

        internal static string JoinIgnoreNull(string separator, IEnumerable<string> value)
        {
            return string.Join(separator, value.Where(s => !string.IsNullOrEmpty(s)));
        }

        internal static ShaderFunctionAndMethodDeclarationSyntax GetShaderFunction(
            BaseMethodDeclarationSyntax node,
            Compilation compilation,
            bool generateOrderedFunctionList)
        {
            SemanticModel semanticModel = compilation.GetSemanticModel(node.SyntaxTree);

            string functionName;
            TypeReference returnTypeReference;
            if (node is MethodDeclarationSyntax mds)
            {
                functionName = mds.Identifier.ToFullString();
                returnTypeReference = new TypeReference(semanticModel.GetFullTypeName(mds.ReturnType), semanticModel.GetTypeInfo(mds.ReturnType).Type);
            }
            else if (node is ConstructorDeclarationSyntax cds)
            {
                functionName = ".ctor";
                ITypeSymbol typeSymbol = semanticModel.GetDeclaredSymbol(cds).ContainingType;
                returnTypeReference = new TypeReference(GetFullTypeName(typeSymbol, out _), typeSymbol);
            }
            else
            {
                throw new ArgumentOutOfRangeException(nameof(node), "Unsupported BaseMethodDeclarationSyntax type.");
            }

            UInt3 computeGroupCounts = new UInt3();
            bool isFragmentShader = false, isComputeShader = false;
            bool isVertexShader = GetMethodAttributes(node, "VertexShader").Any();
            if (!isVertexShader)
            {
                isFragmentShader = GetMethodAttributes(node, "FragmentShader").Any();
            }
            if (!isVertexShader && !isFragmentShader)
            {
                AttributeSyntax computeShaderAttr = GetMethodAttributes(node, "ComputeShader").FirstOrDefault();
                if (computeShaderAttr != null)
                {
                    isComputeShader = true;
                    computeGroupCounts.X = GetAttributeArgumentUIntValue(computeShaderAttr, 0);
                    computeGroupCounts.Y = GetAttributeArgumentUIntValue(computeShaderAttr, 1);
                    computeGroupCounts.Z = GetAttributeArgumentUIntValue(computeShaderAttr, 2);
                }
            }

            ShaderFunctionType type = isVertexShader
                ? ShaderFunctionType.VertexEntryPoint
                : isFragmentShader
                    ? ShaderFunctionType.FragmentEntryPoint
                    : isComputeShader
                        ? ShaderFunctionType.ComputeEntryPoint
                        : ShaderFunctionType.Normal;

            string nestedTypePrefix = GetFullNestedTypePrefix(node, out bool nested);



            List<ParameterDefinition> parameters = new List<ParameterDefinition>();
            foreach (ParameterSyntax ps in node.ParameterList.Parameters)
            {
                parameters.Add(ParameterDefinition.GetParameterDefinition(compilation, ps));
            }

            ShaderFunction sf = new ShaderFunction(
                nestedTypePrefix,
                functionName,
                returnTypeReference,
                parameters.ToArray(),
                type,
                computeGroupCounts);

            ShaderFunctionAndMethodDeclarationSyntax[] orderedFunctionList;
            if (type != ShaderFunctionType.Normal && generateOrderedFunctionList)
            {
                FunctionCallGraphDiscoverer fcgd = new FunctionCallGraphDiscoverer(
                    compilation,
                    new TypeAndMethodName { TypeName = sf.DeclaringType, MethodName = sf.Name });
                fcgd.GenerateFullGraph();
                orderedFunctionList = fcgd.GetOrderedCallList();
            }
            else
            {
                orderedFunctionList = new ShaderFunctionAndMethodDeclarationSyntax[0];
            }

            return new ShaderFunctionAndMethodDeclarationSyntax(sf, node, orderedFunctionList);
        }

        private static uint GetAttributeArgumentUIntValue(AttributeSyntax attr, int index)
        {
            if (attr.ArgumentList.Arguments.Count < index + 1)
            {
                throw new ShaderGenerationException(
                    "Too few arguments in attribute " + attr.ToFullString() + ". Required + " + (index + 1));
            }
            string fullArg0 = attr.ArgumentList.Arguments[index].ToFullString();
            if (uint.TryParse(fullArg0, out uint ret))
            {
                return ret;
            }
            else
            {
                throw new ShaderGenerationException("Incorrectly formatted attribute: " + attr.ToFullString());
            }
        }
    }
}
