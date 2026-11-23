using Shared.Data.Models;
using Shared.Services.Privacy;
using Xunit;

namespace Tests.UnitTests.Shared.Privacy;

/// <summary>
/// The filter builders are covered through the MongoDB sources. These tests cover the identifiers a person is
/// stored under, which those tests don't vary.
/// </summary>
public class PrivacyFiltersTests
{
    [Fact]
    public void PersonIds_IncludeUserIdsAndProviderSubjectsAsStored()
    {
        var subject = new PrivacySubject
        {
            UserIds = ["Auth0|AbC"],
            ParticipantIds = ["auth0|abc", "abc", "jane@example.com"]
        };

        Assert.Equal(["AbC", "Auth0|AbC", "abc", "auth0|abc", "jane@example.com"], PrivacyFilters.PersonIds(subject));
    }

    [Fact]
    public void PersonIds_HaveNoProviderSubjectWhenThePipeIsAtAnEnd()
    {
        var subject = new PrivacySubject { UserIds = ["|start", "end|"] };

        Assert.Equal(["end|", "|start"], PrivacyFilters.PersonIds(subject));
    }
}
