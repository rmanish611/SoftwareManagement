# Progress index

Exactly one line per phase, appended at G11 when the phase closes. This is the file a
fresh session reads in full to know where the build stands.

Format: `P<NN> | STATUS | tag <phase-NN-closed> | sha <short> | REQ range | tests <n> | cov <pct> | ADRs | BLK: <none|BLK-n> | next: <action>`

P01 | IN_PROGRESS | tag pending | sha pending | no REQ rows (guardrails) | tests 0 | cov n/a | - | BLK: none | next: P01 gate
