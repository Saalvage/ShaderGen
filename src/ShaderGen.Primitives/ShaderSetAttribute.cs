using System;

namespace ShaderGen
{
    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
    public class ShaderSetAttribute : Attribute
    {
        public ShaderSetAttribute(string name, string vs, string fs)
        {
        }
    }
}
