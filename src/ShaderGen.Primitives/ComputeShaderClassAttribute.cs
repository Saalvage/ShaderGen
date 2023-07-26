using System;

namespace ShaderGen
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = true)]
    public class ComputeShaderClassAttribute : Attribute
    {
        public ComputeShaderClassAttribute()
        {
        }

        public ComputeShaderClassAttribute(string cs, string name = null)
        {
        }
    }
}
