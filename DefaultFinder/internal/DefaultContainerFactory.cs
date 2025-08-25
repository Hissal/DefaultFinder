using DefaultFinder.Attributes;

namespace DefaultFinder;

public static class DefaultContainerFactory {
    public static void BuildContainer(DefaultContainer container) {
        var defaultInfos = DefaultInfoFinder.FindDefaultInfos();
        var failedAdds = new List<DefaultInfo>();
        
        foreach (var defaultInfo in defaultInfos) {
            if (TryAddContainedDefault(defaultInfo.ConcreteType, defaultInfo.AsType, defaultInfo.Flags, container))
                continue;
            
            failedAdds.Add(defaultInfo);
        }
        
        if (failedAdds.Count == 0)
            return;
        
        var toProcess = new List<DefaultInfo>(failedAdds.Count);
        
        while (failedAdds.Count > 0) {
            var failedBefore = failedAdds.Count;
            
            toProcess.Clear();
            toProcess.AddRange(failedAdds);
            failedAdds.Clear();
            
            foreach (var defaultInfo in toProcess) {
                if (TryAddContainedDefault(defaultInfo.ConcreteType, defaultInfo.AsType, defaultInfo.Flags, container))
                    continue;
                
                failedAdds.Add(defaultInfo);
            }
            
            if (failedAdds.Count == failedBefore)
                throw new Exception($"Could not resolve dependencies for default implementations: {string.Join(", ", failedAdds.Select(d => d.ToString()))}");
        }
    }
    
    static bool TryAddContainedDefault(Type concreteType, Type asType, DefaultFlags defaultFlags, DefaultContainer container) {
        // Transient non clonable (needs ctor invoker)
        if (defaultFlags.HasFlag(DefaultFlags.Transient) && !defaultFlags.HasFlag(DefaultFlags.Cloneable))
            return TryAddTransient(concreteType, asType, defaultFlags, container);
        
        // Singleton && Cloneable (needs instance only)
        return TryAddSingleton(concreteType, asType, defaultFlags, container);
    }

    static bool TryAddSingleton(Type concreteType, Type asType, DefaultFlags defaultFlags, DefaultContainer container) {
        if (!DefaultCtorFactory.TryCreateFromConstructor(concreteType, container, out var instance)) 
            return false;
        
        var containedDefault = new ContainedDefault(concreteType, asType, instance, defaultFlags);
        container.Add(containedDefault);
        return true;
    }

    static bool TryAddTransient(Type concreteType, Type asType, DefaultFlags defaultFlags, DefaultContainer container) {
        if (!DefaultCtorFactory.TryCreateTransientCtor(concreteType, container, out var transientCtor))
            return false;
        
        var transientInstance = transientCtor.Invoke();
        var containedDefault = new ContainedDefault(concreteType, asType, transientInstance, defaultFlags) {
            TransientCtor = transientCtor
        };
        
        container.Add(containedDefault);
        return true;
    }
}