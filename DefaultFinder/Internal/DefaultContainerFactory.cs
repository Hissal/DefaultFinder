using DefaultFinder.Attributes;

namespace DefaultFinder.Internal;

internal static class DefaultContainerFactory {
    public static DefaultContainer CreateContainer(DefaultTypeInfos defaultTypeInfos) {
        var container = new DefaultContainer();
        var failedAdds = new List<DefaultInfo>();
        
        foreach (var defaultInfo in defaultTypeInfos.NonGenericInfos) {
            if (TryAddContainedDefault(defaultInfo.ConcreteType, defaultInfo.AsType, defaultInfo.Flags, container))
                continue;
            
            failedAdds.Add(defaultInfo);
        }
        
        if (failedAdds.Count == 0) {
            AddGenericDefinitions(container, defaultTypeInfos.GenericInfos);
            return container;
        }
        
        // handle failed adds (due to dependencies on other defaults)
        var toProcess = new List<DefaultInfo>(failedAdds.Count);
        var haveAddedGenerics = false;
        
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
            
            if (failedAdds.Count == failedBefore) {
                if (haveAddedGenerics)
                    throw new Exception($"Could not resolve dependencies for default implementations: {string.Join(", ", failedAdds.Select(d => d.ToString()))}");
                
                AddGenericDefinitions(container, defaultTypeInfos.GenericInfos);
                haveAddedGenerics = true;
            }        
        }
        
        if (!haveAddedGenerics)
            AddGenericDefinitions(container, defaultTypeInfos.GenericInfos);
        
        return container;
    }
    
    static void AddGenericDefinitions(DefaultContainer container, GenericDefaultInfo[][] genericInfoArrays) {
        foreach (var genericInfoArray in genericInfoArrays) {
            var containedGenericDefinition = new ContainedGenericDefinition(genericInfoArray);
            container.AddGenericDefinitionArray(containedGenericDefinition, genericInfoArray[0].AsTypeDefinition);
        }
    }
    
    static bool TryAddContainedDefault(Type concreteType, Type asType, DefaultFlags defaultFlags, DefaultContainer container) {
        if (!ContainedDefaultFactory.TryBuildContainedDefault(concreteType, asType, defaultFlags, container, out var containedDefault)) 
            return false;
        
        container.Add(containedDefault);
        return true;
    }
}