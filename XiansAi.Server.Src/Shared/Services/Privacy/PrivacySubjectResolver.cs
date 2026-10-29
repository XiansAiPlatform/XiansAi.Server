using Shared.Data.Models;
using Shared.Repositories;

namespace Shared.Services.Privacy;

/// <summary>
/// Turns the one identifier a requester gives into every identifier the person has within a request's scope.
/// </summary>
public interface IPrivacySubjectResolver
{
    /// <exception cref="ArgumentException">The input is blank, or the scope names no tenant.</exception>
    /// <exception cref="PrivacyRequestException">The subject's identifiers include a shared participant ID.</exception>
    Task<PrivacySubject> ResolveAsync(PrivacySubjectInput input, PrivacyScope scope, CancellationToken cancellationToken);
}

public class PrivacySubjectResolver : IPrivacySubjectResolver
{
    // Fallbacks the Apps and User API webhooks use when the sender is unknown.
    private static readonly string[] BuiltInSharedParticipantIds = ["webhook", "unknown"];

    private readonly IUserRepository _userRepository;
    private readonly ITenantRepository _tenantRepository;
    private readonly IConversationRepository _conversationRepository;
    private readonly IAppIntegrationRepository _appIntegrationRepository;

    public PrivacySubjectResolver(
        IUserRepository userRepository,
        ITenantRepository tenantRepository,
        IConversationRepository conversationRepository,
        IAppIntegrationRepository appIntegrationRepository)
    {
        _userRepository = userRepository ?? throw new ArgumentNullException(nameof(userRepository));
        _tenantRepository = tenantRepository ?? throw new ArgumentNullException(nameof(tenantRepository));
        _conversationRepository = conversationRepository ?? throw new ArgumentNullException(nameof(conversationRepository));
        _appIntegrationRepository = appIntegrationRepository ?? throw new ArgumentNullException(nameof(appIntegrationRepository));
    }

    public async Task<PrivacySubject> ResolveAsync(
        PrivacySubjectInput input, PrivacyScope scope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentException.ThrowIfNullOrWhiteSpace(input.Value, nameof(input));

        var value = input.Value.Trim();
        var tenantIds = await TenantsInScopeAsync(scope, cancellationToken);
        var holdersByEmail = new Dictionary<string, List<User>>(StringComparer.Ordinal);
        var accounts = await FindAccountsAsync(input.Kind, value, holdersByEmail);

        var subject = new PrivacySubject
        {
            UserIds = accounts.Select(account => account.UserId).ToList(),
            TenantIds = tenantIds.ToList()
        };

        var participantIds = new HashSet<string>(StringComparer.Ordinal);
        var emails = new HashSet<string>(StringComparer.Ordinal);
        var memberships = new HashSet<string>(StringComparer.Ordinal);

        if (accounts.Count == 0)
        {
            AddBareIdentifier(input.Kind, value, participantIds, emails);
        }

        foreach (var account in accounts)
        {
            await AddAccountIdentifiersAsync(account, subject.UserIds, holdersByEmail, participantIds, emails);
            memberships.UnionWith(account.TenantRoles.Select(role => role.Tenant).Where(tenantIds.Contains));
        }

        await RejectSharedParticipantIdsAsync(tenantIds, participantIds);

        subject.ParticipantIds = participantIds.Order(StringComparer.Ordinal).ToList();
        subject.Emails = emails.Order(StringComparer.Ordinal).ToList();
        subject.MembershipTenantIds = memberships.Order(StringComparer.Ordinal).ToList();
        subject.WorkflowIds = await _conversationRepository.GetWorkflowIdsForParticipantsAsync(
            subject.TenantIds, subject.ParticipantIds);

        return subject;
    }

    // Threads under a shared participant ID hold many senders' messages, so erasing them would reach other people.
    private async Task RejectSharedParticipantIdsAsync(IReadOnlyCollection<string> tenantIds, HashSet<string> participantIds)
    {
        var configured = await _appIntegrationRepository.GetDefaultParticipantIdsAsync(tenantIds);
        if (participantIds.Overlaps(BuiltInSharedParticipantIds) || participantIds.Overlaps(configured))
        {
            throw new PrivacyRequestException(
                "The subject's identifiers include a participant ID shared by many senders, such as an integration's " +
                "default participant ID. Data under it must be handled manually.");
        }
    }

    private async Task<HashSet<string>> TenantsInScopeAsync(PrivacyScope scope, CancellationToken cancellationToken)
    {
        var tenantIds = scope.AllTenants
            ? await _tenantRepository.GetAllTenantIdsAsync(cancellationToken)
            : scope.TenantIds;

        var result = tenantIds.Where(id => !string.IsNullOrWhiteSpace(id)).ToHashSet(StringComparer.Ordinal);
        if (!scope.AllTenants && result.Count == 0)
        {
            throw new ArgumentException("The scope must name at least one tenant or cover all tenants.", nameof(scope));
        }

        return result;
    }

    // An email, or a participant ID equal to an email, resolves every account holding it. The server allows one
    // address on accounts from several identity providers, and those accounts are treated as one person.
    private async Task<List<User>> FindAccountsAsync(
        PrivacySubjectKind kind, string value, Dictionary<string, List<User>> holdersByEmail)
    {
        if (kind == PrivacySubjectKind.UserId)
        {
            var account = await _userRepository.GetByUserIdAsync(value);
            return account == null ? [] : [account];
        }

        if (kind == PrivacySubjectKind.ParticipantId && !value.Contains('@'))
        {
            return [];
        }

        var accounts = await HoldersAsync(value.ToLowerInvariant(), holdersByEmail);
        return accounts.OrderBy(account => account.CreatedAt).ThenBy(account => account.UserId, StringComparer.Ordinal).ToList();
    }

    private async Task<List<User>> HoldersAsync(string email, Dictionary<string, List<User>> holdersByEmail)
    {
        if (!holdersByEmail.TryGetValue(email, out var holders))
        {
            holders = await _userRepository.GetAllByUserEmailAsync(email);
            holdersByEmail[email] = holders;
        }

        return holders;
    }

    // With no account the input is a participant ID only, as for Slack, Teams, webhook and API-key participants.
    private static void AddBareIdentifier(
        PrivacySubjectKind kind, string value, HashSet<string> participantIds, HashSet<string> emails)
    {
        var normalised = value.ToLowerInvariant();
        participantIds.Add(normalised);

        if (kind == PrivacySubjectKind.Email || normalised.Contains('@'))
        {
            emails.Add(normalised);
        }
    }

    // Threads can live under the user ID (the legacy participant ID), the email, or the provider subject, which the
    // server uses while the email is blank or shared. Whether the email was shared can change over time, so the
    // subject is always included.
    private async Task AddAccountIdentifiersAsync(
        User account,
        IReadOnlyCollection<string> subjectUserIds,
        Dictionary<string, List<User>> holdersByEmail,
        HashSet<string> participantIds,
        HashSet<string> emails)
    {
        participantIds.Add(account.UserId.ToLowerInvariant());

        var providerSubject = ProviderSubject(account.UserId);
        if (providerSubject != null)
        {
            participantIds.Add(providerSubject.ToLowerInvariant());
        }

        var email = account.Email?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(email))
        {
            return;
        }

        // An address another person's account also holds would reach that person's threads and invitations.
        var holders = await HoldersAsync(email, holdersByEmail);
        if (holders.All(holder => subjectUserIds.Contains(holder.UserId)))
        {
            emails.Add(email);
            participantIds.Add(email);
        }
    }

    // Null for user IDs that are not "provider|subject", such as GUIDs. Splits at the first separator because a
    // subject can contain one, for example Auth0's "auth0|123".
    private static string? ProviderSubject(string userId)
    {
        var separator = userId.IndexOf('|');
        return separator > 0 && separator < userId.Length - 1 ? userId[(separator + 1)..] : null;
    }
}
