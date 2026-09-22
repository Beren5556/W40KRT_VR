using System;
using System.Collections.Generic;
using System.Reflection;

namespace RTMaquetaXR
{
    // Cache metadata only: pooled views can bind a different model at any time.
    // Every field/property value is still read from the current live instance.
    internal sealed class ReflectionMetadataCache
    {
        const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        readonly Func<Type, bool> followType;
        readonly Dictionary<Type, FieldInfo> viewModels = new Dictionary<Type, FieldInfo>();
        readonly Dictionary<Type, FieldInfo[]> followFields = new Dictionary<Type, FieldInfo[]>();
        readonly Dictionary<Type, Dictionary<string, PropertyInfo>> properties = new Dictionary<Type, Dictionary<string, PropertyInfo>>();
        public int MetadataScans { get; private set; }

        public ReflectionMetadataCache(Func<Type, bool> followType) { this.followType = followType; }

        public FieldInfo ViewModelField(Type type)
        {
            if (viewModels.TryGetValue(type, out var field)) return field;
            ++MetadataScans;
            int depth = 0;
            for (var current = type; current != null && current != typeof(object) && depth < 6; current = current.BaseType, ++depth)
            {
                field = current.GetField("<ViewModel>k__BackingField", Instance | BindingFlags.DeclaredOnly);
                if (field != null) break;
            }
            viewModels[type] = field;
            return field;
        }

        public FieldInfo[] FollowFields(Type type)
        {
            if (followFields.TryGetValue(type, out var result)) return result;
            ++MetadataScans;
            var fields = new List<FieldInfo>();
            int depth = 0;
            for (var current = type; current != null && current != typeof(object) && depth < 6; current = current.BaseType, ++depth)
                foreach (var field in current.GetFields(Instance | BindingFlags.DeclaredOnly))
                {
                    var valueType = field.FieldType;
                    bool entityRef = valueType.IsGenericType && valueType.Name.StartsWith("EntityRef", StringComparison.Ordinal);
                    if (entityRef || followType(valueType)) fields.Add(field);
                }
            result = fields.ToArray(); followFields[type] = result;
            return result;
        }

        public object ReadProperty(object instance, string name)
        {
            if (instance == null) return null;
            try
            {
                var type = instance.GetType();
                if (!properties.TryGetValue(type, out var byName))
                    properties[type] = byName = new Dictionary<string, PropertyInfo>();
                if (!byName.TryGetValue(name, out var property))
                {
                    ++MetadataScans;
                    // Missing, indexed or ambiguous properties stay a safe miss.
                    try { property = type.GetProperty(name, Instance); } catch (AmbiguousMatchException) { property = null; }
                    if (property != null && (!property.CanRead || property.GetIndexParameters().Length != 0)) property = null;
                    byName[name] = property;
                }
                return property?.GetValue(instance, null);
            }
            catch { return null; }
        }
    }
}
