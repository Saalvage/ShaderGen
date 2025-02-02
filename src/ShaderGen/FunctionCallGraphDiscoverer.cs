﻿using Microsoft.CodeAnalysis.CSharp;
using System.Collections.Generic;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using Microsoft.CodeAnalysis;
using System.Linq;
using System.Diagnostics;

namespace ShaderGen
{
    internal class FunctionCallGraphDiscoverer
    {
        public Compilation Compilation { get; }
        private CallGraphNode _rootNode;
        private Dictionary<TypeAndMethodName, CallGraphNode> _nodesByName = new Dictionary<TypeAndMethodName, CallGraphNode>();

        public FunctionCallGraphDiscoverer(Compilation compilation, TypeAndMethodName rootMethod)
        {
            Compilation = compilation;
            _rootNode = new CallGraphNode() { Name = rootMethod };
            bool foundDecl = GetDeclaration(rootMethod, out _rootNode.Declaration);
            Debug.Assert(foundDecl);
            _nodesByName.Add(rootMethod, _rootNode);
        }

        public ShaderFunctionAndMethodDeclarationSyntax[] GetOrderedCallList()
        {
            HashSet<ShaderFunctionAndMethodDeclarationSyntax> result = new HashSet<ShaderFunctionAndMethodDeclarationSyntax>();
            TraverseNode(result, _rootNode);
            return result.ToArray();
        }

        private void TraverseNode(HashSet<ShaderFunctionAndMethodDeclarationSyntax> result, CallGraphNode node)
        {
            foreach (ShaderFunctionAndMethodDeclarationSyntax existing in result)
            {
                if (node.Parents.Any(cgn => cgn.Name.Equals(existing)))
                {
                    throw new ShaderGenerationException("There was a cyclical call graph involving " + existing + " and " + node.Name);
                }
            }

            foreach (CallGraphNode child in node.Children)
            {
                TraverseNode(result, child);
            }

            ShaderFunctionAndMethodDeclarationSyntax sfab = Utilities.GetShaderFunction(node.Declaration, Compilation, false);

            result.Add(sfab);
        }

        public void GenerateFullGraph()
        {
            ExploreCallNode(_rootNode);
        }

        private void ExploreCallNode(CallGraphNode node)
        {
            Debug.Assert(node.Declaration != null);
            MethodWalker walker = new MethodWalker(this);
            walker.Visit(node.Declaration);
            TypeAndMethodName[] childrenNames = walker.GetChildren();
            foreach (TypeAndMethodName childName in childrenNames)
            {
                if (childName.Equals(node.Name))
                {
                    throw new ShaderGenerationException(
                        $"A function invoked transitively by {_rootNode.Name} calls {childName}, which calls itself. Recursive functions are not supported.");
                }
                CallGraphNode childNode = GetNode(childName);
                if (childNode.Declaration != null)
                {
                    childNode.Parents.Add(node);
                    node.Children.Add(childNode);
                    ExploreCallNode(childNode);
                }
            }
        }

        private CallGraphNode GetNode(TypeAndMethodName name)
        {
            if (!_nodesByName.TryGetValue(name, out CallGraphNode node))
            {
                node = new CallGraphNode() { Name = name };
                GetDeclaration(name, out node.Declaration);
                _nodesByName.Add(name, node);
            }

            return node;
        }

        private bool GetDeclaration(TypeAndMethodName name, out BaseMethodDeclarationSyntax decl)
        {
            bool isConstructor = name.MethodName == ".ctor";
            INamedTypeSymbol symb = Compilation.GetTypeByMetadataName(name.TypeName);
            foreach (SyntaxReference synRef in symb.DeclaringSyntaxReferences)
            {
                SyntaxNode node = synRef.GetSyntax();
                foreach (SyntaxNode child in node.ChildNodes())
                {
                    if (isConstructor)
                    {
                        if (child is ConstructorDeclarationSyntax cds)
                        {
                            decl = cds;
                            return true;
                        }
                    }


                    if (child is MethodDeclarationSyntax mds)
                    {
                        if (mds.Identifier.ToFullString() == name.MethodName)
                        {
                            decl = mds;
                            return true;
                        }
                    }
                }
            }

            decl = null;
            return false;
        }

        private class MethodWalker : CSharpSyntaxWalker
        {
            private readonly FunctionCallGraphDiscoverer _discoverer;
            private readonly HashSet<TypeAndMethodName> _children = new HashSet<TypeAndMethodName>();

            public MethodWalker(FunctionCallGraphDiscoverer discoverer) : base(SyntaxWalkerDepth.StructuredTrivia)
            {
                _discoverer = discoverer;
            }

            public override void VisitObjectCreationExpression(ObjectCreationExpressionSyntax node)
            {
                SymbolInfo symbolInfo = _discoverer.Compilation.GetSemanticModel(node.SyntaxTree).GetSymbolInfo(node);
                ISymbol symbol = symbolInfo.Symbol;
                if (symbol == null && symbolInfo.CandidateSymbols.Length == 1)
                {
                    symbol = symbolInfo.CandidateSymbols[0];
                }
                if (symbol == null)
                {
                    throw new ShaderGenerationException($"A constructor reference could not be identified: {node}");
                }

                string containingType = symbol.ContainingType.GetFullMetadataName();
                _children.Add(new TypeAndMethodName() { TypeName = containingType, MethodName = ".ctor" });

                base.VisitObjectCreationExpression(node);
            }

            public override void VisitInvocationExpression(InvocationExpressionSyntax node)
            {
                if (node.Expression is IdentifierNameSyntax ins)
                {
                    SymbolInfo symbolInfo = _discoverer.Compilation.GetSemanticModel(node.SyntaxTree).GetSymbolInfo(ins);
                    ISymbol symbol = symbolInfo.Symbol;
                    if (symbol == null && symbolInfo.CandidateSymbols.Length == 1)
                    {
                        symbol = symbolInfo.CandidateSymbols[0];
                    }
                    if (symbol == null)
                    {
                        throw new ShaderGenerationException($"A member reference could not be identified: {node.Expression}");
                    }

                    string containingType = symbol.ContainingType.ToDisplayString();
                    string methodName = symbol.Name;
                    _children.Add(new TypeAndMethodName() { TypeName = containingType, MethodName = methodName });
                }
                else if (node.Expression is MemberAccessExpressionSyntax maes)
                {
                    SymbolInfo methodSymbol = _discoverer.Compilation.GetSemanticModel(maes.SyntaxTree).GetSymbolInfo(maes);
                    ISymbol symbol = methodSymbol.Symbol;
                    if (symbol == null && methodSymbol.CandidateSymbols.Length == 1)
                    {
                        symbol = methodSymbol.CandidateSymbols[0];
                    }
                    if (symbol == null)
                    {
                        throw new ShaderGenerationException($"A member reference could not be identified: {node.Expression}");
                    }

                    if (symbol is IMethodSymbol ims)
                    {
                        string containingType = Utilities.GetFullMetadataName(ims.ContainingType);
                        string methodName = ims.MetadataName;
                        _children.Add(new TypeAndMethodName() { TypeName = containingType, MethodName = methodName });
                    }
                }
                else
                {
                    throw new NotImplementedException();
                }

                base.VisitInvocationExpression(node);
            }

            public TypeAndMethodName[] GetChildren() => _children.ToArray();
        }
    }

    internal class CallGraphNode
    {
        public TypeAndMethodName Name;
        /// <summary>
        /// May be null.
        /// </summary>
        public BaseMethodDeclarationSyntax Declaration;
        /// <summary>
        /// Functions called by this function.
        /// </summary>
        public HashSet<CallGraphNode> Children = new HashSet<CallGraphNode>();
        public HashSet<CallGraphNode> Parents = new HashSet<CallGraphNode>();
    }
}
