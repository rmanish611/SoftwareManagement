# 01 Research

`RESEARCH_MODE: LIVE`. Every row below was retrieved in this session on **2026-09-07 between 22:45Z and 23:15Z**, either by the WebFetch tool or by `curl.exe` followed by local text extraction (`_evidence/phase-00/research-access.md` holds the three-domain access proof). Quotes are copied from the retrieved page text; whitespace was collapsed and nothing else was changed. Rows whose quote could not be reproduced from the fetched bytes were deleted rather than reworded.

## Source Ledger

| S-ID | URL | Class | Retrieved (UTC) | Fetch status | Page title exactly as returned | VERBATIM QUOTE | HARD SPECIFIC | Used for | Confidence |
|---|---|---|---|---|---|---|---|---|---|
| S-01 | https://www.w3.org/TR/WCAG22/ | 6 | 2026-09-07T22:46Z | 200 | Web Content Accessibility Guidelines (WCAG) 2.2 | "Web Content Accessibility Guidelines (WCAG) 2.2 defines how to make web content more accessible to people with disabilities. Accessibility involves a wide range of disabilities, including visual, auditory, physical, speech, cognitive, language, learning, and neurological disabilities." | W3C Recommendation 12 December 2024; nine new criteria incl. 2.5.8 Target Size (Minimum) AA; 1.4.3 contrast 4.5:1 | NFR-ACC-01, NFR-ACC-02, REQ-SITE-012 | High |
| S-02 | https://www.sitemaps.org/protocol.html | 6 | 2026-09-07T22:46Z | 200 | Sitemaps XML format | "You can provide multiple Sitemap files, but each Sitemap file that you provide must have no more than 50,000 URLs and must be no larger than 50MB (52,428,800 bytes)." | 50,000 URLs and 50MB (52,428,800 bytes) per sitemap file | REQ-INT-006, NFR-SEO-01 | High |
| S-03 | https://schema.org/SoftwareApplication | 6 | 2026-09-07T22:46Z | 200 | SoftwareApplication - Schema.org Type | "A software application. Property Expected Type Description Properties from SoftwareApplication applicationCategory Text or URL Type of software application, e.g. 'Game, Multimedia'. applicationSubCategory Text or URL Subcategory of the application, e.g. 'Arcade Game'." | Property names: applicationCategory, featureList, screenshot, softwareVersion, operatingSystem, offers, releaseNotes | REQ-CAT-014, REQ-INT-007 | High |
| S-04 | https://developers.cloudflare.com/turnstile/get-started/server-side-validation/ | 6 | 2026-09-07T23:02Z | 200 | Validate the token · Cloudflare Turnstile docs | "An attacker can submit any string to your form endpoint without completing a challenge. Tokens expire. Each token is valid for 300 seconds (5 minutes) after generation. Each token can only be validated once. A replayed token will be rejected with the timeout-or-duplicate error code." | Token valid 300 seconds, single use; replay returns `timeout-or-duplicate`; POST to `https://challenges.cloudflare.com/turnstile/v0/siteverify` | REQ-LEAD-004, BR-LEAD-02, EX-183 | High |
| S-05 | https://www.rfc-editor.org/rfc/rfc9457.html | 6 | 2026-09-07T22:46Z | 200 | RFC 9457: Problem Details for HTTP APIs | "The canonical model for problem details is a JSON object. When serialized in a JSON document, that format is identified with the 'application/problem+json' media type" | Media type `application/problem+json`; members type, title, status, detail, instance; guidance against exposing a stack dump | NFR-OBS-03, ADR-05, REQ-ADM-012 | High |
| S-06 | https://docs.umbraco.com/umbraco-cms/manage-and-publish-content/publishing-and-workflow/editorial-tools/scheduled-publishing | 2 | 2026-09-07T23:02Z | 200 | Scheduled Publishing \| CMS \| Umbraco Documentation | "Your server may be in a different timezone than where you are located. You are able to select a date and time in your timezone and Umbraco will make sure that the item gets published at that time. So, if you select 12 PM then the item will be published at 12PM in the timezone you are in." | Editor picks local time, server converts; the dialog shows the server-time equivalent (12 PM local = 8 PM server) | A-08, REQ-SITE-006, BR-SITE-04 | High |
| S-07 | https://docs.strapi.io/cms/features/draft-and-publish | 2 | 2026-09-07T22:46Z | 200 | Draft & Publish \| Strapi 5 Documentation | "Your content can have 3 statuses: Published: The content was previously published. There are no pending draft changes saved. Modified: The content was previously published. You made some changes to the draft version and saved these changes, but the changes have not been published yet. Draft: The content has never been published yet." | Exactly three states: Draft, Modified, Published; reserved attribute name `status`; relations to unpublished content break the public API | A-22, REQ-SITE-004, BR-SITE-01, EX-104 | High |
| S-08 | https://www.odoo.com/documentation/18.0/applications/sales/crm/acquire_leads/convert.html | 2 | 2026-09-07T22:45Z | 200 | Convert leads into opportunities - Odoo 18.0 documentation | "When merging, Odoo gives priority to whichever lead/opportunity was created in the system first, merging the information into the first created lead/opportunity." | Merge keeps the earliest-created record; on conversion the customer is either created, linked to an existing one, or not linked at all | REQ-LEAD-016, REQ-CUST-004, BR-LEAD-08 | High |
| S-09 | https://learn.microsoft.com/en-us/dynamics365/sales/create-edit-lead-sales | 1 | 2026-09-07T23:10Z | 200 | Create or edit leads \| Microsoft Learn | "Create leads in Dynamics 365 to track potential new customers. A lead can be an existing client or someone you've never done business with before. You might get leads from many sources, such as advertising, networking, or email campaigns. You can add notes, activities, and related contacts to your leads." | Only Topic and Last name are required on a lead; qualifying auto-populates First Name, Last Name, Job Title, Business Phone, Mobile Phone and Email from the linked contact | REQ-LEAD-001, REQ-LEAD-015, REQ-CUST-004 | High |
| S-10 | https://support.atlassian.com/jira-service-management-cloud/docs/what-are-request-types/ | 1 | 2026-09-07T23:12Z | 200 | Categorize customer requests into request types \| Jira Service Management Cloud \| Atlassian Support | "Request types are the types of requests that your customers can create. They help you to define and organize incoming requests so that your team can help customers more efficiently." | Request types carry their own field set and are grouped into portal groups so customers can find the right one | REQ-LEAD-002, REQ-LEAD-003, ADR-11 | High |
| S-11 | https://knowledge.hubspot.com/forms/create-forms | 1 | 2026-09-07T23:10Z | 200 | Create forms (legacy) | "Add form fields to collect information from your website visitors and contacts. You can also add rich text areas between form fields to add customizable text, create headers, or add spacing to your form." | A form can be bound to a template whose submissions open a ticket in the conversations inbox or help desk | REQ-LEAD-002, REQ-LEAD-007 | High |
| S-12 | https://www.hubspot.com/pricing/marketing | 3 | 2026-09-07T22:45Z | 200 | Marketing Software Pricing \| HubSpot | "Marketing Hub Generate leads and automate marketing that drives growth Calculate your price Free Start generating and emailing new leads, and measuring your success - for free $0/mo Free for up to 2 users. No credit card required. Get started free See Details Starter" | Free tier capped at 2 users and 2,000 email sends per month; Professional $800/mo with 2,000 marketing contacts | NFR-DATA-01, REQ-NOTIF-008, scope boundary | Medium |
| S-13 | https://www.pipedrive.com/en/pricing | 3 | 2026-09-07T23:02Z | 200 | CRM Pricing Plans \| Affordable CRM Software Costs \| Pipedrive | "Plans built to help you close more deals, faster Free 14-day trial. No commitment - you can make changes anytime. Billed monthly Billed annually (Save up to 26%) Lite Now with AI Organize your sales in one simple, intuitive workspace US$ 14 One payment of US$ 168 per seat/year" | Lite at US$14 per seat per month billed annually (US$168/seat/year); 14-day trial with no credit card; annual saving up to 26% | REQ-SALE-002, REQ-CAT-006, BR-SALE-03 | High |
| S-14 | https://calendly.com/pricing | 3 | 2026-09-07T23:10Z | 200 | Pricing \| Calendly | "Billed yearly Billed monthly Billed yearly Save up to 20% Free Always free Get started Includes: Scheduling One event type One calendar connection One-on-one scheduling Customizable booking page Browser extension Standard Add Notetaker & Callie $10 /seat/mo Seats are required for users to connect calendars and host Calendly meetings - meeting invitees do not require a seat." | Free tier capped at one event type and one calendar connection; Standard $10/seat/mo; Enterprise starts at $15k/yr and 50 seats | REQ-LEAD-011, ADR-12 | High |
| S-15 | https://www.calbar.ca.gov/Portals/0/documents/rfp/2024/RFP-Website-Redesign.pdf | 4 | 2026-09-07T22:55Z | 200 (PDF, text extracted locally) | REQUEST FOR PROPOSAL - Website Redesign (The State Bar of California) | "One of the goals of this RFP is to evaluate the current version of the existing CMS platform, understand its usage, capabilities, and scalability, and determine its ability to meet the desired requirements described in this RFP." | Section H "Content Management System Evaluation Phase"; the same document makes search-engine-friendly URLs, canonical URLs, page titles, description tags and semantic markup contractual requirements | REQ-SITE-011, NFR-SEO-01, NFR-MAINT-04 | High |
| S-16 | https://wordpress.org/support/topic/emails-from-website-contact-form-going-to-spam/ | 5 | 2026-09-07T22:52Z | 200 | Emails from website contact form going to spam | "Without proper configuration, the site may don't know how to send emails, or it may rely on the server that is hosting your site to send emails; hence why the sender shows it's from Hostgator." | Envelope sender belongs to the hosting server, not the site domain, so receiving servers spam-filter the notification | EX-261, EX-262, A-14, REQ-NOTIF-002 | High |
| S-17 | https://wordpress.org/support/topic/not-receiving-email-notifications-of-form-submissions/ | 5 | 2026-09-07T23:02Z | 200 | Not receiving email notifications of form submissions \| WordPress.org | "Overall, just not sure if any emails are being sent. Viewing 6 replies - 1 through 6 (of 6 total) Plugin Support Imran - WPMU DEV Support (@wpmudev-support9) 10 months ago Hello @jazzybearbb , I hope things are going great for you. Are you receiving the default emails from WordPress, such as plugin updates, password changes, and user account creation?" | The site owner cannot tell whether any mail was sent at all: there is no delivery log to look at | EX-263, REQ-NOTIF-005, REQ-RPT-008 | High |
| S-18 | https://angular.dev/reference/releases | 7 | 2026-09-07T22:46Z | 200 | Angular versioning and releases | "All major releases are typically supported for 24 months. Active support lasts 12 months with regularly-scheduled updates, followed by 12 months of long-term support with only critical fixes and security patches." | v22.0.0 released 2026-06-03, active support to 2027-06, LTS to 2028-06; v21 already in LTS | ADR-02, NFR-MAINT-01 | High |
| S-19 | https://learn.microsoft.com/en-us/aspnet/core/performance/rate-limit?view=aspnetcore-10.0 | 7 | 2026-09-07T22:46Z | 200 | Rate limiting middleware in ASP.NET Core \| Microsoft Learn | "The Microsoft.AspNetCore.RateLimiting middleware provides rate limiting middleware. Apps configure rate limiting policies and then attach the policies to endpoints. Apps using rate limiting should be carefully load tested and reviewed before deploying." | Four algorithms (fixed window, sliding window, token bucket, concurrency); options PermitLimit, Window, QueueLimit, QueueProcessingOrder; `RejectionStatusCode` and the 429 convention | NFR-SEC-05, REQ-LEAD-005, ADR-16 | High |
| S-20 | https://www.hangfire.io/pricing/ | 7 | 2026-09-07T23:02Z | 200 | Pricing - Hangfire | "Hangfire is completely free even for commercial use. Subscriptions below allow you to use additional options while ensuring the project will stay here for years to come. We provide 30 day unconditional money back guarantee." | Hangfire Core is LGPL and free for commercial use; Pro/Ace are paid from $500/year; SQL Server storage is in the free edition | ADR-13, licence risk table | High |
| S-21 | https://docs.fluentvalidation.net/en/latest/ | 7 | 2026-09-07T22:46Z | 200 | FluentValidation | "FluentValidation is a .NET library for building strongly-typed validation rules. FluentValidation 12 supports .NET 8 and newer (including .NET 10). If you need support for older runtimes, use FluentValidation 11 which runs on .NET Standard 2.0, .NET Core 3.1, .NET 5 and newer." | v12 targets .NET 8+ and explicitly supports .NET 10 | ADR-04, NFR-MAINT-02 | High |

`SOURCES_TOTAL=21`
`CLASS1=3 CLASS2=3 CLASS3=3 CLASS4=1 CLASS5=2 CLASS6=5 CLASS7=4`

Domain rows (cap 3 per domain): w3.org=1, sitemaps.org=1, schema.org=1, cloudflare.com=1, rfc-editor.org=1, umbraco.com=1, strapi.io=1, odoo.com=1, microsoft.com=2, atlassian.com=1, hubspot.com=2, pipedrive.com=1, calendly.com=1, calbar.ca.gov=1, wordpress.org=2, angular.dev=1, hangfire.io=1, fluentvalidation.net=1.

### Version and licence grounding

Every version below carries the command that produced it, run on this machine on 2026-09-07. Nothing is pinned that was not printed by a command.

| Package | Latest stable | Command | Licence | Verdict |
|---|---|---|---|---|
| @angular/cli, @angular/core | 22.1.7 / 22.1.5 | `npm view @angular/cli version`, `npm view @angular/core version` | MIT | ADOPT (v22 is in active support until 2027-06, S-18) |
| @angular/material + cdk | 22.1.5 | `npm view @angular/material version`; peer deps `@angular/core ^22.0.0 \|\| ^23.0.0` | MIT (`npm view @angular/material license`) | ADOPT (ADR-03) |
| @angular/ssr, @angular/build | 22.1.7 | `npm view @angular/ssr version` | MIT | ADOPT (SSR for public routes, A-23) |
| angular-eslint / typescript-eslint | 22.5.0 / 8.70.0 | `npm view angular-eslint version` | MIT | ADOPT |
| Microsoft.EntityFrameworkCore.SqlServer / .Design | 10.0.11 | `curl.exe -s https://api.nuget.org/v3-flatcontainer/microsoft.entityframeworkcore.sqlserver/index.json` | MIT | ADOPT |
| Microsoft.AspNetCore.Identity.EntityFrameworkCore | 10.0.11 | same, `.../microsoft.aspnetcore.identity.entityframeworkcore/index.json` | MIT | ADOPT |
| Microsoft.AspNetCore.Authentication.JwtBearer | 10.0.11 | same | MIT | ADOPT |
| Microsoft.AspNetCore.OpenApi | 10.0.11 | same | MIT | ADOPT (built-in OpenAPI document) |
| Swashbuckle.AspNetCore | 10.2.3 | same | MIT | ADOPT (Swagger UI only; the document comes from the built-in generator) |
| FluentValidation (+ DependencyInjectionExtensions) | 12.1.1 | same | Apache-2.0 | ADOPT (S-21) |
| Hangfire.Core / .SqlServer / .AspNetCore | 1.8.25 | same | LGPL-3.0, free for commercial use (S-20) | ADOPT with the licence recorded in ADR-13 |
| MailKit | 4.17.0 | same | MIT | ADOPT |
| Serilog.AspNetCore / Sinks.File | 10.0.0 / 7.0.0 | same | Apache-2.0 | ADOPT |
| xunit / xunit.runner.visualstudio / Microsoft.NET.Test.Sdk | 2.9.3 / 4.0.0 / 18.9.0 | same | Apache-2.0 / MIT | ADOPT |
| **FluentAssertions** | 8.10.0 | same | **Paid licence for commercial use from v8** | **REJECT** - replaced by AwesomeAssertions 9.6.0 (Apache-2.0 fork). Licence risk row below. |
| AwesomeAssertions | 9.6.0 | `curl.exe -s .../awesomeassertions/index.json` | Apache-2.0 | ADOPT |
| NSubstitute | 6.2.0 | same | BSD | ADOPT |
| coverlet.collector | 10.0.1 | same | MIT | ADOPT |
| Bogus | 35.6.5 | same | MIT | ADOPT (seed data for demos only, never in `src/**`) |
| Microsoft.AspNetCore.Mvc.Testing | 10.0.11 | same | MIT | ADOPT (integration tests) |
| SixLabors.ImageSharp | 4.1.1 | same | **Six Labors Split Licence - free only under revenue/company-size thresholds** | **FLAG for veto** - default is to use `System.Drawing`-free resizing via SkiaSharp 4.151.2 (MIT) instead |
| SkiaSharp | 4.151.2 | same | MIT | ADOPT for image resizing |

**Licence risk table (owner must veto or accept at the gate):**

| Package | Risk | Default I chose |
|---|---|---|
| FluentAssertions 8+ | Commercial use requires a paid licence; it throws no error, it is simply a licence breach | Not used. AwesomeAssertions (Apache-2.0) instead. |
| SixLabors.ImageSharp 4.x | Split licence with revenue and headcount thresholds | Not used. SkiaSharp (MIT). |
| Hangfire Pro / Ace | Paid from $500/year | Not used. Hangfire Core (LGPL) with SQL Server storage only. |
| Cloudflare Turnstile | Free, but a third-party dependency on every public form | Accepted, with a honeypot plus rate limit as the fallback if Turnstile is unreachable (EX-183). |

## Competitive Teardown Matrix

Products studied: **HubSpot** (marketing + forms + CRM), **Pipedrive** (sales pipeline), **Webflow** (visual CMS), **Strapi/Umbraco** (headless and .NET CMS), **Dynamics 365 Sales** and **Odoo CRM** (lead lifecycle), **Atlassian** (product index and request types), **Calendly** (meeting booking). Rows are raw, as each vendor names the capability.

| C-ID | Capability (as the vendor names it) | Seen in | Seen in RFP? | Why it exists (the real job) | In scope? | REQ ID | S-ID |
|---|---|---|---|---|---|---|---|
| C-01 | Drag-and-drop form builder | HubSpot | Yes | Marketing changes a form without a developer | v2 - forms are configuration rows, not a builder UI | REQ-LEAD-002 | S-11 |
| C-02 | Form templates per use case | HubSpot | No | Fast start for a new enquiry type | Yes | REQ-LEAD-002 | S-11 |
| C-03 | Conditional / progressive fields | HubSpot | No | Ask less on first contact | Won't (v1) | - | S-11 |
| C-04 | Multi-step forms | HubSpot | No | Higher completion on long forms | Won't (v1) | - | S-11 |
| C-05 | Spam filtering on submissions | HubSpot | Yes | The inbox stays usable | Yes | REQ-LEAD-004 | S-11 |
| C-06 | Submission opens a ticket in an inbox | HubSpot | Yes | Nothing is lost between the form and a human | Yes | REQ-LEAD-007 | S-11 |
| C-07 | Form analytics: views, submissions, conversion rate | HubSpot | Yes | Prove which page produces business | Yes | REQ-RPT-002 | S-11 |
| C-08 | Automated follow-up email to the submitter | HubSpot | Yes | The visitor knows they were heard | Yes | REQ-NOTIF-003 | S-11 |
| C-09 | Free tier capped at 2 users / 2,000 sends | HubSpot | No | Monetisation boundary; for us a volume reality check | Informs NFR only | - | S-12 |
| C-10 | Marketing contacts limit per tier | HubSpot | No | Pricing lever | No | - | S-12 |
| C-11 | Deal pipeline with stages | Pipedrive | Yes | See where every opportunity is | Yes | REQ-LEAD-009 | S-13 |
| C-12 | Per-seat pricing with annual discount | Pipedrive | Yes | How SaaS is actually sold | Yes - our own plans copy the model | REQ-CAT-006 | S-13 |
| C-13 | 14-day free trial, no credit card | Pipedrive | Yes | Remove friction before buying | Yes - trial subscriptions | REQ-SALE-005 | S-13 |
| C-14 | Web forms feeding the pipeline | Pipedrive | Yes | One path from website to sales | Yes | REQ-LEAD-008 | S-13 |
| C-15 | Activity reminders on a deal | Pipedrive | Yes | Nothing goes cold | Yes | REQ-LEAD-013 | S-13 |
| C-16 | Custom fields per entity | Pipedrive | Yes | Every business asks something extra | Won't (v1) - fixed schema, documented | - | S-13 |
| C-17 | Lead inbox separate from the pipeline | Pipedrive | Yes | Triage before committing to a deal | Yes | REQ-LEAD-008 | S-13 |
| C-18 | Round-robin lead assignment | Calendly/Pipedrive | Yes | Share work across a team | Won't (v1) - single owner (A-02) | - | S-14 |
| C-19 | Meeting scheduling link on the site | Calendly | Yes | Book the demo without email ping-pong | Yes - embed the owner's link | REQ-LEAD-011 | S-14 |
| C-20 | Qualify, route and schedule leads | Calendly | Yes | Send serious buyers to a slot immediately | Partial - qualification stage only | REQ-LEAD-010 | S-14 |
| C-21 | One event type / one calendar on the free tier | Calendly | No | Pricing lever | No | - | S-14 |
| C-22 | Booking page customisation | Calendly | No | Brand consistency | No - external tool | - | S-14 |
| C-23 | Request types with their own field sets | Atlassian | Yes | A demo request asks different questions from a support request | Yes - form definitions | REQ-LEAD-002 | S-10 |
| C-24 | Request types grouped into portal groups | Atlassian | Yes | Customers find the right form | Yes - form categories | REQ-LEAD-003 | S-10 |
| C-25 | Queues for the team | Atlassian | Yes | Work the list in a defined order | Yes - saved filters on the lead inbox | REQ-LEAD-012 | S-10 |
| C-26 | Product index grouped by persona, use case, industry and company size | Atlassian | Yes | Buyers arrive with a job, not a product name | Yes | REQ-CAT-013 | S-10 |
| C-27 | Product collections / suites | Atlassian | No | Cross-sell the bundle | Yes - product categories | REQ-CAT-002 | S-10 |
| C-28 | Lead entity distinct from Contact and Account | Dynamics 365 | Yes | An unqualified enquiry is not yet a customer | Yes | REQ-LEAD-001 | S-09 |
| C-29 | Minimal required fields on a lead (Topic, Last name) | Dynamics 365 | Yes | Capture beats completeness | Yes | BR-LEAD-01 | S-09 |
| C-30 | Qualify a lead into account + contact + opportunity | Dynamics 365 | Yes | The moment an enquiry becomes a deal | Yes | REQ-LEAD-015 | S-09 |
| C-31 | Auto-populate contact fields on qualification | Dynamics 365 | No | Stop re-typing | Yes | REQ-CUST-004 | S-09 |
| C-32 | Notes, activities and related contacts on a lead | Dynamics 365 | Yes | The history of the conversation | Yes | REQ-LEAD-012 | S-09 |
| C-33 | Lead source tracking | Dynamics 365 | Yes | Know which channel pays | Yes | REQ-LEAD-017 | S-09 |
| C-34 | Merge duplicate leads, earliest record wins | Odoo | Yes | Two forms, one human | Yes | REQ-LEAD-016 | S-08 |
| C-35 | Convert with or without linking a customer | Odoo | Yes | Not every enquiry has a company yet | Yes | REQ-CUST-004 | S-08 |
| C-36 | Lead as a qualifying step before an opportunity | Odoo | Yes | Filter noise before forecasting | Yes | REQ-LEAD-009 | S-08 |
| C-37 | Draft / Modified / Published content states | Strapi | Yes | Edit a live page without publishing it | Yes | REQ-SITE-004 | S-07 |
| C-38 | Reserved `status` attribute on content | Strapi | No | The API can ask for drafts or published | Yes | REQ-SITE-004 | S-07 |
| C-39 | Bulk publish / unpublish | Strapi | Yes | Launch a whole section at once | Yes | REQ-SITE-007 | S-07 |
| C-40 | Relations to unpublished content break the public API | Strapi | No | The classic broken-link-after-launch bug | Yes - publish-time validation | BR-SITE-03, EX-104 | S-07 |
| C-41 | Scheduled publishing and unpublishing | Umbraco | Yes | Announce at 09:00 without being awake | Yes | REQ-SITE-006 | S-06 |
| C-42 | Editor's timezone respected, server time shown | Umbraco | No | The 12 PM / 8 PM trap | Yes | BR-SITE-04 | S-06 |
| C-43 | Content versions and rollback | Umbraco | Yes | Undo a bad edit on a live site | Yes | REQ-SITE-005 | S-06 |
| C-44 | Permissions on who may schedule | Umbraco | Yes | Editors publish, juniors draft | Yes | AZ rows | S-06 |
| C-45 | Visual CMS collections and reusable templates | Webflow | Yes | Content people work without developers | Yes - typed content, admin UI | REQ-SITE-002 | - (FROM-MEMORY) |
| C-46 | Built-in SEO fields and clean semantic markup | Webflow / RFP | Yes | Search visibility is the point of the site | Yes | REQ-SITE-011 | S-15 |
| C-47 | Search-engine-friendly URLs, canonical URLs, title and description tags | State Bar RFP | Yes | Contractual in real tenders | Yes | REQ-SITE-011 | S-15 |
| C-48 | CMS platform evaluated for usage, capability and scalability | State Bar RFP | Yes | Buyers audit the CMS itself | Yes - ADR-01 records it | ADR-01 | S-15 |
| C-49 | Accessibility to persons with disabilities as an award criterion | State Bar RFP | Yes | Legal and moral obligation | Yes | NFR-ACC-01 | S-15 |
| C-50 | Analytics reviewed to understand user patterns | State Bar RFP | Yes | Decisions from data | Yes | REQ-RPT-001 | S-15 |
| C-51 | Style guide and pattern library as a deliverable | State Bar RFP | Yes | Consistency beyond launch day | Partial - design tokens + component library | NFR-MAINT-05 | S-15 |
| C-52 | Responsive across many screen sizes and devices | State Bar RFP | Yes | Most buyers arrive on a phone | Yes | NFR-BROW-01 | S-15 |
| C-53 | Sitemap file limits (50,000 URLs / 50MB) | sitemaps.org | No | Correctness of the generated sitemap | Yes | REQ-INT-006 | S-02 |
| C-54 | JSON-LD SoftwareApplication with featureList, screenshot, offers | schema.org | No | Rich results for product pages | Yes | REQ-INT-007 | S-03 |
| C-55 | Server-side captcha verification with single-use tokens | Cloudflare | Yes | Client-side widgets stop nothing | Yes | REQ-LEAD-004 | S-04 |
| C-56 | Idempotency key on captcha verification | Cloudflare | No | Retries must not fail a legitimate submit | Yes | BR-LEAD-02 | S-04 |
| C-57 | Machine-readable error format (`application/problem+json`) | RFC 9457 | No | Clients can handle errors; no stack traces leak | Yes | NFR-OBS-03 | S-05 |
| C-58 | Rate limiting with fixed window, sliding window, token bucket, concurrency | ASP.NET Core | Yes | Abuse protection on public endpoints | Yes | NFR-SEC-05 | S-19 |
| C-59 | Background job server with persistent storage | Hangfire | Yes | Email retries survive a restart | Yes | ADR-13 | S-20 |
| C-60 | Delivery log the owner can actually read | (absent everywhere; the complaint threads exist because of it) | Yes | "Did the email go out?" must be answerable | Yes | REQ-NOTIF-005 | S-17 |
| C-61 | Envelope sender aligned to the site domain (SPF/DKIM) | WordPress threads | Yes | Notifications reach the inbox, not spam | Yes | REQ-NOTIF-002 | S-16 |
| C-62 | Confirmation email to the submitter | WordPress threads | Yes | The visitor knows the form worked | Yes | REQ-NOTIF-003 | S-17 |
| C-63 | Per-seat plan comparison table on the pricing page | Pipedrive / HubSpot | Yes | Buyers self-qualify before contacting | Yes | REQ-CAT-007 | S-13 |
| C-64 | Public API catalogue with versions and status | (Atlassian/Microsoft developer portals) | Yes | Technical buyers check integration first | Yes | REQ-API-001 | S-03 |
| C-65 | Case studies with outcome metrics | Consulting sites in the RFP class | Yes | Proof that the company delivered before | Yes | REQ-PRJ-003 | S-15 |

## Surprise Declaration

Capabilities that exist in the products studied and were **not** in my first mental model of "a company website":

1. **Modified as a third content state** (Strapi, S-07). I would have built Draft and Published only. Without `Modified`, editing a live page either publishes half-finished text or forces the editor to unpublish the page first. Added as BR-SITE-01.
2. **The editor's timezone, not the server's** (Umbraco, S-06). Scheduled publishing that silently uses server time is the kind of bug nobody reports for months. Added as BR-SITE-04 with the dialog showing the server-time equivalent.
3. **Merge keeps the earliest-created record** (Odoo, S-08). My instinct was "keep the newest". Odoo's rule preserves the original enquiry date, which is what the SLA and the funnel report are measured against. Added as BR-LEAD-08.
4. **Turnstile tokens are single-use and expire in 300 seconds** (Cloudflare, S-04). A naive implementation that re-verifies on a retry gets `timeout-or-duplicate` and rejects a genuine customer. Added as BR-LEAD-02 with the idempotency key.
5. **Relations to unpublished content break the public API** (Strapi, S-07). Publishing a product that points at an unpublished plan or screenshot yields a broken public page. Added as BR-SITE-03: publish-time referential validation.
6. **A delivery log is the missing feature in every complaint thread** (S-16, S-17). Both threads are people who cannot answer "was the mail sent?". That is why REQ-NOTIF-005 exists.
7. **Request types carry their own field set and are grouped for findability** (Atlassian, S-10). "Contact us" is not one form; a demo request, a quote request and a partnership enquiry ask different questions.
8. **Only two fields are required on a lead** (Dynamics 365, S-09). Capture beats completeness: a long mandatory form loses the enquiry entirely.

## Explicit Scope Boundary - what exists in the market and I am deliberately NOT building

| Not building | Why |
|---|---|
| Online checkout / card payment on this site | A-04: money is taken outside the site in v1. The site records payments, it does not process them. |
| Licence keys, activation limits, secure download links | A-03: the products are hosted SaaS tenants, not downloadable software. This is the biggest single difference from the earlier pass-1 blueprint. |
| Multi-seller marketplace, seller onboarding, payouts | A-02: one company sells its own products. |
| A visual drag-and-drop page builder (Webflow-style) | Typed content with a fixed, well-designed admin is achievable and testable; a builder is a product in itself. |
| Custom fields defined at runtime (Pipedrive C-16) | A fixed, migrated schema is what makes the gates and tests meaningful. Revisit in v2. |
| Marketing automation, email campaigns, newsletters at scale | Transactional email only. Bulk sending brings deliverability and consent obligations the owner does not need yet. |
| Round-robin assignment, multi-rep territories | One owner today (A-02). |
| Live chat / chatbot | Adds a staffing promise the owner cannot keep; the SLA promise in A-28 is the honest alternative. |
| Customer self-service portal with login | Nothing in the brief requires a customer to log in; adding a second identity surface doubles the authz work. |
| Provisioning or hosting the demo/tenant instances | A-20, A-21: this site links and records, it does not deploy. |
| Full-text search engine (Elasticsearch/Lucene) | SQL Server full-text on a few hundred content items is enough at A-10 volumes. |
