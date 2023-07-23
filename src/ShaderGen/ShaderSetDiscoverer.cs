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

            foreach (var (attr, attrName) in node.AttributeLists
                         .SelectMany(x => x.Attributes)
                         .Select(x => (x, Name: x.Name.ToString()))
                         .Where(x => x.Name.Contains("ShaderClass")))
            {
                className ??= node.Identifier.Text;
                fullClassName ??= Utilities.GetFullNamespace(node) + '.' + className;

                string shaderName;

                if (attrName.Contains("ComputeShaderClass"))
                {
                    shaderName = GetStringParam(attr, 1) ?? className;

                    string cs = null;
                    if (attr.ArgumentList?.Arguments.Any() ?? false)
                    {
                        cs = GetStringParam(attr, 0);
                        AddComputeShaderInfo(shaderName, cs);
                        continue;
                    }

                    foreach (var (funcName, funcAttr)
                             in GetFunctionCandidates(node))
                    {
                        if (!funcAttr.Contains("ComputeShader"))
                            continue;

                        if (cs != null)
                            throw new ShaderGenerationException($"ComputeShaderClassAttribute for class {fullClassName} has ambiguous shader");

                        cs = funcName;
                    }

                    AddComputeShaderInfo(shaderName, cs);

                    continue;
                }

                shaderName = GetStringParam(attr, 2) ?? className;

                string vs = null;
                string fs = null;
                if (attr.ArgumentList?.Arguments.Any() ?? false)
                {
                    vs = GetStringParam(attr, 0);
                    fs = GetStringParam(attr, 1);

                    AddShaderSetInfo(shaderName, fullClassName + '.' + vs, fullClassName + '.' + fs);
                    continue;
                }

                foreach (var (funcName, funcAttr) in GetFunctionCandidates(node))
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

                AddShaderSetInfo(shaderName, fullClassName + '.' + vs, fullClassName + '.' + fs);
            }
        }

        public override void VisitAttribute(AttributeSyntax node)
        {
            if (!((node.Parent as AttributeListSyntax)?.Target?.Identifier.IsKind(SyntaxKind.AssemblyKeyword) ?? false))
                return;

            var attrName = node.Name.ToString();
            if (!attrName.Contains("ShaderSet"))
                return;

            string name;

            if (attrName.Contains("ComputeShaderSet"))
            {
                name = GetStringParam(node, 0);
                var cs = GetStringParam(node, 1);
                AddComputeShaderInfo(name, cs);
                return;
            }

            name = GetStringParam(node, 0);
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
                    x.AttributeLists .SelectMany(x => x.Attributes, (_, y) => y.Name.ToString()),
                    (x, y) => (x.Identifier.Text, y));

        private static string GetStringParam(AttributeSyntax node, int index)
        {
            string text = node.ArgumentList?.Arguments[index].ToString();
            if (text == "null")
            {
                return null;
            }
            else
            {
                return text;
            }
        }

        public IReadOnlyList<ShaderSetInfo> GetShaderSets() => _shaderSets;
    }
}
