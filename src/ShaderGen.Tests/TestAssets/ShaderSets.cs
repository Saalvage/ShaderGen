using ShaderGen;
using System.Runtime.CompilerServices;
using TestShaders;

// Multi-line declaration
[assembly: ShaderSet(
    "VertexAndFragment",
    "TestShaders.Basic.VS",
    "TestShaders.Basic.FS")]
[assembly: ShaderSet(
    "VertexAndFragment2",
    typeof(Basic), nameof(Basic.VS),
    typeof(Basic), nameof(Basic.FS))]

[assembly: ShaderSet("VertexOnly", "TestShaders.Basic.VS", null)]
[assembly: ShaderSet("VertexOnly2", typeof(Basic), nameof(Basic.VS), null, null)]

[assembly: ShaderSet("FragmentOnly", null, "TestShaders.Basic.FS")]
[assembly: ShaderSet("FragmentOnly2", null, null, typeof(Basic), nameof(Basic.FS))]

[assembly: ComputeShaderSet("SimpleCompute", "TestShaders.Compute.CS1")]
[assembly: ComputeShaderSet("SimpleCompute2", typeof(Compute), nameof(Compute.CS1))]
[assembly: ComputeShaderSet(typeof(ComputeClone), nameof(ComputeClone.CS2))]

namespace TestShaders;

[ShaderClass]
public class OnlyVS
{
    [VertexShader] public void VertexShader() { }
    /* unmarked */ public void FragmentShader() { }

    [ComputeShader(1, 1, 1)] public void ComputeShader() { }
}

[ShaderClass]
public class OnlyFS
{
    [FragmentShader] public void FragmentShader() { }
    /* any attribute */ [MethodImpl(MethodImplOptions.AggressiveInlining)] public void DoThing() { }
}

[ShaderClass]
public class Basic
{
    [VertexShader] public void VS() { }
    [FragmentShader] public void FS() { }
}

[ShaderClass(nameof(VS1), nameof(FS1))]
[ShaderClass("VS2", "FS2", "Multiple2"),
    ShaderClass("VS1", "FS2", "Multiple3")]
public class Multiple
{
    [VertexShader] public void VS1() { }
    [VertexShader] public void FS1() { }
    [VertexShader] public void VS2() { }
    [VertexShader] public void FS2() { }
}

[ShaderClass(null, "FS")]
public class ExplicitNull
{
    [VertexShader] public void VS() { }
    [FragmentShader] public void FS() { }
}

[ComputeShaderClass]
public class ComputeInferred
{
    [VertexShader] public void VS() { }
    [FragmentShader] public void FS() { }

    [ComputeShader(1, 1, 1)] public void ComputeShader() { }
}

[ComputeShaderClass("CS1")]
[ComputeShaderClass("CS2", "Compute2"),
    ComputeShaderClass("CS1", "Compute3")]
public class Compute
{
    [ComputeShader(1, 1, 1)] public void CS1() { }
    [ComputeShader(1, 1, 1)] public void CS2() { }
}

public class ComputeClone {
    [ComputeShader(1, 1, 1)] public void CS1() { }
    [ComputeShader(1, 1, 1)] public void CS2() { }
}
