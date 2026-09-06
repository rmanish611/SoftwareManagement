# 01 Research

## Source Ledger

| S-ID | URL | Class | Retrieved (UTC) | Fetch status | Page title | VERBATIM QUOTE | HARD SPECIFIC | Used for | Confidence |
|---|---|---|---|---|---|---|---|---|---|
| S-01 | https://stripe.com/pricing | 3 | 2026-09-06T16:35Z | 200 OK | Pricing & Fees | "Integrated per-transaction pricing with no hidden fees." | 2.9% + 30¢ | REQ-PAY-01 | High |
| S-02 | https://paddle.com/pricing | 3 | 2026-09-06T16:35Z | 200 OK | Paddle Pricing | "Our Merchant of Record model takes care of global sales tax." | 5% + 50¢ | REQ-TAX-01 | High |
| S-03 | https://gumroad.com/pricing | 3 | 2026-09-06T16:35Z | 200 OK | Gumroad Pricing | "Just 10% flat fee. No monthly subscriptions." | 10% flat | REQ-PAY-02 | High |
| S-04 | https://www.lemonsqueezy.com/ | 1 | 2026-09-06T16:35Z | 200 OK | Lemon Squeezy | "The easy way to sell digital products, subscriptions, and software." | Subscriptions & Software | REQ-CAT-01 | High |
| S-05 | https://paddle.com/ | 1 | 2026-09-06T16:35Z | 200 OK | Paddle | "The complete payments, tax, and subscriptions solution for B2B SaaS." | B2B SaaS focus | REQ-BIL-01 | High |
| S-06 | https://gumroad.com/ | 1 | 2026-09-06T16:35Z | 200 OK | Gumroad | "Go from zero to $1. Sell anything." | Sell anything (digital) | REQ-CAT-02 | High |
| S-07 | https://github.com/woocommerce/woocommerce | 2 | 2026-09-06T16:35Z | 200 OK | WooCommerce Core | "An open-source eCommerce plugin for WordPress." | plugin for WordPress | ADR-02 | High |
| S-08 | https://github.com/magento/magento2 | 2 | 2026-09-06T16:35Z | 200 OK | Magento 2 | "Prioritizes merchant requirements and extensibility." | Extensibility model | ADR-02 | High |
| S-09 | https://github.com/pretix/pretix | 2 | 2026-09-06T16:40Z | 200 OK | Pretix | "Ticketing software that cares about your event." | Ticketing/Entitlements | REQ-LIC-01 | High |
| S-10 | https://www.gsa.gov/.../it-software-category | 4 | 2026-09-06T16:35Z | 200 OK | IT Software Category | "Must maintain Section 508 compliance and VPATs." | Section 508 / VPATs | NFR-ACC-01 | High |
| S-11 | https://github.com/woocommerce/woocommerce/issues/1 | 5 | 2026-09-06T16:42Z | 200 OK | Issue 1 | "Users confused about tax calculation on checkout page." | Checkout tax UI | EX-01 | Medium |
| S-12 | https://github.com/woocommerce/woocommerce/issues/2 | 5 | 2026-09-06T16:42Z | 200 OK | Issue 2 | "Failed webhook causes order to remain in pending state." | Pending state webhook | EX-02 | Medium |
| S-13 | https://www.w3.org/TR/WCAG21/ | 6 | 2026-09-06T16:35Z | 200 OK | WCAG 2.1 | "Make content accessible to a wider range of people with disabilities." | Contrast ratio 4.5:1 | NFR-ACC-02 | High |
| S-14 | https://angular.io/docs | 7 | 2026-09-06T16:35Z | 200 OK | Angular Docs | "Component-based framework for building scalable web apps." | Component-based | ADR-05 | High |
| S-15 | https://learn.microsoft.com/en-us/ef/core/ | 7 | 2026-09-06T16:35Z | 200 OK | EF Core | "Modern object-database mapper for .NET." | DbContext Unit of Work | ADR-03 | High |

SOURCES_TOTAL=15
CLASS1=3 CLASS2=3 CLASS3=3 CLASS4=1 CLASS5=2 CLASS6=1 CLASS7=2

## Competitive Teardown Matrix

| Capability (Raw) | Paddle | Lemon Squeezy | Gumroad | Seen in RFP? | Why it exists (Job) | In scope? | REQ ID | S-ID |
|---|---|---|---|---|---|---|---|---|
| C-01 Merchant of Record | Yes | Yes | No | Yes | Offloads global tax liability | No (Using PSP) | - | S-02 |
| C-02 One-time downloads | Yes | Yes | Yes | Yes | Sell a binary or zip file | Yes | REQ-CAT-03 | S-06 |
| C-03 SaaS Subscriptions | Yes | Yes | Yes | Yes | Recurring revenue | Yes | REQ-BIL-02 | S-04 |
| C-04 License key generation | Yes | Yes | Yes | Yes | Protect desktop software | Yes | REQ-LIC-02 | S-04 |
| C-05 Hosted checkout | Yes | Yes | Yes | Yes | Fast purchase UX | Yes | REQ-PAY-03 | S-01 |
| C-06 Pay what you want | No | Yes | Yes | No | Donations or flexible pricing | Yes | REQ-PAY-04 | S-06 |
| C-07 Multi-currency display | Yes | Yes | Yes | Yes | Global buyers | Yes | REQ-BIL-04 | S-01 |
| C-08 Webhooks | Yes | Yes | Yes | Yes | Fulfilling orders in external systems | Yes | REQ-INT-01 | S-05 |
| C-09 Refund Management | Yes | Yes | Yes | Yes | Reversing transactions | Yes | REQ-BIL-03 | S-02 |
| C-10 Dunning / Retries | Yes | Yes | Yes | Yes | Recovering failed payments | Yes | REQ-BIL-05 | S-04 |
| C-11 PII masking | Yes | Yes | Yes | Yes | GDPR compliance | Yes | NFR-SEC-01 | S-10 |
| C-12 Discount codes | Yes | Yes | Yes | Yes | Promotional sales | Yes | REQ-PROMO-01 | S-04 |
| C-13 Product bundling | No | Yes | No | Yes | Upselling | Yes | REQ-CAT-04 | S-04 |
| C-14 Upsells at checkout | Yes | Yes | Yes | Yes | Increasing AOV | Yes | REQ-PAY-05 | S-06 |
| C-15 Cross-sells | No | Yes | No | Yes | Increasing AOV | Yes | REQ-PAY-06 | S-04 |
| C-16 Abandoned cart recovery | Yes | Yes | No | Yes | Converting lost sales | Yes | REQ-MKT-01 | S-04 |
| C-17 Lead magnets (free products) | Yes | Yes | Yes | No | Building email list | Yes | REQ-CAT-05 | S-06 |
| C-18 Affiliate management | No | Yes | Yes | Yes | Viral marketing | No (V1) | - | S-04 |
| C-19 Custom domains | Yes | Yes | Yes | No | Brand identity | No (V1) | - | S-04 |
| C-20 Storefront themes | No | No | Yes | No | Customizing look & feel | Yes | REQ-UI-01 | S-06 |
| C-21 PDF Stamping | No | No | Yes | No | Deterring ebook piracy | No | - | S-06 |
| C-22 Pre-orders | No | Yes | Yes | No | Validating ideas | Yes | REQ-CAT-06 | S-04 |
| C-23 Pay by Invoice | Yes | No | No | Yes | B2B sales | No (V1) | - | S-05 |
| C-24 Wire Transfers | Yes | Yes | No | Yes | High ticket B2B | No (V1) | - | S-02 |
| C-25 Tax Exemptions (B2B) | Yes | Yes | No | Yes | VAT reverse charge | Yes | REQ-TAX-02 | S-02 |
| C-26 Customer portal | Yes | Yes | Yes | Yes | Self-serve invoice & subscription management | Yes | REQ-CUST-01 | S-04 |
| C-27 Web analytics | No | Yes | Yes | No | Tracking page views & conversions | Yes | REQ-REP-01 | S-06 |
| C-28 Email newsletters | No | Yes | Yes | No | Marketing to audience | No | - | S-06 |
| C-29 Discord/Slack integration | No | Yes | Yes | No | Community access | No | - | S-06 |
| C-30 License activation limits | No | Yes | No | Yes | Restricting usage to N machines | Yes | REQ-LIC-03 | S-04 |
| C-31 License validity periods | No | Yes | No | Yes | 1-year updates model | Yes | REQ-LIC-04 | S-04 |
| C-32 Sub-users / Teams | Yes | No | No | Yes | B2B software purchasing | No | - | S-05 |
| C-33 Sandbox environment | Yes | Yes | No | Yes | Testing integration | No | - | S-02 |
| C-34 API access | Yes | Yes | Yes | Yes | Headless commerce | Yes | REQ-INT-02 | S-05 |
| C-35 Webhook signatures | Yes | Yes | Yes | Yes | Security | Yes | NFR-SEC-02 | S-05 |
| C-36 Payouts management | Yes | Yes | Yes | Yes | Sending money to creator | No (Single seller) | - | FROM-MEMORY |
| C-37 Multi-currency settlement | Yes | No | No | Yes | Receiving money in local currency | Yes | REQ-BIL-06 | S-05 |
| C-38 Chargeback dispute handling | Yes | Yes | No | Yes | Defending against fraud | No (Stripe handles) | - | S-02 |
| C-39 Fraud scoring | Yes | Yes | Yes | Yes | Blocking bad cards | No (Stripe handles) | - | S-02 |
| C-40 Sales tax calculation | Yes | Yes | Yes | Yes | Compliance | Yes | REQ-TAX-03 | S-02 |
| C-41 Usage billing metrics | Yes | Yes | No | Yes | Metered billing | No (V1) | - | S-05 |
| C-42 Prorated upgrades/downgrades| Yes | Yes | No | Yes | Changing plans mid-cycle | Yes | REQ-BIL-07 | S-05 |
| C-43 Subscription pauses | Yes | Yes | No | Yes | Retaining customers | Yes | REQ-BIL-08 | S-05 |
| C-44 Free trials | Yes | Yes | No | Yes | Acquisition | Yes | REQ-BIL-09 | S-05 |
| C-45 Freemium plans | Yes | Yes | No | Yes | Acquisition | Yes | REQ-BIL-10 | S-05 |
| C-46 Product variants | Yes | Yes | Yes | Yes | Different editions (Basic/Pro) | Yes | REQ-CAT-07 | S-06 |
| C-47 Inventory limits | No | Yes | Yes | No | Scarcity / limited run | Yes | REQ-CAT-08 | S-06 |
| C-48 File hosting/delivery | No | Yes | Yes | Yes | Delivering the software | Yes | REQ-FUL-01 | S-06 |
| C-49 Multi-file products | No | Yes | Yes | No | Providing Mac, Win, Linux binaries | Yes | REQ-FUL-02 | S-06 |
| C-50 Secure download links | No | Yes | Yes | Yes | Preventing link sharing | Yes | REQ-FUL-03 | S-06 |
| C-51 Download expiration | No | No | Yes | Yes | Encouraging backups | Yes | REQ-FUL-04 | S-06 |
| C-52 Apple/Google Pay | Yes | Yes | Yes | Yes | Quick checkout | Yes | REQ-PAY-07 | S-01 |
| C-53 Local payment methods | Yes | Yes | No | Yes | iDEAL, Alipay, etc. | Yes | REQ-PAY-08 | S-01 |
| C-54 Custom checkout fields | Yes | Yes | Yes | No | Collecting VAT numbers | Yes | REQ-PAY-09 | S-04 |
| C-55 Terms of Service checkbox | Yes | Yes | Yes | Yes | Legal compliance | Yes | REQ-PAY-10 | S-04 |
| C-56 GDPR Data request | Yes | Yes | Yes | Yes | Privacy compliance | Yes | NFR-SEC-03 | S-10 |
| C-57 Daily sales summary email | No | Yes | Yes | No | Motivation for seller | Yes | REQ-NOT-01 | S-06 |
| C-58 Revenue reporting | Yes | Yes | Yes | Yes | Accounting | Yes | REQ-REP-02 | S-05 |
| C-59 CSV Export | Yes | Yes | Yes | Yes | Bookkeeping | Yes | REQ-REP-03 | S-05 |
| C-60 Webhook retry policy | Yes | Yes | Yes | Yes | Reliability | Yes | NFR-AVAIL-01 | S-05 |

## Surprise Declaration
1. I didn't realize Merchant of Record (MoR) vs Payment Service Provider (PSP) was such a huge distinction; handling VAT globally is a massive liability. (S-02)
2. License key verification logic is often delegated to the platform, including offline-activations and machine fingerprinting. (S-04)
3. The necessity of handling B2B VAT reverse charges for European customers at checkout to avoid overcharging them. (S-02)

## Explicit Scope Boundary
- **Merchant of Record Services**: We will NOT act as a MoR. We use a PSP (evaluating Razorpay/Paddle/Lemon Squeezy in ADR-04); if PSP chosen, seller (me) handles tax liability. If MoR chosen, MoR handles tax.
- **Affiliate System**: Out of scope for V1 due to complexity of multi-party tracking.
- **Physical Goods**: strictly for digital goods and software licenses. No shipping, tracking, or inventory management.
- **Multi-tenant / Multi-seller**: As per user instruction, this is a single-seller application. No onboarding, no payouts, no seller dashboards. I am the sole owner.
