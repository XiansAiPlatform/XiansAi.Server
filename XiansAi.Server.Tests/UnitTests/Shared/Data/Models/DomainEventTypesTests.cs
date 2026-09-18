using System.Reflection;
using Shared.Data.Models;

namespace Tests.UnitTests.Shared.Data.Models;

public class DomainEventTypesTests
{
    [Fact]
    public void Describe_CoversEveryEventTypeConstant()
    {
        var constants = typeof(DomainEventTypes)
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
            .Where(field => field.IsLiteral && !field.IsInitOnly && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!);

        foreach (var eventType in constants)
        {
            Assert.False(
                string.IsNullOrWhiteSpace(DomainEventTypes.Describe(eventType)),
                $"DomainEventTypes.Describe has no sentence for '{eventType}'.");
        }
    }

    [Fact]
    public void Describe_ReturnsNull_ForUnknownEventType()
    {
        Assert.Null(DomainEventTypes.Describe("not.a.real.event"));
    }
}
