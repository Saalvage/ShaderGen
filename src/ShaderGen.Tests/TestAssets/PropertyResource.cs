using ShaderGen;

namespace TestShaders
{
    public class PropertyResource
    {
        public float Scale { get; }

        [VertexShader]
        public SystemPosition4 VS(Position4 input)
        {
            SystemPosition4 output = default;
            output.Position = input.Position * Scale;
            return output;
        }
    }
}
