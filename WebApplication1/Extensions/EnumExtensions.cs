using System.ComponentModel.DataAnnotations;
using System.Reflection;

namespace WebApplication1.Extensions;

public static class EnumExtensions
{
    /// <summary>The [Display(Name = ...)] of an enum value, falling back to its identifier.</summary>
    public static string GetDisplayName(this Enum value)
    {
        var member = value.GetType().GetMember(value.ToString()).FirstOrDefault();
        return member?.GetCustomAttribute<DisplayAttribute>()?.GetName() ?? value.ToString();
    }
}
