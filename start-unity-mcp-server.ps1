# Start MCP for Unity HTTP server (port 8080)
# Keep this running while using Unity MCP from Grok/Cursor.
$uvx = "C:\Users\84720\AppData\Local\hermes\bin\uvx.exe"
$port = 8080
$existing = Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue
if ($existing) {
  Write-Host "Already listening on $port (PID $($existing.OwningProcess))"
  exit 0
}
Write-Host "Starting mcp-for-unity on http://127.0.0.1:$port/mcp ..."
& $uvx --from mcpforunityserver mcp-for-unity --transport http --http-host 127.0.0.1 --http-port $port
