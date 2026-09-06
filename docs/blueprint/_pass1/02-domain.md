# 02 Domain

## 1. Actors / Roles
| ID | Actor | Description |
|---|---|---|
| ACT-01 | **Buyer (Anonymous)** | Browses products, views pricing, initiates checkout. |
| ACT-02 | **Customer (Authenticated)** | Has purchased items, logs in to download files, view license keys, manage subscriptions. |
| ACT-03 | **Admin (Store Owner)** | The single seller (me). Creates products, sets pricing, views orders, issues refunds. |
| ACT-04 | **System (Background)** | Hangfire jobs. Retries failed webhooks, generates license keys, syncs Paddle events. |
| ACT-05 | **Payment Gateway (Paddle)** | Sends webhooks for successful payments, subscription renewals, refunds. |
| ACT-06 | **Support Auditor** | (For future expansion) Read-only access to investigate customer complaints. |
| ACT-07 | **API Consumer** | A customer's deployed software making API calls to validate license keys. |
| ACT-08 | **Analytics Engine** | Automated daily aggregation of sales data for the Admin dashboard. |

## 2. Entities
| ID | Entity | Description |
|---|---|---|
| E-01 | Product | The software or digital good being sold. |
| E-02 | ProductVariant | Editions of a product (e.g., Basic, Pro) or license types (1-year, lifetime). |
| E-03 | Order | A completed purchase transaction. |
| E-04 | OrderLineItem | Individual products within an Order. |
| E-05 | Customer | The purchaser, identified by email. |
| E-06 | LicenseKey | An alphanumeric string granting access to a ProductVariant. |
| E-07 | LicenseActivation | A record of a machine/IP activating a LicenseKey. |
| E-08 | Subscription | A recurring billing agreement for a ProductVariant. |
| E-09 | PaymentEvent | A webhook payload received from Paddle (audit trail). |
| E-10 | DigitalAsset | The physical file (zip, exe) associated with a Product. |
| E-11 | DownloadLink | A secure, time-bound, signed URL for downloading a DigitalAsset. |
| E-12 | DiscountCode | A promotional code reducing the price of an Order. |
| E-13 | Refund | A record of money returned to a Customer. |
| E-14 | Category | Grouping for Products in the storefront. |
| E-15 | WebhookDelivery | Log of webhooks sent from our system to external integrations. |
| E-16 | DailySalesReport | Aggregated sales metrics for the Admin dashboard. |
| E-17 | Feature | Capabilities listed on the product page (e.g., "Unlimited projects"). |
| E-18 | ProductVariantFeature | Junction linking Features to Variants for pricing tables. |
| E-19 | AuditLog | Append-only system audit trail for Admin actions. |
| E-20 | CustomerSession | Active login sessions for Customers. |
| E-21 | SystemSetting | Global key-value configuration for the storefront. |
| E-22 | OutboxMessage | Reliable transactional messaging queue table. |
| E-23 | EmailTemplate | Liquid/HTML templates for transactional emails. |
| E-24 | Cart | Transient state before a purchase becomes an Order. |
| E-25 | CartItem | Items in the Cart. |

## 3. Lifecycle State Machines
- **Order State**: `PendingPayment` -> `Paid` -> `Fulfilled` (Terminal) or `Refunded` (Terminal)
- **Subscription State**: `Active` -> `PastDue` -> `Canceled` (Terminal)
- **LicenseKey State**: `Active` -> `Suspended` (if refund/chargeback) -> `Expired` (if 1-year expires)

## 4. Business Rules
| ID | Rule | Description |
|---|---|---|
| BR-ORD-01 | Order Fulfillment | An Order transitions to Fulfilled ONLY when Paddle sends `payment_succeeded`. |
| BR-LIC-01 | License Generation | One LicenseKey is generated per Quantity in the OrderLineItem if the ProductVariant requires a license. |
| BR-LIC-02 | Activation Limit | A LicenseKey cannot be activated if `ActiveCount >= MaxActivations`. |
| BR-LIC-03 | Expiration | A 1-year LicenseKey expires exactly 365 days from the Order Paid date. |
| BR-SUB-01 | Sub Suspension | If Paddle sends `subscription_payment_failed`, Subscription state becomes `PastDue` and associated LicenseKeys are `Suspended`. |
| BR-SUB-02 | Sub Recovery | If Paddle sends `subscription_payment_succeeded` for a PastDue sub, state becomes `Active` and LicenseKeys are `Active`. |
| BR-RFD-01 | Refund Consequence | Refunding an Order immediately sets all associated LicenseKeys to `Suspended`. |
| BR-DL-01 | Secure Downloads | DownloadLinks expire 24 hours after generation and are bound to the Customer's IP address. |
| BR-DIS-01 | Discount Validation | A DiscountCode cannot be applied if `UsageCount >= MaxUses` or `CurrentDate > ExpiryDate`. |
| BR-PRD-01 | Product Visibility | Products marked `IsDraft = true` are 404 for Buyers but visible to Admin. |
*(Drafting first 10, will expand to 40 in final blueprint)*

## 5. Money & Billing Flows
- **Checkout**: Buyer initiates checkout -> System creates Paddle checkout session -> Buyer pays Paddle -> Paddle sends `payment_succeeded` webhook -> System creates Order, generates License Keys, sends receipt email.
- **Refunds**: Admin clicks Refund -> API calls Paddle Refund endpoint -> Paddle processes -> Webhook updates System -> License revoked.
- **Taxes**: Fully handled by Paddle as MoR.

## 6. Notifications Matrix
- `Order.Receipt`: Sent to Customer on `payment_succeeded`. Includes License Keys and Download links.
- `Subscription.Failing`: Sent to Customer on `subscription_payment_failed` (Dunning).
- `Admin.DailySummary`: Sent to Admin at 23:59 UTC aggregating total sales.
- **Retry**: SendGrid delivery failures are logged; Hangfire retries up to 3 times with exponential backoff.

## 7. Reporting
- **Operational**: Daily sales volume, refund rate, active subscriptions.
- **Managerial**: Monthly Recurring Revenue (MRR), Churn Rate.
- **Statutory**: None (Paddle handles tax reporting).

## 8. Back-office & Master Data
- The Admin Panel is the back-office.
- Master data: Products, Variants, Features, Discount Codes.

## 9. Audit Trail
- Entity `AuditLog` captures `Timestamp, Actor, Action, EntityId, OldValues, NewValues`. Append-only.

## 10. Compliance & Data-Protection
- PII (Email, IP) is masked in application logs.
- Customer account deletion overwrites Email with `deleted_<guid>@example.com` (Soft Delete).

## 11. Multi-tenancy
- **N/A - Single Tenant**. Per user instruction, this is a single-seller application.

## 12. Offline/Edge Cases
- Software validation requests fail-open if the API is unreachable (to prevent blocking legitimate users during downtime), but log the failure for async verification.

## 13. Failure & Exception Flows
| ID | Exception Flow | Resolution |
|---|---|---|
| EX-01 | Paddle webhook signature invalid | Reject with 401, log security event. Do not process. |
| EX-02 | Database unreachable during webhook | Return 500 to Paddle so Paddle retries later. |
| EX-03 | Email delivery fails | Catch exception, enqueue Hangfire retry job. |
| EX-04 | User tries to activate maxed license | Return 403 Forbidden with `MAX_ACTIVATIONS_REACHED`. |
*(Drafting first 4, will expand to 40 in final blueprint)*

## 14. Integrations & Devices
- **Paddle**: Payment Gateway and MoR.
- **SendGrid**: Transactional Emails.

## 15. Time/Calendar Rules
- All datetimes stored in UTC. Admin dashboard renders charts in configured local timezone.
- Subscriptions billing cycles are anchored to the UTC purchase timestamp.

## 16. Search & Discovery
- Products are searchable by Title on the storefront.
- Admin can search Orders by Customer Email or Order ID.

## 17. Data Migration
- Day-one onboarding requires manual creation of initial Products via Admin UI. No Excel import required for V1.

## 18. Human Process
- Support inquiries from Customers via email (external to system). Admin issues refunds via the Admin UI to resolve complaints.
