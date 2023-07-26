using System;

namespace ShaderGen
{
    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
    public class ShaderSetAttribute : Attribute
    {
        // TODO: If these are removed, the calls to TypeAndMethodName.Get in ShaderSetDiscoverer can probably be removed.
        [Obsolete("Use the constructor taking a type and a method name.")]
        public ShaderSetAttribute(string name, string vs, string fs)
        {
        }

        public ShaderSetAttribute(string name, Type vsType, string vsMethod, Type fsType, string fsMethod)
        {
        }
    }
}
