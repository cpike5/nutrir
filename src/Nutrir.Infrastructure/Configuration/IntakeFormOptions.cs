namespace Nutrir.Infrastructure.Configuration;

public class IntakeFormOptions
{
    public const string SectionName = "IntakeForm";

    public int ExpiryDays { get; set; } = 7;

    public string ConsentPolicyVersion { get; set; } = "1.0";
}
