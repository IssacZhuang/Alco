using System;
using System.Linq;
using System.Reflection;

namespace Alco;

public static class UtilsAttribute
{
    public static (MethodInfo, T)[] GetMethodsWithAttribute<T>(BindingFlags bindingFlags) where T : Attribute
    {
        return AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(assembly => assembly.GetTypes())
            .SelectMany(type => type.GetMethods(bindingFlags))
            .Where(method => method.GetCustomAttributes<T>().Any())
            .Select(method => (method, method.GetCustomAttribute<T>()!))
            .ToArray();
    }

    public static (Type, T)[] GetTypesWithAttribute<T>(BindingFlags bindingFlags) where T : Attribute
    {
        return AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => type.GetCustomAttributes<T>().Any())
            .Select(type => (type, type.GetCustomAttribute<T>()!))
            .ToArray();
    }
}
