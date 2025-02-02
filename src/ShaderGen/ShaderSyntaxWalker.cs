﻿using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Text;
using System.Linq;
using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using System.Diagnostics;

namespace ShaderGen
{
    internal class ShaderSyntaxWalker : CSharpSyntaxWalker
    {
        private readonly StringBuilder _sb = new StringBuilder();
        private readonly Compilation _compilation;
        private readonly LanguageBackend[] _backends;
        private readonly ShaderSetInfo _shaderSet;

        private readonly Dictionary<int, int> _setCounts = new Dictionary<int, int>();

        public ShaderSyntaxWalker(Compilation compilation, LanguageBackend[] backends, ShaderSetInfo ss)
            : base(SyntaxWalkerDepth.Token)
        {
            _compilation = compilation;
            _backends = backends;
            _shaderSet = ss;
        }

        private SemanticModel GetModel(SyntaxNode node) => _compilation.GetSemanticModel(node.SyntaxTree);

        public override void VisitMethodDeclaration(MethodDeclarationSyntax node)
        {
            ShaderFunctionAndMethodDeclarationSyntax sfab = Utilities.GetShaderFunction(node, _compilation, true);
            foreach (LanguageBackend b in _backends)
            {
                b.AddFunction(_shaderSet.Name, sfab);

                foreach (var calledFunction in sfab.OrderedFunctionList)
                {
                    b.AddFunction(_shaderSet.Name, calledFunction);
                }
            }
        }

        public override void VisitStructDeclaration(StructDeclarationSyntax node)
        {
            TryGetStructDefinition(GetModel(node), node, out var sd);
            foreach (var b in _backends) { b.AddStructure(_shaderSet.Name, sd); }
        }

        private static string GetFullTypeName(TypeDeclarationSyntax node)
        {
            string fullNestedTypePrefix = Utilities.GetFullNestedTypePrefix(node, out bool nested);
            string structName = node.Identifier.ToFullString().Trim();
            if (!string.IsNullOrEmpty(fullNestedTypePrefix))
            {
                string joiner = nested ? "+" : ".";
                structName = fullNestedTypePrefix + joiner + structName;
            }
            return structName.Trim();
        }

        public static bool TryGetStructDefinition(SemanticModel model, TypeDeclarationSyntax node, out StructureDefinition sd)
        {
            int structCSharpSize = 0;
            int structShaderSize = 0;
            int structCSharpAlignment = 0;
            int structShaderAlignment = 0;
            List<FieldDefinition> fields = new List<FieldDefinition>();

            IEnumerable<(CSharpSyntaxNode, SyntaxToken, TypeSyntax)> cSharpFields = node switch
            {
                StructDeclarationSyntax => Enumerable.Empty<(CSharpSyntaxNode, SyntaxToken, TypeSyntax)>(),
                RecordDeclarationSyntax {ParameterList: { }} cds when cds.IsKind(SyntaxKind.RecordStructDeclaration)
                        => cds.ParameterList.Parameters
                    .Select(x => ((CSharpSyntaxNode)x, x.Identifier, x.Type)),
                _ => null,
            };

            if (cSharpFields is null)
            {
                sd = null;
                return false;
            }

            cSharpFields = cSharpFields.Concat(
                node.Members
                    .OfType<FieldDeclarationSyntax>()
                    .Where(x => !x.Modifiers.Any(x => x.IsKind(SyntaxKind.ConstKeyword)))
                    .Where(x => !x.AttributeLists.SelectMany(x => x.Attributes)
                        .Any(x => x.Name.ToString().Contains("ResourceIgnore")))
                    .SelectMany(x => x.Declaration.Variables,
                        (x, y) => ((CSharpSyntaxNode) y, y.Identifier, x.Declaration.Type))
                    .Concat(node.Members // Auto-properties (no explicit getter/setter)
                        .OfType<PropertyDeclarationSyntax>()
                        .Where(x => 
                            x.AccessorList?.Accessors.All(x => x.Body == null && x.ExpressionBody == null) == true
                            || x.AttributeLists.SelectMany(x => x.Attributes)
                                .Any(x => x.Name.ToString().Contains("TreatAsAutoProperty")))
                        
                        .Select(x => ((CSharpSyntaxNode) x, x.Identifier, x.Type)))
            );

            foreach (var (declaration, identifier, type) in cSharpFields)
            {
                string fieldName = identifier.Text.Trim();
                string typeName = model.GetFullTypeName(type, out bool isArray);
                TypeInfo typeInfo = model.GetTypeInfo(type);
                int arrayElementCount = 0;
                ITypeSymbol elementType = null;
                if (isArray)
                {
                    elementType = ((IArrayTypeSymbol)typeInfo.Type)!.ElementType;
                    arrayElementCount = GetArrayCountValue(declaration, model);
                }

                if (!isArray && typeName == "System.Span`1" && TryGetArrayCountValue(declaration, model, out arrayElementCount))
                {
                    isArray = true;
                    elementType = ((INamedTypeSymbol)typeInfo.Type)!.TypeArguments[0];
                    typeName = ((INamedTypeSymbol)model.GetTypeInfo(type).Type)!.TypeArguments[0].GetFullMetadataName();
                }

                AlignmentInfo fieldSizeAndAlignment;

                if (isArray)
                {
                    AlignmentInfo elementSizeAndAlignment = TypeSizeCache.Get(elementType);
                    fieldSizeAndAlignment = new AlignmentInfo(
                        elementSizeAndAlignment.CSharpSize * arrayElementCount,
                        elementSizeAndAlignment.ShaderSize * arrayElementCount,
                        elementSizeAndAlignment.CSharpAlignment,
                        elementSizeAndAlignment.ShaderAlignment);
                }
                else
                {
                    fieldSizeAndAlignment = TypeSizeCache.Get(typeInfo.Type);
                }

                structCSharpSize += structCSharpSize % fieldSizeAndAlignment.CSharpAlignment;
                structCSharpSize += fieldSizeAndAlignment.CSharpSize;
                structCSharpAlignment = Math.Max(structCSharpAlignment, fieldSizeAndAlignment.CSharpAlignment);

                structShaderSize += structShaderSize % fieldSizeAndAlignment.ShaderAlignment;
                structShaderSize += fieldSizeAndAlignment.ShaderSize;
                structShaderAlignment = Math.Max(structShaderAlignment, fieldSizeAndAlignment.ShaderAlignment);

                TypeReference tr = new TypeReference(typeName, model.GetTypeInfo(type).Type);
                SemanticType semanticType = GetSemanticType(declaration);
                fields.Add(new FieldDefinition(fieldName, tr, semanticType, arrayElementCount, fieldSizeAndAlignment));
            }

            sd = new StructureDefinition(
                GetFullTypeName(node),
                fields.ToArray(),
                new AlignmentInfo(structCSharpSize, structShaderSize, structCSharpAlignment, structShaderAlignment));
            return true;
        }

        private static bool TryGetArrayCountValue(CSharpSyntaxNode vds, SemanticModel semanticModel, out int arraySize)
        {
            AttributeSyntax[] arraySizeAttrs = Utilities.GetMemberAttributes(vds, "ArraySize");
            if (arraySizeAttrs.Length != 1)
            {
                arraySize = 0;
                return false;
            }
            AttributeSyntax arraySizeAttr = arraySizeAttrs[0];
            arraySize = GetAttributeArgumentIntValue(arraySizeAttr, 0, semanticModel);
            return true;
        }

        private static int GetArrayCountValue(CSharpSyntaxNode vds, SemanticModel semanticModel)
        {
            if (!TryGetArrayCountValue(vds, semanticModel, out int arraySize))
            {
                throw new ShaderGenerationException(
                    "Array fields in structs must have a constant size specified by an ArraySizeAttribute.");
            }
            return arraySize;
        }

        private static int GetAttributeArgumentIntValue(AttributeSyntax attr, int index, SemanticModel semanticModel)
        {
            if (attr.ArgumentList == null || attr.ArgumentList.Arguments.Count <= index)
            {
                throw new ShaderGenerationException(
                    "Too few arguments in attribute " + attr.ToFullString() + ". Required + " + (index + 1));
            }
            return GetConstantIntFromExpression(attr.ArgumentList.Arguments[index].Expression, semanticModel);
        }

        private static int GetConstantIntFromExpression(ExpressionSyntax expression, SemanticModel semanticModel)
        {
            var constantValue = semanticModel.GetConstantValue(expression);
            if (!constantValue.HasValue)
            {
                throw new ShaderGenerationException("Expression did not contain a constant value: " + expression.ToFullString());
            }
            return (int)constantValue.Value;
        }

        private static SemanticType GetSemanticType(CSharpSyntaxNode cssn)
        {
            AttributeSyntax[] attrs = Utilities.GetMemberAttributes(cssn, "VertexSemantic");
            if (attrs.Length == 1)
            {
                AttributeSyntax semanticTypeAttr = attrs[0];
                string fullArg0 = semanticTypeAttr.ArgumentList.Arguments[0].ToFullString();
                if (fullArg0.Contains("."))
                {
                    fullArg0 = fullArg0.Substring(fullArg0.LastIndexOf('.') + 1);
                }
                if (Enum.TryParse(fullArg0, out SemanticType ret))
                {
                    return ret;
                }
                else
                {
                    throw new ShaderGenerationException("Incorrectly formatted attribute: " + semanticTypeAttr.ToFullString());
                }
            }
            else if (attrs.Length > 1)
            {
                throw new ShaderGenerationException("Too many vertex semantics applied to field: " + cssn.ToFullString());
            }

            if (CheckSingleAttribute(cssn, "SystemPositionSemantic"))
            {
                return SemanticType.SystemPosition;
            }
            else if (CheckSingleAttribute(cssn, "PositionSemantic"))
            {
                return SemanticType.Position;
            }
            else if (CheckSingleAttribute(cssn, "NormalSemantic"))
            {
                return SemanticType.Normal;
            }
            else if (CheckSingleAttribute(cssn, "TextureCoordinateSemantic"))
            {
                return SemanticType.TextureCoordinate;
            }
            else if (CheckSingleAttribute(cssn, "ColorSemantic"))
            {
                return SemanticType.Color;
            }
            else if (CheckSingleAttribute(cssn, "TangentSemantic"))
            {
                return SemanticType.Tangent;
            }
            else if (CheckSingleAttribute(cssn, "ColorTargetSemantic"))
            {
                return SemanticType.ColorTarget;
            }

            return SemanticType.None;
        }

        private static bool CheckSingleAttribute(CSharpSyntaxNode cssn, string name)
        {
            AttributeSyntax[] attrs = Utilities.GetMemberAttributes(cssn, name);
            return attrs.Length == 1;
        }

        public override void VisitFieldDeclaration(FieldDeclarationSyntax node)
        {
            if (node.Modifiers.Any(x => x.IsKind(SyntaxKind.ConstKeyword)))
            {
                return;
            }

            base.VisitFieldDeclaration(node);
        }

        public override void VisitPropertyDeclaration(PropertyDeclarationSyntax node) {
            if (node.Modifiers.Any(x => x.IsKind(SyntaxKind.StaticKeyword)))
            {
                return;
            }

            HandleResourceDeclaration(node, node.Type, node.Identifier.Text);
        }

        public override void VisitVariableDeclaration(VariableDeclarationSyntax node) {
            if (node.Variables.Count != 1)
            {
                throw new ShaderGenerationException("Cannot declare multiple variables together.");
            }

            HandleResourceDeclaration(node.Parent, node.Type, node.Variables[0].Identifier.Text);
        }

        public void HandleResourceDeclaration(SyntaxNode node, TypeSyntax type, string name)
        {
            if (node.DescendantNodes()
                .OfType<AttributeSyntax>()
                .Any(x => x.ToString().Contains("ResourceIgnore")))
            {
                return;
            }

            TypeInfo typeInfo = GetModel(node).GetTypeInfo(type);
            string fullTypeName = GetModel(node).GetFullTypeName(type);
            TypeReference valueType = new TypeReference(fullTypeName, typeInfo.Type);
            ShaderResourceKind kind = ClassifyResourceKind(fullTypeName);

            if (kind == ShaderResourceKind.StructuredBuffer
                || kind == ShaderResourceKind.RWStructuredBuffer
                || kind == ShaderResourceKind.RWTexture2D)
            {
                valueType = ParseElementType(type);
            }

            int set = 0; // Default value if not otherwise specified.
            if (GetResourceDecl(node, out AttributeSyntax resourceSetDecl))
            {
                set = GetAttributeArgumentIntValue(resourceSetDecl, 0, GetModel(node));
            }

            int resourceBinding = GetAndIncrementBinding(set);

            ResourceDefinition rd = new ResourceDefinition(name, set, resourceBinding, valueType, kind);
            if (kind == ShaderResourceKind.Uniform)
            {
                ValidateUniformType(typeInfo);
            }

            foreach (LanguageBackend b in _backends) { b.AddResource(_shaderSet.Name, rd); }
        }

        private TypeReference ParseElementType(TypeSyntax fieldType)
        {
            while (fieldType is QualifiedNameSyntax qns)
            {
                fieldType = qns.Right;
            }

            GenericNameSyntax gns = (GenericNameSyntax)fieldType;
            TypeSyntax type = gns.TypeArgumentList.Arguments[0];
            string fullName = GetModel(fieldType).GetFullTypeName(type);
            return new TypeReference(fullName, GetModel(fieldType).GetTypeInfo(type).Type);
        }

        private int GetAndIncrementBinding(int set)
        {
            if (!_setCounts.TryGetValue(set, out int ret))
            {
                ret = 0;
                _setCounts.Add(set, ret);
            }
            else
            {
                ret += 1;
                _setCounts[set] = ret;
            }

            return ret;
        }

        private void ValidateUniformType(TypeInfo typeInfo)
        {
            string name = typeInfo.Type.ToDisplayString();
            if (name != nameof(ShaderGen) + "." + nameof(Texture2DResource)
                && name != nameof(ShaderGen) + "." + nameof(Texture2DArrayResource)
                && name != nameof(ShaderGen) + "." + nameof(TextureCubeResource)
                && name != nameof(ShaderGen) + "." + nameof(Texture2DMSResource)
                && name != nameof(ShaderGen) + "." + nameof(SamplerResource)
                && name != nameof(ShaderGen) + "." + nameof(SamplerComparisonResource))
            {
                if (typeInfo.Type.IsReferenceType)
                {
                    throw new ShaderGenerationException("Shader resource fields must be simple blittable structures.");
                }
            }
        }

        private ShaderResourceKind ClassifyResourceKind(string fullTypeName)
        {
            if (fullTypeName == "ShaderGen.Texture2DResource")
            {
                return ShaderResourceKind.Texture2D;
            }
            if (fullTypeName == "ShaderGen.Texture2DArrayResource")
            {
                return ShaderResourceKind.Texture2DArray;
            }
            else if (fullTypeName == "ShaderGen.TextureCubeResource")
            {
                return ShaderResourceKind.TextureCube;
            }
            else if (fullTypeName == "ShaderGen.Texture2DMSResource")
            {
                return ShaderResourceKind.Texture2DMS;
            }
            else if (fullTypeName == "ShaderGen.SamplerResource")
            {
                return ShaderResourceKind.Sampler;
            }
            else if (fullTypeName == "ShaderGen.SamplerComparisonResource")
            {
                return ShaderResourceKind.SamplerComparison;
            }
            else if (fullTypeName.Contains("ShaderGen.RWStructuredBuffer"))
            {
                return ShaderResourceKind.RWStructuredBuffer;
            }
            else if (fullTypeName.Contains("ShaderGen.StructuredBuffer"))
            {
                return ShaderResourceKind.StructuredBuffer;
            }
            else if (fullTypeName.Contains("ShaderGen.RWTexture2DResource"))
            {
                return ShaderResourceKind.RWTexture2D;
            }
            else if (fullTypeName.Contains("ShaderGen.DepthTexture2DResource"))
            {
                return ShaderResourceKind.DepthTexture2D;
            }
            else if (fullTypeName.Contains("ShaderGen.DepthTexture2DArrayResource"))
            {
                return ShaderResourceKind.DepthTexture2DArray;
            }
            else if (fullTypeName.Contains("ShaderGen.AtomicBuffer"))
            {
                return ShaderResourceKind.AtomicBuffer;
            }
            else
            {
                return ShaderResourceKind.Uniform;
            }
        }


        private bool GetResourceDecl(SyntaxNode node, out AttributeSyntax attr)
        {
            attr = (node.DescendantNodes().OfType<AttributeSyntax>().FirstOrDefault(
                attrSyntax => attrSyntax.ToString().Contains("Resource")));
            return attr != null;
        }
    }
}
