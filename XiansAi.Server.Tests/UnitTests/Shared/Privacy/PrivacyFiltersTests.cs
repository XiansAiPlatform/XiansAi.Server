using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using Shared.Data.Models;
using Shared.Services.Privacy;
using Xunit;

namespace Tests.UnitTests.Shared.Privacy;

public class PrivacyFiltersTests
{
    private static string Render(FilterDefinition<BsonDocument> filter) =>
        filter.Render(new RenderArgs<BsonDocument>(
            BsonSerializer.SerializerRegistry.GetSerializer<BsonDocument>(), BsonSerializer.SerializerRegistry)).ToString();

    [Fact]
    public void InScope_IsNullWithoutTenants()
    {
        Assert.Null(PrivacyFilters.InScope("tenant_id", new PrivacySubject(), new PrivacyScope { AllTenants = true }));
    }

    [Fact]
    public void InScope_ReachesDocumentsWithNoTenantOnlyForAllTenantRequests()
    {
        var subject = new PrivacySubject { TenantIds = ["acme"] };

        var scoped = Render(PrivacyFilters.InScope("tenant_id", subject, new PrivacyScope())!);
        var allTenants = Render(PrivacyFilters.InScope("tenant_id", subject, new PrivacyScope { AllTenants = true })!);

        Assert.DoesNotContain("null", scoped);
        Assert.Contains("null", allTenants);
    }

    [Fact]
    public void AnyOf_IsNullWithoutUsableValues()
    {
        Assert.Null(PrivacyFilters.AnyOf("participant_id", []));
        Assert.Null(PrivacyFilters.AnyOf("participant_id", ["", "  "]));
    }

    [Fact]
    public void All_IsNullWhenAnyPartIsMissing()
    {
        Assert.Null(PrivacyFilters.All(PrivacyFilters.AnyOf("a", ["1"]), null));
    }

    [Fact]
    public void Either_UsesThePartsThatExist()
    {
        Assert.Null(PrivacyFilters.Either(null, null));
        Assert.Equal("{ \"a\" : { \"$in\" : [\"1\"] } }", Render(PrivacyFilters.Either(null, PrivacyFilters.AnyOf("a", ["1"]))!));
    }

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

    [Fact]
    public void PersonIds_TakeTheProviderSubjectAfterTheFirstPipe()
    {
        var subject = new PrivacySubject { UserIds = ["provider|sub|part"] };

        Assert.Equal(["provider|sub|part", "sub|part"], PrivacyFilters.PersonIds(subject));
    }
}
