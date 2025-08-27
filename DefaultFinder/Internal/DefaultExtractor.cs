using DefaultFinder.Attributes;

namespace DefaultFinder.Internal;

internal static class DefaultExtractor {
    public static bool TryExtractDefault(Type type, DefaultContainer container, out object instance, FinderFlags finderFlags) {
        if (container.TryGet(type, out var containedDefault)) {
            instance = ExtractDefault(containedDefault, container, finderFlags);
            return true;
        }
        
        instance = null!;
        return false;
    }
    static object ExtractDefault(ContainedDefault containedDefault, DefaultContainer container, FinderFlags finderFlags) {
        if ((containedDefault.HasFlag(DefaultFlags.Transient) || finderFlags.HasFlag(FinderFlags.ForceTransient))
            && !finderFlags.HasFlag(FinderFlags.ForceSingleton)) 
        {
            return GetTransient(containedDefault, container, finderFlags);
        }
        
        return containedDefault.Instance;
    }
    
    static object GetTransient(ContainedDefault containedDefault, DefaultContainer container, FinderFlags finderFlags) {
        if (containedDefault.HasFlag(DefaultFlags.Cloneable)) {
            return containedDefault.Instance is ICloneable clonable
                ? clonable.Clone()
                : throw new Exception($"Default {containedDefault} is marked as Cloneable but does not implement ICloneable.");
        }
        
        return CreateTransientInstance(containedDefault, container);
    }
    
    static object CreateTransientInstance(ContainedDefault containedDefault, DefaultContainer container) {
        var ctor = containedDefault.TransientCtor ??= DefaultCtorFactory.CreateTransientCtor(containedDefault.ConcreteType, container);
        return ctor.Invoke();
    }
}