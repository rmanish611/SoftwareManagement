# Progress index

Exactly one line per phase, appended at G11 when the phase closes. This is the file a
fresh session reads in full to know where the build stands.

Format: `P<NN> | STATUS | tag <phase-NN-closed> | sha <short> | REQ range | tests <n> | cov <pct> | ADRs | BLK: <none|BLK-n> | next: <action>`

P01 | DONE | tag phase-01-closed | sha f5be796 | no REQ rows (guardrails) | tests 0 | cov n/a | ADR-00..ADR-22 frozen | BLK: none | next: P02 start B1
