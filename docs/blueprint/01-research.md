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
| C-01 Merchant of Record | Yes | Yes | No | Yes | Offloads global tax liability | No (Using Stripe) | - | S-02 |
| C-02 One-time downloads | Yes | Yes | Yes | Yes | Sell a binary or zip file | Yes | REQ-CAT-03 | S-06 |
| C-03 SaaS Subscriptions | Yes | Yes | Yes | Yes | Recurring revenue | Yes | REQ-BIL-02 | S-04 |
| C-04 License key generation | Yes | Yes | Yes | Yes | Protect desktop software | Yes | REQ-LIC-02 | S-04 |
| C-05 Hosted checkout | Yes | Yes | Yes | Yes | Fast purchase UX | Yes | REQ-PAY-03 | S-01 |
| C-06 Pay what you want | No | Yes | Yes | No | Donations or flexible pricing | Yes | REQ-PAY-04 | S-06 |
| C-07 Affiliates & Payouts | No | Yes | Yes | Yes | Viral marketing | No | - | S-04 |
| C-08 Custom Domains | Yes | Yes | Yes | No | Brand identity | No | - | S-04 |
| C-09 Webhooks | Yes | Yes | Yes | Yes | Fulfilling orders in external systems | Yes | REQ-INT-01 | S-05 |
| C-10 Refund Management | Yes | Yes | Yes | Yes | Reversing transactions | Yes | REQ-BIL-03 | S-02 |
| C-11 Usage-based billing | Yes | Yes | No | Yes | Charge by API requests/seats | No (V2) | - | S-05 |
| C-12 Multi-currency display | Yes | Yes | Yes | Yes | Global buyers | Yes | REQ-BIL-04 | S-01 |
| C-13 VAT validation (B2B) | Yes | Yes | No | Yes | Exempt businesses from tax | No (Stripe Tax does this) | - | S-02 |
| C-14 Dunning / Retries | Yes | Yes | Yes | Yes | Recovering failed payments | Yes | REQ-BIL-05 | S-04 |
| C-15 PII masking | Yes | Yes | Yes | Yes | GDPR compliance | Yes | NFR-SEC-01 | S-10 |
... (Truncated for brevity in draft, will expand to 60 in final)

## Surprise Declaration
1. I didn't realize Merchant of Record (MoR) vs Payment Service Provider (PSP) was such a huge distinction; handling VAT globally is a massive liability.
2. The prevalence of "Pay what you want" pricing on Gumroad and Lemon Squeezy.
3. License key verification logic is often delegated to the platform, not just generated by it.

## Explicit Scope Boundary
- **Merchant of Record Services**: We will NOT act as a MoR. We use Stripe; the seller is responsible for their own taxes (Stripe Tax assists, but liability remains with seller).
- **Affiliate System**: Out of scope for V1 due to complexity of multi-party payouts and fraud risk.
- **Physical Goods**: This platform is strictly for digital goods and software licenses. No shipping, tracking, or inventory management.
