using System.Collections.Concurrent;
using System.Reflection;
using DefaultFinder.Attributes;

namespace DefaultFinder;

public static class DefaultValidator {
    public static bool Validate<T>(object instance, DefaultFlags flags) where T : class => Validate(typeof(T), instance, flags);

    public static bool Validate(Type type, object instance, DefaultFlags flags) {
        if (!type.IsInstanceOfType(instance))
            return false;

        return true;
    }
}

public record TransientCtorInvoker(ConstructorInvoker Constructor, object?[]? Arguments) {
    public object Invoke() => Arguments is null ? Constructor.Invoke() : Constructor.Invoke(Arguments.AsSpan());
}

public record ContainedDefault(Type ConcreteType, Type AsType, object Instance, DefaultFlags Flags) {
    public TransientCtorInvoker? TransientCtor;
    
    public bool HasFlag(DefaultFlags flag) => (Flags & flag) == flag;
    public T As<T>() where T : class => (T)Instance;

    public override string ToString() {
        return $"{ConcreteType.FullName} as {AsType.FullName} (Flags: {Flags})";
    }
}

public class DefaultContainer {
    readonly ConcurrentDictionary<Type, ContainedDefault> defaults = new();
    readonly ConcurrentDictionary<(string, Type), ContainedDefault> keyedDefaults = new();
    
    public bool Contains(Type type) => defaults.ContainsKey(type);
    public ContainedDefault Get(Type type) => defaults[type];
    public bool TryGet(Type type, out ContainedDefault containedDefault) => defaults.TryGetValue(type, out containedDefault!);
    public void Add(ContainedDefault containedDefault) {
        if (DefaultValidator.Validate(containedDefault.AsType, containedDefault.Instance, containedDefault.Flags)) {
            defaults[containedDefault.AsType] = containedDefault;
            return;
        }
        
        throw new Exception($"Instance of type {containedDefault.Instance.GetType().FullName} is not of the correct type {containedDefault.AsType.FullName}.");
    }
    
    public bool Contains(string key, Type type) => keyedDefaults.ContainsKey((key, type));
    public ContainedDefault Get(string key, Type type) => keyedDefaults[(key, type)];
    public bool TryGet(string key, Type type, out ContainedDefault containedDefault) => keyedDefaults.TryGetValue((key, type), out containedDefault!);
    public void Add(string key, ContainedDefault containedDefault) {
        if (DefaultValidator.Validate(containedDefault.AsType, containedDefault.Instance, containedDefault.Flags)) {
            keyedDefaults[(key, containedDefault.AsType)] = containedDefault;
            return;
        }
        
        throw new Exception($"Instance of type {containedDefault.Instance.GetType().FullName} is not of the correct type {containedDefault.AsType.FullName}.");
    }
}