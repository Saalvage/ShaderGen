using ShaderGen;

namespace TestShaders;

[ShaderClass("VS1", "FS1")]
[ShaderClass("VS2", "FS2")]
public class DuplicateName
{
    [VertexShader] public void VS1() { }
    [FragmentShader] public void FS1() { }
    [VertexShader] public void VS2() { }
    [FragmentShader] public void FS2() { }
}
