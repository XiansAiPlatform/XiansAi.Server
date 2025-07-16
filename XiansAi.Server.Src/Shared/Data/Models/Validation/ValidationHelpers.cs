using System.Text.RegularExpressions;

namespace Shared.Data.Models.Validation;

/// <summary>
/// Helper class for common validation and sanitization operations
/// </summary>
public static class ValidationHelpers
{
    // Common regex patterns for validation
    public static class Patterns
    {
        public static readonly Regex SafeId = new(@"^[a-fA-F0-9]{24}$", RegexOptions.Compiled);
        public static readonly Regex SafeName = new(@"^[a-zA-Z0-9\s._@-]{1,100}$", RegexOptions.Compiled);
        public static readonly Regex SafeUrl = new(@"^https?://[^\s/$.?#].[^\s]*$", RegexOptions.Compiled);
        public static readonly Regex SafeWorkflowType = new(@"^[a-zA-Z0-9._-]{1,100}$", RegexOptions.Compiled);
        public static readonly Regex SafeDomain = new(@"^[a-zA-Z0-9._\-+:|=#]+(\.[a-zA-Z]{2,})$", RegexOptions.Compiled);
        public static readonly Regex SafeTenantId = new(@"^[a-fA-F0-9]{24}$", RegexOptions.Compiled);
        public static readonly Regex SafeBase64 = new(@"^[A-Za-z0-9+/]*={0,2}$", RegexOptions.Compiled);
        public static readonly Regex AgentNamePattern=  new(@"^[a-zA-Z0-9\s._@|+\-:/\\,#=]+$", RegexOptions.Compiled); 
        public static readonly Regex WorkflowIdPattern=  new(@"^[a-zA-Z0-9\s._@|+\-:/\\,#=]+$", RegexOptions.Compiled);
        public static readonly Regex ActivityIdPattern=  new(@"^[0-9]+$", RegexOptions.Compiled);
        public static readonly Regex CertificateThumbprintPattern=  new(@"^[a-fA-F0-9]{40}$", RegexOptions.Compiled);
        public static readonly Regex WorkflowTypePattern=  new(@"^[a-zA-Z0-9\s._@|+\-:/\\,#='’]+$", RegexOptions.Compiled);
    }

    // Add these private static readonly Regex fields for sanitization:
    private static readonly Regex ControlChars = new Regex(@"[\x00-\x1F\x7F]", RegexOptions.Compiled);
    private static readonly Regex MultiWhitespace = new Regex(@"\s+", RegexOptions.Compiled);

    /// <summary>
    /// Sanitizes a string by removing control characters and normalizing whitespace
    /// </summary>
    public static string SanitizeString(string? input)
    {
        if (string.IsNullOrEmpty(input))
            return string.Empty;

        // Use compiled regexes for better performance
        var sanitized = ControlChars.Replace(input, "");
        sanitized = MultiWhitespace.Replace(sanitized, " ");

        return sanitized.Trim();
    }

    /// <summary>
    /// Sanitizes a list of strings
    /// </summary>
    public static List<string> SanitizeStringList(List<string>? input)
    {
        if (input == null)
            return new List<string>();

        return input.Select(SanitizeString)
                   .Where(s => !string.IsNullOrEmpty(s))
                   .ToList();
    }

    /// <summary>
    /// Sanitizes a URL by removing potentially dangerous characters
    /// </summary>
    public static string? SanitizeUrl(string? input)
    {
        if (string.IsNullOrEmpty(input))
            return null;

        var sanitized = SanitizeString(input);

        // Ensure it starts with http:// or https://
        if (!sanitized.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !sanitized.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return sanitized;
    }

    /// <summary>
    /// Sanitizes base64 data by removing invalid characters
    /// </summary>
    public static string? SanitizeBase64(string? input)
    {
        if (string.IsNullOrEmpty(input))
            return null;

        // Remove any whitespace and invalid characters
        var sanitized = Regex.Replace(input, @"[^A-Za-z0-9+/=]", "");

        // Ensure proper padding
        var remainder = sanitized.Length % 4;
        if (remainder > 0)
        {
            sanitized += new string('=', 4 - remainder);
        }

        return sanitized;
    }

    /// <summary>
    /// Validates that a string is not null, empty, or whitespace
    /// </summary>
    public static bool IsValidRequiredString(string? input)
    {
        return !string.IsNullOrWhiteSpace(input);
    }

    /// <summary>
    /// Validates that a string matches a specific pattern
    /// </summary>
    public static bool IsValidPattern(string? input, Regex pattern)
    {
        return !string.IsNullOrEmpty(input) && pattern.IsMatch(input);
    }

    /// <summary>
    /// Validates that a string length is within specified bounds
    /// </summary>
    public static bool IsValidLength(string? input, int minLength, int maxLength)
    {
        if (string.IsNullOrEmpty(input))
            return minLength == 0;

        return input.Length >= minLength && input.Length <= maxLength;
    }

    /// <summary>
    /// Validates that a list is not null and contains valid items
    /// </summary>
    public static bool IsValidList<T>(List<T>? input, Func<T, bool>? itemValidator = null)
    {
        if (input == null)
            return false;

        if (itemValidator == null)
            return true;

        return input.All(itemValidator);
    }

    
    public static bool IsValidDate(DateTime date)
    {
        var now = DateTime.UtcNow;
        return date >= now.AddYears(-5) && date <= now.AddYears(5);

    }

    /// <summary>
    /// Validates that a date range is valid
    /// </summary>
    public static bool IsValidDateRange(DateTime? startDate, DateTime? endDate)
    {
        if (!startDate.HasValue || !endDate.HasValue)
            return true;

        return startDate.Value <= endDate.Value;
    }

    /// <summary>
    /// Validates that a domain format is correct
    /// </summary>
    public static bool IsValidDomain(string? input)
    {
        if (string.IsNullOrEmpty(input))
            return false;

        return Patterns.SafeDomain.IsMatch(input);
    }

    /// <summary>
    /// Validates that a timezone format is correct
    /// </summary>
    public static bool IsValidTimezone(string? input)
    {
        if (string.IsNullOrEmpty(input))
            return false;

        // Basic timezone validation - can be enhanced with actual timezone database
        var timezonePattern = new Regex(@"^[A-Za-z_]+/[A-Za-z_]+$", RegexOptions.Compiled);
        return timezonePattern.IsMatch(input);
    }

    /// <summary>
    /// Validates that a URL format is correct
    /// </summary>
    public static bool IsValidUrl(string? input)
    {
        if (string.IsNullOrEmpty(input))
            return false;

        return Patterns.SafeUrl.IsMatch(input);
    }

    /// <summary>
    /// Validates that base64 data format is correct
    /// </summary>
    public static bool IsValidBase64(string? input)
    {
        if (string.IsNullOrEmpty(input))
            return false;

        return Patterns.SafeBase64.IsMatch(input);
    }
}