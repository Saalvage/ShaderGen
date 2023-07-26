using ShaderGen;

namespace TestShaders;

[ShaderClass]
public class Ambiguous
{
    [VertexShader] public void VS() { }
    [FragmentShader] public void FS() { }
    [VertexShader] public void VS2() { }
    [FragmentShader] public void FS2() { }
}
