# 05 Non-Functional Requirements (NFR)

## Performance
| ID | Requirement | Measurement Method |
|---|---|---|
| NFR-PERF-01 | **API Latency (Read)**: `GET /api/products` p95 response time must be < 200ms. | Artillery load test against Azure App Service. |
| NFR-PERF-02 | **API Latency (Write)**: `POST /api/webhook/paddle` p95 response time must be < 500ms. | Artillery load test. |
| NFR-PERF-03 | **UI Initial Bundle Size**: Angular `main.js` must be < 250 KB (gzipped). | `ng build` output stats in CI. |
| NFR-PERF-04 | **UI Lazy Bundle Size**: Any lazy-loaded module (e.g., Admin) must be < 150 KB (gzipped). | `ng build` output stats in CI. |
| NFR-PERF-05 | **LCP**: Largest Contentful Paint for the public homepage must be < 1.5 seconds. | Lighthouse CI. |

## Concurrency
| ID | Requirement | Measurement Method |
|---|---|---|
| NFR-CONC-01 | **Peak Concurrent Users**: System must support 100 concurrent HTTP connections without dropping requests (A-08). | Artillery (100 VUs). |
| NFR-CONC-02 | **License Activation Deadlocks**: Concurrent `POST /api/licenses/validate` for the same key from 5 threads must not exceed `MaxActivations` and must not deadlock. | Integration test running `Task.WhenAll` on 5 threads. |

## Data Volume & Retention
| ID | Requirement | Measurement Method |
|---|---|---|
| NFR-DATA-01 | **Order Capacity**: Database must hold 15,000 orders (Yr-3 estimate) within 2GB Azure SQL Basic limit. | Database size estimation script / seeding test. |
| NFR-DATA-02 | **Retention**: Financial data (Orders, Refunds) must not be hard-deleted for 7 years. | Code review of EF Core configurations (no hard deletes on `Order`). |

## Security
| ID | Requirement | Measurement Method |
|---|---|---|
| NFR-SEC-01 | **PII Masking**: Customer Emails and IPs must never be logged in plain text in Application Insights. | Integration test verifying `ILogger` output. |
| NFR-SEC-02 | **Webhook Verification**: 100% of Paddle webhooks must be verified using Ed25519 signatures before processing. | Unit test passing invalid signature expecting 401. |
| NFR-SEC-03 | **JWT Lifetime**: Access tokens must expire in 15 minutes; Refresh tokens in 7 days. | Unit test asserting `exp` claim. |
| NFR-SEC-04 | **Rate Limiting**: `POST /api/licenses/validate` is limited to 10 requests per minute per IP. | E2E test hitting 429 Too Many Requests. |
| NFR-SEC-05 | **Account Lockout**: Admin login locks out for 15 minutes after 5 failed attempts. | Integration test asserting lockout status. |

## Authorization
| ID | Requirement | Measurement Method |
|---|---|---|
| NFR-AUTH-01 | **Default Deny**: All API endpoints must return 401 unless explicitly decorated with `[AllowAnonymous]` (health, login, webhooks). | CI script scanning controllers for missing `[Authorize]` attributes (D7). |

## Privacy / PII Inventory
| ID | Requirement | Measurement Method |
|---|---|---|
| NFR-PRIV-01 | **Soft Deletion**: Deleting a customer account must anonymize the email to `deleted_{guid}@example.com`. | Integration test verifying DB row post-deletion. |

## Availability + RPO/RTO
| ID | Requirement | Measurement Method |
|---|---|---|
| NFR-AVAIL-01 | **Webhook Retries**: Failed webhooks (e.g., DB down) must be retried by Paddle, and local Hangfire jobs must retry transient email failures up to 3 times. | Unit tests for exponential backoff logic. |
| NFR-AVAIL-02 | **RPO**: Recovery Point Objective is 5 minutes. | Azure SQL Point-in-Time Restore configuration. |
| NFR-AVAIL-03 | **RTO**: Recovery Time Objective is 4 hours. | Executing a manual restore drill in Staging environment. |

## Observability
| ID | Requirement | Measurement Method |
|---|---|---|
| NFR-OBS-01 | **Structured Logging**: All logs must be emitted as structured JSON. | Code review of Serilog configuration. |
| NFR-OBS-02 | **Banned Logging**: Passwords, License Keys, and JWTs must never be passed to `ILogger`. | Regex scan in CI (`logger\.Log[A-Z].*(Password|Key|Token)`). |

## Internationalization (i18n)
| ID | Requirement | Measurement Method |
|---|---|---|
| NFR-I18N-01 | **Currency Formatting**: All prices rendered on UI must format according to `en-US` locale (USD). | E2E test asserting `$0.00` format. |

## Accessibility
| ID | Requirement | Measurement Method |
|---|---|---|
| NFR-ACC-01 | **WCAG 2.2 AA**: All public storefront pages must pass automated WCAG 2.2 AA checks with 0 errors. | `axe-core` run in Cypress/Playwright CI pipeline. |

## Browser / Device
| ID | Requirement | Measurement Method |
|---|---|---|
| NFR-UI-01 | **Browser Support**: UI must render without JS errors in Chrome, Safari, Edge, Firefox (latest 2 versions). | Browserslist configuration in Angular. |

## Maintainability
| ID | Requirement | Measurement Method |
|---|---|---|
| NFR-MAINT-01 | **Backend Coverage Floor**: Core Domain & Application layers must have >= 85% line coverage. | `dotnet test /p:CollectCoverage=true /p:Threshold=85` in CI (D15). |
| NFR-MAINT-02 | **Frontend Coverage Floor**: Angular components must have >= 70% line coverage. | Karma coverage reporter in CI. |

## Deployability & Cost
| ID | Requirement | Measurement Method |
|---|---|---|
| NFR-DEP-01 | **Monthly Cost**: Production hosting cost must be <= $30/month (Azure App Service B1 + Azure SQL Basic). | Cost calculation in ADR. |
| NFR-DEP-02 | **Zero-Downtime Deploy**: EF Core migrations must be backward compatible (no dropping columns) to allow blue-green deployment. | Code review of Migration files. |
