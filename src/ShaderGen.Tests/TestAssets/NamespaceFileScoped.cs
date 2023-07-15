using ShaderGen;

namespace TestShaders;

public class FileScopedNamespaceShader
{
    [VertexShader]
    SystemPosition4 VS(Position4 input)
    {
        SystemPosition4 output;
        output.Position = input.Position;
        return output;
    }
}
