using Microsoft.Extensions.Options;
using Nutrir.Core.Interfaces;
using Nutrir.Core.Models;
using Nutrir.Infrastructure.Configuration;

namespace Nutrir.Infrastructure.Services;

public class DefaultConsentFormTemplate : IConsentFormTemplate
{
    private readonly ConsentFormOptions _options;

    public DefaultConsentFormTemplate(IOptions<ConsentFormOptions> options)
    {
        _options = options.Value;
    }

    public string Version => "1.0";

    public ConsentFormContent Generate(string clientName, string practitionerName, DateTime date)
    {
        return new ConsentFormContent
        {
            Title = "Consent for Nutrition Counseling Services & Privacy Notice",
            PracticeName = _options.PracticeName,
            FormVersion = Version,
            ClientName = clientName,
            PractitionerName = practitionerName,
            Date = date,
            Sections = BuildSections(),
            SignatureBlockText = "By signing below, I acknowledge that I have read and understood this consent form in its entirety. I voluntarily consent to receive nutrition counseling services and agree to the collection, use, and disclosure of my personal information as described above."
        };
    }

    private List<ConsentSection> BuildSections()
    {
        return
        [
            new ConsentSection
            {
                Heading = "1. Nutrition Counseling Services",
                Paragraphs =
                [
                    $"{_options.PracticeName} provides nutrition counseling, meal planning, and dietary guidance services. These services are provided by a Registered Dietitian or qualified nutrition professional.",
                    "I understand that nutrition counseling is not a substitute for medical advice, diagnosis, or treatment. I agree to inform my healthcare provider about the nutrition services I am receiving.",
                    "I understand that results may vary and that adherence to recommended plans is my responsibility. The practitioner will make reasonable efforts to provide evidence-based guidance tailored to my needs."
                ]
            },
            new ConsentSection
            {
                Heading = "2. Scope of Practice",
                Paragraphs =
                [
                    "The nutrition professional will provide services within their regulated scope of practice. Services may include nutritional assessment, dietary counseling, meal planning, and ongoing progress monitoring.",
                    "The practitioner will not diagnose or treat medical conditions, prescribe medications, or provide services outside their professional scope. If concerns arise that require medical attention, I will be referred to an appropriate healthcare provider."
                ]
            },
            new ConsentSection
            {
                Heading = "3. Privacy & Data Protection (PIPEDA Compliance)",
                Paragraphs =
                [
                    $"{_options.PracticeName} is committed to protecting your personal information in accordance with the Personal Information Protection and Electronic Documents Act (PIPEDA) and applicable provincial privacy legislation.",
                    "We collect personal information including your name, contact details, date of birth, health and dietary information, and progress measurements. This information is collected for the purpose of providing nutrition counseling services.",
                    "Your personal information will be stored securely using encryption at rest and in transit. Access is restricted to authorized practitioners and staff on a need-to-know basis.",
                    "We will not collect more personal information than is necessary for the identified purposes. Personal information will only be used for the purposes for which it was collected, or for a consistent purpose, unless you provide further consent."
                ]
            },
            new ConsentSection
            {
                Heading = "4. Third-Party Disclosure",
                Paragraphs =
                [
                    "Your personal information will not be shared with third parties without your explicit consent, except where required by law or regulation.",
                    "In the event that disclosure is necessary for referral to another healthcare provider, we will seek your consent before sharing any personal health information.",
                    "We may use anonymized, de-identified data for quality improvement and practice analytics. This data cannot be used to identify you."
                ]
            },
            new ConsentSection
            {
                Heading = "5. Right to Withdraw Consent",
                Paragraphs =
                [
                    "You have the right to withdraw your consent at any time by notifying your practitioner in writing. Withdrawal of consent may limit our ability to continue providing nutrition counseling services.",
                    "Withdrawal of consent does not affect the legality of information collected prior to withdrawal. Records created during the period of active consent will be retained in accordance with our data retention policy."
                ]
            },
            new ConsentSection
            {
                Heading = "6. Data Retention",
                Paragraphs =
                [
                    "Your personal information will be retained for a minimum period as required by applicable professional regulatory requirements and provincial health records legislation.",
                    "After the retention period, your personal information will be securely destroyed. You may request early deletion of your records, subject to legal and regulatory retention requirements."
                ]
            },
            new ConsentSection
            {
                Heading = "7. Access & Correction Rights",
                Paragraphs =
                [
                    "You have the right to access your personal information held by this practice. Requests for access can be made to your practitioner and will be responded to within 30 days.",
                    "You have the right to request correction of any inaccuracies in your personal information. If a correction request is not resolved to your satisfaction, you may file a complaint with the Office of the Privacy Commissioner of Canada."
                ]
            },
            new ConsentSection
            {
                Heading = "8. Electronic Records",
                Paragraphs =
                [
                    $"{_options.PracticeName} maintains electronic health records for the provision of nutrition counseling services. These records are stored securely and access is audited.",
                    "By consenting to electronic record-keeping, you acknowledge that your information will be stored digitally. You may request a printed copy of your records at any time."
                ]
            },
            new ConsentSection
            {
                Heading = "9. Questions & Complaints",
                Paragraphs =
                [
                    "If you have questions about this consent form or our privacy practices, please contact your practitioner directly.",
                    "If you are not satisfied with how your personal information is being handled, you may file a complaint with the Office of the Privacy Commissioner of Canada at www.priv.gc.ca."
                ]
            }
        ];
    }
}
