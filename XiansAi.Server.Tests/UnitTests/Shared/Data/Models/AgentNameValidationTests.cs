using System.ComponentModel.DataAnnotations;
using System.Text;
using Shared.Data.Models;
using Shared.Data.Models.Validation;
using Xunit;

namespace Tests.UnitTests.Shared.Data.Models;

/// <summary>
/// Agent and related human-facing names must accept Norwegian/Unicode letters
/// while still rejecting markup and control characters.
/// </summary>
public class AgentNameValidationTests
{
    [Theory]
    [InlineData("Order Manager")]
    [InlineData("Kjøpsassistent")]
    [InlineData("Norge Agent")]
    [InlineData("ÆØÅ-agent")]
    [InlineData("søknad_v1")]
    [InlineData("Agent:Kjøp/Salg")]
    public void SanitizeAndValidateName_AcceptsUnicodeAndExistingAsciiNames(string agentName)
    {
        Assert.Equal(agentName, Agent.SanitizeAndValidateName(agentName));
    }

    [Theory]
    [InlineData("<script>")]
    [InlineData("agent\"name")]
    [InlineData("agent'name")]
    [InlineData("agent{name}")]
    [InlineData("agent;drop")]
    public void SanitizeAndValidateName_RejectsMarkupAndInjectionCharacters(string agentName)
    {
        var exception = Assert.Throws<ValidationException>(() => Agent.SanitizeAndValidateName(agentName));
        Assert.Contains("Invalid agent name format", exception.Message);
    }

    [Fact]
    public void SanitizeAndValidateName_RejectsBlankName()
    {
        Assert.Throws<ValidationException>(() => Agent.SanitizeAndValidateName("   "));
    }

    [Fact]
    public void SanitizeAndValidateName_NormalizesDecomposedNorwegianLettersToNfc()
    {
        // å as 'a' + combining ring (NFD) must store as the single å code point (NFC)
        var decomposed = "Kjøp" + "a\u030A";
        var normalized = Agent.SanitizeAndValidateName(decomposed);

        Assert.True(normalized.IsNormalized(NormalizationForm.FormC));
        Assert.Equal("Kjøpå".Normalize(NormalizationForm.FormC), normalized);
    }

    [Fact]
    public void AgentModel_AcceptsNorwegianNameOnFullValidation()
    {
        var agent = CreateAgent("Kjøpsassistent");

        var sanitized = agent.SanitizeAndValidate();

        Assert.Equal("Kjøpsassistent", sanitized.Name);
    }

    [Fact]
    public void AgentActivation_AcceptsNorwegianAgentAndActivationNames()
    {
        var activation = new AgentActivation
        {
            Id = "507f1f77bcf86cd799439011",
            Name = "Produksjon-kjøp",
            AgentName = "Kjøpsassistent",
            CreatedBy = "test-user",
            CreatedAt = DateTime.UtcNow,
            TenantId = "default"
        };

        var sanitized = activation.SanitizeAndValidate();

        Assert.Equal("Produksjon-kjøp", sanitized.Name);
        Assert.Equal("Kjøpsassistent", sanitized.AgentName);
    }

    [Theory]
    [InlineData("KjøpsFlyt")]
    [InlineData("Søknad Workflow")]
    public void SanitizeAndValidateType_AcceptsUnicodeWorkflowTypes(string workflowType)
    {
        Assert.Equal(workflowType, FlowDefinition.SanitizeAndValidateType(workflowType));
    }

    [Theory]
    [InlineData("Kjøpsassistent")]
    [InlineData("Norge Agent")]
    public void SafeTqlValue_AllowsUnicodeAgentNames(string agentName)
    {
        Assert.True(ValidationHelpers.IsValidPattern(agentName, ValidationHelpers.Patterns.SafeTqlValue));
    }

    [Theory]
    [InlineData("agent'name")]
    [InlineData("x AND TenantId = y")]
    public void SafeTqlValue_StillRejectsTqlInjection(string value)
    {
        Assert.False(ValidationHelpers.IsValidPattern(value, ValidationHelpers.Patterns.SafeTqlValue));
    }

    [Fact]
    public void SanitizeString_DoesNotChangeAsciiNames()
    {
        Assert.Equal("Order Manager", ValidationHelpers.SanitizeString("Order Manager"));
    }

    private static Agent CreateAgent(string name)
    {
        return new Agent
        {
            Id = "507f1f77bcf86cd799439011",
            Name = name,
            Tenant = "default",
            OriginTenant = "default",
            CreatedBy = "test-user",
            CreatedAt = DateTime.UtcNow
        };
    }
}
