using DefaultFinder.Attributes;

namespace DefaultFinder.Internal;

internal static class DefaultValidator {
    public static bool Validate(Type asType, object instance, DefaultFlags flags) {
        if (!asType.IsInstanceOfType(instance))
            return false;

        return true;
    }
    
    public static bool CanBe(GenericDefaultInfo genericDefaultInfo, Type asType) {
        if (!asType.IsGenericType)
            return false;
        
        if (asType.GetGenericTypeDefinition() != genericDefaultInfo.AsTypeDefinition)
            return false;
        
        var typeParams = asType.GetGenericArguments();
        for (int i = 0; i < typeParams.Length; i++) {
            var defaultAsArg = genericDefaultInfo.AsTypeGenericArgs[i];
            if (!defaultAsArg.IsGenericParameter && defaultAsArg != typeParams[i])
                return false;
        }

        return true;
    }
}