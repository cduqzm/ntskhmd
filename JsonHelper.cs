using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows.Media;

namespace ValCC;

public static class JsonHelper
{
    public static readonly JsonSerializerOptions CompactOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static readonly JsonSerializerOptions IndentedOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true
    };

    public static JsonNode? Parse(string text)
    {
        try
        {
            return JsonNode.Parse(text);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static JsonNode? Child(JsonNode? node, string name)
    {
        if (node is not JsonObject jsonObject)
        {
            return null;
        }

        foreach (KeyValuePair<string, JsonNode?> property in jsonObject)
        {
            if (string.Equals(property.Key, name, StringComparison.OrdinalIgnoreCase))
            {
                return property.Value;
            }
        }

        return null;
    }

    public static void SetChild(JsonObject jsonObject, string name, JsonNode? value)
    {
        string key = name;
        foreach (KeyValuePair<string, JsonNode?> property in jsonObject)
        {
            if (string.Equals(property.Key, name, StringComparison.OrdinalIgnoreCase))
            {
                key = property.Key;
                break;
            }
        }

        jsonObject[key] = value;
    }

    public static string Text(JsonNode? node, string name)
    {
        if (Child(node, name) is JsonValue value && value.GetValueKind() == JsonValueKind.String)
        {
            return value.GetValue<string>();
        }

        return "";
    }

    public static double Number(JsonNode? node, string name)
    {
        if (Child(node, name) is JsonValue value && value.GetValueKind() == JsonValueKind.Number)
        {
            return double.Parse(value.ToJsonString(), CultureInfo.InvariantCulture);
        }

        return 0;
    }

    public static bool Flag(JsonNode? node, string name)
    {
        return Child(node, name) is JsonValue value && value.GetValueKind() == JsonValueKind.True;
    }

    public static Color ReadColor(JsonNode? node, string name)
    {
        JsonNode? color = Child(node, name);
        return Color.FromRgb(ToByte(Number(color, "r")), ToByte(Number(color, "g")), ToByte(Number(color, "b")));
    }

    public static void WriteColor(JsonNode? node, string name, Color color)
    {
        if (node is not JsonObject parent)
        {
            return;
        }

        if (Child(parent, name) is not JsonObject target)
        {
            target = new JsonObject { ["r"] = 0, ["g"] = 0, ["b"] = 0, ["a"] = 255 };
            SetChild(parent, name, target);
        }

        SetChild(target, "r", (int)color.R);
        SetChild(target, "g", (int)color.G);
        SetChild(target, "b", (int)color.B);
        if (Child(target, "a") == null)
        {
            SetChild(target, "a", 255);
        }
    }

    private static byte ToByte(double value)
    {
        return (byte)Math.Clamp(Math.Round(value), 0, 255);
    }
}
