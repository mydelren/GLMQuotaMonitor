// CDP 截图脚本：连接 Edge 9222 调试端口，渲染小红书图文并落盘
// 用法：node cdp-shot.mjs [slide ...]（缺省全部四张）
import http from "node:http";
import fs from "node:fs";
import net from "node:net";
import crypto from "node:crypto";

const ROOT = "C:/Users/kwang/ZCodeProject/GLMQuotaMonitor";
const XHS = `${ROOT}/assets/xiaohongshu`;
const ALL = ["01-cover", "02-details", "03-features", "04-settings"];
const slides = process.argv.slice(2).length ? process.argv.slice(2) : ALL;

function httpReq(method, url) {
  return new Promise((resolve, reject) => {
    const r = http.request(url, { method }, (res) => {
      let d = "";
      res.on("data", (c) => (d += c));
      res.on("end", () => resolve({ status: res.statusCode, body: d }));
    });
    r.on("error", reject);
    r.end();
  });
}

class MiniWS {
  constructor() {
    this.buf = Buffer.alloc(0);
    this.handlers = [];
    this.open = false;
    this.closed = false;
  }
  // 把原始字节喂进解析器（握手完成后由外部转发）
  pump(data) {
    if (!data || !data.length) return;
    this.buf = Buffer.concat([this.buf, data]);
    for (;;) {
      if (this.buf.length < 2) return;
      const opcode = this.buf[0] & 0x0f;
      let len = this.buf[1] & 0x7f;
      let off = 2;
      if (len === 126) { if (this.buf.length < 4) return; len = this.buf.readUInt16BE(2); off = 4; }
      else if (len === 127) { if (this.buf.length < 10) return; len = Number(this.buf.readBigUInt64BE(2)); off = 10; }
      if (this.buf.length < off + len) return;
      const payload = Buffer.from(this.buf.subarray(off, off + len));
      this.buf = this.buf.subarray(off + len);
      if (opcode === 9) this.#sendFrame(0x0a, payload);           // ping → pong
      else if (opcode === 8) { this.open = false; this.sock.end(); }
      else if (opcode === 1) for (const h of [...this.handlers]) h(payload.toString());
    }
  }
  #sendFrame(opcode, payload) {
    const mask = crypto.randomBytes(4);
    let header;
    if (payload.length < 126) header = Buffer.from([0x80 | opcode, 0x80 | payload.length]);
    else if (payload.length < 65536) {
      header = Buffer.alloc(4); header[0] = 0x80 | opcode; header[1] = 0x80 | 126; header.writeUInt16BE(payload.length, 2);
    } else {
      header = Buffer.alloc(10); header[0] = 0x80 | opcode; header[1] = 0x80 | 127; header.writeBigUInt64BE(BigInt(payload.length), 2);
    }
    const out = Buffer.alloc(payload.length);
    payload.forEach((b, i) => (out[i] = b ^ mask[i % 4]));
    this.sock.write(Buffer.concat([header, mask, out]));
  }
  send(str) { this.#sendFrame(0x01, Buffer.from(str)); }
  onMessage(h) { this.handlers.push(h); }
}

async function connect(wsUrl) {
  const u = new URL(wsUrl);
  const sock = net.connect(Number(u.port || 80), u.hostname);
  await new Promise((res, rej) => { sock.once("connect", res); sock.once("error", rej); });
  sock.write(
    `GET ${u.pathname}${u.search} HTTP/1.1\r\nHost: ${u.host}\r\nUpgrade: websocket\r\nConnection: Upgrade\r\n` +
    `Sec-WebSocket-Key: ${crypto.randomBytes(16).toString("base64")}\r\nSec-WebSocket-Version: 13\r\n\r\n`
  );
  // 收齐握手响应（头之后的首帧字节也保留）
  let raw = Buffer.alloc(0);
  const splitAt = await new Promise((res, rej) => {
    const onData = (d) => {
      raw = Buffer.concat([raw, d]);
      const idx = raw.indexOf("\r\n\r\n");
      if (idx >= 0) { sock.off("data", onData); res(idx); }
    };
    sock.on("data", onData);
    setTimeout(() => rej(new Error("handshake timeout")), 8000);
  }).catch((e) => { sock.destroy(); throw e; });

  const statusLine = raw.subarray(0, raw.indexOf("\r\n")).toString();
  console.log("[ws]", statusLine.trim());
  if (!statusLine.includes(" 101")) { sock.destroy(); throw new Error("handshake failed: " + statusLine); }

  const ws = new MiniWS();
  ws.sock = sock;
  sock.on("data", (d) => ws.pump(d));
  ws.pump(raw.subarray(splitAt + 4));   // 喂回头之后已到达的首帧
  return ws;
}

const created = await httpReq("PUT", "http://127.0.0.1:9222/json/new?about:blank");
if (created.status !== 200) throw new Error("json/new HTTP " + created.status);
const info = JSON.parse(created.body);

let seq = 0;
const pending = new Map();
const loadMarks = [];
const ws = await connect(info.webSocketDebuggerUrl);
ws.onMessage((text) => {
  const m = JSON.parse(text);
  if (m.method === "Page.loadEventFired") loadMarks.push(Date.now());
  if (m.id && pending.has(m.id)) {
    const p = pending.get(m.id);
    clearTimeout(p.timer);
    pending.delete(m.id);
    m.error ? p.reject(new Error(m.error.message)) : p.resolve(m.result);
  }
});

function cdp(method, params = {}, timeoutMs = 20000) {
  const id = ++seq;
  return new Promise((resolve, reject) => {
    const timer = setTimeout(() => {
      if (pending.has(id)) { pending.delete(id); reject(new Error("cdp timeout " + method)); }
    }, timeoutMs);
    pending.set(id, { resolve, reject, timer });
    ws.send(JSON.stringify({ id, method, params }));
  });
}

await cdp("Runtime.enable");
await cdp("Page.enable");
await cdp("Emulation.setDeviceMetricsOverride", { width: 1080, height: 1440, deviceScaleFactor: 1, mobile: false });

const results = [];
for (const s of slides) {
  const url = `file:///C:/Users/kwang/ZCodeProject/GLMQuotaMonitor/assets/xiaohongshu/${s}.html`;
  loadMarks.length = 0;
  await cdp("Page.navigate", { url });
  const t0 = Date.now();
  while (!loadMarks.some((t) => t > t0) && Date.now() - t0 < 8000) {
    const r = await cdp("Runtime.evaluate", { expression: "document.readyState", returnByValue: true }, 5000)
      .catch(() => null);
    if (r?.result?.value === "complete") break;
    await new Promise((z) => setTimeout(z, 150));
  }
  await new Promise((z) => setTimeout(z, 600)); // 字体/图片余量

  const shot = await cdp("Page.captureScreenshot", { format: "png" }, 30000);
  const bytes = Buffer.from(shot.data, "base64");
  fs.writeFileSync(`${XHS}/${s}.png`, bytes);
  results.push(`${s}.png ${bytes.readUInt32BE(16)}x${bytes.readUInt32BE(20)} ${(bytes.length / 1024).toFixed(0)}KB`);
}

await httpReq("GET", `http://127.0.0.1:9222/json/close/${info.id}`);
console.log(results.join("\n"));
