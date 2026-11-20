namespace Shared.Services.Privacy;

/// <summary>Operator-approved information supplied for one access response.</summary>
public sealed record PrivacyExportDisclosure(
    string Controller,
    string Contact,
    string Purposes,
    string DataCategories,
    string Recipients,
    string Retention,
    string DataSources,
    string AutomatedDecisions,
    string InternationalTransfers,
    string ComplaintAuthority)
{
    public void Validate()
    {
        if (new[] { Controller, Contact, Purposes, DataCategories, Recipients, Retention,
            DataSources, AutomatedDecisions, InternationalTransfers, ComplaintAuthority }
            .Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("Every privacy export disclosure field is required.");
        }
    }
}
