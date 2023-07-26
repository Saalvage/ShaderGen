using ShaderGen;
using System.Numerics;

namespace TestShaders
{
    public class RecordStructs
    {
        public record struct VertexInput([PositionSemantic] Vector3 Position, [TextureCoordinateSemantic] Vector2 TextureCoords);
        public record struct FragmentInput([SystemPositionSemantic] Vector4 Position, [TextureCoordinateSemantic] Vector2 TextureCoords);

        public record struct VertexConstants(Matrix4x4 Transform);
        public VertexConstants Constants;

        [VertexShader]
        public FragmentInput VS(VertexInput input)
        {
            FragmentInput output = default;
            output.Position = default;
            output.Position = ShaderBuiltins.Mul(Constants.Transform, new Vector4(input.Position, 1f));
            output.TextureCoords = input.TextureCoords;
            return output;
        }
    }
}
