# Gmail SMTP Setup Guide

Configuration guide for practitioners and administrators deploying Nutrir with Gmail as the outbound email provider.

## Prerequisites

- A **Google Workspace** account (e.g., Workspace Starter, Business Starter, or higher). Consumer Gmail accounts (`@gmail.com`) work for testing but have tighter rate limits and are not recommended for production.
- Administrative access to the Google account that will send email on behalf of the practice.
- DNS management access to your domain (for deliverability records).

## Step 1: Enable 2-Step Verification

Google App Passwords (required in Step 2) are only available on accounts with 2-Step Verification active.

1. Sign in to the Google account at [myaccount.google.com](https://myaccount.google.com).
2. Navigate to **Security**.
3. Under "How you sign in to Google," select **2-Step Verification**.
4. Follow the prompts to enable it.

## Step 2: Generate an App Password

App Passwords let a specific application authenticate with your Google account without using your main password, and without requiring OAuth.

1. Return to **Security** in [myaccount.google.com](https://myaccount.google.com).
2. Under "How you sign in to Google," select **App passwords** (only visible once 2-Step Verification is enabled).
3. In the "App name" field, enter a descriptive label such as `Nutrir`.
4. Click **Create**.
5. Google displays a 16-character password. **Copy it immediately** — it is not shown again.

This value becomes `SMTP_PASSWORD` in your `.env` file.

## Step 3: Configure DNS Records for Deliverability

Without proper DNS records, outbound email is likely to land in recipients' spam folders or be rejected outright. Configure the following records on your domain.

### SPF

Authorizes Gmail's servers to send email on behalf of your domain.

| Type | Host | Value |
|------|------|-------|
| TXT | `@` (or your domain) | `v=spf1 include:_spf.google.com ~all` |

If you already have an SPF record, add `include:_spf.google.com` to the existing record rather than creating a second TXT record. Only one SPF record per domain is valid.

### DKIM

Adds a cryptographic signature to outgoing messages so receiving servers can verify authenticity.

1. In the Google Admin console ([admin.google.com](https://admin.google.com)), navigate to **Apps > Google Workspace > Gmail > Authenticate email**.
2. Select your domain and click **Generate new record**.
3. Google provides a TXT record with a selector (e.g., `google._domainkey`) and a long public key value.
4. Add that record to your DNS exactly as shown.
5. Return to the Admin console and click **Start authentication** once DNS has propagated.

### DMARC

Tells receiving mail servers what to do with messages that fail SPF or DKIM checks, and where to send aggregate reports.

Add the following TXT record, substituting your actual email address for the `rua` report destination:

| Type | Host | Value |
|------|------|-------|
| TXT | `_dmarc` | `v=DMARC1; p=none; rua=mailto:dmarc-reports@yourdomain.com` |

See the [DMARC progression strategy](#dmarc-progression-strategy) section below before tightening the policy.

## Step 4: Configure the .env File

Nutrir reads SMTP credentials from environment variables. In your `.env` file (copy from `.env.example` if starting fresh):

```
SMTP_SENDER_EMAIL=hello@yourdomain.com
SMTP_USERNAME=hello@yourdomain.com
SMTP_PASSWORD=abcd efgh ijkl mnop
```

| Variable | Description |
|----------|-------------|
| `SMTP_SENDER_EMAIL` | The "From" address shown to recipients. Must be the Gmail/Workspace address. |
| `SMTP_USERNAME` | The Gmail/Workspace address used to authenticate with Google's SMTP server. Usually identical to `SMTP_SENDER_EMAIL`. |
| `SMTP_PASSWORD` | The 16-character App Password generated in Step 2 (not your regular Google account password). |

The host (`smtp.gmail.com`), port (`587`), and sender name (`Nutrir`) are pre-configured in `appsettings.json` and do not need to be set in `.env` unless you want to override them.

After editing `.env`, restart the application:

```bash
docker compose up -d --force-recreate app
```

## Sending Limits

| Account Type | Daily Limit |
|--------------|-------------|
| Google Workspace Starter | 2,000 messages/day |
| Google Workspace Business/Enterprise | 2,000 messages/day (per user) |
| Consumer Gmail (`@gmail.com`) | 500 messages/day |

Nutrir's current email usage (intake form notifications, appointment confirmations) is well within these limits for typical private practices. If volume grows significantly, consider a dedicated transactional email provider such as SendGrid or Postmark.

## DMARC Progression Strategy

Start with `p=none` (monitor mode) and tighten the policy only after confirming all legitimate email passes SPF and DKIM. A typical progression:

| Phase | Policy | When to Move On |
|-------|--------|-----------------|
| 1 — Monitor | `p=none` | After 2–4 weeks with no legitimate failures in aggregate reports |
| 2 — Quarantine | `p=quarantine` | After another 2–4 weeks of clean reports |
| 3 — Reject | `p=reject` | When confident all outbound email is covered by SPF and DKIM |

Update the `_dmarc` TXT record by changing the `p=` value at each stage. No application changes are required.

Aggregate reports (`rua`) are sent by receiving mail servers to the address you specified. Tools such as [dmarcian](https://dmarcian.com) or [MXToolbox](https://mxtoolbox.com/dmarc.aspx) can parse and visualize these reports.
