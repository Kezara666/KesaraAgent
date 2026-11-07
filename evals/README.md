# Agent evaluation harness

Runs fixed prompts against the running API and checks what the agent did.

- `cases.json`: the cases. Each has a prompt and expectations: tools that must run (`expectTools`), tools that must be held for approval (`expectHeld`), no tool calls (`expectNoTools`), and an answer pattern (`answerMatches`).
- `workspace/`: fixture files the cases read and edit. Start the API with `AGENT_WORKSPACE` pointing here (the run script does this when set).

Run it:

```powershell
$env:AGENT_TOKEN = "<your API token>"
$env:AGENT_WORKSPACE = "$PWD\evals\workspace"
powershell -ExecutionPolicy Bypass -File scripts/dev/run-local-api.ps1   # in one window
powershell -ExecutionPolicy Bypass -File scripts/eval/run-evals.ps1     # in another
```

Tool calls are read from the audit trail: `agent.tool.call` (ran) and `agent.tool.held` (waiting for approval). The token is read from the environment only.
