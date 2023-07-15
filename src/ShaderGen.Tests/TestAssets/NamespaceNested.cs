using ShaderGen;

namespace TestShaders
{
    namespace NamespaceNested
    {
        public class MyShader
        {
            [VertexShader]
            SystemPosition4 VS(Position4 input)
            {
                SystemPosition4 output;
                output.Position = input.Position;
                return output;
            }
        }
    }
}
