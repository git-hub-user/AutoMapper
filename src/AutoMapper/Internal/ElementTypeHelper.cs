using System;
using System.Collections;
using System.Linq;
using System.Reflection;

namespace AutoMapper.Internal
{
    public static class ElementTypeHelper
    {
        public static Type GetElementType(Type enumerableType) => GetEnumerableElementTypes(enumerableType, null)[0];
        public static Type[] GetElementTypes(Type enumerableType, ElementTypeFlags flags = ElementTypeFlags.None) => GetElementTypes(enumerableType, null, flags);
        public static Type[] GetElementTypes(Type enumerableType, IEnumerable enumerable, ElementTypeFlags flags = ElementTypeFlags.None)
        {
            var iDictionaryType = enumerableType.GetDictionaryType();
            if (iDictionaryType != null && flags.HasFlag(ElementTypeFlags.BreakKeyValuePair))
            {
                return iDictionaryType.GenericTypeArguments;
            }
            var iReadOnlyDictionaryType = enumerableType.GetReadOnlyDictionaryType();
            if (iReadOnlyDictionaryType != null && flags.HasFlag(ElementTypeFlags.BreakKeyValuePair))
            {
                return iReadOnlyDictionaryType.GenericTypeArguments;
            }
            return GetEnumerableElementTypes(enumerableType, enumerable);
        }
        public static Type[] GetEnumerableElementTypes(Type enumerableType, IEnumerable enumerable)
        {
            if (enumerableType.HasElementType)
            {
                return new[] { enumerableType.GetElementType() };
            }
            var iEnumerableType = enumerableType.GetIEnumerableType();
            if (iEnumerableType != null)
            {
                return iEnumerableType.GenericTypeArguments;
            }
            if (enumerableType.IsEnumerableType())
            {
                var first = enumerable?.Cast<object>().FirstOrDefault();
                return new[] { first?.GetType() ?? typeof(object) };
            }
            throw new ArgumentException($"Unable to find the element type for type '{enumerableType}'.", nameof(enumerableType));
        }
    }
    public enum ElementTypeFlags
    {
        None = 0,
        BreakKeyValuePair = 1
    }
}