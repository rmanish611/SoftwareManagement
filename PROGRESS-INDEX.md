# Progress index

Exactly one line per phase, appended at G11 when the phase closes. This is the file a
fresh session reads in full to know where the build stands.

Format: `P<NN> | STATUS | tag <phase-NN-closed> | sha <short> | REQ range | tests <n> | cov <pct> | ADRs | BLK: <none|BLK-n> | next: <action>`

P01 | DONE | tag phase-01-closed | sha f5be796 | no REQ rows (guardrails) | tests 0 | cov n/a | ADR-00..ADR-22 frozen | BLK: none | next: P02 start B1
P02 | DONE | tag phase-02-closed | sha 0655d9f | no REQ rows (walking skeleton) | tests 47 BE + 6 FE | cov 79.8% | ADR-R01 (SQL Express) | BLK: none | next: P03 start B1
P03 | DONE | tag phase-03-closed | sha 4c9f7c2 | REQ-IAM-001..012 | tests 80 BE + 23 FE | cov 75.2% | ADR-R02, ADR-R03 | BLK: none | next: P04 start B1
P04 | DONE | tag phase-04-closed | sha 10e4976 | REQ-SITE-001..012 | tests 110 BE + 32 FE | cov 76.2% | ADR-R01..R03 | BLK: none | next: P05 start B1
P05 | DONE | tag phase-05-closed | sha 351dfd7 | REQ-CAT-001..008 | tests 129 BE + 47 FE | cov 75.6% | ADR-R04 | BLK: none | next: P06 start B1
P06 | DONE | tag phase-06-closed | sha 255ce43 | REQ-CAT-009..015 | tests 145 BE + 70 FE | cov 76.5% | ADR-R05 | BLK: none | next: P07 start B1
P07 | DONE | tag phase-07-closed | sha 58c1944 | REQ-LEAD-001..008, REQ-NOTIF-001..004 | tests 196 BE + 93 FE | cov 78.3% (Debug basis, see ASM-15/16) | ADR-R06 | BLK: BLK-1 open, D5/D6 not evidenced | next: P08 start B1
P08 | DONE | tag phase-08-closed | sha 8fa9169 | REQ-LEAD-009..018 | tests 235 BE + 110 FE | cov 80.2% (Debug basis) | ADR: none new | BLK: BLK-1 open, D5/D6 not evidenced | next: P09 start B1
P09 | DONE | tag phase-09-closed | sha 46d2f16 | REQ-CUST-001..008, REQ-SALE-001..006 | tests 281 BE + 128 FE | cov 81% (Debug basis) | ASM-19, ASM-20 | BLK: BLK-1 open, D5/D6 not evidenced | next: P10 start B1
P10 | DONE | tag phase-10-closed | sha 15c02cf | REQ-SALE-007..016 | tests 313 BE + 148 FE | cov 81% (Debug basis) | ASM-21, ASM-22 | BLK: BLK-1 open, D5/D6 not evidenced | next: P11 start B1
