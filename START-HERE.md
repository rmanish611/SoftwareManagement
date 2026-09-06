# START HERE - Software Management

Workspace : G:\software-management
Repo      : https://github.com/rmanish611/SoftwareManagement.git
Brief     : The public corporate website of a software company (the owner's own, initially a one-person .NET shop), built the way large IT companies present themselves: it presents the company (about, services, the technology stack it works in such as .NET/C#, Angular, SQL Server, Python), lists every software product the company builds (multi-tenant SaaS such as ERP, hospital management, billing, school and college management) with a product page each (overview, feature list, screenshots, pricing plans, live-demo link, public API/docs listing), showcases delivered projects and public APIs, and captures leads: visitors browse, open a live demo, and contact the company / request a demo / request a quote or a tenant; the owner qualifies and follows up every lead, records the tenant or subscription he sells, and manages all site content (company profile, services, tech stack, products, plans, projects, APIs, testimonials, enquiries) from an admin panel. It is a showcase + lead-generation + sales-tracking site for the company's own products - NOT an IT software-asset or licence-management tool, NOT a marketplace for other sellers, and NOT the ERP/HMS products themselves.

## 1. Antigravity settings (do this ONCE, before the first run)
Settings (gear, bottom-left) - these are what make the run truly unattended:

  Model                        : Gemini 3.1 Pro (High)
  Tool Execution Policy        : always-proceed   (else it pauses on every command)
  Network Access Rules > Open  : ALLOW list, add:   read_url(*)
                                 (official default is ASK - without this the research
                                  phase pauses and prompts on EVERY web page)
  Tool Permissions > Open      : ALLOW list, add:   command(*)
                                 (belt-and-braces with always-proceed for an unattended run)
  Non-Workspace File Access    : allow            (backups -> G:\_backups, clone test -> G:\_acceptance)
  Terminal Sandbox             : OFF              (needs real LocalDB, real ports, robocopy)
  Artifact Review Policy       : Always Proceed
  Browser JS Execution Policy  : Always Proceed   (only matters if the agent uses /browser for research)
  Windows power plan           : plugged-in sleep = NEVER   (run once in a terminal:
                                  powercfg -change -standby-timeout-ac 0 )

Then: Projects > add this folder as a project:  G:\software-management
Open "Skills & Customizations" in the sidebar and confirm AGENTS.md is listed as an
active rule. If it is not listed, the agent is not seeing the protocol - stop and fix that first.

## 2. Kickoff
New Conversation inside the project, Mode: Local. Paste exactly this and send:

   Read AGENTS.md in full. It is your complete, binding operating protocol and is already loaded as a workspace rule. The three inputs (name, repo URL, brief) are in its INPUTS box. Start at section 4 (input echo), then section 5 preflight, then section 6 research and blueprint, then STOP at the approval gate. Talk to me in Hinglish.

The agent will research, write the blueprint to docs/blueprint/, push it, and STOP.
Read the one-page summary it posts. If you agree, reply with exactly:

   APPROVED - EXECUTE

Then leave it alone. The Stop hook in .agents/hooks.json re-enters the loop after
every phase report, so it does not wait for you between phases. It WILL stop and ask
only for: GitHub auth, missing web access, missing SQL Server, or a destructive action
outside the project folder.

## 3. Before the run - the protocol halts on each of these if missing
- GitHub push auth. In a terminal run:   git ls-remote https://github.com/rmanish611/SoftwareManagement.git
  If it prompts for a password or fails, run once:   git credential-manager github login
- Close Chrome and VS Code during the run. This laptop has 8 GB RAM.

## Other agents
Claude Code : open a terminal in G:\software-management, run  claude , then type:  Read AGENTS.md and start at section 4.
OpenAI Codex: reads AGENTS.md automatically. Open G:\software-management and type:  Start at section 4 of AGENTS.md.