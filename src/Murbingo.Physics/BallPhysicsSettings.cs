using System.Text.Json;
using System.Text.Json.Serialization;

namespace Murbingo.Physics;
/// <summary>Defines portable simulation parameters and their safe defaults independently of game and window code.</summary>
[JsonConverter(typeof(BallPhysicsSettingsConverter))]
public sealed record BallPhysicsSettings
{
    public bool Enabled { get; init; } = true;
    public double MassKg { get; init; } = .15;
    public double Restitution { get; init; } = .2;
    public double Friction { get; init; } = .4;
    public double LinearDamping { get; init; } = .8;
    public double AngularDamping { get; init; } = 1.2;
    public int SpawnIntervalMs { get; init; } = 35;

    /// <summary>Checks a finite numeric range and substitutes a safe default for one invalid parameter.</summary>
    static double Valid(double value, double min, double max, double fallback) => double.IsFinite(value) && value >= min && value <= max ? value : fallback;
    /// <summary>Validates each physics parameter independently so valid neighboring settings survive a configuration mistake.</summary>
    public BallPhysicsSettings Normalize() => this with
    {
        MassKg = Valid(MassKg, .001, 100, .15),
        Restitution = Valid(Restitution, 0, 1, .2),
        Friction = Valid(Friction, 0, 2, .4),
        LinearDamping = Valid(LinearDamping, 0, 20, .8),
        AngularDamping = Valid(AngularDamping, 0, 20, 1.2),
        SpawnIntervalMs = SpawnIntervalMs is >= 0 and <= 1000 ? SpawnIntervalMs : 35
    };
}

// A malformed field must not discard valid settings or the rest of config.json.
/// <summary>Tolerates malformed individual JSON fields so physics settings cannot invalidate the rest of configuration.</summary>
public sealed class BallPhysicsSettingsConverter : JsonConverter<BallPhysicsSettings>
{
    public override bool HandleNull => true;

    /// <summary>Reads optional typed fields and normalizes their ranges without rejecting unrelated valid configuration values.</summary>
    public override BallPhysicsSettings Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            return new();
        // Purpose: Finds an optional setting case-insensitively, returning an undefined element when it is absent.
        JsonElement Field(string name) => root.EnumerateObject().FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).Value;
        // Purpose: Extracts a numeric field or its default without coercing invalid text or null into a simulation value.
        double Number(string name, double fallback)
        {
            var field = Field(name);
            return field.ValueKind == JsonValueKind.Number && field.TryGetDouble(out double value) ? value : fallback;
        }

        var enabled = Field("enabled");
        var interval = Field("spawnIntervalMs");
        return new BallPhysicsSettings
        {
            Enabled = enabled.ValueKind is JsonValueKind.True or JsonValueKind.False ? enabled.GetBoolean() : true,
            MassKg = Number("massKg", .15),
            Restitution = Number("restitution", .2),
            Friction = Number("friction", .4),
            LinearDamping = Number("linearDamping", .8),
            AngularDamping = Number("angularDamping", 1.2),
            SpawnIntervalMs = interval.ValueKind == JsonValueKind.Number && interval.TryGetInt32(out int ms) ? ms : 35
        }.Normalize();
    }

    /// <summary>Writes a clear portable JSON object without recursively invoking the custom converter.</summary>
    public override void Write(Utf8JsonWriter writer, BallPhysicsSettings value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteBoolean("enabled", value.Enabled);
        writer.WriteNumber("massKg", value.MassKg);
        writer.WriteNumber("restitution", value.Restitution);
        writer.WriteNumber("friction", value.Friction);
        writer.WriteNumber("linearDamping", value.LinearDamping);
        writer.WriteNumber("angularDamping", value.AngularDamping);
        writer.WriteNumber("spawnIntervalMs", value.SpawnIntervalMs);
        writer.WriteEndObject();
    }
}
