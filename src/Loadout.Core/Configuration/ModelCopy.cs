using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;

namespace Loadout.Core.Configuration;

/// <summary>
/// Deep copies of what <see cref="YamlStore"/> reads, so a value kept between
/// reads can be handed to a caller who may change it.
/// </summary>
/// <remarks>
/// <para>
/// Copies what the deserialiser fills: public properties with a public getter
/// and setter, through the kinds it builds them from. Values that cannot
/// change (strings, numbers, enums, times) are shared; lists, dictionaries,
/// arrays and classes are copied all the way down, and a dictionary keeps its
/// comparer, so a case-insensitive one stays so.
/// </para>
/// <para>
/// A type holding anything else, an <c>object</c>, an interface or a struct
/// of its own, is not <see cref="Supports">supported</see>, and its files are
/// read every time as before. Guessing at a copy of something this does not
/// understand would hand two callers the same object without either knowing.
/// </para>
/// </remarks>
internal static class ModelCopy
{
    private static readonly ConcurrentDictionary<Type, bool> Supported = new();
    private static readonly ConcurrentDictionary<Type, PropertyInfo[]> Writable = new();

    /// <summary>Whether every value of <paramref name="type"/> can be copied by <see cref="Of{T}"/>.</summary>
    public static bool Supports(Type type) => Supported.GetOrAdd(type, one => Check(one, []));

    /// <summary>A copy of <paramref name="value"/> sharing nothing that can be changed.</summary>
    /// <exception cref="NotSupportedException">The type is one <see cref="Supports"/> refuses.</exception>
    public static T Of<T>(T value)
        where T : class
    {
        if (!Supports(typeof(T)))
        {
            throw new NotSupportedException($"{typeof(T).Name} holds something that cannot be copied safely.");
        }

        return (T)Copy(value)!;
    }

    private static bool IsUnchangeable(Type type) =>
        type.IsPrimitive
        || type.IsEnum
        || type == typeof(string)
        || type == typeof(decimal)
        || type == typeof(DateTime)
        || type == typeof(DateTimeOffset)
        || type == typeof(TimeSpan)
        || type == typeof(TimeOnly)
        || type == typeof(DateOnly)
        || type == typeof(Guid)
        || type == typeof(Uri);

    private static bool Check(Type type, HashSet<Type> seen)
    {
        if (IsUnchangeable(type))
        {
            return true;
        }

        if (Nullable.GetUnderlyingType(type) is { } underlying)
        {
            return IsUnchangeable(underlying);
        }

        if (type.IsArray)
        {
            return type.GetArrayRank() == 1 && Check(type.GetElementType()!, seen);
        }

        if (type.IsGenericType)
        {
            var definition = type.GetGenericTypeDefinition();
            var arguments = type.GetGenericArguments();

            if (definition == typeof(List<>))
            {
                return Check(arguments[0], seen);
            }

            if (definition == typeof(Dictionary<,>))
            {
                return IsUnchangeable(arguments[0]) && Check(arguments[1], seen);
            }
        }

        if (!type.IsClass
            || type == typeof(object)
            || type.IsAbstract
            || typeof(IEnumerable).IsAssignableFrom(type)
            || type.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, Type.EmptyTypes) is null)
        {
            return false;
        }

        // A type that holds itself, directly or further down, is checked once.
        if (!seen.Add(type))
        {
            return true;
        }

        return PropertiesOf(type).All(property => Check(property.PropertyType, seen));
    }

    private static PropertyInfo[] PropertiesOf(Type type) =>
        Writable.GetOrAdd(type, one => one
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(property => property.GetIndexParameters().Length == 0
                && property.GetMethod is { IsPublic: true }
                && property.SetMethod is { IsPublic: true })
            .ToArray());

    private static object? Copy(object? value)
    {
        if (value is null)
        {
            return null;
        }

        var type = value.GetType();

        if (IsUnchangeable(type))
        {
            return value;
        }

        if (value is Array array)
        {
            var copy = Array.CreateInstance(type.GetElementType()!, array.Length);

            for (var i = 0; i < array.Length; i++)
            {
                copy.SetValue(Copy(array.GetValue(i)), i);
            }

            return copy;
        }

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
        {
            var source = (IList)value;
            var copy = (IList)Activator.CreateInstance(type, source.Count)!;

            foreach (var item in source)
            {
                copy.Add(Copy(item));
            }

            return copy;
        }

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Dictionary<,>))
        {
            var comparer = type.GetProperty(nameof(Dictionary<string, string>.Comparer))!.GetValue(value);
            var copy = (IDictionary)Activator.CreateInstance(type, comparer)!;

            foreach (DictionaryEntry entry in (IDictionary)value)
            {
                copy.Add(entry.Key, Copy(entry.Value));
            }

            return copy;
        }

        var made = Activator.CreateInstance(type, nonPublic: true)!;

        foreach (var property in PropertiesOf(type))
        {
            property.SetValue(made, Copy(property.GetValue(value)));
        }

        return made;
    }
}
