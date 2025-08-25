using DefaultFinder.Attributes;

namespace DefaultFinder;

public static class DefaultFindR {
    static readonly DefaultContainer s_container = new();
    static bool s_initialized = false;

    static void Initialize() {
        DefaultContainerFactory.BuildContainer(s_container);
        s_initialized = true;
    }
    
    public static T Find<T>(FinderFlags finderFlags = FinderFlags.None) where T : class => (T)Find(typeof(T), finderFlags);

    public static object Find(Type type, FinderFlags finderFlags = FinderFlags.None) {
        if (!s_initialized)
            Initialize();
        
        return s_container.TryGet(type, out var containedDefault) 
            ? GetDefault(containedDefault, s_container, finderFlags) 
            : throw new Exception($"No default implementation found for type {type.FullName}.");
    }
    
    internal static object Find(Type type, DefaultContainer container, FinderFlags finderFlags = FinderFlags.None) {
        return container.TryGet(type, out var containedDefault) 
            ? GetDefault(containedDefault, container, finderFlags) 
            : throw new Exception($"No default implementation found for type {type.FullName} in container {container}.");
    }
    internal static bool TryFind(Type type, DefaultContainer container, out object instance, FinderFlags finderFlags = FinderFlags.None) {
        if (container.TryGet(type, out var containedDefault)) {
            instance = GetDefault(containedDefault, container, finderFlags);
            return true;
        }
        
        instance = null!;
        return false;
    }
    
    static object GetDefault(ContainedDefault containedDefault, DefaultContainer container, FinderFlags finderFlags) {
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