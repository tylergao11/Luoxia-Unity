const http = require("http");
const fs = require("fs");

function rpc(method, params, sessionId, timeoutMs = 120000) {
  return new Promise((resolve, reject) => {
    const body = JSON.stringify({ jsonrpc: "2.0", id: Date.now(), method, params });
    const headers = {
      "Content-Type": "application/json",
      Accept: "application/json, text/event-stream",
      "Content-Length": Buffer.byteLength(body),
    };
    if (sessionId) headers["Mcp-Session-Id"] = sessionId;
    const req = http.request(
      { hostname: "127.0.0.1", port: 8080, path: "/mcp", method: "POST", headers },
      (res) => {
        let d = "";
        res.on("data", (c) => (d += c));
        res.on("end", () => {
          const sid = res.headers["mcp-session-id"] || sessionId;
          const lines = d
            .split(/\r?\n/)
            .filter((l) => l.startsWith("data: "))
            .map((l) => l.slice(6));
          let last = null;
          for (const l of lines) {
            try {
              last = JSON.parse(l);
            } catch {}
          }
          resolve({ sid, last });
        });
      },
    );
    req.setTimeout(timeoutMs, () => req.destroy(new Error("http timeout")));
    req.on("error", reject);
    req.write(body);
    req.end();
  });
}

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const text = (last) => JSON.stringify(last || {});

async function main() {
  const init = await rpc("initialize", {
    protocolVersion: "2024-11-05",
    capabilities: {},
    clientInfo: { name: "rebuild", version: "1" },
  });
  await rpc("notifications/initialized", {}, init.sid);
  await rpc("tools/call", { name: "manage_editor", arguments: { action: "stop" } }, init.sid).catch(
    () => {},
  );
  await sleep(2000);
  await rpc("tools/call", { name: "read_console", arguments: { action: "clear" } }, init.sid).catch(
    () => {},
  );
  // Wait compile after script edits
  for (let i = 0; i < 20; i++) {
    const st = await rpc("resources/read", { uri: "mcpforunity://editor/state" }, init.sid);
    const t = text(st.last);
    if (/is_compiling\":true|domain_reload_pending\":true/i.test(t)) {
      console.log("waiting compile", i);
      await sleep(2000);
      continue;
    }
    break;
  }
  const build = await rpc(
    "tools/call",
    { name: "execute_menu_item", arguments: { menu_path: "Luoxia/UI/Build Main World Screen" } },
    init.sid,
  );
  console.log("build", text(build.last).slice(0, 800));
  await sleep(12000);
  const cons = await rpc(
    "tools/call",
    {
      name: "read_console",
      arguments: { action: "get", types: ["log", "error"], count: 30, format: "plain" },
    },
    init.sid,
  );
  console.log("console", text(cons.last).slice(0, 2500));
  const scene = fs.readFileSync("Assets/Scenes/MainWorld.unity", "utf8");
  console.log("retryButtonLabel wired", /retryButtonLabel: \{fileID: (?!0\})/.test(scene));
  console.log(
    "ImmersiveShell has CanvasGroup blocks false",
    /m_Name: ImmersiveShell[\s\S]{0,400}?m_BlocksRaycasts: 0/.test(scene),
  );
}

main().catch((e) => {
  console.error(e);
  process.exit(1);
});
