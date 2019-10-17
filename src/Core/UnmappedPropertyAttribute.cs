using System;

namespace ManualMappingGuard
{
  [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
  public sealed class UnmappedPropertyAttribute : Attribute
  {
    public string PropertyName { get; }

    public UnmappedPropertyAttribute(string propertyName)
    {
      PropertyName = propertyName ?? throw new ArgumentNullException(nameof(propertyName));
    }
  }
}
