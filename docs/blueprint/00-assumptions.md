# 00 Assumptions

| ID | Assumption | Default I chose | Basis | Impact if wrong | Affected REQ IDs | Cost to change |
|---|---|---|---|---|---|---|
| A-01 | Domain definition | Public marketing and sales website for software products, product showcase plus storefront. | PROJECT_BRIEF | Entire scope is invalid | ALL | High |
| A-02 | Geography | Global, multi-country sales | Digital goods are inherently borderless | Tax/VAT calculation changes | REQ-BIL-*, REQ-TAX-* | High |
| A-03 | Currency | USD as base, localized display | Standard for SaaS and software sales | Multi-currency reconciliation needed | REQ-BIL-* | Medium |
| A-04 | Timezone rule | Store UTC, render local to user | Best practice for global systems | Timestamp confusion across regions | ALL | Low |
| A-05 | Languages | English only for v1 | Simplifies initial scope | Needs i18n support | REQ-UI-* | Medium |
| A-06 | Org size | Small to medium indie developers | Typical target for such platforms | Scalability bottlenecks | NFR-PERF-* | Medium |
| A-07 | Tenancy | Multi-tenant SaaS | Necessary for a platform serving multiple sellers | Complete architecture rewrite | ALL | High |
| A-08 | Peak concurrent users | 1000 peak concurrent users | Generous baseline for standard storefronts | Database connection exhaustion | NFR-PERF-01 | Medium |
| A-09 | Writes/sec | 50 writes/sec | High burst during product launches | Deadlocks and timeouts | NFR-PERF-02 | Medium |
| A-10 | Yr-1 volume | 100,000 orders | Standard growth model | Storage capacity limits | NFR-DATA-01 | Low |
| A-11 | Yr-3 volume | 1,000,000 orders | Standard growth model | Archiving strategy needed | NFR-DATA-02 | Low |
| A-12 | Payments provider | Stripe | Industry standard, robust API | Re-implementing payment gateway | REQ-PAY-* | High |
| A-13 | Notification provider | SendGrid | Reliable email delivery | Migrating templates and webhooks | REQ-NOT-* | Low |
| A-14 | Identity provider | ASP.NET Core Identity (Local) | Self-contained, no external dependency | Migrating to OAuth/OIDC later | REQ-ID-* | Medium |
| A-15 | Hosting target | Azure App Service + Azure SQL | Compatible with .NET Stack | Migration to AWS/GCP | NFR-DEP-* | Medium |
| A-16 | Data Retention | 7 years for financial data | Standard compliance | Legal penalties | NFR-SEC-* | Low |
| A-17 | Browser matrix | Chrome, Safari, Edge, Firefox (latest 2 versions) | Covers 95%+ of target audience | UI glitches on older browsers | NFR-UI-* | Low |
| A-18 | License Generation | Unique alphanumeric keys generated per purchase | Standard software license model | Changes to DRM integration | REQ-LIC-* | Medium |
| A-19 | Product Types | Digital downloads and SaaS subscriptions | Required by PROJECT_BRIEF | Fulfillment logic changes | REQ-CAT-* | Medium |
| A-20 | Offline Support | Not supported | Web-based storefront requires internet | Need PWA or edge caching | NFR-AVAIL-* | High |
| A-21 | Tax Calculation | Handled via payment provider (Stripe Tax) | Offloads complex tax compliance | Manual tax logic required | REQ-TAX-* | High |
| A-22 | Fraud Prevention | Handled by Stripe Radar | Offloads risk management | Increased chargebacks | REQ-PAY-* | High |
