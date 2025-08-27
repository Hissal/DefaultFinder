namespace DefaultFinder.Attributes;

[Flags]
public enum DefaultFlags {
    None = 0,
    Overrideable = 1 << 0,
    Transient = 1 << 1,
    Cloneable = 1 << 2 | Transient,
}

[Flags]
public enum FinderFlags {
    None = 0,
    ForceTransient = 1 << 0,
    ForceSingleton = 1 << 1,
}

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public class DefaultAttribute : Attribute {
    public readonly Type AsType;
    public readonly DefaultFlags Flags;
    //public readonly string Key;
    public DefaultAttribute(Type @as, DefaultFlags flags = DefaultFlags.None) {
        AsType = @as;
        Flags = flags;
    }
    // public DefaultAttribute(string key, Type @as, DefaultFlags flags = DefaultFlags.None) {
    //     AsType = @as;
    //     Flags = flags;
    //     Key = key;
    // }
}

[AttributeUsage(AttributeTargets.Constructor)]
public class DefaultConstructorAttribute : Attribute {
    public readonly FinderFlags FinderFlags;

    public DefaultConstructorAttribute(FinderFlags finderFlags = FinderFlags.None) {
        FinderFlags = finderFlags;
    }
}