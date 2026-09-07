# Security posture

Target: OWASP ASVS 4.0 Level 2 for authentication, session management, access control,
input validation, error handling and logging (NFR-SEC-01). This file is updated in the same
commit as any change that affects a control below. A control with no test is not a control.

## Controls and where each is enforced

| Area | Control | Enforced at | Verified by |
|---|---|---|---|
| Authentication | Password at least 12 characters with three of four character classes, hashed by the ASP.NET Core Identity hasher | Identity options | BR-IAM-01 tests, P03 |
| Authentication | Lockout after 5 failures in 15 minutes; the response for an unknown account is identical to a wrong password | Identity lockout plus a constant-time path | BR-IAM-02 tests, P03 |
| Session | 15-minute access token, 14-day rotating refresh token, reuse revokes the whole chain and alerts the owner | Auth endpoints | BR-IAM-03 tests, P03 |
| Access control | Deny by default: every route outside the frozen allowlist returns 401 without a token; a token whose role lacks the permission returns 403 | Endpoint authorization policies plus query scoping | D7 and D7b gates, every phase from P03 |
| Access control | A draft is 404 to an anonymous caller, never 403 | Public read endpoints | NFR-AUTHZ-04 tests |
| Input validation | FluentValidation on every request, 422 with a field-keyed dictionary | Application layer | NFR-OBS-04 contract tests |
| Input validation | Uploads validated by magic number, never by extension or client content type | Media pipeline | NFR-SEC-06 test, P04 |
| Bot protection | Turnstile token verified server-side once with an idempotency key, plus honeypot and per-IP rate limits | Public form endpoint | BR-LEAD-02 and BR-LEAD-04 tests, P07 |
| Rate limiting | 5 form submissions per IP per 10 minutes, 10 login attempts per IP per 15 minutes, 300 public requests per IP per minute, each answered with 429 and Retry-After | ASP.NET Core rate limiting middleware | NFR-SEC-05 tests |
| Transport and headers | HSTS max-age 31536000, CSP with no unsafe-inline for scripts, X-Content-Type-Options nosniff, Referrer-Policy strict-origin-when-cross-origin, X-Frame-Options DENY | Middleware | NFR-SEC-03 test |
| CORS | Explicit origins only. AllowAnyOrigin combined with credentials appears nowhere in the codebase | Startup configuration | Source scan in the D11 gate |
| Error handling | RFC 9457 problem details with a traceId, never a stack trace or an inner exception message | Global exception handler | NFR-OBS-03 test |
| Secrets | Never committed. user-secrets in development, environment variables in production; committed config carries empty placeholders | Staged-index secret scan before every commit | G9 gate, every phase |
| Audit | Every mutating admin action written append-only with before and after values, sensitive fields redacted | SaveChangesAsync override | BR-ADM-01 tests, P14 |
| Dependencies | No known high or critical vulnerability at a phase gate | `dotnet list package --vulnerable --include-transitive`, `npm audit --omit=dev` | NFR-SEC-08, pasted every phase |

## Anonymous allowlist (frozen at approval)

```
/health
/health/live
/health/ready
/api/v1/auth/login
/api/v1/auth/refresh
/api/v1/public/**            (published content, read-only)
POST /api/v1/public/forms/{key}/submit
```

Every other path returns 401 without a token. The exit gate enumerates the OpenAPI document
each phase and proves it; a single 200 on an unlisted path fails the phase.

## Reporting a vulnerability

Email the address in the site footer. There is no bug bounty. Expect an acknowledgement
within one business day (A-28).
