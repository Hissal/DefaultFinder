using System.Reflection;
using DefaultFinder.Attributes;

namespace DefaultFinder.Internal;

internal record DefaultInfo(Type ConcreteType, Type AsType, DefaultFlags Flags) {
    public bool HasFlag(DefaultFlags flag) => (Flags & flag) == flag;
    public override string ToString() {
        return $"{ConcreteType.FullName} as {AsType.FullName} (Flags: {Flags})";
    }
}

internal record GenericDefaultInfo(Type GenericConcreteType, Type AsTypeDefinition, Type[] AsTypeGenericArgs, DefaultFlags Flags) {
    public bool HasFlag(DefaultFlags flag) => (Flags & flag) == flag;
    public override string ToString() {
        return $"{GenericConcreteType.FullName} as {AsTypeDefinition.FullName}<{string.Join(", ", AsTypeGenericArgs.Select(t => t.Name))}> (Flags: {Flags})";
    }
}

internal record DefaultTypeInfos(DefaultInfo[] NonGenericInfos, GenericDefaultInfo[][] GenericInfos);

internal static class DefaultInfoFinder {
    internal static DefaultTypeInfos FindDefaultInfos() {
        var assemblies = AppDomain.CurrentDomain.GetAssemblies()
            .Where (assembly => 
                !assembly.IsDynamic 
                && !assembly.GetName().FullName.StartsWith("Unity") 
                && !assembly.GetName().FullName.StartsWith("System") 
                && !assembly.GetName().FullName.StartsWith("Microsoft")
            ).ToArray();
            
        List<Type> allTypes = new();
        
        foreach (var assembly in assemblies) {
            Type[] types;
            try {
                types = assembly.GetTypes();
            } 
            catch (ReflectionTypeLoadException e) {
                types = e.Types.Where(t => t != null).ToArray()!;
            }
            allTypes.AddRange(types);
        }
        
        var typeArray = allTypes.ToArray();
        var fullyTypedDefaultInfos = FindFullyTypedDefaults(typeArray);
        var genericDefaultInfos = FindGenericTypeDefaults(typeArray);

        Dictionary<Type, GenericDefaultInfo> GetDefaultInfoDict(Type openGenericType) {
            if (genericDefaultInfos.TryGetValue(openGenericType, out var dict))
                return dict;
            
            dict = new Dictionary<Type, GenericDefaultInfo>();
            genericDefaultInfos[openGenericType] = dict;
            return dict;
        }
        
        foreach (var fullyTyped in fullyTypedDefaultInfos) {
            if (!fullyTyped.Value.Flags.HasFlag(DefaultFlags.Overrideable))
                continue;
                
            if (!fullyTyped.Value.AsType.IsGenericType)
                continue;

            foreach (var genericTyped in GetDefaultInfoDict(fullyTyped.Value.AsType.GetGenericTypeDefinition())) {
                if (genericTyped.Value.Flags.HasFlag(DefaultFlags.Overrideable))
                    continue;
                    
                var genericAsTypeDef = genericTyped.Value.AsTypeDefinition.GetGenericTypeDefinition();
                var fullyTypedAsTypeDef = fullyTyped.Value.AsType.GetGenericTypeDefinition();
                    
                if (genericAsTypeDef != fullyTypedAsTypeDef)
                    continue;
                  
                var fullyTypedArgs = fullyTyped.Value.AsType.GetGenericArguments();
                var genericTypedArgs = genericTyped.Value.AsTypeGenericArgs!;
                    
                var canOverride = true;
                    
                for (var i = 0; i < fullyTypedArgs.Length; i++) {
                    if (genericTypedArgs[i].IsGenericTypeParameter || fullyTypedArgs[i] == genericTypedArgs[i])
                        continue;
                        
                    canOverride = false;
                    break;
                }

                if (canOverride)
                    fullyTypedDefaultInfos.Remove(fullyTyped.Key);
            }
        }
        
        return new DefaultTypeInfos(
            fullyTypedDefaultInfos.Values.ToArray(), 
            genericDefaultInfos.Values.Select(dict => dict.Values.ToArray()).ToArray()
        );
    }

    static Dictionary<Type, DefaultInfo> FindFullyTypedDefaults(Type[] types) {
        Dictionary<Type, DefaultInfo> fullyTypedDefaultInfos = new();
        
        foreach (var type in types.Where(t => !t.IsGenericType)) {
            foreach (var defaultAttribute in type.GetCustomAttributes<DefaultAttribute>()) {
                var asType = defaultAttribute.AsType;
                    
                if (fullyTypedDefaultInfos.TryGetValue(asType, out var existingDefaultInfo)) {
                    var collisionResolution = GetCollisionResolution(existingDefaultInfo.Flags, defaultAttribute.Flags);

                    switch (collisionResolution) {
                        case DefaultCollisionResolution.Override:
                            fullyTypedDefaultInfos[asType] = new DefaultInfo(type, asType, defaultAttribute.Flags);
                            continue;
                        case DefaultCollisionResolution.KeepExisting:
                            continue;
                        case DefaultCollisionResolution.Error:
                            throw new Exception(
                                $"Multiple non overridable default implementations found for type {asType.FullName}: [{type.FullName} and {fullyTypedDefaultInfos[asType].ConcreteType.FullName}]");
                        default:
                            throw new ArgumentOutOfRangeException();
                    }
                }

                // No existing implementation, just add it
                fullyTypedDefaultInfos[asType] = new DefaultInfo(type, asType, defaultAttribute.Flags);
            }
        }
        
        return fullyTypedDefaultInfos;
    }

    static Dictionary<Type, Dictionary<Type, GenericDefaultInfo>> FindGenericTypeDefaults(Type[] types) {
        Dictionary<Type, Dictionary<Type, GenericDefaultInfo>> genericDefaultInfos = new();
        
        Dictionary<Type, GenericDefaultInfo> GetDefaultInfoDict(Type openGenericType) {
            if (genericDefaultInfos.TryGetValue(openGenericType, out var dict))
                return dict;
            
            dict = new Dictionary<Type, GenericDefaultInfo>();
            genericDefaultInfos[openGenericType] = dict;
            return dict;
        }
        
        // Find generic type defaults
        foreach (var type in types.Where(t => t.IsGenericType)) {
            foreach (var defaultAttribute in type.GetCustomAttributes<DefaultAttribute>()) {
                var asType = defaultAttribute.AsType;
            
                var implementedAsType = type.GetGenericTypeDefinition() == asType
                    ? type
                    : type.BaseType is { IsGenericType: true } && type.BaseType.GetGenericTypeDefinition() == asType
                        ? type.BaseType
                        : type.GetInterfaces()
                              .FirstOrDefault(iface => iface.IsGenericType && iface.GetGenericTypeDefinition() == asType)
                              ?? throw new InvalidOperationException($"Type {type.FullName} does not implement {asType.FullName}");

                var implementedAsTypeGenericArgs = implementedAsType.GetGenericArguments();
                
                // TODO: Allow passing concrete type arguments in the attribute
                if (implementedAsTypeGenericArgs.Count(arg => arg.IsGenericTypeParameter) != type.GetGenericArguments().Length)
                    throw new ArgumentException($"{type.FullName} does not have the correct number of generic parameters to implement {asType.FullName}");
                
                var defaultInfoDict = GetDefaultInfoDict(asType);
                
                if (defaultInfoDict.TryGetValue(implementedAsType, out var existingDefaultInfo)) {
                    var collisionResolution = GetCollisionResolution(existingDefaultInfo.Flags, defaultAttribute.Flags);

                    switch (collisionResolution) {
                        case DefaultCollisionResolution.Override:
                            defaultInfoDict[implementedAsType] = new GenericDefaultInfo(type, asType, implementedAsTypeGenericArgs, defaultAttribute.Flags);
                            continue;
                        case DefaultCollisionResolution.KeepExisting:
                            continue;
                        case DefaultCollisionResolution.Error:
                            throw new Exception(
                                $"Multiple non overridable default implementations found for type {asType.FullName}: [{type.FullName} and {existingDefaultInfo.GenericConcreteType.FullName}]");
                        default:
                            throw new ArgumentOutOfRangeException();
                    }
                }

                // No existing implementation, just add it
                defaultInfoDict[asType] = new GenericDefaultInfo(type, asType, implementedAsTypeGenericArgs, defaultAttribute.Flags);
            }
        }
        
        // Handle generic overrides
        foreach (var defaultInfoDict in genericDefaultInfos.Values.ToArray()) {
            if (defaultInfoDict.Count == 1)
                continue;
            
            var genericTypes = defaultInfoDict.Keys.ToArray();
            foreach (var type in genericTypes) {
                foreach (var otherType in genericTypes) {
                    if (type == otherType)
                        continue;
                    
                    var typedArgCount = defaultInfoDict[type].AsTypeGenericArgs.Count(arg => !arg.IsGenericTypeParameter);
                    var otherTypedArgCount = defaultInfoDict[otherType].AsTypeGenericArgs.Count(arg => !arg.IsGenericTypeParameter);
                    
                    var higherTypedArgCount = Math.Max(typedArgCount, otherTypedArgCount);
                    var moreTypedArgsType = higherTypedArgCount == typedArgCount ? type : otherType;
                    var lessTypedArgsType = higherTypedArgCount == typedArgCount ? otherType : type;
                    
                    if (defaultInfoDict[moreTypedArgsType].Flags.HasFlag(DefaultFlags.Overrideable) && !defaultInfoDict[lessTypedArgsType].Flags.HasFlag(DefaultFlags.Overrideable)) {
                        var moreTypedArgs = defaultInfoDict[moreTypedArgsType].AsTypeGenericArgs;
                        var lessTypedArgs = defaultInfoDict[lessTypedArgsType].AsTypeGenericArgs;

                        var canOverride = true;
                        
                        for (var i = 0; i < moreTypedArgs.Length; i++) {
                            if (moreTypedArgs[i].IsGenericTypeParameter && !lessTypedArgs[i].IsGenericTypeParameter) {
                                canOverride = false;
                                break;
                            }
                            
                            if (!moreTypedArgs[i].IsGenericTypeParameter && !lessTypedArgs[i].IsGenericTypeParameter && moreTypedArgs[i] != lessTypedArgs[i]) {
                                canOverride = false;
                                break;
                            }
                        }

                        if (canOverride)
                            defaultInfoDict.Remove(moreTypedArgsType);
                    }
                }
            }
        }
        
        return genericDefaultInfos;
    }

    static DefaultCollisionResolution GetCollisionResolution(DefaultFlags existingFlags, DefaultFlags newFlags) {
        if (!existingFlags.HasFlag(DefaultFlags.Overrideable) &&
            !newFlags.HasFlag(DefaultFlags.Overrideable))
            return DefaultCollisionResolution.Error;
        
        if (existingFlags.HasFlag(DefaultFlags.Overrideable) &&
            newFlags.HasFlag(DefaultFlags.Overrideable)) {
            // TODO: Warn about multiple overridable implementations (Total guess work for which one it will pick)
            return DefaultCollisionResolution.KeepExisting;
        }

        return !existingFlags.HasFlag(DefaultFlags.Overrideable) 
            ? DefaultCollisionResolution.KeepExisting 
            : DefaultCollisionResolution.Override;
    }
}

internal enum DefaultCollisionResolution {
    Error,
    Override,
    KeepExisting
}