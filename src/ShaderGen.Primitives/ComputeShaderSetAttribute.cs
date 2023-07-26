using System;

namespace ShaderGen
{
    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
    public class ComputeShaderSetAttribute : Attribute
    {
        [Obsolete("Use the constructor taking a type and a method name.")]
        public ComputeShaderSetAttribute(string setName, string computeShaderFunctionName)
        {
        }

        public ComputeShaderSetAttribute(string name, Type type, string methodName)
        {
        }

        public ComputeShaderSetAttribute(Type type, string methodName) 
        {
        }
    }
}
