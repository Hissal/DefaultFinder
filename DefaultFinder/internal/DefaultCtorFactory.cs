using System.Buffers;
using System.Reflection;
using DefaultFinder.Attributes;

namespace DefaultFinder;

public static class DefaultCtorFactory {
    internal static ConstructorInfo GetDefaultConstructor(Type type) {
        var ctors = type.GetConstructors(BindingFlags.Public | BindingFlags.Instance);
        var ctor = ctors.FirstOrDefault(c => c.GetCustomAttribute<DefaultConstructorAttribute>() != null)
                   ?? (ctors.Length == 1 ? ctors[0] : type.GetConstructor(Type.EmptyTypes));

        return ctor != null ? ctor
            : throw new Exception($"Type {type.FullName} must have either a parameterless constructor only one constructor or a constructor marked with [DefaultConstructor].");
    }
    
    internal static bool TryCreateFromConstructor(Type type, DefaultContainer container, out object instance) => 
        TryCreateFromConstructor(GetDefaultConstructor(type), container, out instance);
    internal static bool TryCreateFromConstructor(ConstructorInfo ctor, DefaultContainer container, out object instance) {
        var ctorAttribute = ctor.GetCustomAttribute<DefaultConstructorAttribute>();
        var ctorParams = ctor.GetParameters();

        if (ctorParams.Length == 0) {
            instance = ctor.Invoke(null);
            return true;
        }

        if (TryBuildArgs(ctorParams, container, ctorAttribute?.FinderFlags ?? FinderFlags.None, out var args)) {
            instance = ctor.Invoke(args);
            return true;
        }
        
        instance = null!;
        return false;
    }
    
    internal static bool TryCreateTransientCtor(Type type, DefaultContainer container, out TransientCtorInvoker transientCtorInvoker) => 
        TryCreateTransientCtor(GetDefaultConstructor(type), container, out transientCtorInvoker);
    internal static bool TryCreateTransientCtor(ConstructorInfo ctor, DefaultContainer container, out TransientCtorInvoker transientCtorInvoker) {
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
    
    internal static TransientCtorInvoker CreateTransientCtor(Type type, DefaultContainer container) => 
        CreateTransientCtor(GetDefaultConstructor(type), container);
    internal static TransientCtorInvoker CreateTransientCtor(ConstructorInfo ctor, DefaultContainer container) {
        var ctorAttribute = ctor.GetCustomAttribute<DefaultConstructorAttribute>();
        var ctorParams = ctor.GetParameters();
        var constructorInvoker = ConstructorInvoker.Create(ctor);
        
        if (ctorParams.Length == 0) {
            return new TransientCtorInvoker(constructorInvoker, null);
        }
        
        return TryBuildArgs(ctorParams, container, ctorAttribute?.FinderFlags ?? FinderFlags.None, out var args) 
            ? new TransientCtorInvoker(constructorInvoker, args) 
            : throw new Exception($"Could not build arguments for transient constructor of type {ctor.DeclaringType?.FullName}.");
    }

    internal static bool TryBuildArgs(ParameterInfo[] ctorParams, DefaultContainer container, FinderFlags finderFlags, out object[]? args) {
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