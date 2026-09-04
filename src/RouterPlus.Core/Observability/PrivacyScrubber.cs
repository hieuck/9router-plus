using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;

namespace RouterPlus.Core.Observability;

/// <summary>
/// Sanitizes sensitive data from objects before logging to prevent credential leaks.
/// </summary>
public static class PrivacyScrubber
{
    private static readonly string[] SensitivePropertyNames =
    {
        "Password", "Passphrase", "ApiKey", "Token", "TotpSecret", "Cookie",
        "Authorization", "Secret", "Credential", "Key", "AccessToken", "RefreshToken"
    };

    private static readonly Regex PasswordPattern = new(
        @"(password|pwd|pass|passphrase)\s*[:=]\s*[""']?[^""'\s]+[""']?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ApiKeyPattern = new(
        @"(api[_-]?key|token|secret|authorization)\s*[:=]\s*[""']?[^""'\s]+[""']?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private const string RedactedValue = "[REDACTED]";

    /// <summary>
    /// Scrubs sensitive data from an object graph, returning a sanitized copy.
    /// </summary>
    public static object? Scrub(object? obj)
    {
        if (obj == null) return null;

        var type = obj.GetType();

        // Primitive types and strings - check for patterns
        if (type.IsPrimitive || type == typeof(string))
        {
            return obj is string str ? ScrubString(str) : obj;
        }

        // DateTime and other value types - pass through
        if (type.IsValueType && !type.IsEnum)
        {
            return obj;
        }

        // Enums - pass through
        if (type.IsEnum)
        {
            return obj;
        }

        // Collections
        if (obj is IEnumerable enumerable and not string)
        {
            return ScrubCollection(enumerable);
        }

        // Complex objects - scrub properties
        return ScrubObject(obj);
    }

    /// <summary>
    /// Scrubs sensitive patterns from strings.
    /// </summary>
    public static string ScrubString(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;

        // Replace password patterns
        text = PasswordPattern.Replace(text, m =>
        {
            var key = m.Groups[1].Value;
            return $"{key}={RedactedValue}";
        });

        // Replace API key patterns
        text = ApiKeyPattern.Replace(text, m =>
        {
            var key = m.Groups[1].Value;
            return $"{key}={RedactedValue}";
        });

        return text;
    }

    private static object ScrubObject(object obj)
    {
        var type = obj.GetType();
        var scrubbed = new Dictionary<string, object?>();

        // Get all readable properties
        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead);

        foreach (var prop in properties)
        {
            try
            {
                var value = prop.GetValue(obj);

                // Check if property name is sensitive
                if (IsSensitivePropertyName(prop.Name))
                {
                    scrubbed[prop.Name] = RedactedValue;
                    continue;
                }

                // Recursively scrub the value
                scrubbed[prop.Name] = Scrub(value);
            }
            catch
            {
                // Skip properties that throw on access
                scrubbed[prop.Name] = "[ERROR_READING_PROPERTY]";
            }
        }

        return scrubbed;
    }

    private static object ScrubCollection(IEnumerable enumerable)
    {
        var scrubbed = new List<object?>();

        foreach (var item in enumerable)
        {
            scrubbed.Add(Scrub(item));
        }

        return scrubbed;
    }

    private static bool IsSensitivePropertyName(string propertyName)
    {
        return SensitivePropertyNames.Any(sensitive =>
            propertyName.Contains(sensitive, StringComparison.OrdinalIgnoreCase));
    }
}
