using System.Buffers;
using System.Reflection;
using DefaultFinder.Attributes;

namespace DefaultFinder;

internal readonly record struct DefaultInfo(Type ConcreteType, Type AsType, DefaultFlags Flags);

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
                        if (!defaultAttribute.Flags.HasFlag(DefaultFlags.AllowOverride) &&
                            !defaultInfos[asType].Flags.HasFlag(DefaultFlags.AllowOverride))
                            throw new Exception(
                                $"Multiple non overridable default implementations found for type {asType.FullName}: [{type.FullName} and {defaultInfos[asType].ConcreteType.FullName}]");

                        if (defaultInfos[asType].Flags.HasFlag(DefaultFlags.AllowOverride) &&
                            defaultAttribute.Flags.HasFlag(DefaultFlags.AllowOverride)) {
                            // TODO: Warn about multiple overridable implementations (Total guess work for which one it will pick)
                            continue;
                        }

                        if (!defaultInfos[asType].Flags.HasFlag(DefaultFlags.AllowOverride))
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

public static class DefaultCtorFactory {
    static ConstructorInfo GetDefaultConstructor(Type type) {
        var ctors = type.GetConstructors(BindingFlags.Public | BindingFlags.Instance);
        var ctor = ctors.FirstOrDefault(c => c.GetCustomAttribute<DefaultConstructorAttribute>() != null)
                   ?? (ctors.Length == 1 ? ctors[0] : type.GetConstructor(Type.EmptyTypes));

        return ctor != null ? ctor
            : throw new Exception($"Type {type.FullName} must have either a parameterless constructor only one constructor or a constructor marked with [DefaultConstructor].");
    }
    
    static bool TryCreateFromConstructor(ConstructorInfo ctor, DefaultContainer container, FinderFlags finderFlags, out object instance) {
        var ctorParams = ctor.GetParameters();

        if (ctorParams.Length == 0) {
            instance = ctor.Invoke(null);
            return true;
        }

        if (TryBuildArgs(ctorParams, container, finderFlags, out var args)) {
            instance = ctor.Invoke(args);
            return true;
        }
        
        instance = null!;
        return false;
    }
    
    static bool TryCreateTransientCtor(Type type, DefaultContainer container, out TransientCtorInvoker transientCtorInvoker) => 
        TryCreateTransientCtor(GetDefaultConstructor(type), container, out transientCtorInvoker);
    static bool TryCreateTransientCtor(ConstructorInfo ctor, DefaultContainer container, out TransientCtorInvoker transientCtorInvoker) {
        var ctorAttribute = ctor.GetCustomAttribute<DefaultConstructorAttribute>();
        var ctorParams = ctor.GetParameters();
        var constructorInvoker = ConstructorInvoker.Create(ctor);
        
        if (ctorParams.Length == 0) {
            transientCtorInvoker = new TransientCtorInvoker(constructorInvoker, null);
            return true;
        }
        
        if (TryBuildArgs(ctorParams, container, ctorAttribute?.FinderFlags ?? FinderFlags.None, out var args)) {
            transientCtorInvoker = new TransientCtorInvoker(constructorInvoker, args);
            return true;
        }
        
        transientCtorInvoker = null!;
        return false;
    }

    static bool TryBuildArgs(ParameterInfo[] ctorParams, DefaultContainer container, FinderFlags finderFlags, out object[]? args) {
        var argInstances = ArrayPool<object>.Shared.Rent(ctorParams.Length);
        for (var index = 0; index < ctorParams.Length; index++) {
            var parameter = ctorParams[index];
            if (DefaultFindR.TryFind(parameter.ParameterType, container, out var foundDefault, finderFlags)) {
                argInstances[index] = foundDefault;
            }
            else {
                ArrayPool<object>.Shared.Return(argInstances, true);
                args = null;
                return false;
            }
        }

        args = argInstances.AsSpan(0, ctorParams.Length).ToArray();
        ArrayPool<object>.Shared.Return(argInstances, true);
        return true;
    }
}

public static class DefaultContainerFactory {
    public static void BuildContainer(DefaultContainer container) {
        var defaultInfos = DefaultInfoFinder.FindDefaultInfos();
        
        foreach (var defaultInfo in defaultInfos) {
            var containedDefault = TryAddContainedDefault(defaultInfo.ConcreteType, defaultInfo.AsType, defaultInfo.Flags, container);
            container.Add(containedDefault);
        }
    }
    
    static bool TryAddContainedDefault(Type concreteType, Type asType, DefaultFlags defaultFlags, DefaultContainer container) {
        // Transient non clonable (needs ctor invoker)
        if (defaultFlags.HasFlag(DefaultFlags.Transient) && !defaultFlags.HasFlag(DefaultFlags.Cloneable)) {
            var transientCtor = CreateTransientInvoker(concreteType);
            var transientInstance = transientCtor.Invoke();
            return new ContainedDefault(concreteType, asType, transientInstance, defaultFlags) {
                TransientCtor = transientCtor
            };
        }
        
        // Singleton && Cloneable (needs instance only)
        var ctor = GetConstructor(concreteType);
        var ctorAttribute = ctor.GetCustomAttribute<DefaultConstructorAttribute>();
        var instance = CreateFromConstructor(ctor, ctorAttribute?.FinderFlags ?? FinderFlags.None);
        return new ContainedDefault(concreteType, asType, instance, defaultFlags);
    }
}

public static class DefaultFindR {
    static readonly DefaultContainer s_container = new();
    static bool s_initialized = false;

    public static T Find<T>(FinderFlags finderFlags = FinderFlags.None) where T : class => (T)Find(typeof(T), finderFlags);

    public static object Find(Type type, FinderFlags finderFlags = FinderFlags.None) {
        if (!s_initialized)
            Initialize();
        
        if (s_container.TryGet(type, out var containedDefault)) {
            return GetDefault(containedDefault, finderFlags);
        }
        
        throw new Exception($"No default implementation found for type {type.FullName}.");
    }

    static void Initialize() {
        DefaultContainerFactory.BuildContainer(s_container);
        s_initialized = true;
    }

    public static object Find(Type type, DefaultContainer container, FinderFlags finderFlags = FinderFlags.None) {
        if (container.TryGet(type, out var containedDefault)) {
            return GetDefault(containedDefault, finderFlags);
        }
        
        throw new Exception($"No default implementation found for type {type.FullName} in container {container}.");
    }
    public static bool TryFind(Type type, DefaultContainer container, out object instance, FinderFlags finderFlags = FinderFlags.None) {
        if (container.TryGet(type, out var containedDefault)) {
            instance = GetDefault(containedDefault, finderFlags);
            return true;
        }
        
        instance = null!;
        return false;
    }
    
    static object GetDefault(ContainedDefault containedDefault, FinderFlags finderFlags) {
        if ((containedDefault.HasFlag(DefaultFlags.Transient) || finderFlags.HasFlag(FinderFlags.ForceTransient))
            && !finderFlags.HasFlag(FinderFlags.ForceSingleton)) 
        {
            return GetTransient(containedDefault, finderFlags);
        }
        
        return containedDefault.Instance;
    }
    
    static object GetTransient(ContainedDefault containedDefault, FinderFlags finderFlags) {
        if (containedDefault.HasFlag(DefaultFlags.Cloneable)) {
            return containedDefault.Instance is ICloneable clonable
                ? clonable.Clone()
                : throw new Exception($"Default {containedDefault} is marked as Cloneable but does not implement ICloneable.");
        }
        
        return CreateTransientInstance(containedDefault);
    }
    
    static object CreateTransientInstance(ContainedDefault containedDefault) {
        var ctor = containedDefault.TransientCtor ??= CreateTransientInvoker(containedDefault.ConcreteType);
        return ctor.Invoke();
    }
}