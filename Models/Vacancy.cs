namespace AiRecruiter.Models;

/// <summary>
/// Represents a single vacancy found on hh.ru.
/// </summary>
public class Vacancy
{
    public string Title { get; set; } = string.Empty;
    public string Company { get; set; } = string.Empty;
    public string Salary { get; set; } = string.Empty;
    public string Link { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    public string Experience { get; set; } = string.Empty;
    public string WorkFormat { get; set; } = string.Empty;
    public string Schedule { get; set; } = string.Empty;
    public string Employment { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public string HiringFormats { get; set; } = string.Empty;
    public string Requirements { get; set; } = string.Empty;
    public string Responsibilities { get; set; } = string.Empty;
    public string Conditions { get; set; } = string.Empty;
    public string PublishedAt { get; set; } = string.Empty;
    public List<string> KeySkills { get; set; } = new();

    public const string DescriptionNotLoaded = "Описание не загружено";

    public bool HasDescription =>
        !string.IsNullOrWhiteSpace(Description) && Description != DescriptionNotLoaded;

    public bool HasStructuredSections =>
        !string.IsNullOrWhiteSpace(Requirements)
        || !string.IsNullOrWhiteSpace(Responsibilities)
        || !string.IsNullOrWhiteSpace(Conditions);

    public override string ToString() =>
        $"{Title} | {Company} | {Salary} | {Link}";
}
