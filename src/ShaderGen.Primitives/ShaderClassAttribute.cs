using System;

namespace ShaderGen
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = true)]
    public class ShaderClassAttribute : Attribute
    {
        public ShaderClassAttribute()
        {
        }

        public ShaderClassAttribute(string vs, string fs, string name = null)
        {
        }
    }
}
