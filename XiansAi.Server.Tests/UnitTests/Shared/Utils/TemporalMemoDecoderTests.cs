using Google.Protobuf;
using Shared.Utils.Temporal;
using Temporalio.Api.Common.V1;
using Temporalio.Converters;

namespace Tests.UnitTests.Shared.Utils;

public class TemporalMemoDecoderTests
{
    private static IEncodedRawValue Encoded(object? value) =>
        new EncodedRawValue(DataConverter.Default, DataConverter.Default.PayloadConverter.ToPayload(value));

    private static IEncodedRawValue RawText(string text) =>
        new EncodedRawValue(DataConverter.Default, new Payload { Data = ByteString.CopyFromUtf8(text) });

    [Fact]
    public void TemporalEscapesNonAsciiCharacters_InStoredMemoValues()
    {
        var stored = Encoded("LDF ågent").Payload.Data.ToStringUtf8();

        Assert.Equal("\"LDF \\u00E5gent\"", stored);
    }

    [Theory]
    [InlineData("LDF ågent")]
    [InlineData("Kjøpsassistent")]
    [InlineData("Blåbærsyltetøy Æ Ø Å")]
    [InlineData("plain-ascii")]
    [InlineData("")]
    public void Decode_ReturnsTheOriginalText(string original)
    {
        Assert.Equal(original, TemporalMemoDecoder.Decode(Encoded(original)));
    }

    [Fact]
    public void Decode_KeepsQuotesAndBackslashesThatArePartOfTheValue()
    {
        const string original = "Review \"Q4\" at C:\\reports";

        Assert.Equal(original, TemporalMemoDecoder.Decode(Encoded(original)));
    }

    [Fact]
    public void Decode_KeepsCommaSeparatedActionsIntact()
    {
        Assert.Equal("Godkjenn,Avslå", TemporalMemoDecoder.Decode(Encoded("Godkjenn,Avslå")));
    }

    [Theory]
    [InlineData(true, "true")]
    [InlineData(false, "false")]
    public void Decode_ReturnsBooleansAsText(bool value, string expected)
    {
        Assert.Equal(expected, TemporalMemoDecoder.Decode(Encoded(value)));
    }

    [Fact]
    public void Decode_ReturnsNull_ForJsonNull()
    {
        Assert.Null(TemporalMemoDecoder.Decode(RawText("null")));
    }

    [Fact]
    public void Decode_ReturnsNull_ForMissingValue()
    {
        Assert.Null(TemporalMemoDecoder.Decode(null));
    }

    [Fact]
    public void Decode_FallsBackToRawText_WhenPayloadIsNotJson()
    {
        Assert.Equal("not json", TemporalMemoDecoder.Decode(RawText("not json")));
    }

    [Fact]
    public void Decode_ReturnsEmptyString_ForEmptyPayload()
    {
        Assert.Equal(string.Empty, TemporalMemoDecoder.Decode(RawText(string.Empty)));
    }

    [Fact]
    public void GetString_ReturnsTheDecodedValue_ForAnExistingKey()
    {
        var memo = new Dictionary<string, IEncodedRawValue> { ["idPostfix"] = Encoded("LDF ågent") };

        Assert.Equal("LDF ågent", TemporalMemoDecoder.GetString(memo, "idPostfix"));
    }

    [Fact]
    public void GetString_ReturnsNull_WhenTheKeyIsAbsent()
    {
        Assert.Null(TemporalMemoDecoder.GetString(new Dictionary<string, IEncodedRawValue>(), "idPostfix"));
    }
}
