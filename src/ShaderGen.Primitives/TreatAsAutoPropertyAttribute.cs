using System;

namespace ShaderGen
{
    /// <summary>
    /// Signal that a computed property should be treated as an auto property.
    /// <remarks>
    /// The intended use case is a computed Span property with a fixed size array as a backing field
    /// of differing type (marked with <see cref="ResourceIgnoreAttribute"/>).
    /// </remarks>
    /// </summary>
    [AttributeUsage(AttributeTargets.Property)]
    public class TreatAsAutoPropertyAttribute : Attribute
    {
    }
}
