# Breach Response Plan

> **Status**: Active
> **Last reviewed**: 2026-03-22
> **Next review**: 2027-03-22
> **Owner**: Practitioner (sole operator)
>
> This plan applies to Nutrir, a self-hosted nutrition practice management application for a solo Canadian dietitian. It covers all personal health information (PHI) stored in the system: client health profiles, appointment notes, meal plans, progress entries, consent records, and AI conversation data that references PHI.
>
> **Legal basis**: PIPEDA s.10.1, PHIPA s.12 (Ontario), HIA s.60 (Alberta), Breach of Security Safeguards Regulations (SOR/2018-64). See [privacy-research.md](privacy-research.md) for full legal analysis.

---

## 1. Definitions

**Breach of security safeguards** (PIPEDA s.10.1): The loss of, unauthorized access to, or unauthorized disclosure of personal information resulting from a breach of an organization's security safeguards, or from a failure to establish those safeguards.

**Real risk of significant harm (RROSH)**: The threshold under PIPEDA for mandatory notification. Significant harm includes bodily harm, humiliation, damage to reputation or relationships, loss of employment, financial loss, identity theft, negative effects on a credit record, and damage to or loss of property. Factors to consider: sensitivity of the information and the probability that it will be misused.

**PHI in Nutrir**: Client names, contact information, health histories, dietary restrictions, allergies, medications, medical conditions, meal plans, progress notes, appointment notes, consent records, and any AI conversation content referencing client health data.

---

## 2. Roles and Responsibilities

This plan is designed for a sole practitioner context. The practitioner is the primary responsible party for all steps. External contacts provide support.

| Role | Responsibility | Contact |
|------|---------------|---------|
| **Practitioner** (primary) | Detection, initial containment, assessment, notification, remediation, post-incident review. Decision authority for all steps. | _[Your name, phone, email]_ |
| **IT Support Contact** | Technical containment (server access, firewall changes, log analysis, backup restoration). Available within 4 hours of contact. | _[Name, phone, email, SLA]_ |
| **Legal Counsel Contact** | Advice on notification obligations, regulatory communications, liability assessment. | _[Name, phone, email]_ |

**Action item**: Fill in the contact details above and confirm availability commitments with each contact annually at the plan review date.

---

## 3. Phase 1 — Detection and Identification

### 3.1 Detection Sources

Breaches may be identified through any of the following channels:

| Source | What to Look For |
|--------|-----------------|
| **Audit log monitoring** | Unusual access patterns: access outside normal hours, bulk record views, access to records of clients not recently scheduled, repeated failed login attempts, MFA bypass attempts. Review via the AuditLogEntry table (`Timestamp`, `Action`, `EntityType`, `UserId`, `IpAddress`). |
| **System anomaly detection** | Unexpected server resource usage, unrecognized processes, modified files, failed SSH login attempts (check `auth.log`), database connections from unknown IPs. |
| **Application alerts** | Authentication failures logged by ASP.NET Identity, certificate expiration warnings, database connection errors from unexpected sources. |
| **User/client reports** | A client reports receiving information they should not have, or reports suspicious contact referencing their health data. |
| **External notification** | Notification from the VPS provider, a security researcher, law enforcement, or a regulator about a compromise. |
| **Self-discovery** | Practitioner discovers a misconfiguration, unpatched vulnerability, lost device, or accidental disclosure. |

### 3.2 Initial Assessment Checklist

Upon suspecting a breach, immediately document:

- [ ] Date and time the breach was discovered
- [ ] Date and time the breach is believed to have occurred (if different)
- [ ] How the breach was detected
- [ ] Type of breach: unauthorized access, unauthorized disclosure, loss, or theft
- [ ] What data may be affected (which clients, which data types)
- [ ] Whether the breach is ongoing or has been contained
- [ ] Who else has been informed

Record this information in writing (not in the compromised system if the system itself is suspect). Use a separate device or paper record if necessary.

---

## 4. Phase 2 — Containment

**Objective**: Stop the breach from continuing and preserve evidence for investigation.

### 4.1 Immediate Technical Steps

Execute the following in order, with IT support contact if needed:

1. **Isolate the affected system**
   - If the VPS is compromised: restrict network access via firewall rules (block all inbound except your known IP)
   - If a specific service is compromised: stop that service (`docker compose stop` for the application container)
   - Do NOT shut down the server entirely unless advised by IT support (this destroys volatile memory evidence)

2. **Revoke compromised credentials**
   - Change the practitioner account password immediately
   - Regenerate MFA secret if MFA may be compromised
   - Rotate database credentials (`POSTGRES_PASSWORD` in `.env`)
   - Rotate any API keys (Anthropic API key, SMTP credentials)
   - Revoke and regenerate SSH keys if SSH access is suspected

3. **Preserve evidence**
   - Take a snapshot/backup of the VPS in its current state before making changes (if the hosting provider supports this)
   - Export the audit log table: `pg_dump -t "AuditLogEntries" nutrir > audit_log_backup_$(date +%Y%m%d).sql`
   - Save relevant system logs: `/var/log/auth.log`, `/var/log/syslog`, Docker container logs
   - Screenshot or save any anomalous activity observed
   - Record the timeline of all containment actions taken

4. **Assess scope**
   - Query the audit log for the suspected breach period:
     - All record access events in the time window
     - All authentication events (logins, failures)
     - All data export events
     - Any actions from unrecognized IP addresses
   - Determine which client records were accessed or disclosed
   - Determine whether data was exfiltrated (exported, downloaded, or transmitted)

### 4.2 If the Breach Involves the AI Assistant

If client PHI may have been exposed through the Anthropic API connection (see [ai-conversation-data-policy.md](ai-conversation-data-policy.md)):

- Note that Anthropic's API data retention policy applies to data already transmitted
- Revoke and regenerate the Anthropic API key
- Review AI conversation logs for the breach period to identify what PHI was transmitted
- Contact Anthropic support if data deletion is required

### 4.3 Communication Hold

Do not make public statements about the breach until the 72-hour assessment (Phase 3) is complete. Inform only those who need to know for containment purposes.

---

## 5. Phase 3 — 72-Hour Assessment

**Legal requirement**: Under PIPEDA s.10.1 and the Breach of Security Safeguards Regulations (SOR/2018-64), you must assess whether the breach creates a real risk of significant harm (RROSH) to any individual. This assessment should be completed as soon as feasible.

Under PHIPA s.12(2), a health information custodian must notify the IPC "at the first reasonable opportunity" after a theft, loss, or unauthorized use or disclosure.

Under HIA s.60.1, notification to the OIPC is required without unreasonable delay.

### 5.1 RROSH Assessment

For each affected individual, assess the following factors:

**Sensitivity of the information involved:**

| Data Type | Sensitivity | RROSH Likelihood |
|-----------|------------|-----------------|
| Client name + contact info only | Medium | Moderate — depends on context |
| Health conditions, allergies, medications | High | High — health data is inherently sensitive |
| Dietary restrictions (medical) | High | High — may reveal health conditions |
| Meal plans (non-medical) | Low-Medium | Lower — unless tied to medical conditions |
| Progress notes with clinical observations | High | High |
| Consent records | Low | Low — administrative data |
| Full client profile (all fields) | Very High | Very High |

**Probability of misuse:**

- Was the data actually accessed or only potentially exposed?
- Was the data exfiltrated to an external party?
- Is the threat actor known or unknown?
- Was the data encrypted at rest (v2 field-level encryption)? If so, was the encryption key also compromised?
- How many individuals are affected?
- Is there evidence of malicious intent or was this accidental?

### 5.2 Assessment Decision

| Finding | Action Required |
|---------|----------------|
| RROSH exists for one or more individuals | Proceed to Phase 4 — Notification (mandatory) |
| RROSH does not exist | No mandatory notification, but document the assessment thoroughly. Still proceed with Phase 5 (Remediation). |
| Uncertain | Err on the side of notification. Consult legal counsel. |

### 5.3 Record-Keeping Requirement

Under SOR/2018-64 s.2, you must maintain a record of every breach of security safeguards for **24 months** after the day on which you determine that the breach has occurred. This record must contain sufficient information to enable the Privacy Commissioner to verify compliance. This applies regardless of whether RROSH is found.

The breach record must include:

- Date or estimated date of the breach
- A general description of the circumstances
- The nature of the information involved
- Whether notification was provided (and if not, the reasons)
- Whether a report was made to the Privacy Commissioner

Store breach records in a secure location outside the potentially compromised system (encrypted local storage or secure cloud document with Canadian residency).

---

## 6. Phase 4 — Notification Procedures

### 6.1 Regulator Notification

Notification is mandatory when RROSH is determined (PIPEDA) or when a breach of PHI occurs (PHIPA, HIA).

**Which regulator to notify depends on your province and applicable law:**

| If Your Practice Is In... | Primary Law | Regulator | Contact |
|--------------------------|-------------|-----------|---------|
| Any province (federal) | PIPEDA s.10.1 | Office of the Privacy Commissioner of Canada (OPC) | [PIPEDA breach report form](https://www.priv.gc.ca/en/report-a-concern/report-a-privacy-breach-at-your-organization/report-a-breach-of-security-safeguards/) |
| Ontario | PHIPA s.12 | Information and Privacy Commissioner of Ontario (IPC) | Phone: 416-326-3333 / 1-800-387-0073, [IPC breach reporting](https://www.ipc.on.ca/health/report-a-privacy-breach/) |
| Alberta | HIA s.60 | Office of the Information and Privacy Commissioner of Alberta (OIPC) | Phone: 780-422-6860 / 1-888-878-4044, [OIPC breach reporting](https://www.oipc.ab.ca/action-items/report-a-privacy-breach.aspx) |

**PIPEDA breach report contents** (per SOR/2018-64 s.1):

1. A description of the circumstances of the breach and, if known, the cause
2. The day on which, or period during which, the breach occurred, or if neither is known, the approximate period
3. A description of the personal information that is the subject of the breach
4. The number of individuals affected or, if unknown, the approximate number
5. A description of the steps the organization has taken to reduce the risk of harm to affected individuals or to mitigate that harm
6. A description of the steps the organization has taken or intends to take to notify affected individuals
7. The name and contact information of a person who can answer questions about the breach on behalf of the organization

### 6.2 Individual Notification

When RROSH exists, affected individuals must be notified directly (PIPEDA s.10.1(3)). Under PHIPA, notification is required at the first reasonable opportunity.

**Notification must contain** (per SOR/2018-64 s.4):

- A description of the circumstances of the breach
- The day or period on which the breach occurred
- A description of the personal information involved
- A description of what the organization has done or will do to reduce risk of harm
- A description of what the individual can do to reduce risk of harm or mitigate harm
- A toll-free number or email address the individual can use to obtain further information
- Information about the individual's right to file a complaint with the Privacy Commissioner

**Method**: Direct notification (email, phone call, or letter). If direct notification would cause further harm, indirect notification (website notice, public announcement) may be used, but only if approved by the Privacy Commissioner.

### 6.3 Individual Notification Template

```
Subject: Important Notice About Your Personal Health Information

Dear [Client Name],

I am writing to inform you of a privacy incident involving your personal
health information held by my nutrition practice.

WHAT HAPPENED
[Describe the breach in plain language — e.g., "On [date], I discovered that
an unauthorized party may have accessed the computer system where your health
records are stored."]

WHAT INFORMATION WAS INVOLVED
[List the specific types of information — e.g., "Your name, contact information,
health history, dietary records, and appointment notes."]

WHAT I AM DOING ABOUT IT
[Describe steps taken — e.g., "I have secured the system, changed all access
credentials, engaged IT security support, and reported this incident to the
[OPC / IPC / OIPC]."]

WHAT YOU CAN DO
- Monitor your accounts and personal information for any unusual activity
- Consider placing a fraud alert with Canada's credit bureaus (Equifax Canada:
  1-800-465-7166, TransUnion Canada: 1-800-663-9980) if financial information
  was involved
- Be cautious of unsolicited communications that reference your health information

FOR MORE INFORMATION
If you have questions, please contact me at:
  [Phone number]
  [Email address]

You also have the right to file a complaint with the Office of the Privacy
Commissioner of Canada:
  Website: https://www.priv.gc.ca
  Phone: 1-800-282-1376

[If Ontario, also include:]
Information and Privacy Commissioner of Ontario:
  Website: https://www.ipc.on.ca
  Phone: 1-800-387-0073

[If Alberta, also include:]
Office of the Information and Privacy Commissioner of Alberta:
  Website: https://www.oipc.ab.ca
  Phone: 1-888-878-4044

I sincerely regret this incident and am taking all necessary steps to prevent
it from happening again.

[Your name]
[Practice name]
[Date]
```

### 6.4 Notification Timeline

| Framework | Timeline |
|-----------|----------|
| PIPEDA | "As soon as feasible" after determination that RROSH exists. No fixed deadline in the statute, but the OPC expects prompt action. |
| PHIPA (Ontario) | "At the first reasonable opportunity" (s.12(2)). The IPC expects notification without delay. |
| HIA (Alberta) | "Without unreasonable delay" (s.60.1(2)). |

**Practical guidance**: Begin preparing notifications immediately upon RROSH determination. Aim to notify regulators within 72 hours of determination and affected individuals as soon as possible thereafter.

---

## 7. Phase 5 — Remediation and Recovery

### 7.1 Technical Remediation

Address the root cause of the breach:

- [ ] Patch the vulnerability or close the attack vector that was exploited
- [ ] Apply all pending security updates to the host OS, Docker images, and application dependencies
- [ ] Review and harden firewall rules (only ports 443 and SSH should be exposed)
- [ ] Verify TLS certificate validity and HSTS configuration
- [ ] Verify MFA is enforced and functioning
- [ ] Review SSH configuration (key-only authentication, non-standard port)
- [ ] If database was accessed: review database user permissions, change all credentials
- [ ] Run a malware/rootkit scan on the VPS
- [ ] Restore from a known-good backup if the system integrity cannot be verified

### 7.2 Application-Level Remediation

- [ ] Review audit log coverage: were there gaps that prevented earlier detection?
- [ ] Verify soft-delete integrity: ensure no hard-deletes occurred outside the purge workflow
- [ ] Review global query filters: confirm soft-deleted records are properly excluded
- [ ] If field-level encryption (v2) was in place: rotate encryption keys
- [ ] If AI conversations contained PHI related to the breach: review retention and consider purging affected conversations
- [ ] Review consent records for affected clients: determine if re-consent is needed

### 7.3 Policy Updates

- [ ] Update this breach response plan based on lessons learned
- [ ] Update the privacy policy if the breach reveals a gap in how data handling is described
- [ ] Review and update the [AI Conversation Data Policy](ai-conversation-data-policy.md) if the AI assistant was involved
- [ ] Document any new security controls added

### 7.4 Enhanced Monitoring

For 90 days following the breach:

- [ ] Increase audit log review frequency (daily review of access patterns)
- [ ] Monitor for signs of the same attack vector being exploited again
- [ ] Monitor for unusual client contact (clients receiving unsolicited communications referencing their health data)

---

## 8. Phase 6 — Post-Incident Review

Conduct a formal review within 30 days of breach resolution.

### 8.1 Root Cause Analysis

Document the following:

1. **Timeline**: Detailed chronology from initial compromise to full containment and recovery
2. **Root cause**: The specific vulnerability, failure, or action that caused the breach
3. **Contributing factors**: Conditions that enabled the breach or delayed detection (e.g., missing monitoring, delayed patching, configuration error)
4. **Detection gap**: Time between breach occurrence and detection. What could have shortened this?
5. **Response effectiveness**: What worked well in the response? What caused delays?

### 8.2 Lessons Learned

For each finding, document:

| Finding | Corrective Action | Owner | Deadline |
|---------|-------------------|-------|----------|
| _[e.g., SSH was using password auth]_ | _[Switch to key-only auth]_ | _[Practitioner / IT support]_ | _[Date]_ |

### 8.3 Plan Updates

Based on the review:

- [ ] Update this breach response plan with any procedural improvements
- [ ] Update the compliance requirements document if new technical controls are needed
- [ ] Update contact information if any contacts were unreachable or unresponsive
- [ ] Schedule any additional training or process changes

### 8.4 Documentation Retention

The complete post-incident review, including all breach records, the RROSH assessment, notification copies, and the root cause analysis, must be retained for a minimum of **24 months** (SOR/2018-64 s.2) and ideally for the full data retention period (7 years) given the health data context.

---

## 9. Annual Review Schedule

This plan must be reviewed annually and whenever a breach occurs.

| Review Activity | Frequency | Next Due |
|----------------|-----------|----------|
| Full plan review and update | Annual | 2027-03-22 |
| Verify contact information (IT support, legal counsel) | Annual | 2027-03-22 |
| Confirm regulator contact details and reporting URLs are current | Annual | 2027-03-22 |
| Test containment procedures (simulated incident) | Annual | 2027-03-22 |
| Review audit log monitoring practices | Quarterly | 2026-06-22 |
| Verify backup restoration capability | Quarterly | 2026-06-22 |

### Sign-Off

| Date | Reviewed By | Notes |
|------|------------|-------|
| 2026-03-22 | _[Practitioner name]_ | Initial version |

---

## Appendix A — Quick Reference Card

Print this page and keep it accessible offline.

```
BREACH DETECTED — IMMEDIATE STEPS

1. STOP the breach (isolate system, revoke credentials)
2. PRESERVE evidence (snapshot, export logs)
3. ASSESS scope (which clients, which data)
4. ASSESS RROSH (is there real risk of significant harm?)
5. NOTIFY regulator if RROSH exists
6. NOTIFY affected individuals
7. REMEDIATE root cause
8. REVIEW and update this plan

KEY CONTACTS

Practitioner:    [phone] [email]
IT Support:      [phone] [email]
Legal Counsel:   [phone] [email]

OPC:  1-800-282-1376 / priv.gc.ca
IPC Ontario: 1-800-387-0073 / ipc.on.ca
OIPC Alberta: 1-888-878-4044 / oipc.ab.ca
```

---

## Appendix B — Breach Record Template

Use this template to document each breach (mandatory under SOR/2018-64 s.2).

```
BREACH RECORD

Breach ID:           [YYYY-MM-DD-NNN]
Date discovered:     [date]
Date occurred:       [date or estimated range]
Discovered by:       [name / method]

Description of circumstances:
[free text]

Information involved:
[list data types and fields]

Number of individuals affected: [count]

RROSH determination:  [ ] Yes  [ ] No
RROSH rationale:
[free text]

Regulator notified:   [ ] Yes  [ ] No  [ ] N/A
  - Which regulator:  [OPC / IPC / OIPC]
  - Date notified:    [date]
  - Reference number: [if assigned]

Individuals notified: [ ] Yes  [ ] No  [ ] N/A
  - Date notified:    [date]
  - Method:           [email / letter / phone]

Containment steps taken:
[list]

Remediation steps taken:
[list]

Post-incident review completed: [ ] Yes  Date: [date]

Record retention expires: [date — minimum 24 months from determination date]
```

---

## References

- [PIPEDA s.10.1 — Breach of security safeguards](https://laws-lois.justice.gc.ca/eng/acts/p-8.6/page-2.html#h-417878)
- [Breach of Security Safeguards Regulations (SOR/2018-64)](https://laws-lois.justice.gc.ca/eng/regulations/SOR-2018-64/)
- [PHIPA s.12 — Health information custodian obligations](https://www.ontario.ca/laws/statute/04p03#BK16)
- [HIA s.60 — Duty to notify Commissioner](https://www.qp.alberta.ca/documents/Acts/H05.pdf)
- [OPC guidance — What you need to know about mandatory breach reporting](https://www.priv.gc.ca/en/privacy-topics/business-privacy/safeguards-and-breaches/privacy-breaches/respond-to-a-privacy-breach-at-your-business/gd_pb_201810/)
- [Nutrir Privacy Research](privacy-research.md) — full legal analysis of applicable Canadian frameworks
- [Nutrir Compliance Requirements](requirements.md) — v1/v2 application-level requirements
- [Nutrir AI Conversation Data Policy](ai-conversation-data-policy.md) — PHI in AI assistant sessions

> **Last updated**: 2026-03-22
