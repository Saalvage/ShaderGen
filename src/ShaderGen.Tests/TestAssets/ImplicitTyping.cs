using ShaderGen;

namespace TestShaders;

public class ImplicitTyping
{
    [VertexShader]
    public SystemPosition4 VS()
    {
        SystemPosition4 output;
        output.Position = new(1f);
        return output;
    }
}
