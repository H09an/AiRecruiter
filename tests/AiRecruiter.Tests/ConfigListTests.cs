using AiRecruiter.Configuration;
using Microsoft.Extensions.Configuration;

namespace AiRecruiter.Tests;

/// <summary>
/// Чтение списка строк из конфигурации: порядок, trim, отбрасывание пустых.
/// </summary>
public class ConfigListTests
{
    private static readonly (string Key, string? Value)[] ListEntries =
    {
        ("Filters:ExcludeTitleWords:0", "senior"),
        ("Filters:ExcludeTitleWords:1", "lead"),
        ("Filters:ExcludeTitleWords:2", "intern"),
    };

    private static readonly (string Key, string? Value)[] OtherSectionEntries =
    {
        ("Other:Key", "value"),
    };

    private static readonly (string Key, string? Value)[] EntriesWithBlanks =
    {
        ("Filters:ExcludeTitleWords:0", "   "),
        ("Filters:ExcludeTitleWords:1", "senior"),
        ("Filters:ExcludeTitleWords:2", ""),
    };

    private static readonly (string Key, string? Value)[] EntriesWithPadding =
    {
        ("Filters:ExcludeTitleWords:0", "  senior  "),
        ("Filters:ExcludeTitleWords:1", "\tlead\t"),
    };

    private static readonly string[] ExpectedSeniorLeadIntern = { "senior", "lead", "intern" };
    private static readonly string[] ExpectedSenior = { "senior" };
    private static readonly string[] ExpectedSeniorLead = { "senior", "lead" };

    private static IConfiguration Build(params (string Key, string? Value)[] entries) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(entries.Select(e => new KeyValuePair<string, string?>(e.Key, e.Value)))
            .Build();

    [Fact]
    public void Read_List_ReturnsValuesInOrder()
    {
        var config = Build(ListEntries);

        Assert.Equal(ExpectedSeniorLeadIntern, StringListConfigReader.Read(config, "Filters:ExcludeTitleWords"));
    }

    [Fact]
    public void Read_MissingSection_ReturnsEmpty()
    {
        var config = Build(OtherSectionEntries);

        Assert.Empty(StringListConfigReader.Read(config, "Filters:ExcludeTitleWords"));
    }

    [Fact]
    public void Read_EmptyAndWhitespaceValues_Dropped()
    {
        var config = Build(EntriesWithBlanks);

        Assert.Equal(ExpectedSenior, StringListConfigReader.Read(config, "Filters:ExcludeTitleWords"));
    }

    [Fact]
    public void Read_ValuesTrimmed()
    {
        var config = Build(EntriesWithPadding);

        Assert.Equal(ExpectedSeniorLead, StringListConfigReader.Read(config, "Filters:ExcludeTitleWords"));
    }

    [Fact]
    public void Read_NullConfig_ReturnsEmpty()
    {
        Assert.Empty(StringListConfigReader.Read(null, "Filters:ExcludeTitleWords"));
    }
}
