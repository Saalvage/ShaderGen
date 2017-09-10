﻿using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ShaderGen
{
    public abstract class LanguageBackend
    {
        protected readonly SemanticModel Model;

        internal List<StructureDefinition> Structures { get; } = new List<StructureDefinition>();
        internal List<UniformDefinition> Uniforms { get; } = new List<UniformDefinition>();
        internal List<ShaderFunctionAndBlockSyntax> Functions { get; } = new List<ShaderFunctionAndBlockSyntax>();

        private readonly Dictionary<ShaderFunction, string> _fullTextShaders = new Dictionary<ShaderFunction, string>();

        internal LanguageBackend(SemanticModel model)
        {
            Model = model;
        }

        internal ShaderModel GetShaderModel()
        {
            return new ShaderModel(
                Structures.ToArray(),
                Uniforms.ToArray(),
                Functions.Select(sfabs => sfabs.Function).ToArray());
        }

        public string GetCode(ShaderFunction entryFunction)
        {
            if (entryFunction == null)
            {
                throw new ArgumentNullException(nameof(entryFunction));
            }
            if (!entryFunction.IsEntryPoint)
            {
                throw new ArgumentException($"IsEntryPoint must be true for parameter {nameof(entryFunction)}");
            }

            if (!_fullTextShaders.TryGetValue(entryFunction, out string result))
            {
                result = GenerateFullTextCore(entryFunction);
                _fullTextShaders.Add(entryFunction, result);
            }

            return result;
        }

        internal string CSharpToShaderType(string fullType)
        {
            if (fullType == null)
            {
                throw new ArgumentNullException(nameof(fullType));
            }

            return CSharpToShaderTypeCore(fullType);
        }

        internal string CSharpToShaderType(TypeSyntax typeSyntax)
        {
            return CSharpToShaderTypeCore(Model.GetFullTypeName(typeSyntax));
        }


        internal virtual void AddStructure(StructureDefinition sd)
        {
            if (sd == null)
            {
                throw new ArgumentNullException(nameof(sd));
            }

            Structures.Add(sd);
        }

        internal virtual void AddUniform(UniformDefinition ud)
        {
            if (ud == null)
            {
                throw new ArgumentNullException(nameof(ud));
            }

            Uniforms.Add(ud);
        }

        internal virtual void AddFunction(ShaderFunctionAndBlockSyntax sf)
        {
            if (sf == null)
            {
                throw new ArgumentNullException(nameof(sf));
            }

            Functions.Add(sf);
        }

        internal string CSharpToShaderFunctionName(string type, string method)
        {
            if (type == null)
            {
                throw new ArgumentNullException(nameof(type));
            }
            if (method == null)
            {
                throw new ArgumentNullException(nameof(method));
            }

            return CSharpToShaderFunctionNameCore(type, method);
        }

        protected abstract string CSharpToShaderTypeCore(string fullType);
        protected abstract string CSharpToShaderFunctionNameCore(string type, string method);
        protected abstract string GenerateFullTextCore(ShaderFunction function);
    }
}
