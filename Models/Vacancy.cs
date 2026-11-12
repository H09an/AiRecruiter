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

    public const string DescriptionNotLoaded = "Описание не загружено";

    public bool HasDescription =>
        !string.IsNullOrWhiteSpace(Description) && Description != DescriptionNotLoaded;

    public override string ToString() =>
        $"{Title} | {Company} | {Salary} | {Link}";
}
