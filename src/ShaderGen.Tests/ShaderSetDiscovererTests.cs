﻿using System.Linq;
using Xunit;
using static TestShaders.VeldridShaders.UIntVertexAttribs;

namespace ShaderGen.Tests
{
    public static class ShaderSetDiscovererTests
    {
        [Fact]
        public static void ShaderSetAutoDiscovery()
        {
            var compilation = TestUtil.GetCompilationFromFiles("ShaderSets.cs");
            var ssd = new ShaderSetDiscoverer(compilation);

            foreach (var tree in compilation.SyntaxTrees)
            {
                ssd.Visit(tree.GetRoot());
            }

            var shaderSets = ssd.GetShaderSets();

            (string, string, string, string)[] shaderSetValues =
            {
                ("VertexAndFragment", "TestShaders.Basic.VS", "TestShaders.Basic.FS", null),
                ("VertexAndFragment2", "TestShaders.Basic.VS", "TestShaders.Basic.FS", null),
                ("VertexOnly", "TestShaders.Basic.VS", null, null),
                ("VertexOnly2", "TestShaders.Basic.VS", null, null),
                ("FragmentOnly", null, "TestShaders.Basic.FS", null),
                ("FragmentOnly2", null, "TestShaders.Basic.FS", null),
                ("SimpleCompute", null, null, "TestShaders.Compute.CS1"),
                ("SimpleCompute2", null, null, "TestShaders.Compute.CS1"),
                ("ComputeClone", null, null, "TestShaders.ComputeClone.CS2"),
                ("OnlyVS", "TestShaders.OnlyVS.VertexShader", null, null),
                ("OnlyFS", null, "TestShaders.OnlyFS.FragmentShader", null),
                ("Basic", "TestShaders.Basic.VS", "TestShaders.Basic.FS", null),
                ("Multiple", "TestShaders.Multiple.VS1", "TestShaders.Multiple.FS1", null),
                ("Multiple2", "TestShaders.Multiple.VS2", "TestShaders.Multiple.FS2", null),
                ("Multiple3", "TestShaders.Multiple.VS1", "TestShaders.Multiple.FS2", null),
                ("ExplicitNull", null, "TestShaders.ExplicitNull.FS", null),
                ("ComputeInferred", null, null, "TestShaders.ComputeInferred.ComputeShader"),
                ("Compute", null, null, "TestShaders.Compute.CS1"),
                ("Compute2", null, null, "TestShaders.Compute.CS2"),
                ("Compute3", null, null, "TestShaders.Compute.CS1"),
            };

            Assert.Equal(shaderSetValues.Length, shaderSets.Count);
            foreach (var (info, shaderSetValue) in shaderSets.Zip(shaderSetValues))
            {
                var (name, vs, fs, cs) = shaderSetValue;
                Assert.Equal(name, info.Name);
                Assert.Equal(vs, info.VertexShader?.ToString());
                Assert.Equal(fs, info.FragmentShader?.ToString());
                Assert.Equal(cs, info.ComputeShader?.ToString());
            }
        }

        [Fact]
        public static void ShaderSetAutoDiscoveryExceptions()
        {
            var compilation = TestUtil.GetCompilationFromFiles("ErroneousAutoDiscoverShaderSets/*.cs");

            // Sanity check, if we don't have any it's not finding the folder.
            Assert.True(compilation.SyntaxTrees.Any());

            foreach (var tree in compilation.SyntaxTrees)
            {
                var ssd = new ShaderSetDiscoverer(compilation);
                Assert.Throws<ShaderGenerationException>(() => ssd.Visit(tree.GetRoot()));
            }

        }
    }
}
