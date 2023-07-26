using System;

namespace ShaderGen
{
    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
    public class ComputeShaderSetAttribute : Attribute
    {
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
