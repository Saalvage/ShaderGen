using ShaderGen;

namespace TestShaders;

[ShaderClass]
public class Ambiguous
{
    [VertexShader] public void VS1() { }
    [FragmentShader] public void FS1() { }
    [VertexShader] public void VS2() { }
    [FragmentShader] public void FS2() { }
}
