using DefaultFinder.Attributes;

namespace DefaultFinder.Internal;

internal static class ContainedDefaultFactory {
    public static bool TryBuildContainedDefault(Type concreteType, Type asType, DefaultFlags defaultFlags, DefaultContainer container, out ContainedDefault containedDefault) {
        // Transient non clonable (needs ctor invoker)
        if (defaultFlags.HasFlag(DefaultFlags.Transient) && !defaultFlags.HasFlag(DefaultFlags.Cloneable))
            return TryBuildSingleton(concreteType, asType, defaultFlags, container, out containedDefault);
        
        // Singleton && Cloneable (needs instance only)
        return TryBuildTransient(concreteType, asType, defaultFlags, container, out containedDefault);
    }
    
    static bool TryBuildSingleton(Type concreteType, Type asType, DefaultFlags defaultFlags, DefaultContainer container, out ContainedDefault containedDefault) {
        if (!DefaultCtorFactory.TryCreateFromConstructor(concreteType, container, out var instance)) {
            containedDefault = null!;
            return false;
        }
        
        containedDefault = new ContainedDefault(concreteType, asType, instance, defaultFlags);
        return true;
    }
    
    static bool TryBuildTransient(Type concreteType, Type asType, DefaultFlags defaultFlags, DefaultContainer container, out ContainedDefault containedDefault) {
        if (!DefaultCtorFactory.TryCreateTransientCtor(concreteType, container, out var transientCtor)) {
            containedDefault = null!;
            return false;
        }
        
        var transientInstance = transientCtor.Invoke();
        containedDefault = new ContainedDefault(concreteType, asType, transientInstance, defaultFlags) {
            TransientCtor = transientCtor
        };
        return true;
    }
    
    internal static bool TryBuildFromGenericInfo(GenericDefaultInfo genericDefaultInfo, Type asType, DefaultContainer container, out ContainedDefault containedDefault) {
        if (!DefaultValidator.CanBe(genericDefaultInfo, asType))
            throw new Exception($"Type {asType.FullName} cannot be built from generic definition {genericDefaultInfo}.");
        
        var concreteType = BuildConcreteType(genericDefaultInfo, asType);
        return TryBuildContainedDefault(concreteType, asType, genericDefaultInfo.Flags, container, out containedDefault);
    }

    static Type BuildConcreteType(GenericDefaultInfo genericDefaultInfo, Type asType) {
        var knownAsTypeGenericArgs = asType.GetGenericArguments();
        var mappedArgs = MapAsTypeArgsToConcrete(
            genericDefaultInfo.GenericConcreteType.GetGenericArguments(), 
            genericDefaultInfo.AsTypeGenericArgs
        );
        
        var concreteTypeArgs = new Type[mappedArgs.Length];

        for (int i = 0; i < genericDefaultInfo.AsTypeGenericArgs.Length; i++) {
            var arg = genericDefaultInfo.AsTypeGenericArgs[i];

            if (!arg.IsGenericTypeParameter)
                continue;
            
            var knownArg = knownAsTypeGenericArgs[i];
            var position = Array.IndexOf(mappedArgs, arg);
            
            concreteTypeArgs[position] = knownArg;
        }
        
        return genericDefaultInfo.GenericConcreteType.GetGenericTypeDefinition().MakeGenericType(concreteTypeArgs);
    }
    
    static Type[] MapAsTypeArgsToConcrete(Type[] concreteTypeGenericArgs, Type[] asTypeGenericArgs) {
        // Map interface parameter names to their Type objects
        var asTypeParamMap = asTypeGenericArgs
            .Where(t => t.IsGenericTypeParameter)
            .ToDictionary(t => t.Name, t => t);

        var mappedArgs = new Type[concreteTypeGenericArgs.Length];
        for (int i = 0; i < concreteTypeGenericArgs.Length; i++) {
            var concreteParam = concreteTypeGenericArgs[i];
            if (concreteParam.IsGenericTypeParameter) {
                if (!asTypeParamMap.TryGetValue(concreteParam.Name, out var mappedType))
                    throw new Exception($"Could not map generic parameter '{concreteParam.Name}' from concrete type to interface.");
                mappedArgs[i] = mappedType;
            } else {
                mappedArgs[i] = concreteParam;
            }
        }
        return mappedArgs;
    }
}