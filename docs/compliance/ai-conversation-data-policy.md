# AI Conversation Data — Compliance Policy

> **Status**: Draft — pending implementation
> **Last updated**: 2026-02-26

---

## 1. Scope

This policy covers data stored in the `AiConversations` and `AiConversationMessages` database tables, and data transmitted to the Anthropic Claude API during AI assistant interactions.

AI conversations are **practitioner work sessions** — they are not the clinical record of care. However, they routinely contain **references to client PHI** (names, health conditions, dietary restrictions) embedded in free-text prompts and structured tool call results.

---

## 2. Data Classification

| Data Element | PHI Risk | Notes |
|---|---|---|
| `DisplayText` (user messages) | **High** | Practitioner free-text often includes client names and health details |
| `DisplayText` (assistant messages) | **Medium** | AI responses reference PHI from prompts and tool results |
| `ContentJson` (tool_use blocks) | **High** | Structured tool call inputs with client identifiers |
| `ContentJson` (tool_result blocks) | **Very High** | Full structured client data returned by tools (health history, meal plans, progress data) |
| `UserId`, `CreatedAt`, `LastMessageAt` | **Low** | Practitioner metadata, no client PHI |

---

## 3. Retention Policy

### 3.1 Active Sessions

Sessions remain active for **8 hours** after the last message (existing behavior). During this window, the full conversation (including `ContentJson`) is retained for session continuity.

### 3.2 Expired Session Cleanup

Once a session expires (8 hours of inactivity), the `ContentJson` field — which contains the richest PHI (tool call inputs and results) — should be **nulled out**. The `DisplayText` field is preserved for the remaining retention window.

### 3.3 Maximum Retention

All AI conversation data (conversations and messages) must be **automatically purged after 90 days** from `LastMessageAt`. This applies regardless of whether the session was manually cleared or expired naturally.

**Rationale**: AI conversations are ephemeral productivity aids, not records of care. The actual clinical outputs (meal plans, progress notes, etc.) are saved to their proper client-owned entities where they receive the full 7-year retention period. Retaining AI conversations beyond 90 days serves no identified purpose and increases PHI breach surface area. This aligns with PIPEDA Principle 5 (Limiting Use, Disclosure, and Retention).

### 3.4 User-Initiated Deletion

Practitioners may clear their conversation history at any time via the "Clear History" function. This performs an immediate hard delete. Audit logging is required (see section 4).

---

## 4. Audit Requirements

All deletion events involving AI conversation data must be audit-logged. No exceptions.

| Event | Audit Action | Details to Log |
|---|---|---|
| User clears history | `AiConversationHistoryCleared` | User ID, count of conversations deleted |
| System trims messages (cap exceeded) | `AiConversationMessagesTrimmed` | Conversation ID, count of messages trimmed |
| Automatic retention purge | `AiConversationRetentionPurge` | Count of conversations purged, retention threshold applied |
| ContentJson nulled (post-expiry) | `AiConversationContentStripped` | Count of messages stripped |

**Important**: Audit log entries must NOT contain the conversation content itself — only metadata (counts, IDs, timestamps).

---

## 5. Soft-Delete Exemption

AI conversations are **exempt from the soft-delete pattern** (v1 requirement 3). Rationale:

- Soft-delete is required for "client-owned entities" — entities that constitute the record of care (Client, Appointment, MealPlan, ProgressEntry).
- AI conversations are **practitioner-owned work sessions**, not client records.
- Applying soft-delete would retain the very PHI we are trying to minimize, contradicting the retention policy.
- The **audit log** provides the accountability trail for deletion events, replacing the need for soft-delete.

---

## 6. Data Residency — OPEN ISSUE

**Status**: Requires investigation before this policy can be finalized.

When a practitioner uses the AI assistant, the conversation content (including any embedded PHI) is transmitted to the Anthropic Claude API for processing.

### Questions to Resolve

1. **Where does Anthropic process API requests?** If processing occurs on US infrastructure, transmitting client PHI may violate Alberta HIA's mandatory Canadian data residency requirement (see `docs/compliance/privacy-research.md`, section 4.1).
2. **Does Anthropic retain prompt data?** What is Anthropic's data retention policy for API inputs/outputs? Does it train on API data?
3. **Is a Data Processing Agreement (DPA) available?** Required under several provincial frameworks when a third party processes PHI.

### Interim Mitigations (Until Resolved)

- The practitioner should be advised (via UI guidance) to **avoid including identifiable client information** in AI prompts where possible (e.g., use initials or anonymized references).
- Consider implementing **automatic PHI stripping** on outbound prompts (future enhancement).
- Document the AI assistant's third-party API usage in the **client privacy policy and consent form**.

---

## 7. PHI in Application Logs

Per v1 requirement 6 (Canadian Data Residency): "Application logs sent to Elastic do not contain PHI fields."

AI conversation content must **never** be written to application logs (Seq, Elastic, or any external log sink). This includes:

- Conversation text in error messages or exception details
- Tool call inputs/results in debug logging
- Message content in structured log properties

**Audit**: All AI-related code paths (`AiAgentService`, `AiConversationStore`, error handling middleware) must be reviewed to confirm no PHI leakage to logs.

---

## 8. Privacy Policy Disclosure

Under PIPEDA Principle 8 (Openness) and Principle 3 (Consent), the client-facing privacy policy and consent form must disclose:

1. That the application includes an AI assistant feature.
2. That conversation data may be transmitted to a third-party AI provider (Anthropic) for processing.
3. The jurisdiction where the AI provider processes data (once determined — see section 6).
4. That AI conversation data is retained for a maximum of 90 days and then destroyed.

This disclosure should be added to the consent form's data handling section (see `docs/compliance/consent-form.md`).

---

## 9. Implementation Checklist

### Phase A — Immediate (High Risk)
- [ ] Investigate Anthropic API data residency and retention policies
- [ ] Add audit logging to `ClearHistoryAsync` (action: `AiConversationHistoryCleared`)
- [ ] Add audit logging to `TrimMessagesAsync` (action: `AiConversationMessagesTrimmed`)

### Phase B — Short Term (Medium Risk)
- [ ] Implement automatic cleanup of conversations older than 90 days (hosted service)
- [ ] Audit all AI code paths for PHI in application logs
- [ ] Write audit entry for each automatic purge run

### Phase C — Medium Term (Completeness)
- [ ] Strip `ContentJson` from messages in expired sessions (> 8 hours)
- [ ] Update privacy policy and consent form to disclose AI assistant usage
- [ ] Add UI guidance advising practitioner on PHI in AI prompts
- [ ] Document AI data processing in Record of Processing Activities

---

## Related Documents

- [Compliance Requirements](requirements.md) — v1/v2 requirements
- [Privacy Research](privacy-research.md) — PIPEDA, PHIPA, HIA analysis
- [Consent Form](consent-form.md) — Consent form specification
- [AI Assistant Spec](../infrastructure/ai-assistant-spec.md) — AI assistant architecture and conversation persistence design
