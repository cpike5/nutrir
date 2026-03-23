# Field-Level Encryption

## Overview

Canadian privacy legislation -- PHIPA (Ontario), HIA (Alberta), and PIPEDA (federal) -- requires organizations handling personal health information to implement reasonable technical safeguards proportional to the sensitivity of the data.

Database-level encryption such as Transparent Data Encryption (TDE) or full-disk encryption protects data at rest against physical disk theft. However, it provides no protection against SQL injection, database credential compromise, or unauthorized queries by anyone with database access. Once an attacker has a valid database connection, TDE-encrypted data is returned in plaintext.

Field-level encryption adds defense-in-depth by encrypting sensitive health fields at the application layer before they reach the database. Even with direct database access, an attacker sees only opaque ciphertext in sensitive columns. Decryption requires the application-layer encryption key, which is stored separately from the database.

---

## Algorithm

All field encryption uses **AES-256-GCM** (Galois/Counter Mode):

- **Key size**: 256-bit (32 bytes)
- **Nonce**: 96-bit (12 bytes), randomly generated per encryption operation
- **Authentication tag**: 128-bit (16 bytes), appended by GCM

**Ciphertext format** stored in database columns:

```
v{keyVersion}:{base64(nonce + ciphertext + tag)}
```

- `v{keyVersion}` -- integer prefix identifying which key encrypted the value (e.g., `v1:`)
- The colon separates the version prefix from the Base64 payload
- The Base64 payload contains the concatenation of nonce (12 bytes) + ciphertext (variable) + GCM authentication tag (16 bytes)

The version prefix enables key rotation: the decryptor reads the version, selects the correct key, and decrypts. New encryptions always use the current key version.

---

## Encrypted Fields

The following fields contain free-text clinical information and are encrypted at rest:

| Entity | Field |
|--------|-------|
| `Client` | `Notes` |
| `Appointment` | `Notes`, `PrepNotes` |
| `MealPlan` | `Notes`, `Description` |
| `MealPlanDay` | `Notes` |
| `MealSlot` | `Notes` |
| `MealItem` | `Notes` |
| `ProgressGoal` | `Description` |
| `ProgressEntry` | `Notes` |
| `ConsentEvent` | `Notes` |
| `ConsentForm` | `Notes` |
| `ClientCondition` | `Notes` |
| `ClientDietaryRestriction` | `Notes` |
| `PractitionerTimeBlock` | `Notes` |
| `SessionNote` | `Notes`, `MeasurementsTaken`, `PlanAdjustments`, `FollowUpActions` |

All encrypted fields are free-text columns. Structured data fields (enums, dates, foreign keys) are not encrypted because they require database-level querying and indexing.

---

## Implementation

Field encryption is implemented transparently using **EF Core value converters** applied in `AppDbContext.OnModelCreating`.

- An `AesGcmFieldEncryptor` singleton is registered in DI. It handles all encrypt and decrypt operations.
- For each encrypted field, a value converter calls `AesGcmFieldEncryptor.Encrypt()` on write and `AesGcmFieldEncryptor.Decrypt()` on read.
- Application services read and write plaintext normally -- encryption is invisible to business logic.
- The value converter handles null values by passing them through without encryption.

This approach means no service, component, or API code needs to be aware of encryption. The protection is applied at the data access layer.

---

## Key Management

### Development

In development, the encryption key is stored in `appsettings.Development.json`:

```json
{
  "Encryption": {
    "Enabled": true,
    "Key": "<base64-encoded-256-bit-key>",
    "KeyVersion": 1,
    "PreviousKeys": {}
  }
}
```

The development key is used only for local development and seeded test data. It must not be reused in production.

### Production

In production, the key is set via environment variable:

```
NUTRIR_ENCRYPTION_KEY=<base64-encoded-256-bit-key>
```

The key version and previous keys are configured via `Encryption:KeyVersion` and `Encryption:PreviousKeys` in production configuration.

### Key Storage Rules

- Keys must **never** be stored in the same database as the encrypted data
- Keys must **never** be committed to source control
- Keys must **never** appear in application logs
- The `appsettings.Development.json` key is acceptable only because it encrypts synthetic seed data, not real PHI

---

## Key Rotation Procedure

1. **Generate a new 256-bit key:**

   ```bash
   openssl rand -base64 32
   ```

2. **Move the current key to `PreviousKeys`:**

   Add the current key to the `PreviousKeys` dictionary, keyed by its version number. For example, if rotating from version 1 to version 2:

   ```json
   {
     "Encryption": {
       "Enabled": true,
       "Key": "<new-base64-key>",
       "KeyVersion": 2,
       "PreviousKeys": {
         "1": "<old-base64-key>"
       }
     }
   }
   ```

3. **Deploy the updated configuration.**

   The application will automatically decrypt old values using the previous key (matched by the `v1:` prefix) and encrypt new writes with the current key (version 2).

4. **Run `FieldEncryptionMigrationService`:**

   This service re-encrypts all existing rows with the new key. It runs at application startup and processes rows in batches. After it completes, all stored values will carry the `v2:` prefix.

5. **Verify and retire old keys:**

   After confirming all rows have been re-encrypted (no `v1:` prefixed values remain in the database), the old key can be removed from `PreviousKeys`.

---

## Data Migration

The `FieldEncryptionMigrationService` handles initial encryption of existing plaintext data and re-encryption during key rotation.

- **Runs once at application startup** as a hosted service
- **Idempotent**: detects already-encrypted values by checking for the `v{N}:` prefix and skips them
- **Batch processing**: processes 100 rows at a time to avoid long-running transactions and excessive memory usage
- **Handles plaintext gracefully**: values without a version prefix are treated as unencrypted plaintext, encrypted with the current key, and saved
- **Can be disabled**: set `Encryption:Enabled` to `false` in configuration to skip all encryption (useful for debugging or migration scenarios)

---

## Performance

- **AES-GCM leverages AES-NI**: modern x86 and ARM CPUs provide hardware acceleration for AES operations. Encryption and decryption overhead is measured in microseconds per field.
- **Negligible application impact**: the encrypted fields are free-text notes loaded during individual record views and edits, not in bulk list queries. The per-request overhead is not measurable.
- **No indexing impact**: none of the encrypted fields are indexed or used in WHERE clauses. They are free-text Notes, Description, and clinical narrative fields. Database query plans are unaffected.
- **Column size increase**: Base64 encoding and the version prefix add approximately 37% overhead to the stored string size. For typical notes fields this is negligible.

---

## What Encryption Does and Does Not Protect Against

### Protects against

| Threat | How field encryption helps |
|--------|---------------------------|
| **SQL injection** | Attacker retrieves ciphertext, not plaintext health data |
| **Database credential compromise** | Direct SELECT queries return opaque encrypted values |
| **Database backup theft** | Backup files contain only ciphertext; key is not in the backup |
| **Unauthorized DBA access** | Database administrators see ciphertext without the application key |

### Does not protect against

| Threat | Why |
|--------|-----|
| **Application-level access** | Anyone with valid application credentials sees decrypted data through the normal UI -- this is by design |
| **Memory dumps** | Plaintext exists in application memory during request processing |
| **Key compromise** | If the encryption key is stolen, all encrypted data can be decrypted |
| **Compromised application server** | An attacker with access to the running application process can extract the key from memory or configuration |

### Defense-in-depth context

Field encryption is one layer in a broader security posture:

- **HTTPS + HSTS** protects data in transit
- **MFA** prevents unauthorized application access
- **Audit logging** detects and records suspicious access
- **Soft-delete** prevents accidental data loss
- **Canadian data residency** keeps all data within Canadian jurisdiction

No single measure is sufficient. Field encryption specifically addresses the gap between transport encryption (HTTPS) and full-disk encryption (TDE) by protecting sensitive fields from database-level attacks.

---

## Configuration Reference

```json
{
  "Encryption": {
    "Enabled": true,
    "Key": "<base64-encoded-256-bit-key>",
    "KeyVersion": 1,
    "PreviousKeys": {}
  }
}
```

| Setting | Type | Description |
|---------|------|-------------|
| `Enabled` | `bool` | Master toggle. When `false`, no encryption or decryption occurs and fields are stored as plaintext. |
| `Key` | `string` | Base64-encoded 256-bit (32-byte) AES key. In production, override via `NUTRIR_ENCRYPTION_KEY` environment variable. |
| `KeyVersion` | `int` | Current key version number. Incremented on each key rotation. Used as the `v{N}:` prefix on new ciphertext. |
| `PreviousKeys` | `Dictionary<string, string>` | Map of version number to Base64-encoded key. Used for decrypting values encrypted with prior key versions during rotation. |

---

> **Last updated**: 2026-03-22
