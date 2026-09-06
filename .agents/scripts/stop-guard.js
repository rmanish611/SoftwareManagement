// Antigravity "Stop" hook. Contract: JSON in on stdin, JSON out on stdout.
// Re-enters the agent loop while STATE.json says the autonomous run is live.
// Allows stopping at the approval gate, at hard stops, when state is not
// moving (stuck), and after an absolute ceiling. ANY error => allow stop ({}).
'use strict';
const fs = require('fs');
const path = require('path');
function out(o) { process.stdout.write(JSON.stringify(o)); }
let raw = '';
process.stdin.setEncoding('utf8');
process.stdin.on('data', d => { raw += d; });
process.stdin.on('end', () => {
  try {
    const inp = raw ? JSON.parse(raw) : {};
    if (inp.terminationReason && inp.terminationReason !== 'model_stop') return out({});
    if (inp.fullyIdle === false) return out({});
    const ws = (inp.workspacePaths && inp.workspacePaths[0]) || path.resolve(process.cwd(), '..');
    const stateFile = path.join(ws, 'STATE.json');
    if (!fs.existsSync(stateFile)) return out({});
    const S = JSON.parse(fs.readFileSync(stateFile, 'utf8'));

    // stuck / runaway protection
    const guardFile = path.join(process.cwd(), '.stop-guard.json');
    let g = { lastSeen: '', same: 0, total: 0 };
    try { g = JSON.parse(fs.readFileSync(guardFile, 'utf8')); } catch (e) {}
    const stamp = String(S.lastUpdatedUtc || '');
    g.same = (stamp && stamp === g.lastSeen) ? (g.same + 1) : 0;
    g.lastSeen = stamp;
    g.total = (g.total || 0) + 1;
    fs.writeFileSync(guardFile, JSON.stringify(g));
    if (g.same >= 3)  return out({});   // STATE.json not moving: let the human look
    if (g.total > 80) return out({});   // absolute ceiling for one project

    const phases = Array.isArray(S.phases) ? S.phases : [];
    const open = phases.filter(p => p && p.status !== 'DONE');
    const execLive = S.stage === 'STAGE_B_EXECUTION' && S.runStatus === 'RUNNING';
    const accLive  = S.stage === 'FINAL_ACCEPTANCE'  && S.runStatus === 'RUNNING' && !(S.acceptance && S.acceptance.verdict);
    if (!execLive && !accLive) return out({});

    let next;
    if (accLive) next = 'continue section 15 at acceptance.currentStep';
    else if (open.length === 0) next = 'all phases are DONE - set stage FINAL_ACCEPTANCE and run section 15';
    else next = S.nextAction || ('start phase ' + open[0].id + ' at B1');

    return out({
      decision: 'continue',
      reason: 'AUTONOMOUS RUN IN PROGRESS. STATE.json: stage=' + S.stage +
        ' runStatus=' + S.runStatus + ' lastVerifiedPhase=' + S.lastVerifiedPhase +
        '/' + S.totalPhases + '. Per AGENTS.md section 9 B8 a message boundary is a ' +
        'checkpoint, NOT a request for permission. Begin the next message at B1: run ' +
        'section 7 rule S1 (read STATE.json and PROGRESS-INDEX.md from disk), then ' +
        'execute nextAction: ' + next + '. Do not ask the user anything. Do not ' +
        'summarize. Continue.'
    });
  } catch (e) { return out({}); }
});