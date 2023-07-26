using System;

namespace ShaderGen
{
    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
    public class ShaderSetAttribute : Attribute
    {
        public ShaderSetAttribute(string name, string vs, string fs)
        {
        }

        public ShaderSetAttribute(string name, Type vsType, string vsMethod, Type fsType, string fsMethod)
        {
        }
    }
}
