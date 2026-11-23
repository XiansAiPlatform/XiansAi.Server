using MongoDB.Driver;
using Shared.Auth;
using Shared.Data.Models;
using Shared.Data.Models.Validation;
using Shared.Repositories;
using Shared.Utils;
using Shared.Utils.Services;

namespace Shared.Services;

public sealed record ContactInput(string? Name, string? Email, string? Phone);

public interface IContactService
{
    Task<ServiceResult<List<Contact>>> ListAsync(string tenantId, string? search);
    Task<ServiceResult<Contact>> GetAsync(string tenantId, string id);
    Task<ServiceResult<Contact>> CreateAsync(string tenantId, ContactInput input);
    Task<ServiceResult<Contact>> UpdateAsync(string tenantId, string id, ContactInput input);
    Task<ServiceResult<bool>> DeleteAsync(string tenantId, string id);

    /// <summary>
    /// Emails every contact of the tenant, one email each so recipients never see each other's addresses.
    /// Returns how many contacts were emailed (0 when the tenant has no contacts or email is off).
    /// </summary>
    Task<ServiceResult<int>> NotifyAllAsync(string tenantId, string subject, string body);
}

public class ContactService : IContactService
{
    private const int MaxNameLength = 200;
    private const int MaxPhoneLength = 30;
    private const int MaxSubjectLength = 200;
    internal const int MaxBodyLength = 20_000;

    private readonly IContactRepository _repository;
    private readonly ITenantContext _tenantContext;
    private readonly IEmailService _emailService;
    private readonly ILogger<ContactService> _logger;

    public ContactService(IContactRepository repository, ITenantContext tenantContext, IEmailService emailService, ILogger<ContactService> logger)
    {
        _repository = repository;
        _tenantContext = tenantContext;
        _emailService = emailService;
        _logger = logger;
    }

    public async Task<ServiceResult<List<Contact>>> ListAsync(string tenantId, string? search)
    {
        return ServiceResult<List<Contact>>.Success(await _repository.ListAsync(tenantId, search));
    }

    public async Task<ServiceResult<Contact>> GetAsync(string tenantId, string id)
    {
        var contact = await _repository.GetAsync(tenantId, id);
        return contact == null
            ? ServiceResult<Contact>.NotFound("Contact not found")
            : ServiceResult<Contact>.Success(contact);
    }

    public async Task<ServiceResult<Contact>> CreateAsync(string tenantId, ContactInput input)
    {
        var error = Validate(input, out var name, out var email, out var phone);
        if (error != null) return ServiceResult<Contact>.BadRequest(error);

        var contact = new Contact
        {
            TenantId = tenantId,
            Name = name,
            Email = email,
            Phone = phone,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = CurrentUser,
        };

        try
        {
            return ServiceResult<Contact>.Success(await _repository.CreateAsync(contact), StatusCode.Created);
        }
        catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            return ServiceResult<Contact>.Conflict("A contact with this email already exists");
        }
    }

    public async Task<ServiceResult<Contact>> UpdateAsync(string tenantId, string id, ContactInput input)
    {
        var contact = await _repository.GetAsync(tenantId, id);
        if (contact == null) return ServiceResult<Contact>.NotFound("Contact not found");

        // PATCH: fields left out keep their current value.
        var merged = new ContactInput(
            input.Name ?? contact.Name,
            input.Email ?? contact.Email,
            input.Phone ?? contact.Phone);

        var error = Validate(merged, out var name, out var email, out var phone);
        if (error != null) return ServiceResult<Contact>.BadRequest(error);

        contact.Name = name;
        contact.Email = email;
        contact.Phone = phone;
        contact.UpdatedAt = DateTime.UtcNow;
        contact.UpdatedBy = CurrentUser;

        try
        {
            return await _repository.UpdateAsync(contact)
                ? ServiceResult<Contact>.Success(contact)
                : ServiceResult<Contact>.NotFound("Contact not found");
        }
        catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            return ServiceResult<Contact>.Conflict("A contact with this email already exists");
        }
    }

    public async Task<ServiceResult<bool>> DeleteAsync(string tenantId, string id)
    {
        return await _repository.DeleteAsync(tenantId, id)
            ? ServiceResult<bool>.Success(true)
            : ServiceResult<bool>.NotFound("Contact not found");
    }

    public async Task<ServiceResult<int>> NotifyAllAsync(string tenantId, string subject, string body)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
            return ServiceResult<int>.BadRequest("Tenant context is not available");
        if (string.IsNullOrWhiteSpace(subject))
            return ServiceResult<int>.BadRequest("Subject is required");
        if (string.IsNullOrWhiteSpace(body))
            return ServiceResult<int>.BadRequest("Body is required");
        if (body.Length > MaxBodyLength)
            return ServiceResult<int>.BadRequest($"Body must be {MaxBodyLength} characters or fewer");

        // Header injection guard: a subject must stay on one line.
        subject = subject.Replace('\r', ' ').Replace('\n', ' ').Trim();
        if (subject.Length > MaxSubjectLength)
            subject = subject[..MaxSubjectLength] + "…";

        if (!await _emailService.IsEnabledAsync(tenantId))
        {
            _logger.LogInformation("Contact notification for tenant {TenantId} skipped: no email server is configured",
                LogSanitizer.Sanitize(tenantId));
            return ServiceResult<int>.Success(0);
        }

        var recipients = (await _repository.ListAsync(tenantId))
            .Select(c => c.Email)
            .Where(e => !string.IsNullOrWhiteSpace(e))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (recipients.Count == 0)
        {
            _logger.LogInformation("Contact notification for tenant {TenantId} skipped: the tenant has no contacts",
                LogSanitizer.Sanitize(tenantId));
            return ServiceResult<int>.Success(0);
        }

        var sent = 0;
        foreach (var recipient in recipients)
        {
            try
            {
                await _emailService.SendEmailAsync(tenantId, recipient, subject, body);
                sent++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // One bad address or a transient SMTP error must not stop the other contacts hearing
                // about it. Cancellation still propagates.
                _logger.LogWarning(ex, "Contact notification for tenant {TenantId} could not be sent to a contact",
                    LogSanitizer.Sanitize(tenantId));
            }
        }

        if (sent == 0)
            return ServiceResult<int>.InternalServerError("The notification could not be sent to any contact");

        _logger.LogInformation("Contact notification for tenant {TenantId} sent to {Sent}/{Total} contact(s)",
            LogSanitizer.Sanitize(tenantId), sent, recipients.Count);
        return ServiceResult<int>.Success(sent);
    }

    private string CurrentUser => string.IsNullOrEmpty(_tenantContext.LoggedInUser) ? "system" : _tenantContext.LoggedInUser;

    private static string? Validate(ContactInput input, out string name, out string email, out string? phone)
    {
        name = input.Name?.Trim() ?? string.Empty;
        email = input.Email?.Trim().ToLowerInvariant() ?? string.Empty;
        phone = string.IsNullOrWhiteSpace(input.Phone) ? null : input.Phone.Trim();

        if (name.Length == 0) return "Name is required";
        if (name.Length > MaxNameLength) return $"Name must be {MaxNameLength} characters or fewer";
        if (!ValidationHelpers.IsValidEmail(email)) return "A valid email is required";
        if (phone != null && phone.Length > MaxPhoneLength) return $"Phone must be {MaxPhoneLength} characters or fewer";
        return null;
    }
}
