using ShaderGen;

namespace TestShaders;

[ShaderClass(null, null)]
public class TooMuchExplicitNull
{
    [VertexShader] public void VS() { }
    [FragmentShader] public void FS() { }
}
