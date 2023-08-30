using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace ShaderGen
{
    internal class ShaderSetDiscoverer : CSharpSyntaxWalker
    {
        private readonly HashSet<string> _discoveredNames = new HashSet<string>();
        private readonly List<ShaderSetInfo> _shaderSets = new List<ShaderSetInfo>();

        private class FragmentCollection
        {
            public List<(string FuncName, string FuncAttr)> Functions = new();
            public string? UnfulfilledName;
            public string? UnfulfilledComputeName;
        }

        private readonly Dictionary<string, FragmentCollection> _partialFragments = new();

        private readonly Compilation _compilation;
        private SemanticModel _currentSemanticModel;

        public ShaderSetDiscoverer(Compilation compilation)
        {
            _compilation = compilation;
        }

        public override void VisitCompilationUnit(CompilationUnitSyntax node)
        {
            _currentSemanticModel = _compilation.GetSemanticModel(node.SyntaxTree);
            base.VisitCompilationUnit(node);
        }

        public override void VisitClassDeclaration(ClassDeclarationSyntax node)
        {
            VisitTypeDeclaration(node);
            base.VisitClassDeclaration(node);
        }

        public override void VisitStructDeclaration(StructDeclarationSyntax node)
        {
            VisitTypeDeclaration(node);
            base.VisitStructDeclaration(node);
        }

        public override void VisitRecordDeclaration(RecordDeclarationSyntax node)
        {
            VisitTypeDeclaration(node);
            base.VisitRecordDeclaration(node);
        }

        private void VisitTypeDeclaration(TypeDeclarationSyntax node)
        {
            string className = null;
            string fullClassName = null;

            var isPartial = node.Modifiers.Any(x => x.IsKind(SyntaxKind.PartialKeyword));

            if (isPartial)
            {
                InitializeNames();

                var frag = GetFragment();
                foreach (var candidate in GetFunctionCandidates(node))
                    frag.Functions.Add(candidate);
            }

            foreach (var (attr, attrName) in node.AttributeLists
                         .SelectMany(x => x.Attributes)
                         .Select(x => (x, Name: x.Name.ToString()))
                         .Where(x => x.Name.Contains("ShaderClass")))
            {
                InitializeNames();

                string shaderName;

                if (attrName.Contains("ComputeShaderClass"))
                {
                    shaderName = GetStringParam(attr, 1) ?? className;

                    string cs = null;
                    if (attr.ArgumentList?.Arguments.Any() ?? false)
                    {
                        cs = GetStringParam(attr, 0);
                        AddComputeShaderInfo(shaderName, PrependClassName(fullClassName, cs));
                        continue;
                    }

                    if (isPartial)
                    {
                        var frag = GetFragment();
                        if (frag.UnfulfilledComputeName != null)
                            throw new($"Multiple unfulfilled compute shader sets in partial class {fullClassName}");

                        frag.UnfulfilledComputeName = shaderName;
                        continue;
                    }

                    ExtractAndAddCompute(fullClassName, shaderName, GetFunctionCandidates(node));
                    continue;
                }

                shaderName = GetStringParam(attr, 2) ?? className;

                string vs = null;
                string fs = null;
                if (attr.ArgumentList?.Arguments.Any() ?? false)
                {
                    vs = GetStringParam(attr, 0);
                    fs = GetStringParam(attr, 1);

                    AddShaderSetInfo(shaderName, PrependClassName(fullClassName, vs), PrependClassName(fullClassName, fs));
                    continue;
                }

                if (isPartial)
                {
                    var frag = GetFragment();
                    if (frag.UnfulfilledName != null)
                        throw new($"Multiple unfulfilled shader sets in partial class {fullClassName}");

                    frag.UnfulfilledName = shaderName;
                    continue;
                }

                ExtractAndAdd(fullClassName, shaderName, GetFunctionCandidates(node), vs, fs);
            }

            void InitializeNames()
            {
                className ??= node.Identifier.Text;
                fullClassName ??= Utilities.GetFullNamespace(node) + '.' + className;
            }

            FragmentCollection GetFragment()
            {
                if (!_partialFragments.TryGetValue(fullClassName, out var val))
                {
                    val = new();
                    _partialFragments.Add(fullClassName, val);
                }

                return val;
            }
        }

        public override void VisitAttribute(AttributeSyntax node)
        {
            if (!((node.Parent as AttributeListSyntax)?.Target?.Identifier.IsKind(SyntaxKind.AssemblyKeyword) ?? false))
                return;

            var attrName = node.Name.ToString();
            if (!attrName.Contains("ShaderSet"))
                return;

            if (attrName.Contains("ComputeShaderSet"))
            {
                var (csName, csFunc) = ParseComputeArgs();
                AddComputeShaderInfo(csName, csFunc);
                return;

                (string, string) ParseComputeArgs() {
                    // Type, string
                    if (TryGetTypeParam(node, 0, out var type0))
                        return (type0.Name, $"{type0.FullName}.{GetStringParam(node, 1)}");

                    // string, Type, string
                    if (TryGetTypeParam(node, 1, out var type1))
                        return (GetStringParam(node, 0), $"{type1.FullName}.{GetStringParam(node, 2)}");

                    // string, string
                    return (GetStringParam(node, 0), GetStringParam(node, 1));
                }
            }

            var name = GetStringParam(node, 0);

            var is1Type = TryGetTypeParam(node, 1, out var type1);
            var is3Type = TryGetTypeParam(node, 3, out var type3);
            if (is1Type || is3Type)
            {
                AddShaderSetInfo(name,
                    !is1Type ? null : $"{type1.FullName}.{GetStringParam(node, 2)}",
                    !is3Type ? null : $"{type3.FullName}.{GetStringParam(node, 4)}");
                return;
            }

            var vs = GetStringParam(node, 1);
            var fs = GetStringParam(node, 2);
            AddShaderSetInfo(name, vs, fs);
        }

        private void AddComputeShaderInfo(string name, string cs)
        {
            if (cs == null)
                throw new ShaderGenerationException("Compute shader set must specify a shader name.");

            var csName = ValidateShaderName(cs, "compute");

            AddDiscoveredNames(name);

            _shaderSets.Add(new ShaderSetInfo(name, csName));
        }

        private void AddShaderSetInfo(string name, string vs, string fs)
        {
            var vsName = ValidateShaderName(vs, "vertex");
            var fsName = ValidateShaderName(fs, "fragment");

            if (vsName == null && fsName == null)
            {
                throw new ShaderGenerationException("Shader set must specify at least one shader name.");
            }

            AddDiscoveredNames(name);

            _shaderSets.Add(new ShaderSetInfo(
                name,
                vsName,
                fsName));
        }

        private void AddDiscoveredNames(string name)
        {
            if (!_discoveredNames.Add(name))
            {
                throw new ShaderGenerationException("Multiple shader sets with the same name were defined: " + name);
            }
        }

        private static TypeAndMethodName ValidateShaderName(string shaderName, string shaderType)
            => shaderName == null
                ? null
                : TypeAndMethodName.Get(shaderName, out var shaderTypeAndMethodName)
                    ? shaderTypeAndMethodName
                    : throw new ShaderGenerationException($"Shader set has an incomplete or invalid {shaderType} shader name.");

        private static IEnumerable<(string FuncName, string FuncAttr)> GetFunctionCandidates(TypeDeclarationSyntax node)
            => node.Members
                .OfType<MethodDeclarationSyntax>()
                .SelectMany(x =>
                    x.AttributeLists.SelectMany(x => x.Attributes, (_, y) => y.Name.ToString()),
                    (x, y) => (x.Identifier.Text, y));

        private bool TryGetTypeParam(AttributeSyntax node, int index, out (string Name, string FullName) typeInfo)
        {
            typeInfo = (null, null);

            var args = node.ArgumentList?.Arguments;
            if (args is null || index >= args?.Count)
                return false;

            if (args.Value[index].Expression is TypeOfExpressionSyntax toes)
            { 
                var symbol = (INamedTypeSymbol)_currentSemanticModel.GetSymbolInfo(toes.Type).Symbol;
                typeInfo = (symbol.Name, Utilities.GetFullName(symbol));
                return true;
            }

            return false;
        }

        private string GetStringParam(AttributeSyntax node, int index)
        {
            var args = node.ArgumentList?.Arguments;
            if (args is null || index >= args?.Count)
                return null;

            var val = _currentSemanticModel.GetConstantValue(args.Value[index].Expression);
            return val.HasValue ? (string)val.Value : null;
        }

        private void ExtractAndAdd(string fullClassName, string shaderName,
            IEnumerable<(string FuncName, string FuncAttr)> enumerable, string vs, string fs)
        {
            foreach (var (funcName, funcAttr) in enumerable)
            {
                if (funcAttr.Contains("VertexShader"))
                {
                    if (vs != null)
                        throw new ShaderGenerationException($"ShaderClassAttribute for class {fullClassName} has ambiguous vertex shader");

                    vs = funcName;
                }
                else if (funcAttr.Contains("FragmentShader"))
                {
                    if (fs != null)
                        throw new ShaderGenerationException($"ShaderClassAttribute for class {fullClassName} has ambiguous fragment shader.");

                    fs = funcName;
                }
            }

            AddShaderSetInfo(shaderName, PrependClassName(fullClassName, vs), PrependClassName(fullClassName, fs));
        }

        public void ExtractAndAddCompute(string fullClassName, string shaderName, IEnumerable<(string FuncName, string FuncAttr)> enumerable)
        {
            string cs = null;

            foreach (var (funcName, funcAttr) in enumerable)
            {
                if (!funcAttr.Contains("ComputeShader"))
                    continue;

                if (cs != null)
                    throw new ShaderGenerationException($"ComputeShaderClassAttribute for class {fullClassName} has ambiguous shader");

                cs = funcName;
            }

            AddComputeShaderInfo(shaderName, PrependClassName(fullClassName, cs));
        }

        private string PrependClassName(string fullClassName, string functionName)
            => functionName == null ? null : fullClassName + '.' + functionName;

        public IReadOnlyList<ShaderSetInfo> GetShaderSets() {
            // Reconcile partial fragments.
            foreach (var (fullClassName, fragment) in _partialFragments)
            {
                if (fragment.UnfulfilledName != null)
                    ExtractAndAdd(fullClassName, fragment.UnfulfilledName, fragment.Functions, null, null);

                if (fragment.UnfulfilledComputeName != null)
                    ExtractAndAddCompute(fullClassName, fragment.UnfulfilledComputeName, fragment.Functions);
            }

            _partialFragments.Clear();

            return _shaderSets;
        }
    }
}
