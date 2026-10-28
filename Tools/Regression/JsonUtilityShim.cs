// Only for the standalone regression executable, never copied into Unity Assets.
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
namespace UnityEngine
{
    public static class JsonUtility
    {
        private static JsonSerializerOptions Options(bool pretty)
        {
            var resolver = new DefaultJsonTypeInfoResolver();
            resolver.Modifiers.Add(info =>
            {
                for (int i = info.Properties.Count - 1; i >= 0; i--)
                    if (info.Properties[i].AttributeProvider is FieldInfo field && field.IsDefined(typeof(NonSerializedAttribute)))
                        info.Properties.RemoveAt(i);
            });
            return new JsonSerializerOptions { IncludeFields = true, WriteIndented = pretty, TypeInfoResolver = resolver };
        }
        public static string ToJson(object value, bool pretty = false) => JsonSerializer.Serialize(value, Options(pretty));
        public static T FromJson<T>(string json) => JsonSerializer.Deserialize<T>(json, Options(false));
    }
}
