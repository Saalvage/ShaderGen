using System;
using Microsoft.CodeAnalysis;
using System.Collections.Generic;
using System.Linq;
using ShaderGen.Hlsl;
using ShaderGen.Tests.Tools;
using Xunit;

namespace ShaderGen.Tests
{
    public static class ShaderSetDiscovererTests
    {
        private static void AssertShaderSetInfoEqual(ShaderSetInfo info, string name, string vs = null, string fs = null, string cs = null)
        {
            Assert.Equal(name, info.Name);
            Assert.Equal(vs, info.VertexShader?.ToString());
            Assert.Equal(fs, info.FragmentShader?.ToString());
            Assert.Equal(cs, info.ComputeShader?.ToString());
        }

        [Fact]
        public static void ShaderSetAutoDiscovery()
        {
            var compilation = TestUtil.GetCompilationFromFiles("ShaderSets.cs");
            var ssd = new ShaderSetDiscoverer();

            foreach (var tree in compilation.SyntaxTrees)
            {
                ssd.Visit(tree.GetRoot());
            }

            var shaderSets = ssd.GetShaderSets();
            Assert.Equal(15, shaderSets.Count);
            AssertShaderSetInfoEqual(shaderSets[0], "VertexAndFragment", "TestShaders.Basic.VS", "TestShaders.Basic.FS");
            AssertShaderSetInfoEqual(shaderSets[1], "VertexOnly", "TestShaders.Basic.VS", null);
            AssertShaderSetInfoEqual(shaderSets[2], "FragmentOnly", null, "TestShaders.Basic.FS");
            AssertShaderSetInfoEqual(shaderSets[3], "SimpleCompute", null, null, "TestShaders.Compute.CS");
            AssertShaderSetInfoEqual(shaderSets[4], "OnlyVS", "TestShaders.OnlyVS.VertexShader", null);
            AssertShaderSetInfoEqual(shaderSets[5], "OnlyFS", null, "TestShaders.OnlyFS.FragmentShader");
            AssertShaderSetInfoEqual(shaderSets[6], "Basic", "TestShaders.Basic.VS", "TestShaders.Basic.FS");
            AssertShaderSetInfoEqual(shaderSets[7], "Multiple", "TestShaders.Multiple.VS1", "TestShaders.Multiple.FS1");
            AssertShaderSetInfoEqual(shaderSets[8], "Multiple2", "TestShaders.Multiple.VS2", "TestShaders.Multiple.FS2");
            AssertShaderSetInfoEqual(shaderSets[9], "Multiple3", "TestShaders.Multiple.VS1", "TestShaders.Multiple.FS2");
            AssertShaderSetInfoEqual(shaderSets[10], "ExplicitNull", null, "TestShaders.ExplicitNull.FS");
            AssertShaderSetInfoEqual(shaderSets[11], "ComputeInferred", null, null, "TestShaders.ComputeInferred.ComputeShader");
            AssertShaderSetInfoEqual(shaderSets[12], "Compute", null, null, "TestShaders.Compute.CS1");
            AssertShaderSetInfoEqual(shaderSets[13], "Compute2", null, null, "TestShaders.Compute.CS2");
            AssertShaderSetInfoEqual(shaderSets[14], "Compute3", null, null, "TestShaders.Compute.CS1");
        }

        [Fact]
        public static void ShaderSetAutoDiscoveryExceptions()
        {
            var compilation = TestUtil.GetCompilationFromFiles("ErroneousAutoDiscoverShaderSets/*.cs");

            // Sanity check, if we don't have any it's not finding the folder.
            Assert.True(compilation.SyntaxTrees.Any());

            foreach (var tree in compilation.SyntaxTrees)
            {
                var ssd = new ShaderSetDiscoverer();
                Assert.Throws<ShaderGenerationException>(() => ssd.Visit(tree.GetRoot()));
            }

        }
    }
}
