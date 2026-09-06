# Software Management

The public corporate website of a software company, built the way large IT companies present themselves: company profile, services and technology stack; a catalogue of the company's own software products (multi-tenant SaaS such as ERP, hospital management, billing, school and college management) each with a product page (overview, feature list, screenshots, pricing plans, live-demo link, public API/docs listing); a portfolio of delivered projects and public APIs; and lead capture (contact, request a demo, request a quote or a tenant). The owner qualifies and follows up leads, records the tenants and subscriptions he sells, and manages all site content from an admin panel.

It is a showcase + lead-generation + sales-tracking site for the company's own products. It is NOT an IT software-asset or licence-management tool, NOT a marketplace for other sellers, and NOT the ERP/HMS products themselves.

## Stack
- Frontend: Angular
- Backend: ASP.NET Core Web API
- Database: SQL Server LocalDB (dev) / SQL Server (prod)

## Blueprint
This project is defined entirely by the blueprint documents in `docs/blueprint/`.
The operating protocol is `docs/PROTOCOL.md` (mirrored as `AGENTS.md` for agent tooling).
Do not alter the architecture without amending the blueprint through the procedure the protocol defines.
