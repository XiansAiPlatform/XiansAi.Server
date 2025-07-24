using System.ComponentModel.DataAnnotations;

namespace Shared.Data.Models.Validation;

/// <summary>
/// Generic interface for model validation and sanitization
/// </summary>
/// <typeparam name="T">The type of the model to validate and sanitize</typeparam>
public interface IModelValidator<T> where T : class
{
    /// <summary>
    /// Validates the model and throws ValidationException if invalid
    /// </summary>
    void Validate();

    /// <summary>
    /// Sanitizes the model and returns a new instance
    /// </summary>
    T SanitizeAndReturn();

    /// <summary>
    /// Returns a sanitized and validated version of the model
    /// </summary>
    T SanitizeAndValidate();
}
/// <summary>
/// Base class for model validation logic
/// </summary>
/// <typeparam name="T">The type of the model</typeparam>
public abstract class ModelValidatorBase<T> : IModelValidator<T> where T : class
{
    public virtual void Validate()
    {
        var results = new List<ValidationResult>();
        var context = new ValidationContext(this);

        if (!Validator.TryValidateObject(this, context, results, validateAllProperties: true))
        {
            var errors = results.Select(r => r.ErrorMessage).ToList();
            throw new ValidationException($"Validation failed: {string.Join("; ", errors)}");
        }
    }

    public abstract T SanitizeAndReturn();

    public virtual T SanitizeAndValidate()
    {
        var sanitized = SanitizeAndReturn();

        // Ensure sanitized object is also validated
        if (sanitized is IModelValidator<T> validator)
        {
            validator.Validate();
            return sanitized;
        }

        throw new InvalidOperationException("Sanitized model does not implement IModelValidator<T>");
    }
}
