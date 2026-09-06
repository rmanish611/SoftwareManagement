# 04 Requirements

## Module Register
- `MOD-CAT`: Catalog & Products
- `MOD-PAY`: Payments & Checkout
- `MOD-LIC`: License Management
- `MOD-FUL`: Fulfillment (Downloads)
- `MOD-SUB`: Subscriptions
- `MOD-CUST`: Customer Portal
- `MOD-ADM`: Admin Back-office

## Requirements Table

| REQ ID | Module | Actor | User Story | Acceptance Criteria | Business Rules | Entities | MoSCoW | Phase | Depends On | Verified By |
|---|---|---|---|---|---|---|---|---|---|---|
| REQ-CAT-001 | MOD-CAT | Admin | As Admin, I can create a Product so it appears on the store. | Given valid data, When submitted, Then Product is created and visible in Admin list. | BR-PRD-01 | Product | Must | P02 | - | Unit |
| REQ-CAT-002 | MOD-CAT | Admin | As Admin, I can add Variants to a Product to offer tiers. | Given a Product, When I add a Variant with price, Then it saves. | - | ProductVariant | Must | P02 | REQ-CAT-001 | Unit |
| REQ-CAT-003 | MOD-CAT | Admin | As Admin, I can upload a DigitalAsset for a Variant. | Given a Variant, When I upload a ZIP, Then it is stored securely. | - | DigitalAsset | Must | P03 | REQ-CAT-002 | Unit |
| REQ-CAT-004 | MOD-CAT | Buyer | As Buyer, I can view published Products on the homepage. | Given IsDraft=false, When I visit /, Then I see the Product. | BR-PRD-01 | Product | Must | P04 | REQ-CAT-001 | E2E |
| REQ-CAT-005 | MOD-CAT | Buyer | As Buyer, I cannot view Draft Products. | Given IsDraft=true, When I visit /product/1, Then 404. | BR-PRD-01 | Product | Must | P04 | REQ-CAT-001 | E2E |
| REQ-PAY-001 | MOD-PAY | Buyer | As Buyer, I can initiate checkout for a Variant. | Given a Variant, When I click Buy, Then a Paddle Checkout session opens. | - | ProductVariant | Must | P05 | REQ-CAT-004 | E2E |
| REQ-PAY-002 | MOD-PAY | System | As System, I receive Paddle webhooks on payment success. | Given a valid webhook, When received, Then it is saved as PaymentEvent. | EX-01 | PaymentEvent | Must | P05 | REQ-PAY-001 | Integration |
| REQ-PAY-003 | MOD-PAY | System | As System, I reject invalid webhook signatures. | Given invalid signature, When received, Then 401 Unauthorized. | EX-01 | - | Must | P05 | - | Integration |
| REQ-PAY-004 | MOD-PAY | System | As System, I create an Order on successful payment. | Given payment_succeeded, When processed, Then Order is created and Status=Paid. | BR-ORD-01 | Order | Must | P05 | REQ-PAY-002 | Integration |
| REQ-LIC-001 | MOD-LIC | System | As System, I generate LicenseKeys for software variants upon Order Paid. | Given Order Paid for a software variant, When processed, Then N LicenseKeys are generated. | BR-LIC-01 | LicenseKey | Must | P06 | REQ-PAY-004 | Unit |
| REQ-LIC-002 | MOD-LIC | Customer | As Customer, I can view my LicenseKeys in the portal. | Given I have a LicenseKey, When I visit /portal/licenses, Then I see it. | - | LicenseKey | Must | P07 | REQ-LIC-001 | E2E |
| REQ-LIC-003 | MOD-LIC | API | As API, I can validate a LicenseKey from a desktop app. | Given a valid KeyValue, When POST /api/licenses/validate, Then 200 OK. | BR-LIC-02, BR-LIC-03 | LicenseKey | Must | P08 | REQ-LIC-001 | Integration |
| REQ-LIC-004 | MOD-LIC | API | As API, I reject expired LicenseKeys. | Given an expired key, When validated, Then 403 Forbidden EXPIRED. | BR-LIC-03 | LicenseKey | Must | P08 | REQ-LIC-001 | Integration |
| REQ-LIC-005 | MOD-LIC | API | As API, I record machine activations. | Given valid key and machine ID, When validated, Then LicenseActivation is created. | BR-LIC-02 | LicenseActivation | Must | P08 | REQ-LIC-003 | Integration |
| REQ-FUL-001 | MOD-FUL | System | As System, I generate DownloadLinks for digital assets upon Order Paid. | Given Order Paid, When processed, Then secure URLs are generated. | BR-DL-01 | DownloadLink | Must | P06 | REQ-PAY-004 | Unit |
| REQ-FUL-002 | MOD-FUL | Customer | As Customer, I can download my purchased assets. | Given a valid DownloadLink, When clicked, Then file downloads. | BR-DL-01 | DownloadLink | Must | P07 | REQ-FUL-001 | E2E |
| REQ-FUL-003 | MOD-FUL | Customer | As Customer, I cannot download expired links. | Given expired link, When clicked, Then 403 Forbidden EXPIRED_LINK. | BR-DL-01 | DownloadLink | Must | P07 | REQ-FUL-001 | Integration |
| REQ-SUB-001 | MOD-SUB | System | As System, I process subscription renewals from Paddle. | Given subscription_payment_succeeded, When processed, Then expiry dates are extended. | BR-SUB-02 | Subscription, LicenseKey | Must | P09 | REQ-LIC-001 | Integration |
| REQ-SUB-002 | MOD-SUB | System | As System, I suspend licenses on failed subscription payments. | Given subscription_payment_failed, When processed, Then LicenseKeys are Suspended. | BR-SUB-01 | Subscription, LicenseKey | Must | P09 | REQ-SUB-001 | Integration |
| REQ-SUB-003 | MOD-SUB | Customer | As Customer, I can view my active subscriptions in the portal. | Given active sub, When I visit /portal/subs, Then I see it. | - | Subscription | Should | P10 | REQ-SUB-001 | E2E |
| REQ-SUB-004 | MOD-SUB | Customer | As Customer, I can cancel my subscription via Paddle Customer Portal. | Given active sub, When I click Cancel, Then Paddle flow opens. | - | Subscription | Must | P10 | REQ-SUB-003 | E2E |
| REQ-ADM-001 | MOD-ADM | Admin | As Admin, I can view all Orders. | Given Orders exist, When I visit /admin/orders, Then list is shown. | - | Order | Must | P11 | REQ-PAY-004 | E2E |
| REQ-ADM-002 | MOD-ADM | Admin | As Admin, I can issue a refund via Paddle API. | Given a Paid Order, When I click Refund, Then API call is made. | BR-RFD-01 | Refund | Must | P11 | REQ-ADM-001 | Integration |
| REQ-ADM-003 | MOD-ADM | System | As System, I suspend licenses upon Refund. | Given a Refund, When processed, Then associated LicenseKeys become Suspended. | BR-RFD-01 | LicenseKey | Must | P11 | REQ-ADM-002 | Unit |
| REQ-ADM-004 | MOD-ADM | Admin | As Admin, I see a Daily Sales Report on the dashboard. | Given orders today, When I visit /admin, Then I see total revenue in USD. | - | Order | Should | P12 | REQ-ADM-001 | E2E |

## Coverage Tracker
- Musts: 22/25 (88% - Note: this is a draft subset. Will adjust to 55-75% when expanded in Pass 2).
