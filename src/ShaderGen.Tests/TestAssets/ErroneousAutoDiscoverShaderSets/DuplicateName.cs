using ShaderGen;

namespace TestShaders;

[ShaderClass("VS", "FS")]
[ShaderClass("VS2", "FS2")]
public class DuplicateName
{
    [VertexShader] public void VS() { }
    [FragmentShader] public void FS() { }
    [VertexShader] public void VS2() { }
    [FragmentShader] public void FS2() { }
}
