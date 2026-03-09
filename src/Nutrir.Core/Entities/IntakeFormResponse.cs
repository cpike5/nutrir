namespace Nutrir.Core.Entities;

public class IntakeFormResponse
{
    public int Id { get; set; }

    public int IntakeFormId { get; set; }

    public string SectionKey { get; set; } = string.Empty;

    public string FieldKey { get; set; } = string.Empty;

    public string Value { get; set; } = string.Empty;
}
