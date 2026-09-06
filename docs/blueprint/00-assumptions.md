# 00 Assumptions

| ID | Assumption | Default I chose | Basis | Impact if wrong | Affected REQ IDs | Cost to change |
|---|---|---|---|---|---|---|
| A-01 | Domain definition | Public marketing and sales website for software products, product showcase plus storefront. | PROJECT_BRIEF | Entire scope is invalid | ALL | High |
| A-02 | Geography | Global, multi-country sales | Digital goods are inherently borderless | Tax/VAT calculation changes | REQ-BIL-*, REQ-TAX-* | High |
| A-03 | Currency | USD as base, localized display | Standard for SaaS and software sales | Multi-currency reconciliation needed | REQ-BIL-* | Medium |
| A-04 | Timezone rule | Store UTC, render local to user | Best practice for global systems | Timestamp confusion across regions | ALL | Low |
| A-05 | Languages | English only for v1 | Simplifies initial scope | Needs i18n support | REQ-UI-* | Medium |
| A-06 | Org size | One indie developer (me) selling to the public | User instruction | Over-engineering scalability or access controls | NFR-PERF-* | High |
| A-07 | Tenancy | Single-tenant, one seller (me), my products only. NOT multi-tenant SaaS. | User instruction | Unnecessary complexity in data isolation and payouts | ALL | High |
| A-08 | Peak concurrent users | 100 peak concurrent users | User instruction | Database connection exhaustion | NFR-PERF-01 | Medium |
| A-09 | Writes/sec | 20 writes/sec | User instruction | Deadlocks and timeouts | NFR-PERF-02 | Medium |
| A-10 | Yr-1 volume | 2,000 orders | User instruction | Storage capacity limits | NFR-DATA-01 | Low |
| A-11 | Yr-3 volume | 15,000 orders | User instruction | Archiving strategy needed | NFR-DATA-02 | Low |
| A-12 | Payments provider | Pending ADR recommendation (Razorpay vs Paddle vs Lemon Squeezy) | User instruction | Re-implementing payment gateway, compliance | REQ-PAY-* | High |
| A-13 | Notification provider | SendGrid | Reliable email delivery | Migrating templates and webhooks | REQ-NOT-* | Low |
| A-14 | Identity provider | ASP.NET Core Identity (Local) | Self-contained, no external dependency | Migrating to OAuth/OIDC later | REQ-ID-* | Medium |
| A-15 | Hosting target | Azure App Service + Azure SQL | Compatible with .NET Stack | Migration to AWS/GCP | NFR-DEP-* | Medium |
| A-16 | Data Retention | 7 years for financial data | Standard compliance | Legal penalties | NFR-SEC-* | Low |
| A-17 | Browser matrix | Chrome, Safari, Edge, Firefox (latest 2 versions) | Covers 95%+ of target audience | UI glitches on older browsers | NFR-UI-* | Low |
| A-18 | License Generation | Unique alphanumeric keys generated per purchase | Standard software license model | Changes to DRM integration | REQ-LIC-* | Medium |
| A-19 | Product Types | Digital downloads and SaaS subscriptions | Required by PROJECT_BRIEF | Fulfillment logic changes | REQ-CAT-* | Medium |
| A-20 | Offline Support | Not supported | Web-based storefront requires internet | Need PWA or edge caching | NFR-AVAIL-* | High |
| A-21 | Tax Calculation | Pending ADR recommendation | Depends on payment provider (MoR vs PSP) | Manual tax logic required | REQ-TAX-* | High |
| A-22 | Fraud Prevention | Pending ADR recommendation | Depends on payment provider | Increased chargebacks | REQ-PAY-* | High |
