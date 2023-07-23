using System;

namespace ShaderGen
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
    public class ShaderClassAttribute : Attribute
    {
        public ShaderClassAttribute(string vs = null, string fs = null, string name = null)
        {
        }
    }
}
