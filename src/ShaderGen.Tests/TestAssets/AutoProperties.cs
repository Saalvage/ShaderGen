using ShaderGen;

namespace TestShaders
{
    public class AutoProperties
    {
        public struct VertexConstants
        {
            public float Test { get; set; }
            // Below are examples of non-auto properties, equivalent to regular function calls.
            public float Test2 => 2;
            public float Test3
            {
                get => 3;
                set { }
            }
        }
        VertexConstants Constants;

        [VertexShader]
        public SystemPosition4 VS(Position4 input)
        {
            SystemPosition4 output = default;
            output.Position = input.Position * Constants.Test;
            return output;
        }
    }
}
