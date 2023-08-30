using System.Numerics;
using ShaderGen;

namespace TestShaders
{
    [ShaderClass(null, "FS", "CoolStuff")]
    public partial class PartialClass
    {
        [ResourceSet(1)] public float Test2 { get; }

        [VertexShader]
        public SystemPosition4 VS(Position4 input)
        {
            SystemPosition4 output = default;
            output.Position = input.Position * Test * Test2;
            return output;
        }
    }

    [ShaderClass]
    public partial class PartialClass
    {
        [ResourceSet(0)]
        public float Test { get; set; }

        [FragmentShader]
        public Vector4 FS(SystemPosition4 input)
        {
             return new(input.Position.XYZ() * Test * Test2, 1);
        }
    }
}
