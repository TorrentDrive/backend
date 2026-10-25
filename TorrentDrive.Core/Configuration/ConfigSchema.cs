using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

using TorrentDrive.Core.Options;

namespace TorrentDrive.Core.Configuration
{
    /// <summary>One documented configuration value.</summary>
    public sealed class ConfigEntry
    {
        /// <summary>What an operator sets, e.g. "JWT_SECRET".</summary>
        public string EnvName { get; init; } = string.Empty;

        /// <summary>The .NET configuration key it binds to, e.g. "Jwt:Key".</summary>
        public string ConfigKey { get; init; } = string.Empty;

        public string Description { get; init; } = string.Empty;
        public string Default { get; init; } = string.Empty;
        public string Type { get; init; } = string.Empty;
        public bool Secret { get; init; }
        public bool Required { get; init; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Deprecated { get; init; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Since { get; init; }
    }

    /// <summary>
    /// Derives the configuration reference from the option types themselves.
    ///
    /// The reference was previously written by hand in five places and no two
    /// agreed on which variables existed, which were required, or what the
    /// defaults were. Generating it from the same declarations the application
    /// validates against means the documentation cannot say something the code
    /// does not do.
    /// </summary>
    public static class ConfigSchema
    {
        /// <summary>
        /// Every options type that carries documented values. Adding a type here
        /// is what puts it in the published reference.
        /// </summary>
        public static readonly IReadOnlyList<Type> DocumentedTypes = new[]
        {
            typeof(DatabaseOptions),
            typeof(RedisOptions),
            typeof(JwtOptions),
            typeof(RuntimeOptions),
            typeof(HangfireOptions),
            typeof(ObservabilityOptions),
            typeof(UploadRoutingOptions),
            typeof(AdminSeedOptions),
        };

        public static IReadOnlyList<ConfigEntry> Describe()
        {
            var entries = new List<ConfigEntry>();

            foreach (var type in DocumentedTypes)
            {
                var section = SectionNameOf(type);

                foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    var doc = property.GetCustomAttribute<ConfigDocAttribute>();
                    if (doc is null) continue;

                    entries.Add(new ConfigEntry
                    {
                        EnvName = doc.EnvName,
                        ConfigKey = section is null ? property.Name : $"{section}:{property.Name}",
                        Description = doc.Description,
                        Default = doc.Default,
                        Type = FriendlyTypeName(property.PropertyType),
                        Secret = doc.Secret,
                        Required = property.GetCustomAttribute<RequiredAttribute>() is not null,
                        Deprecated = string.IsNullOrWhiteSpace(doc.Deprecated) ? null : doc.Deprecated,
                        Since = string.IsNullOrWhiteSpace(doc.Since) ? null : doc.Since,
                    });
                }
            }

            // Required first, then alphabetically. That is the order someone
            // setting the thing up needs, not the order the types happen to be
            // declared in.
            return entries
                .OrderByDescending(e => e.Required)
                .ThenBy(e => e.EnvName, StringComparer.Ordinal)
                .ToList();
        }

        public static string ToJson()
        {
            var payload = new
            {
                schemaVersion = 1,
                generatedBy = "TorrentDrive.Core.Configuration.ConfigSchema",
                productVersion = BuildInfo.Version,
                entries = Describe(),
            };

            return JsonSerializer.Serialize(payload, new JsonSerializerOptions
            {
                WriteIndented = true,
                // camelCase because this file is a published artifact that the
                // documentation site reads, not an internal DTO.
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            });
        }

        private static string? SectionNameOf(Type type)
        {
            // RuntimeOptions and AdminSeedOptions are flat environment
            // variables read from the configuration root, not a section.
            var field = type.GetField("SectionName", BindingFlags.Public | BindingFlags.Static);
            return field?.GetValue(null) as string;
        }

        private static string FriendlyTypeName(Type type)
        {
            var underlying = Nullable.GetUnderlyingType(type) ?? type;

            if (underlying == typeof(string)) return "string";
            if (underlying == typeof(bool)) return "boolean";
            if (underlying == typeof(int) || underlying == typeof(long)) return "integer";
            if (underlying == typeof(double) || underlying == typeof(float)) return "number";
            if (underlying == typeof(TimeSpan)) return "duration";

            return underlying.Name;
        }
    }
}
