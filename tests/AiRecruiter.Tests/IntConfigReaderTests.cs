using AiRecruiter.Configuration;
using Microsoft.Extensions.Configuration;

namespace AiRecruiter.Tests;

public class IntConfigReaderTests
{
    [Theory]
    [InlineData("5", 10, 5)]
    [InlineData("42", 0, 42)]
    public void Read_ReturnsValue_WhenKeyIsValid(string rawValue, int defaultValue, int expected)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Test:Key"] = rawValue,
        }).Build();

        var result = IntConfigReader.Read(config, "Test:Key", defaultValue);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Read_ReturnsDefault_WhenKeyIsMissing(string rawValue)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Test:Key"] = rawValue,
        }).Build();

        var result = IntConfigReader.Read(config, "Test:Key", 42);

        Assert.Equal(42, result);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("12.5")]
    [InlineData("")]
    public void Read_ReturnsDefault_WhenValueIsNotInteger(string rawValue)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Test:Key"] = rawValue,
        }).Build();

        var result = IntConfigReader.Read(config, "Test:Key", 99);

        Assert.Equal(99, result);
    }

    [Fact]
    public void Read_ReturnsDefault_WhenConfigIsNull()
    {
        var result = IntConfigReader.Read(null, "Test:Key", 7);

        Assert.Equal(7, result);
    }
}
