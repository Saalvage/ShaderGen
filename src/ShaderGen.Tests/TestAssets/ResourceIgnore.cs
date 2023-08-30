using System;
using ShaderGen;

namespace TestShaders
{
    public class ResourceIgnore
    {
        [ResourceIgnore] public Type SomeType;
        [ResourceIgnore] public Type SomeOtherType { get; }

        [VertexShader]
        public SystemPosition4 VS(Position4 input)
        {
            SystemPosition4 output = default;
            output.Position = input.Position;
            return output;
        }
    }
}
