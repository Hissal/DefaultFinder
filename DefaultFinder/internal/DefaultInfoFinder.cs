using System.Reflection;
using DefaultFinder.Attributes;

namespace DefaultFinder;

internal readonly record struct DefaultInfo(Type ConcreteType, Type AsType, DefaultFlags Flags) {
    public override string ToString() {
        return $"{ConcreteType.FullName} as {AsType.FullName} (Flags: {Flags})";
    }
}

internal static class DefaultInfoFinder {
    internal static IEnumerable<DefaultInfo> FindDefaultInfos() {
        var assemblies = AppDomain.CurrentDomain.GetAssemblies()
            .Where (assembly => 
                !assembly.IsDynamic 
                && !assembly.GetName().FullName.StartsWith("Unity") 
                && !assembly.GetName().FullName.StartsWith("System") 
                && !assembly.GetName().FullName.StartsWith("Microsoft")
            ).ToArray();
        
        Dictionary<Type, DefaultInfo> defaultInfos = new();
            
        foreach (var assembly in assemblies) {
            Type[] types;
            try {
                types = assembly.GetTypes();
            } 
            catch (ReflectionTypeLoadException e) {
                types = e.Types.Where(t => t != null).ToArray()!;
            }

            foreach (var type in types) {
                foreach (var defaultAttribute in type.GetCustomAttributes<DefaultAttribute>()) {
                    var asType = defaultAttribute.AsType;

                    if (defaultInfos.ContainsKey(asType)) {
                        if (!defaultAttribute.Flags.HasFlag(DefaultFlags.Overrideable) &&
                            !defaultInfos[asType].Flags.HasFlag(DefaultFlags.Overrideable))
                            throw new Exception(
                                $"Multiple non overridable default implementations found for type {asType.FullName}: [{type.FullName} and {defaultInfos[asType].ConcreteType.FullName}]");

                        if (defaultInfos[asType].Flags.HasFlag(DefaultFlags.Overrideable) &&
                            defaultAttribute.Flags.HasFlag(DefaultFlags.Overrideable)) {
                            // TODO: Warn about multiple overridable implementations (Total guess work for which one it will pick)
                            continue;
                        }

                        if (!defaultInfos[asType].Flags.HasFlag(DefaultFlags.Overrideable))
                            continue;
                    }

                    var defaultInfo = new DefaultInfo(type, asType, defaultAttribute.Flags);
                    defaultInfos[asType] = defaultInfo;
                }
            }
        }
        
        return defaultInfos.Values;
    }
}