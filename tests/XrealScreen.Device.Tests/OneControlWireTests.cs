using XrealScreen.Device.XrealOne;

namespace XrealScreen.Device.Tests;

public class OneControlWireTests
{
    [Fact]
    public void GetPropertyRequest_MatchesReference()
    {
        Assert.Equal(new byte[] { 0x18, 0x00 }, OneControlWire.EncodeGetPropertyRequest());
    }

    [Fact]
    public void SetNumericRequest_MatchesReference()
    {
        Assert.Equal(new byte[] { 0x1A, 0x02, 0x08, 0x00 }, OneControlWire.EncodeSetNumericPropertyRequest(0));
        Assert.Equal(new byte[] { 0x1A, 0x02, 0x08, 0x09 }, OneControlWire.EncodeSetNumericPropertyRequest(9));
        Assert.Equal(new byte[] { 0x1A, 0x03, 0x08, 0x80, 0x01 }, OneControlWire.EncodeSetNumericPropertyRequest(128));
    }

    [Fact]
    public void RequestFrame_MarksTheTransactionId()
    {
        byte[] packet = OneControlWire.EncodeRequest(0x271D, 1, OneControlWire.EncodeGetPropertyRequest());
        Assert.Equal(new byte[] { 0x27, 0x1D, 0x00, 0x00, 0x00, 0x06, 0x80, 0x00, 0x00, 0x01, 0x18, 0x00 }, packet);
    }

    [Fact]
    public void StringResponse_DecodesTheValue()
    {
        byte[] payload = [0x22, 0x09, 0x12, 0x07, (byte)'o', (byte)'n', (byte)'e', (byte)'p', (byte)'r', (byte)'o', (byte)'x'];
        Assert.Equal("oneprox", OneControlWire.ParseStringPropertyResponse(payload));
    }

    [Fact]
    public void EmptyResponse_AcceptsSuccessAndRejectsAStatus()
    {
        OneControlWire.ParseEmptyPropertyResponse([0x22, 0x00]);
        OneControlWire.ParseEmptyPropertyResponse([0x22, 0x02, 0x08, 0x00]);
        Assert.Throws<InvalidOperationException>(() => OneControlWire.ParseEmptyPropertyResponse([0x22, 0x03, 0x08, 0x91, 0x4E]));
    }
}
