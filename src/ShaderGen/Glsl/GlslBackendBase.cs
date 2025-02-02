﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;

namespace ShaderGen.Glsl
{
    public abstract class GlslBackendBase : LanguageBackend
    {
        private readonly GlslOptions _options;

        protected readonly HashSet<string> _uniformNames = new HashSet<string>();
        protected readonly HashSet<string> _ssboNames = new HashSet<string>();

        protected GlslBackendBase(Compilation compilation, GlslOptions options) : base(compilation)
        {
            _options = options;
        }

        protected void WriteStructure(StringBuilder sb, StructureDefinition sd)
        {
            sb.AppendLine($"struct {CSharpToShaderType(sd.Name)}");
            sb.AppendLine("{");
            StringBuilder fb = new StringBuilder();
            foreach (FieldDefinition field in sd.Fields)
            {
                string fieldTypeStr = GetStructureFieldType(field);
                fb.Append(fieldTypeStr);
                fb.Append(' ');
                fb.Append(CorrectIdentifier(field.Name.Trim()));
                int arrayCount = field.ArrayElementCount;
                if (arrayCount > 0)
                {
                    fb.Append('['); fb.Append(arrayCount); fb.Append(']');
                }
                fb.Append(';');
                sb.Append("    ");
                sb.AppendLine(fb.ToString());
                fb.Clear();
            }
            sb.AppendLine("};");
            sb.AppendLine();
        }

        protected virtual string GetStructureFieldType(FieldDefinition field)
        {
            return CSharpToShaderType(field.Type);
        }

        protected override MethodProcessResult GenerateFullTextCore(string setName, ShaderFunction function)
        {
            BackendContext context = GetContext(setName);
            StringBuilder sb = new StringBuilder();

            ShaderFunctionAndMethodDeclarationSyntax entryPoint = context.Functions.SingleOrDefault(
                sfabs => sfabs.Function.Name == function.Name);
            if (entryPoint == null)
            {
                throw new ShaderGenerationException("Couldn't find given function: " + function.Name);
            }

            ValidateRequiredSemantics(setName, entryPoint.Function, function.Type);

            StructureDefinition[] orderedStructures
                = StructureDependencyGraph.GetOrderedStructureList(Compilation, context.Structures);

            foreach (StructureDefinition sd in orderedStructures)
            {
                WriteStructure(sb, sd);
            }

            HashSet<ResourceDefinition> resourcesUsed
                = ProcessFunctions(setName, entryPoint, out string funcStr, out string entryStr);

            ValidateResourcesUsed(setName, resourcesUsed);

            int structuredBufferIndex = 0;
            int rwTextureIndex = 0;
            foreach (ResourceDefinition rd in context.Resources)
            {
                if (!resourcesUsed.Contains(rd))
                {
                    continue;
                }

                switch (rd.ResourceKind)
                {
                    case ShaderResourceKind.Uniform:
                        WriteUniform(sb, rd);
                        break;
                    case ShaderResourceKind.Texture2D:
                        WriteTexture2D(sb, rd);
                        break;
                    case ShaderResourceKind.Texture2DArray:
                        WriteTexture2DArray(sb, rd);
                        break;
                    case ShaderResourceKind.TextureCube:
                        WriteTextureCube(sb, rd);
                        break;
                    case ShaderResourceKind.Texture2DMS:
                        WriteTexture2DMS(sb, rd);
                        function.UsesTexture2DMS = true;
                        break;
                    case ShaderResourceKind.Sampler:
                        WriteSampler(sb, rd);
                        break;
                    case ShaderResourceKind.SamplerComparison:
                        WriteSamplerComparison(sb, rd);
                        break;
                    case ShaderResourceKind.StructuredBuffer:
                    case ShaderResourceKind.RWStructuredBuffer:
                    case ShaderResourceKind.AtomicBuffer:
                        WriteStructuredBuffer(sb, rd, rd.ResourceKind == ShaderResourceKind.StructuredBuffer, structuredBufferIndex);
                        structuredBufferIndex++;
                        break;
                    case ShaderResourceKind.RWTexture2D:
                        WriteRWTexture2D(sb, rd, rwTextureIndex);
                        rwTextureIndex++;
                        break;
                    case ShaderResourceKind.DepthTexture2D:
                        WriteDepthTexture2D(sb, rd);
                        break;
                    case ShaderResourceKind.DepthTexture2DArray:
                        WriteDepthTexture2DArray(sb, rd);
                        break;
                    default: throw new ShaderGenerationException("Illegal resource kind: " + rd.ResourceKind);
                }
            }

            sb.AppendLine(funcStr);
            sb.AppendLine(entryStr);

            WriteMainFunction(setName, sb, entryPoint.Function);

            // Append version last because it relies on information from parsing the shader.
            StringBuilder versionSB = new StringBuilder();
            WriteVersionHeader(function, entryPoint.OrderedFunctionList, versionSB);

            sb.Insert(0, versionSB.ToString());

            return new MethodProcessResult(sb.ToString(), resourcesUsed);
        }

        private void WriteMainFunction(string setName, StringBuilder sb, ShaderFunction entryFunction)
        {
            ParameterDefinition input = entryFunction.Parameters.Length > 0
                ? entryFunction.Parameters[0]
                : null;
            StructureDefinition inputType = input != null
                ? GetRequiredStructureType(setName, input.Type)
                : null;
            StructureDefinition outputType =
                entryFunction.ReturnType.Name != "System.Numerics.Vector4"
                && entryFunction.ReturnType.Name != "System.Void"
                    ? GetRequiredStructureType(setName, entryFunction.ReturnType)
                    : null;

            string fragCoordName = null;

            if (inputType != null)
            {
                // Declare "in" variables
                int inVarIndex = 0;
                fragCoordName = null;
                foreach (FieldDefinition field in inputType.Fields)
                {
                    if (entryFunction.Type == ShaderFunctionType.FragmentEntryPoint
                        && fragCoordName == null
                        && field.SemanticType == SemanticType.SystemPosition)
                    {
                        fragCoordName = field.Name;
                    }
                    else
                    {
                        WriteInOutVariable(
                            sb,
                            true,
                            entryFunction.Type == ShaderFunctionType.VertexEntryPoint,
                            CSharpToShaderType(field.Type.Name),
                            CorrectIdentifier(field.Name),
                            inVarIndex);
                        inVarIndex += 1;
                    }
                }
            }

            string mappedReturnType = CSharpToShaderType(entryFunction.ReturnType.Name);

            // Declare "out" variables
            if (entryFunction.Type == ShaderFunctionType.VertexEntryPoint)
            {
                int outVarIndex = 0;
                foreach (FieldDefinition field in outputType.Fields)
                {
                    if (field.SemanticType == SemanticType.SystemPosition)
                    {
                        continue;
                    }
                    else
                    {
                        WriteInOutVariable(
                            sb,
                            false,
                            true,
                            CSharpToShaderType(field.Type.Name),
                            "out_" + CorrectIdentifier(field.Name),
                            outVarIndex);
                        outVarIndex += 1;
                    }
                }
            }
            else
            {
                Debug.Assert(entryFunction.Type == ShaderFunctionType.FragmentEntryPoint
                    || entryFunction.Type == ShaderFunctionType.ComputeEntryPoint);

                if (mappedReturnType == "vec4")
                {
                    WriteInOutVariable(sb, false, false, "vec4", "_outputColor_", 0);
                }
                else if (mappedReturnType != "void")
                {
                    // Composite struct -- declare an out variable for each.
                    int colorTargetIndex = 0;
                    foreach (FieldDefinition field in outputType.Fields)
                    {
                        Debug.Assert(field.SemanticType == SemanticType.ColorTarget);
                        Debug.Assert(field.Type.Name == "System.Numerics.Vector4");
                        int index = colorTargetIndex++;
                        sb.AppendLine($"    layout(location = {index}) out vec4 _outputColor_{index};");
                    }
                }
            }

            sb.AppendLine();

            sb.AppendLine("void main()");
            sb.AppendLine("{");
            if (inputType != null)
            {
                string inTypeName = CSharpToShaderType(inputType.Name);
                sb.AppendLine($"    {inTypeName} {CorrectIdentifier("input")};");

                // Assign synthetic "in" variables (with real field name) to structure passed to actual function.
                int inoutIndex = 0;
                bool foundSystemPosition = false;
                foreach (FieldDefinition field in inputType.Fields)
                {
                    if (entryFunction.Type == ShaderFunctionType.VertexEntryPoint)
                    {
                        sb.AppendLine($"    {CorrectIdentifier("input")}.{CorrectIdentifier(field.Name)} = {CorrectIdentifier(field.Name)};");
                    }
                    else
                    {
                        if (field.SemanticType == SemanticType.SystemPosition && !foundSystemPosition)
                        {
                            Debug.Assert(field.Name == fragCoordName);
                            foundSystemPosition = true;
                            sb.AppendLine($"    {CorrectIdentifier("input")}.{CorrectIdentifier(field.Name)} = gl_FragCoord;");
                        }
                        else
                        {
                            sb.AppendLine($"    {CorrectIdentifier("input")}.{CorrectIdentifier(field.Name)} = fsin_{inoutIndex++};");
                        }
                    }
                }
            }

            // Call actual function.
            string invocationStr = inputType != null
                ? $"{entryFunction.Name}({CorrectIdentifier("input")})"
                : $"{entryFunction.Name}()";
            invocationStr = CorrectEntryPointName(invocationStr);
            if (mappedReturnType != "void")
            {
                sb.AppendLine($"    {mappedReturnType} {CorrectIdentifier("output")} = {invocationStr};");
            }
            else
            {
                sb.AppendLine($"    {invocationStr};");
            }

            // Assign output fields to synthetic "out" variables with normalized "fsin_#" names.
            if (entryFunction.Type == ShaderFunctionType.VertexEntryPoint)
            {
                int inoutIndex = 0;
                FieldDefinition systemPositionField = null;
                foreach (FieldDefinition field in outputType.Fields)
                {
                    if (systemPositionField == null && field.SemanticType == SemanticType.SystemPosition)
                    {
                        systemPositionField = field;
                    }
                    else
                    {
                        sb.AppendLine($"    fsin_{inoutIndex++} = {CorrectIdentifier("output")}.{CorrectIdentifier(field.Name)};");
                    }
                }

                if (systemPositionField == null)
                {
                    // TODO: Should be caught earlier.
                    throw new ShaderGenerationException("Vertex functions must output a SystemPosition semantic.");
                }

                sb.AppendLine($"    gl_Position = {CorrectIdentifier("output")}.{CorrectIdentifier(systemPositionField.Name)};");
                
                if (_options.CorrectDepth)
                {
                    sb.AppendLine("    gl_Position.z = gl_Position.z * 2.0 - gl_Position.w;");
                }

                if (_options.CorrectClipSpace)
                {
                    sb.AppendLine("    gl_Position.y = -gl_Position.y; // Correct for Vulkan clip coordinates");
                }
            }
            else if (entryFunction.Type == ShaderFunctionType.FragmentEntryPoint)
            {
                if (mappedReturnType == "vec4")
                {
                    sb.AppendLine($"    _outputColor_ = {CorrectIdentifier("output")};");
                }
                else if (mappedReturnType != "void")
                {
                    // Composite struct -- assign each field to output
                    int colorTargetIndex = 0;
                    foreach (FieldDefinition field in outputType.Fields)
                    {
                        Debug.Assert(field.SemanticType == SemanticType.ColorTarget);
                        sb.AppendLine($"    _outputColor_{colorTargetIndex++} = {CorrectIdentifier("output")}.{CorrectIdentifier(field.Name)};");
                    }
                }
            }
            sb.AppendLine("}");
        }

        protected override string CSharpToIdentifierNameCore(string typeName, string identifier)
        {
            return GlslKnownIdentifiers.GetMappedIdentifier(typeName, identifier);
        }

        internal override string CorrectIdentifier(string identifier)
        {
            if (s_glslKeywords.Contains(identifier))
            {
                return identifier + "_";
            }

            return identifier;
        }

        internal override void AddResource(string setName, ResourceDefinition rd)
        {
            if (rd.ResourceKind == ShaderResourceKind.Uniform)
            {
                _uniformNames.Add(rd.Name);
            }
            if (rd.ResourceKind == ShaderResourceKind.StructuredBuffer
                || rd.ResourceKind == ShaderResourceKind.RWStructuredBuffer
                || rd.ResourceKind == ShaderResourceKind.AtomicBuffer)
            {
                _ssboNames.Add(rd.Name);
            }

            base.AddResource(setName, rd);
        }

        internal override string CorrectFieldAccess(SymbolInfo symbolInfo)
        {
            string originalName = symbolInfo.Symbol.Name;
            string mapped = CSharpToShaderIdentifierName(symbolInfo);
            string identifier = CorrectIdentifier(mapped);
            if (_uniformNames.Contains(originalName) || _ssboNames.Contains(originalName))
            {
                return "field_" + identifier;
            }
            else
            {
                return identifier;
            }
        }

        internal override string GetComputeGroupCountsDeclaration(UInt3 groupCounts)
        {
            return $"layout(local_size_x = {groupCounts.X}, local_size_y = {groupCounts.Y}, local_size_z = {groupCounts.Z}) in;";
        }

        internal override string ParameterDirection(ParameterDirection direction)
        {
            switch (direction)
            {
                case ShaderGen.ParameterDirection.Out:
                    return "out";
                case ShaderGen.ParameterDirection.InOut:
                    return "inout";
                default:
                    return string.Empty;
            }
        }

        private static readonly HashSet<string> s_glslKeywords = new HashSet<string>()
        {
            "input", "output",
        };

        protected abstract void WriteVersionHeader(
            ShaderFunction function,
            ShaderFunctionAndMethodDeclarationSyntax[] orderedFunctions,
            StringBuilder sb);
        protected abstract void WriteUniform(StringBuilder sb, ResourceDefinition rd);
        protected abstract void WriteSampler(StringBuilder sb, ResourceDefinition rd);
        protected abstract void WriteSamplerComparison(StringBuilder sb, ResourceDefinition rd);
        protected abstract void WriteTexture2D(StringBuilder sb, ResourceDefinition rd);
        protected abstract void WriteTexture2DArray(StringBuilder sb, ResourceDefinition rd);
        protected abstract void WriteTextureCube(StringBuilder sb, ResourceDefinition rd);
        protected abstract void WriteTexture2DMS(StringBuilder sb, ResourceDefinition rd);
        protected abstract void WriteStructuredBuffer(StringBuilder sb, ResourceDefinition rd, bool isReadOnly, int index);
        protected abstract void WriteRWTexture2D(StringBuilder sb, ResourceDefinition rd, int index);
        protected abstract void WriteDepthTexture2D(StringBuilder sb, ResourceDefinition rd);
        protected abstract void WriteDepthTexture2DArray(StringBuilder sb, ResourceDefinition rd);

        protected abstract void WriteInOutVariable(
            StringBuilder sb,
            bool isInVar,
            bool isVertexStage,
            string normalizedType,
            string normalizedIdentifier,
            int index);

        internal override string CorrectCastExpression(string type, string expression)
        {
            return $"{type}({expression})";
        }

        internal override string CorrectEntryPointName(string entryPoint) => "entrypoint_" + entryPoint;
    }
}
