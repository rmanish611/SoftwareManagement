# Progress index

Exactly one line per phase, appended at G11 when the phase closes. This is the file a
fresh session reads in full to know where the build stands.

Format: `P<NN> | STATUS | tag <phase-NN-closed> | sha <short> | REQ range | tests <n> | cov <pct> | ADRs | BLK: <none|BLK-n> | next: <action>`

P01 | DONE | tag phase-01-closed | sha f5be796 | no REQ rows (guardrails) | tests 0 | cov n/a | ADR-00..ADR-22 frozen | BLK: none | next: P02 start B1
P02 | DONE | tag phase-02-closed | sha 0655d9f | no REQ rows (walking skeleton) | tests 47 BE + 6 FE | cov 79.8% | ADR-R01 (SQL Express) | BLK: none | next: P03 start B1
P03 | DONE | tag phase-03-closed | sha 4c9f7c2 | REQ-IAM-001..012 | tests 80 BE + 23 FE | cov 75.2% | ADR-R02, ADR-R03 | BLK: none | next: P04 start B1
