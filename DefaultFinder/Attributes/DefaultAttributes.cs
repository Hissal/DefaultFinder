namespace DefaultFinder.Attributes;

public readonly record struct Any;

[Flags]
public enum DefaultFlags {
    None = 0,
    Overrideable = 1 << 0,
    Transient = 1 << 1,
    Cloneable = 1 << 2 | Transient,
}

public enum FinderFlags {
    None = 0,
    ForceTransient = 1 << 0,
    ForceSingleton = 1 << 1,
}

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public class DefaultAttribute : Attribute {
    public readonly Type AsType;
    public readonly DefaultFlags Flags;
    public readonly string Key;
    public DefaultAttribute(Type @as, DefaultFlags flags = DefaultFlags.None) : this(string.Empty, @as, flags) {}
    public DefaultAttribute(string key, Type @as, DefaultFlags flags = DefaultFlags.None) {
        AsType = @as;
        Flags = flags;
        Key = key;
    }
}

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public class DefaultGenericAttribute : Attribute {
    public readonly Type GenericType;
    public readonly DefaultFlags Flags;
    public readonly Type[] GenericArgs;
    public readonly string Key;
    
    public DefaultGenericAttribute(Type genericType, Type[] genericArgs, DefaultFlags flags = DefaultFlags.None) : this(string.Empty, genericType, genericArgs, flags) {}
    public DefaultGenericAttribute(string key, Type genericType, Type[] genericArgs, DefaultFlags flags = DefaultFlags.None) {
        GenericType = genericType;
        Flags = flags;
        GenericArgs = genericArgs;
        Key = key;
    }
}

[AttributeUsage(AttributeTargets.Constructor)]
public class DefaultConstructorAttribute : Attribute {
    public readonly FinderFlags FinderFlags;

    public DefaultConstructorAttribute(FinderFlags finderFlags = FinderFlags.None) {
        FinderFlags = finderFlags;
    }
}