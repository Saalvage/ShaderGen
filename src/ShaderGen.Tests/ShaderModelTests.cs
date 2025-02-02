﻿using Microsoft.CodeAnalysis;
using System.Collections.Generic;
using ShaderGen.Hlsl;
using ShaderGen.Tests.Tools;
using TestShaders;
using Xunit;

namespace ShaderGen.Tests
{
    public class ShaderModelTests
    {
        private GeneratedShaderSet Compile(string vertexFunctionName, string fragmentFunctionName = null)
        {
            Compilation compilation = TestUtil.GetCompilation();
            HlslBackend backend = new HlslBackend(compilation);
            ShaderGenerator sg = new ShaderGenerator(compilation, backend, vertexFunctionName, fragmentFunctionName);
            ShaderGenerationResult genResult = sg.GenerateShaders();
            IReadOnlyList<GeneratedShaderSet> sets = genResult.GetOutput(backend);
            Assert.Equal(1, sets.Count);
            return sets[0];
        }

        [Fact]
        public void TestVertexShader_ShaderModel()
        {
            string functionName = "TestShaders.TestVertexShader.VS";
            ShaderModel shaderModel = Compile(functionName).Model;

            Assert.Equal(2, shaderModel.Structures.Length);
            Assert.Equal(3, shaderModel.AllResources.Length);
            ShaderFunction vsEntry = shaderModel.GetFunction(functionName);
            Assert.Equal("VS", vsEntry.Name);
            Assert.Single(vsEntry.Parameters);
            Assert.True(vsEntry.IsEntryPoint);
            Assert.Equal(ShaderFunctionType.VertexEntryPoint, vsEntry.Type);
        }

        [Fact]
        public void TestVertexShader_VertexSemantics()
        {
            string functionName = "TestShaders.TestVertexShader.VS";
            ShaderModel shaderModel = Compile(functionName).Model;

            StructureDefinition vsInput = shaderModel.GetStructureDefinition(nameof(TestShaders) + "." + nameof(PositionTexture));
            Assert.Equal(SemanticType.Position, vsInput.Fields[0].SemanticType);
            Assert.Equal(SemanticType.TextureCoordinate, vsInput.Fields[1].SemanticType);

            StructureDefinition fsInput = shaderModel.GetStructureDefinition(
                nameof(TestShaders) + "." + nameof(TestVertexShader) + "+" + nameof(TestVertexShader.VertexOutput));
            Assert.Equal(SemanticType.SystemPosition, fsInput.Fields[0].SemanticType);
            Assert.Equal(SemanticType.TextureCoordinate, fsInput.Fields[1].SemanticType);
        }

        [SkippableFact(typeof(RequiredToolFeatureMissingException))]
        public void PartialFiles()
        {
            ToolChain toolChain = ToolChain.Get(ToolFeatures.ToCompiled);
            if (toolChain == null)
            {
                throw new RequiredToolFeatureMissingException("No tool chain supporting compilation was found!");
            }

            GeneratedShaderSet set = Compile("TestShaders.PartialVertex.VertexShaderFunc");

            string vsCode = set.VertexShaderCode;
            CompileResult result = toolChain.Compile(vsCode, Stage.Vertex, "VertexShaderFunc");
            Assert.False(result.HasError, result.ToString());
        }

        [Fact]
        public void PointLightsInfo_CorrectSize()
        {
            ShaderModel shaderModel = Compile("TestShaders.PointLightTestShaders.VS").Model;

            Assert.Single(shaderModel.AllResources);
            Assert.Equal(208, shaderModel.GetTypeSize(shaderModel.AllResources[0].ValueType));
        }

        [Fact]
        public void MultipleResourceSets_CorrectlyParsed()
        {
            ShaderModel shaderModel = Compile("TestShaders.MultipleResourceSets.VS").Model;

            Assert.Equal(13, shaderModel.AllResources.Length);

            Assert.Equal(0, shaderModel.AllResources[0].Set);
            Assert.Equal(0, shaderModel.AllResources[0].Binding);
            Assert.Equal(0, shaderModel.AllResources[1].Set);
            Assert.Equal(1, shaderModel.AllResources[1].Binding);
            Assert.Equal(1, shaderModel.AllResources[2].Set);
            Assert.Equal(0, shaderModel.AllResources[2].Binding);
            Assert.Equal(2, shaderModel.AllResources[3].Set);
            Assert.Equal(0, shaderModel.AllResources[3].Binding);
            Assert.Equal(3, shaderModel.AllResources[4].Set);
            Assert.Equal(0, shaderModel.AllResources[4].Binding);
            Assert.Equal(4, shaderModel.AllResources[5].Set);
            Assert.Equal(0, shaderModel.AllResources[5].Binding);
            Assert.Equal(0, shaderModel.AllResources[6].Set);
            Assert.Equal(2, shaderModel.AllResources[6].Binding);

            Assert.Equal(0, shaderModel.AllResources[7].Set);
            Assert.Equal(3, shaderModel.AllResources[7].Binding);
            Assert.Equal(4, shaderModel.AllResources[8].Set);
            Assert.Equal(1, shaderModel.AllResources[8].Binding);
            Assert.Equal(0, shaderModel.AllResources[9].Set);
            Assert.Equal(4, shaderModel.AllResources[9].Binding);

            Assert.Equal(2, shaderModel.AllResources[10].Set);
            Assert.Equal(1, shaderModel.AllResources[10].Binding);
            Assert.Equal(0, shaderModel.AllResources[11].Set);
            Assert.Equal(5, shaderModel.AllResources[11].Binding);
            Assert.Equal(1, shaderModel.AllResources[12].Set);
            Assert.Equal(1, shaderModel.AllResources[12].Binding);
        }

        [Fact]
        public void RecordStructs_CorrectlyParsed()
        {
            ShaderModel shaderModel = Compile("TestShaders.RecordStructs.VS").Model;

            Assert.Single(shaderModel.VertexResources);
            Assert.Equal(SemanticType.Position, shaderModel.Structures[2].Fields[0].SemanticType);
            Assert.Equal(SemanticType.TextureCoordinate, shaderModel.Structures[2].Fields[1].SemanticType);

            Assert.Equal(SemanticType.SystemPosition, shaderModel.Structures[1].Fields[0].SemanticType);
            Assert.Equal(SemanticType.TextureCoordinate, shaderModel.Structures[1].Fields[1].SemanticType);
        }

        [Fact]
        public void ResourcesUsedInStages()
        {
            ShaderModel shaderModel = Compile("TestShaders.UsedResourcesShaders.VS",
                "TestShaders.UsedResourcesShaders.FS").Model;

            Assert.Equal(4, shaderModel.VertexResources.Length);
            Assert.Contains(shaderModel.VertexResources, rd => rd.Name == "VS_M0");
            Assert.Contains(shaderModel.VertexResources, rd => rd.Name == "VS_M1");
            Assert.Contains(shaderModel.VertexResources, rd => rd.Name == "VS_T0");
            Assert.Contains(shaderModel.VertexResources, rd => rd.Name == "VS_S0");

            Assert.Equal(5, shaderModel.FragmentResources.Length);
            Assert.Contains(shaderModel.FragmentResources, rd => rd.Name == "FS_M0");
            Assert.Contains(shaderModel.FragmentResources, rd => rd.Name == "FS_M1");
            Assert.Contains(shaderModel.FragmentResources, rd => rd.Name == "FS_T0");
            Assert.Contains(shaderModel.FragmentResources, rd => rd.Name == "FS_S0");
            Assert.Contains(shaderModel.FragmentResources, rd => rd.Name == "FS_M2_Indirect");
        }

        [Fact]
        public void StructureSizes()
        {
            ShaderModel shaderModel = Compile("TestShaders.StructureSizeTests.VS").Model;

            StructureDefinition test0 = shaderModel.GetStructureDefinition(
                    nameof(TestShaders) + "." + nameof(StructureSizeTests) + "+" + nameof(StructureSizeTests.SizeTest_0));
            Assert.Equal(48, test0.Alignment.CSharpSize);
            Assert.True(test0.CSharpMatchesShaderAlignment);

            StructureDefinition test1 = shaderModel.GetStructureDefinition(
                    nameof(TestShaders) + "." + nameof(StructureSizeTests) + "+" + nameof(StructureSizeTests.SizeTest_1));
            Assert.Equal(52, test1.Alignment.CSharpSize);
            Assert.True(test1.CSharpMatchesShaderAlignment);

            StructureDefinition test2 = shaderModel.GetStructureDefinition(
                    nameof(TestShaders) + "." + nameof(StructureSizeTests) + "+" + nameof(StructureSizeTests.SizeTest_2));
            Assert.Equal(48, test2.Alignment.CSharpSize);
            Assert.False(test2.CSharpMatchesShaderAlignment);

            StructureDefinition test3 = shaderModel.GetStructureDefinition(
                    nameof(TestShaders) + "." + nameof(StructureSizeTests) + "+" + nameof(StructureSizeTests.SizeTest_3));
            Assert.Equal(64, test3.Alignment.CSharpSize);
            Assert.False(test3.CSharpMatchesShaderAlignment);

            Assert.Equal(4, shaderModel.GetTypeSize(test3.Fields[0].Type));
            Assert.Equal(12, shaderModel.GetTypeSize(test3.Fields[1].Type));
            Assert.Equal(12, shaderModel.GetTypeSize(test3.Fields[2].Type));
            Assert.Equal(16, shaderModel.GetTypeSize(test3.Fields[3].Type));
            Assert.Equal(4, shaderModel.GetTypeSize(test3.Fields[4].Type));
            Assert.Equal(16, shaderModel.GetTypeSize(test3.Fields[5].Type));
        }
    }
}
