# 06 Authorization (AuthZ)

## Roles
- **Admin**: The single seller (me). Full access to back-office.
- **Customer**: Authenticated buyer accessing their own purchases.
- **Anonymous**: Unauthenticated public internet.
- **System**: Internal background workers (Hangfire) and Paddle webhook calls.

## Permissions Catalogue
- `Products.ReadPublished`
- `Products.Manage`
- `Orders.ReadOwn`
- `Orders.Manage`
- `Licenses.ReadOwn`
- `Licenses.Validate`
- `Webhooks.Process`

## Authorization Matrix
One row per `Resource : Action`.

| ID | Resource : Action | Admin | Customer | Anonymous | System | Enforcement Point |
|---|---|---|---|---|---|---|
| AZ-01 | `Products : ReadPublished` | Y | Y | Y | Y | Endpoint filter (AllowAnonymous) + EF Global Query Filter (`IsDraft == false` for non-admins) |
| AZ-02 | `Products : ReadDrafts` | Y | N | N | N | Endpoint filter (`[Authorize(Roles="Admin")]`) |
| AZ-03 | `Products : Create/Update` | Y | N | N | N | Endpoint filter (`[Authorize(Roles="Admin")]`) |
| AZ-04 | `Orders : ReadList` | Y | Y-OWN | N | N | Endpoint filter (`[Authorize]`) + EF Query Filter (`CustomerId == User.Id`) |
| AZ-05 | `Orders : ReadAll` | Y | N | N | N | Endpoint filter (`[Authorize(Roles="Admin")]`) |
| AZ-06 | `Orders : Refund` | Y | N | N | N | Endpoint filter (`[Authorize(Roles="Admin")]`) |
| AZ-07 | `Licenses : ReadList` | Y | Y-OWN | N | N | Endpoint filter (`[Authorize]`) + EF Query Filter (`CustomerId == User.Id`) |
| AZ-08 | `Licenses : Validate` | Y | Y | Y | Y | Endpoint filter (AllowAnonymous, auth is the LicenseKey itself) |
| AZ-09 | `Webhooks : Receive` | N | N | Y-AUDIT | Y | Endpoint filter (AllowAnonymous, but requires Ed25519 Paddle signature validation) |
| AZ-10 | `Dashboard : Read` | Y | N | N | N | Endpoint filter (`[Authorize(Roles="Admin")]`) |

## Anonymous Allowlist (anonAllowlist)
These exact paths are permitted to answer anonymously without a 401:
- `GET /api/health`
- `POST /api/auth/login`
- `POST /api/auth/refresh`
- `GET /api/products` (List published products)
- `GET /api/products/{id}` (Read single published product)
- `POST /api/webhooks/paddle` (Paddle payload with signature in header)
- `POST /api/licenses/validate` (Software pinging license server)
