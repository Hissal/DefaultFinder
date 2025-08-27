using System.Collections.Concurrent;
using System.Reflection;
using DefaultFinder.Attributes;

namespace DefaultFinder.Internal;

internal record TransientCtorInvoker(ConstructorInvoker Constructor, object?[]? Arguments) {
    public object Invoke() => Arguments is null ? Constructor.Invoke() : Constructor.Invoke(Arguments.AsSpan());
}

internal record ContainedDefault(Type ConcreteType, Type AsType, object Instance, DefaultFlags Flags) {
    public TransientCtorInvoker? TransientCtor;
    
    public bool HasFlag(DefaultFlags flag) => (Flags & flag) == flag;
    public T As<T>() where T : class => (T)Instance;

    public override string ToString() {
        return $"{ConcreteType.FullName} as {AsType.FullName} (Flags: {Flags})";
    }
}

internal record ContainedGenericDefinition(GenericDefaultInfo[] GenericInfos) {
    public bool Contains(Type asType) {
        // TODO: this is a stupid implementation (no need to check all just check until first match)
        var asTypeParams = asType.GetGenericArguments();
        GenericDefaultInfo? currentCandidate = null;
        
        var highestMatchingParams = 0;

        foreach (var containedDefinition in GenericInfos) {
            // No specified type params (lowest priority)
            if (containedDefinition.AsTypeGenericArgs.All(arg => arg.IsGenericTypeParameter)) {
                if (HandleFullyOpenGeneric(currentCandidate, containedDefinition)) {
                    currentCandidate = containedDefinition;
                }
                
                continue;
            }

            if (HandlePartiallyTypedGeneric(currentCandidate, containedDefinition, asTypeParams, ref highestMatchingParams)) {
                currentCandidate = containedDefinition;
            }
        }
        
        return currentCandidate != null;
    }
    
    public GenericDefaultInfo GetGenericInfo(Type asType) {
        var asTypeParams = asType.GetGenericArguments();
        GenericDefaultInfo? currentCandidate = null;
        
        var highestMatchingParams = 0;

        foreach (var containedDefinition in GenericInfos) {
            // No specified type params (lowest priority)
            if (containedDefinition.AsTypeGenericArgs.All(arg => arg.IsGenericTypeParameter)) {
                if (HandleFullyOpenGeneric(currentCandidate, containedDefinition)) {
                    currentCandidate = containedDefinition;
                }
                
                continue;
            }

            if (HandlePartiallyTypedGeneric(currentCandidate, containedDefinition, asTypeParams, ref highestMatchingParams)) {
                currentCandidate = containedDefinition;
            }
        }
        
        return currentCandidate 
               ?? throw new Exception($"No suitable generic default implementation found for type {asType.FullName} in definitions: {string.Join(", ", GenericInfos.Select(d => d.ToString()))}");
    }

    static bool HandleFullyOpenGeneric(GenericDefaultInfo? currentCandidate, GenericDefaultInfo defaultInfo) {
        return currentCandidate == null || (currentCandidate.HasFlag(DefaultFlags.Overrideable) 
                                            && !defaultInfo.HasFlag(DefaultFlags.Overrideable));
    }
    
    // ReSharper disable once CognitiveComplexity
    static bool HandlePartiallyTypedGeneric(GenericDefaultInfo? currentCandidate, GenericDefaultInfo defaultInfo, Type[] asTypeParams, ref int highestMatchingParams) {
        if (defaultInfo.AsTypeGenericArgs is null)
            throw new InvalidOperationException("AsTypeParameters cannot be null when handling non null params.");
        
        var isMatch = true;
        var matchingParams = 0;
           
        // Find match and params
        for (var i = 0; i < asTypeParams.Length; i++) {
            var containedParam = defaultInfo.AsTypeGenericArgs[i];
            if (containedParam.IsGenericTypeParameter)
                continue;
                
            if (asTypeParams[i] != containedParam) {
                isMatch = false;
                break;
            }
                
            matchingParams++;
        }

        // Check match
        switch (isMatch) {
            case true when highestMatchingParams < matchingParams 
                           && !defaultInfo.HasFlag(DefaultFlags.Overrideable):
                highestMatchingParams = matchingParams;
                return true;
            case true when highestMatchingParams == matchingParams 
                           && (currentCandidate == null || (currentCandidate.HasFlag(DefaultFlags.Overrideable) 
                                                            && !defaultInfo.HasFlag(DefaultFlags.Overrideable))):
                return true;
            default:
                return false;
        }
    }
}

internal class DefaultContainer {
    readonly ConcurrentDictionary<Type, ContainedDefault> defaults = new();
    //readonly ConcurrentDictionary<(string, Type), ContainedDefault> keyedDefaults = new();
    
    readonly ConcurrentDictionary<Type, ContainedGenericDefinition> genericDefinitions = new();
    
    public bool Contains(Type type) {
        if (defaults.ContainsKey(type))
            return true;
        
        var genericTypeDef = type.IsGenericType ? type.GetGenericTypeDefinition() : null;
        return genericTypeDef != null && genericDefinitions.ContainsKey(genericTypeDef) 
                                      && genericDefinitions[genericTypeDef].Contains(type);
    }

    public ContainedDefault Get(Type type) {
        if (defaults.TryGetValue(type, out var containedDefault) || TryAddFromGenericDefinitions(type, out containedDefault))
            return containedDefault;

        throw new Exception($"No default implementation found for type {type.FullName}.");
    }

    public bool TryGet(Type type, out ContainedDefault containedDefault) => 
        defaults.TryGetValue(type, out containedDefault!) || TryAddFromGenericDefinitions(type, out containedDefault);

    public void Add(ContainedDefault containedDefault) {
        if (DefaultValidator.Validate(containedDefault.AsType, containedDefault.Instance, containedDefault.Flags)) {
            defaults[containedDefault.AsType] = containedDefault;
            return;
        }
        
        throw new Exception($"Instance of type {containedDefault.Instance.GetType().FullName} is not of the correct type {containedDefault.AsType.FullName}.");
    }
    
    // public bool Contains(string key, Type type) => keyedDefaults.ContainsKey((key, type));
    // public ContainedDefault Get(string key, Type type) => keyedDefaults[(key, type)];
    // public bool TryGet(string key, Type type, out ContainedDefault containedDefault) => keyedDefaults.TryGetValue((key, type), out containedDefault!);
    // public void Add(string key, ContainedDefault containedDefault) {
    //     if (DefaultValidator.Validate(containedDefault.AsType, containedDefault.Instance, containedDefault.Flags)) {
    //         keyedDefaults[(key, containedDefault.AsType)] = containedDefault;
    //         return;
    //     }
    //     
    //     throw new Exception($"Instance of type {containedDefault.Instance.GetType().FullName} is not of the correct type {containedDefault.AsType.FullName}.");
    // }
    
    bool TryAddFromGenericDefinitions(Type type, out ContainedDefault containedDefault) {
        var genericTypeDef = type.IsGenericType ? type.GetGenericTypeDefinition() : null;
        if (genericTypeDef != null && genericDefinitions.TryGetValue(genericTypeDef, out var containedGenericDefinitionArray)) {
            var genericInfo = containedGenericDefinitionArray.GetGenericInfo(type);
            
            if (ContainedDefaultFactory.TryBuildFromGenericInfo(genericInfo, type, this, out containedDefault)) {
                Add(containedDefault);
                return true;
            }
        }
        
        containedDefault = null!;
        return false;
    }
    
    internal void AddGenericDefinitionArray(ContainedGenericDefinition containedGenericDefinition, Type asGenericTypeDefinition) {
        genericDefinitions[asGenericTypeDefinition] = containedGenericDefinition;
    }
}