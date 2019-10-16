using System;

namespace ManualMappingGuard
{
  [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
  public class UnmappedPropertyAttribute : UnmappedPropertiesAttribute
  {
    public UnmappedPropertyAttribute(string propertyName)
      : base(propertyName == null ? null! : new[] {propertyName})
    {
    }
  }
}
