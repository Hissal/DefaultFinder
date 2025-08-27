using DefaultFinder.Attributes;
using DefaultFinder.Internal;

namespace DefaultFinder;

public static class DefaultFindR {
    static readonly DefaultContainer s_container;
    
    public static IServiceProvider ServiceProvider { get; }

    static DefaultFindR() {
        var defaultTypeInfos = DefaultInfoFinder.FindDefaultInfos();
        s_container = DefaultContainerFactory.CreateContainer(defaultTypeInfos);
        ServiceProvider = new DefaultServiceProvider(s_container);
    }
    
    public static object Find(Type type, FinderFlags finderFlags = FinderFlags.None) => Find(type, s_container, finderFlags);
    public static T Find<T>(FinderFlags finderFlags = FinderFlags.None) where T : class => (T)Find(typeof(T), s_container, finderFlags);
    
    public static bool TryFind(Type type, out object found, FinderFlags finderFlags = FinderFlags.None) => TryFind(type, s_container, out found, finderFlags);
    public static bool TryFind<T>(out T found, FinderFlags finderFlags = FinderFlags.None) where T : class {
        if (TryFind(typeof(T), s_container, out var foundObj, finderFlags)) {
            found = (T)foundObj;
            return true;
        }
        
        found = null!;
        return false;
    }

    internal static object Find(Type type, DefaultContainer container, FinderFlags finderFlags = FinderFlags.None) {
        return DefaultExtractor.TryExtractDefault(type, container, out var instance, finderFlags) 
            ? instance 
            : throw new Exception($"No default implementation found for type {type.FullName} in container {container}.");
    }
    internal static bool TryFind(Type type, DefaultContainer container, out object instance, FinderFlags finderFlags = FinderFlags.None) {
        return DefaultExtractor.TryExtractDefault(type, container, out instance!, finderFlags);
    }
}

internal class DefaultServiceProvider : IServiceProvider {
    readonly DefaultContainer _container;

    public DefaultServiceProvider(DefaultContainer container) {
        _container = container;
    }
    
    public object? GetService(Type serviceType) {
        return DefaultFindR.TryFind(serviceType, _container, out var service) ? service : null;
    }
}