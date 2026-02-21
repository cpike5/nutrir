# ADR-0001: URL Structure & Application Routing

**Status**: Accepted
**Date**: 2026-02-20

## Context

The application serves two purposes under a single domain (elisamtz.ca):

1. A public-facing practice website for Elisa Martinez Garcia
2. The Nutrir CRM application for managing clients, scheduling, meal plans, and progress tracking

We need a URL structure that cleanly separates the public site from the authenticated application.

## Decision

### URL Layout

| Path | Purpose | Auth Required |
|------|---------|---------------|
| `elisamtz.ca` | Public landing page (practice info, services, contact) | No |
| `elisamtz.ca/nutrir` | CRM login (if unauthenticated) / dashboard (if authenticated) | Yes |
| `elisamtz.ca/nutrir/*` | All CRM pages (clients, schedule, meal-plans, etc.) | Yes |

### Security Benefit

Hosting the authentication endpoint at `/nutrir` rather than conventional paths (`/login`, `/account`, `/admin`) provides a passive layer of defense against automated attacks:

- Bots and scanners typically target well-known auth paths (`/login`, `/admin`, `/wp-admin`, `/auth`, etc.)
- An unconventional path reduces exposure to brute-force login attempts and credential stuffing
- This is **not a substitute** for proper security controls (MFA, rate limiting, account lockout) but is a free, zero-cost additional layer

## Consequences

- All authenticated Blazor routes must be nested under `/nutrir`
- The public landing page can be a simple static page or a separate Blazor layout without auth
- Bookmarks and deep links to CRM pages will all start with `/nutrir/`
- If the app name ever changes, routes would need updating
