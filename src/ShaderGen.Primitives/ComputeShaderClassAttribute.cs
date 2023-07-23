using System;

namespace ShaderGen
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
    public class ComputeShaderClassAttribute : Attribute
    {
        public ComputeShaderClassAttribute(string cs = null, string name = null)
        {
        }
    }
}
