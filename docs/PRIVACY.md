# Privacy and personal data

Posture: India's Digital Personal Data Protection Act 2023 (A-16). The owner is the data
fiduciary. This file is the PII inventory required by NFR-PRIV-01 and must be updated in the
same commit as any schema change that adds or removes a personal-data column.

## What personal data this system holds

| Data | Entity and column | Purpose | Retention | Erasure behaviour |
|---|---|---|---|---|
| Name | Lead.FullName, Contact.FullName, TeamMember.FullName | Contacting an enquirer or customer | 3 years after last activity | Anonymised |
| Email address | Lead.Email, Contact.Email, Unsubscribe.EmailAddress, LoginAttempt.EmailAttempted | Replying, quoting, invoicing, suppression | 3 years after last activity; login attempts 12 months | Anonymised. The suppression list is retained because it is itself a privacy control |
| Phone number | Lead.Phone, Contact.Phone | Contacting an enquirer | 3 years after last activity | Anonymised |
| Company and job title | Lead.CompanyName, Contact.JobTitle, Organisation.LegalName | Qualifying and invoicing | While the commercial relationship exists | Retained where an invoice requires it |
| Message text | Lead.Message, FormSubmission.PayloadJson | Understanding the enquiry | 3 years after last activity | Anonymised |
| IP address | FormSubmission.IpAddress, ConsentRecord.IpAddress, LoginAttempt.IpAddress, RefreshToken.CreatedByIp | Abuse prevention and proof of consent | 12 months, except consent records | Consent records are immutable proof and are retained |
| User agent | FormSubmission.UserAgent | Abuse prevention | 12 months | Deleted with the submission |
| Consent record | ConsentRecord, every column | Proof that consent was given and for what purpose | 7 years | Never mutated, never deleted |
| Billing identifiers | Organisation.Gstin and the address columns | Statutory invoicing | 7 years | Retained: tax law requires it |
| Email content and delivery result | OutboxEmail, EmailDeliveryLog | Proving what was sent and what the mail server said | 12 months for delivery logs | Deleted on schedule |

## Rights and how each is served

- **Access**: an export by email address returns every record holding that address (REQ-ADM-008).
- **Correction**: the lead or contact is edited directly and the change is audited.
- **Erasure**: personal fields are replaced with anonymised values while invoices and audit
  rows survive, because tax law requires them (REQ-ADM-009). Report counts do not change.
- **Consent withdrawal**: the suppression list stops non-transactional mail (BR-NOTIF-04).
  Transactional messages such as a quote or an invoice are still sent.

## Rules the code must keep

- Consent is an unticked checkbox. Its exact text, version, purpose, timestamp and IP are
  stored with the submission and never mutated (BR-LEAD-06, NFR-PRIV-02).
- No third-party script, cookie or beacon loads before consent, and the choice persists for
  180 days and is revocable from the footer (NFR-PRIV-04).
- Logs never contain a password, token, demo credential, full email address or full IP
  address. Emails are masked and IPv4 addresses truncated to a /24 (NFR-PRIV-03).
- Personal data is stored in an India region (A-17).
- Retention runs automatically and is audited; it is not a manual promise (REQ-ADM-010).

## If there is a breach

The owner is the responsible person. Contain it, establish what data was involved using the
audit log and the delivery log, notify the affected people and the Data Protection Board as
the Act requires, and record the incident, its cause and the fix in this file.
