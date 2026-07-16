# MCP SQL Server — Issues Encountered

Notes from using the `sqlserver` MCP against `TaskTrackerDb` on 2026-07-13.

## `execute_query` is disabled

Calling `mcp__sqlserver__execute_query` with a plain `SELECT` fails:

```
execute_query is disabled. Set MSSQL_ENABLE_EXECUTE_QUERY=true to enable this high-risk tool.
```

The tool is gated behind the `MSSQL_ENABLE_EXECUTE_QUERY` environment variable on
whatever process hosts the MCP server, and it isn't set. This meant a
straightforward query like:

```sql
SELECT IsDone, COUNT(*) AS TaskCount
FROM dbo.Tasks
GROUP BY IsDone;
```

could not be run directly.

**Workaround used:** `mcp__sqlserver__analyze_data_distribution` (with
`tableName: Tasks`, `columnName: IsDone`) returns per-value frequency counts
and percentages, which was enough to answer "how many done vs. not done"
without needing raw SQL.

**If ad-hoc SELECT access is needed going forward:** set
`MSSQL_ENABLE_EXECUTE_QUERY=true` in the environment of the MCP server process
(not this repo), then restart it. Worth confirming with whoever manages that
server config before flipping it, since the tool description flags it as
"high-risk."

## Fixed (2026-07-16)

Added `MSSQL_ENABLE_EXECUTE_QUERY: "true"` to the `sqlserver` server's `env`
block in the local Claude Code config (`~/.claude.json`, under this project's
`mcpServers` entry — not part of this repo). `execute_query` now works after
restarting the MCP connection (new Claude Code session, or otherwise
reconnecting to the server).
