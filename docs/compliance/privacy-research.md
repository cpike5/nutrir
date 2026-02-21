# Canadian Privacy & Compliance Requirements for a Health/Nutrition CRM

> **Purpose**: Practical compliance reference for a solo nutritionist/dietitian in Canada storing client health data (dietary info, meal plans, progress tracking, basic health history) in a self-hosted CRM application.
>
> **Disclaimer**: This is a research summary, not legal advice. Consult a privacy lawyer for your specific situation, especially regarding which provincial law applies to your practice.

---

## 1. PIPEDA — Federal Privacy Law

### What Is PIPEDA?

The **Personal Information Protection and Electronic Documents Act (PIPEDA)** is Canada's federal private-sector privacy law. It governs how private-sector organizations collect, use, and disclose personal information in the course of commercial activity.

### Does PIPEDA Apply to a Solo Health Practitioner?

**It depends on your province.**

PIPEDA applies to private-sector commercial activity across Canada, **except** where a province has enacted its own privacy legislation that the federal government has declared "substantially similar." In those provinces, the provincial law applies instead of PIPEDA for activities within that province.

Key points for a solo nutritionist/dietitian:

- **You are engaged in commercial activity** (charging clients for services), so private-sector privacy law applies to you.
- **If you operate in a province with substantially similar legislation**, that provincial law governs your in-province activities instead of PIPEDA.
- **PIPEDA still applies** to any cross-provincial or cross-border data flows, even in provinces with their own laws.
- **Health information receives heightened protection** under both PIPEDA and provincial laws. PIPEDA treats health information as "sensitive personal information" requiring explicit consent.

### Provinces with Substantially Similar Private-Sector Laws

| Province | General Private-Sector Law | Health-Specific Law |
|----------|--------------------------|---------------------|
| **British Columbia** | PIPA (Personal Information Protection Act) | — (PIPA covers health info) |
| **Alberta** | PIPA (Personal Information Protection Act) | HIA (Health Information Act) |
| **Quebec** | Law 25 / Act Respecting the Protection of Personal Information in the Private Sector | — (general law covers health) |
| **Ontario** | — (no general private-sector law) | PHIPA (Personal Health Information Protection Act) |
| **Manitoba** | — | PHIA (Personal Health Information Act) |
| **Saskatchewan** | — | HIPA (Health Information Protection Act) |
| **New Brunswick** | POPIA (Personal Information Protection Act, 2024) | — |
| **Nova Scotia, PEI, NL, Territories** | — | — |

**If you are in a province without substantially similar legislation** (e.g., Nova Scotia, PEI, Newfoundland, territories), **PIPEDA applies directly**.

---

## 2. Provincial Health Privacy Laws — What Matters

### The Big Question: Are You a "Health Information Custodian"?

Provincial health privacy laws typically apply to **health information custodians** — regulated health professionals, hospitals, clinics, etc. Whether a nutritionist/dietitian qualifies depends on:

- **Whether you are a regulated health professional in your province** (dietitians are regulated in most provinces; nutritionists may or may not be)
- **The specific definition in the provincial law**

If you are a **Registered Dietitian**, you are almost certainly a health information custodian under your province's health privacy law.

If you are an unregulated **nutritionist**, the general private-sector privacy law (PIPA or PIPEDA) likely applies instead.

### Ontario — PHIPA

- Applies to **health information custodians** (HICs), which includes regulated health professionals
- Registered Dietitians in Ontario are HICs under PHIPA
- Covers **personal health information (PHI)**: health history, dietary records, meal plans tied to health goals, progress notes
- Requires: privacy policies, consent management, breach reporting to the Information and Privacy Commissioner of Ontario (IPC), access and correction rights
- **Agents** (anyone acting on behalf of a HIC, including software) must comply with HIC's privacy practices

### Alberta — HIA

- The Health Information Act applies to **custodians** including regulated health professionals
- Dietitians registered with the College of Dietitians of Alberta are custodians
- Strict rules on collection, use, disclosure, and retention of health information
- Breach notification to the Office of the Information and Privacy Commissioner of Alberta (OIPC)
- Electronic health records have additional requirements

### Other Provinces

- **Manitoba (PHIA)**, **Saskatchewan (HIPA)**: Similar custodian-based frameworks for regulated health professionals
- **BC and Quebec**: Health information is covered under their general private-sector privacy laws (PIPA and Law 25 respectively), with health data treated as sensitive

---

## 3. Key Requirements (Common Across All Frameworks)

Regardless of which specific law applies, the core obligations are remarkably consistent.

### 3.1 Consent

| Requirement | Details |
|-------------|---------|
| **Type** | Health information almost always requires **express/explicit consent** (not implied) |
| **Informed** | Client must understand what is collected, why, how it is used, who can access it |
| **Specific** | Consent for each distinct purpose (treatment planning vs. marketing are separate) |
| **Withdrawable** | Clients can withdraw consent at any time (with reasonable notice) |
| **Documented** | Record when and how consent was obtained |

**For the CRM**: Implement a consent workflow at client onboarding. Store a timestamped consent record. Allow clients to view and withdraw consent.

### 3.2 Data Retention

| Requirement | Details |
|-------------|---------|
| **Minimum retention** | Most provincial colleges of dietitians require retaining client records for **at least 7-10 years** after last contact (check your specific college's standards) |
| **Maximum retention** | Do not keep data longer than necessary for the purpose it was collected |
| **Destruction** | When retention period expires, data must be securely destroyed |
| **Minors** | Records for minors are often retained until the client reaches age of majority + the standard retention period |

**For the CRM**: Implement retention tracking per client. Add a review/purge workflow. Log all deletions.

### 3.3 Breach Notification

All frameworks now require breach notification:

| Framework | Notification Requirements |
|-----------|--------------------------|
| **PIPEDA** | Report to OPC and notify affected individuals if breach creates "real risk of significant harm" (RROSH). Keep records of all breaches for 24 months. |
| **PHIPA (ON)** | Report to IPC as soon as possible. Notify affected individuals. |
| **HIA (AB)** | Report to OIPC. Notify affected individuals. |
| **Alberta PIPA** | Similar RROSH threshold to PIPEDA. |
| **Quebec Law 25** | Report to CAI. Notify affected individuals. Mandatory privacy impact assessments. |

**For the CRM**: Implement audit logging sufficient to detect and investigate breaches. Have a breach response plan documented. Log all access to PHI.

### 3.4 Right to Access and Correction

- Clients have the right to **access their personal health information** upon request
- Clients have the right to **request corrections** to inaccurate information
- You must respond within a defined timeframe (typically 30 days under PIPEDA, 30-60 days under provincial laws)
- You can charge a nominal fee for access requests but cannot make it prohibitive

**For the CRM**: Build a data export feature (client can get a copy of all their data). Allow annotation/correction of records. Log access requests and responses.

### 3.5 Privacy Impact Assessment

Quebec's Law 25 explicitly requires privacy impact assessments (PIAs) for systems handling personal information. Other frameworks strongly recommend them. Given this is a health data system, **do a PIA regardless of province**.

---

## 4. Technical Implications

### 4.1 Data Residency

**Must data stay in Canada?**

| Framework | Data Residency Requirement |
|-----------|---------------------------|
| **PIPEDA** | No strict residency requirement, but the organization remains responsible for data protection regardless of where data is stored. Must inform individuals that data may be processed outside Canada. |
| **PHIPA (ON)** | No blanket prohibition, but HICs must ensure equivalent protection. Practical guidance strongly favors keeping PHI in Canada. |
| **HIA (AB)** | **Yes — health information must be stored and accessible in Canada.** Disclosure outside Canada requires consent or specific authorization. This is the strictest. |
| **Quebec Law 25** | Must conduct a PIA before transferring outside Quebec. Must ensure equivalent protection. |
| **BC PIPA** | Public bodies must store in Canada; private sector has more flexibility but must ensure protection. |

**Recommendation**: **Store all data in Canada.** This satisfies the strictest requirements (Alberta HIA) and is the simplest path. A Canadian VPS provider handles this.

### 4.2 Encryption

No Canadian privacy law specifies exact encryption algorithms, but all require "appropriate safeguards." For health data, the standard of care is:

| Layer | Minimum Recommendation |
|-------|----------------------|
| **Data in transit** | TLS 1.2+ (enforce TLS 1.3 where possible). HTTPS everywhere. |
| **Data at rest** | AES-256 encryption for database and backups. Full-disk encryption on the VPS. |
| **Application-level** | Encrypt sensitive health fields at the application level (not just database-level). This protects against database dump exposure. |
| **Backups** | Encrypted backups. Encrypted transfer to backup storage. |
| **Key management** | Keys stored separately from encrypted data. Not in source control. |

### 4.3 Access Controls

| Control | Implementation |
|---------|---------------|
| **Authentication** | Strong passwords + MFA for the practitioner. No shared accounts. |
| **Authorization** | Role-based access (even if solo now, design for it). Principle of least privilege. |
| **Session management** | Auto-timeout after inactivity. Secure session tokens. |
| **Client portal** (if any) | Clients should only see their own data. Enforce at the query level, not just the UI. |

### 4.4 Audit Logging

This is not optional for health data. Every framework expects you to demonstrate accountability.

| What to Log | Details |
|-------------|---------|
| **Access logs** | Who viewed what record, when |
| **Modification logs** | What changed, old value vs. new value, who changed it, when |
| **Authentication events** | Login, logout, failed attempts, MFA events |
| **Data exports** | When data is exported or downloaded |
| **Consent events** | When consent is given, modified, or withdrawn |
| **Deletion events** | What was deleted, when, by whom, under what authority |

**Retention**: Keep audit logs for at least as long as the data retention period (7-10 years). Audit logs should be append-only / tamper-resistant.

---

## 5. Practical Recommendations

What a solo practitioner self-hosting on a Canadian VPS actually needs to do.

### 5.1 Infrastructure

- [ ] **Canadian VPS provider** — confirm the data centre is physically in Canada
- [ ] **Full-disk encryption** on the VPS
- [ ] **Automated encrypted backups** to a Canadian location. Test restores regularly.
- [ ] **Firewall** — only expose ports 443 (HTTPS) and SSH (key-only, non-standard port)
- [ ] **Automatic security updates** enabled on the host OS
- [ ] **TLS certificate** (Let's Encrypt is fine) — enforce HTTPS, HSTS headers

### 5.2 Application

- [ ] **Consent capture** — record client consent at onboarding with timestamp, purpose, and version of privacy policy they agreed to
- [ ] **Audit logging** — log all access and modifications to client health data (append-only table or separate log store)
- [ ] **Encrypt sensitive fields** at the application level (health history, progress notes, dietary restrictions) using AES-256. Store keys outside the database.
- [ ] **Data export** — allow export of a single client's data in a portable format (JSON, PDF) for access requests
- [ ] **Data deletion workflow** — soft-delete with hard-delete after retention period. Log the deletion.
- [ ] **Retention tracking** — track "last interaction date" per client and flag records approaching end of retention
- [ ] **MFA** on the practitioner's login

### 5.3 Policies and Documentation

You need these even as a solo practitioner:

1. **Privacy Policy** — give to every client. Plain language. Covers: what you collect, why, how it is stored, who can access it, how long you keep it, their rights.
2. **Breach Response Plan** — what you do if data is compromised (who to notify, timelines, steps to contain).
3. **Data Retention Schedule** — document how long each type of data is kept and when it is destroyed.
4. **Record of Processing Activities** — what personal information you process and on what legal basis (required by Quebec Law 25, good practice everywhere).

### 5.4 What You Can Probably Skip (As a Solo Practitioner)

- **Privacy officer appointment** — you are the privacy officer
- **Cross-border data transfer agreements** — not needed if everything stays in Canada
- **Complex data sharing agreements** — not needed unless you share data with other practitioners
- **HIPAA compliance** — this is a US law, not applicable in Canada (but if you ever have US clients, it could become relevant)

### 5.5 Common Pitfalls

| Pitfall | How to Avoid |
|---------|-------------|
| Storing PHI in email/SMS | Use the CRM for all health data. If clients email health info, import it and delete the email. |
| No encryption at rest | Enable full-disk encryption AND application-level encryption for sensitive fields. |
| No breach plan | Write a one-page plan now, before you need it. |
| Keeping data forever | Implement automated retention review. |
| Consent not documented | The CRM should make consent recording part of the intake workflow. |
| Audit logs missing | Implement from day one. Retrofitting is painful. |
| Backups not encrypted or not tested | Encrypt backups. Test a restore quarterly. |

---

## Summary Decision Matrix

| If You Are In... | Primary Law | Health-Specific? | Data Must Stay in Canada? |
|------------------|-------------|------------------|--------------------------|
| Ontario (Registered Dietitian) | PHIPA | Yes | Strongly recommended |
| Ontario (unregulated nutritionist) | PIPEDA | No (but health data is sensitive) | Recommended |
| Alberta | HIA | Yes | **Yes (mandatory)** |
| BC | PIPA | No (PIPA covers it) | Recommended |
| Quebec | Law 25 | No (but PIA required) | PIA required before transfer out |
| Saskatchewan | HIPA | Yes | Recommended |
| Manitoba | PHIA | Yes | Recommended |
| Other provinces/territories | PIPEDA | No | Recommended |

---

## References

- [PIPEDA overview](https://www.priv.gc.ca/en/privacy-topics/privacy-laws-in-canada/the-personal-information-protection-and-electronic-documents-act-pipeda/)
- [Provincial laws deemed substantially similar](https://www.priv.gc.ca/en/privacy-topics/privacy-laws-in-canada/the-personal-information-protection-and-electronic-documents-act-pipeda/r_o_p/provincial-legislation-deemed-substantially-similar-to-pipeda/)
- [PHIPA (Ontario)](https://www.ontario.ca/laws/statute/04p03)
- [HIA (Alberta)](https://www.qp.alberta.ca/documents/Acts/H05.pdf)
- [Quebec Law 25](https://www.quebec.ca/en/government/governance/loi-25)
- [PIPEDA breach notification regulations](https://laws-lois.justice.gc.ca/eng/regulations/SOR-2018-64/)
- [OPC guidance on health information](https://www.priv.gc.ca/en/privacy-topics/health-genetic-and-other-body-information/)

> **Last updated**: 2026-02-20
