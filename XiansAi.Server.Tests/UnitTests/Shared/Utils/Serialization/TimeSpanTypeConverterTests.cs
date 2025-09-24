using Shared.Utils.Serialization;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Tests.UnitTests.Shared.Utils.Serialization;

public class TimeSpanTypeConverterTests
{
    private readonly TimeSpanTypeConverter _converter = new();
    
    private readonly ISerializer _serializer = new SerializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .WithTypeConverter(new TimeSpanTypeConverter())
        .Build();
    
    private readonly IDeserializer _deserializer = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .WithTypeConverter(new TimeSpanTypeConverter())
        .Build();
    
    public static IEnumerable<object[]> ValidTimeSpanData => new List<object[]>
    {
        new object[] { "30d", TimeSpan.FromDays(30) },
        new object[] { "1w", TimeSpan.FromDays(7) },
        new object[] { "24h", TimeSpan.FromHours(24) },
        new object[] { "60m", TimeSpan.FromMinutes(60) },
        new object[] { "3600s", TimeSpan.FromSeconds(3600) },
        new object[] { "1d 12h", TimeSpan.FromDays(1) + TimeSpan.FromHours(12) },
        new object[] { "2w 3d 6h", TimeSpan.FromDays(17) + TimeSpan.FromHours(6) },
        new object[] { "10m 30s", TimeSpan.FromMinutes(10) + TimeSpan.FromSeconds(30) },
    };

    public static IEnumerable<object[]> ValidTimeSpanSerializationData => new List<object[]>
    {
        new object[] { TimeSpan.FromDays(30), "time_to_live: 30d" },
        new object[] { TimeSpan.FromDays(7), "time_to_live: 7d" },
        new object[] { TimeSpan.FromHours(24), "time_to_live: 1d" },
        new object[] { TimeSpan.FromMinutes(60), "time_to_live: 1h" },
        new object[] { TimeSpan.FromSeconds(3600), "time_to_live: 1h" },
        new object[] { TimeSpan.FromDays(1) + TimeSpan.FromHours(12), "time_to_live: 1d 12h" },
        new object[] { TimeSpan.FromDays(17) + TimeSpan.FromHours(6), "time_to_live: 17d 6h" },
        new object[] { TimeSpan.FromMinutes(10) + TimeSpan.FromSeconds(30), "time_to_live: 10m 30s" },
        new object[] { TimeSpan.Zero, "time_to_live: 0s" },
    };

    [Theory]
    [MemberData(nameof(ValidTimeSpanData))]
    public void ReadYaml_ValidTimespan_ReturnsCorrectTimeSpan(string input, TimeSpan expected)
    {
        var result = DeserializeTimeSpan(input);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ReadYaml_NullOrEmptyTimespan_ReturnsNull()
    {
        Assert.Null(DeserializeTimeSpan("null"));
        Assert.Null(DeserializeTimeSpan("~"));
    }

    [Theory]
    [InlineData("10x")]
    [InlineData("1y 2d")]
    [InlineData("1 d")]
    [InlineData("d1")]
    [InlineData("1d 2")]
    [InlineData("1w 2x")]
    public void ReadYaml_InvalidTimespan_ThrowsFormatException(string input)
    {
        Assert.Throws<FormatException>(() => DeserializeTimeSpan(input));
    }

    [Theory]
    [MemberData(nameof(ValidTimeSpanSerializationData))]
    public void WriteYaml_ValidTimeSpan_SerializesCorrectly(TimeSpan input, string expectedYaml)
    {
        var yaml = _serializer.Serialize(new Foo<TimeSpan> { TimeToLive = input }).Trim();
        Assert.Equal(expectedYaml, yaml);
    }
    
    [Fact]
    public void WriteYaml_NullTimeSpan_SerializesToNull()
    {
        var yaml = _serializer.Serialize(new Foo<TimeSpan?> { TimeToLive = null }).Trim();
        Assert.Equal("time_to_live: null", yaml);
    }

    [Fact]
    public void WriteYaml_NegativeTimeSpan_ThrowsException()
    {
        var negativeTimeSpan = new Foo<TimeSpan> { TimeToLive = TimeSpan.FromSeconds(-1) };
        Assert.Throws<ArgumentOutOfRangeException>(() => _serializer.Serialize(negativeTimeSpan));
    }

    private object? DeserializeTimeSpan(string yaml)
    {
        var parser = new Parser(new StringReader(yaml));
        parser.Consume<StreamStart>();
        parser.Consume<DocumentStart>();
        var result = _converter.ReadYaml(parser, typeof(TimeSpan?));
        parser.Consume<DocumentEnd>();
        parser.Consume<StreamEnd>();
        return result;
    }
    
    private class Foo<T>
    {
        public T TimeToLive { get; init; } = default!;
    }

    [Fact]
    public void Deserialize_NullableTimeSpan_DeserializesCorrectly()
    {
        const string yaml = "time_to_live: 1d 2h";
        var result = _deserializer.Deserialize<Foo<TimeSpan?>>(yaml);

        Assert.Equal(TimeSpan.FromDays(1) + TimeSpan.FromHours(2), result.TimeToLive);
    }

    [Fact]
    public void Deserialize_Null_ToNullableTimeSpan_DeserializesCorrectly()
    {
        const string yaml = "time_to_live: null";
        var result = _deserializer.Deserialize<Foo<TimeSpan?>>(yaml);

        Assert.Null(result.TimeToLive);
    }

    [Fact]
    public void Deserialize_Null_ToStrictTimeSpan_DeserializesToZero()
    {
        const string yaml = "time_to_live: null";
        var result = _deserializer.Deserialize<Foo<TimeSpan>>(yaml);

        Assert.Equal(TimeSpan.Zero, result.TimeToLive);
    }
} 