const COMMANDS = [
  ["/clear", "Clear all messages"],
  ["/new", "Start new conversation"],
  ["/export", "Export chat transcript"],
  ["/model <id>", "Switch active model"],
  ["/tokens", "Show usage stats"],
  ["/voice [on|off]", "Toggle voice input"],
  ["/settings", "Open settings panel"],
  ["/tools", "Open MCP server catalog"],
  ["/help", "Show command list"]
];

const state = {
  busy: false,
  models: [],
  selectedModelId: "",
  messages: [],
  settings: {},
  mcpServers: [],
  terminal: {
    running: false,
    lines: []
  }
};

const viewCache = {
  modelsKey: "",
  settingsKey: ""
};

const dom = {
  modelSelect: document.getElementById("modelSelect"),
  refreshBtn: document.getElementById("refreshBtn"),
  settingsBtn: document.getElementById("settingsBtn"),
  mcpBtn: document.getElementById("mcpBtn"),
  terminalBtn: document.getElementById("terminalBtn"),
  commandsBtn: document.getElementById("commandsBtn"),
  exportBtn: document.getElementById("exportBtn"),
  newBtn: document.getElementById("newBtn"),
  clearBtn: document.getElementById("clearBtn"),
  quickList: document.getElementById("quickList"),
  messages: document.getElementById("messages"),
  thinkingBubbles: document.getElementById("thinkingBubbles"),
  statusText: document.getElementById("statusText"),
  inputBox: document.getElementById("inputBox"),
  sendBtn: document.getElementById("sendBtn"),
  commandsList: document.getElementById("commandsList"),
  settingsOverlay: document.getElementById("settingsOverlay"),
  commandsOverlay: document.getElementById("commandsOverlay"),
  mcpOverlay: document.getElementById("mcpOverlay"),
  terminalOverlay: document.getElementById("terminalOverlay"),
  apiEndpoint: document.getElementById("apiEndpoint"),
  whisperEndpoint: document.getElementById("whisperEndpoint"),
  enableVoice: document.getElementById("enableVoice"),
  showTokens: document.getElementById("showTokens"),
  showContext: document.getElementById("showContext"),
  autoScroll: document.getElementById("autoScroll"),
  showTimeAgo: document.getElementById("showTimeAgo"),
  saveSettingsBtn: document.getElementById("saveSettingsBtn"),
  mcpList: document.getElementById("mcpList"),
  mcpSearch: document.getElementById("mcpSearch"),
  mcpCategory: document.getElementById("mcpCategory"),
  terminalOutput: document.getElementById("terminalOutput"),
  terminalInput: document.getElementById("terminalInput"),
  terminalRunBtn: document.getElementById("terminalRunBtn")
};

function sendHost(type, payload = {}) {
  if (window.chrome && window.chrome.webview && window.chrome.webview.postMessage) {
    window.chrome.webview.postMessage({ type, payload });
  }
}

function esc(text) {
  return (text || "")
    .replace(/&/g, "&amp;")
    .replace(/</g, "&lt;")
    .replace(/>/g, "&gt;");
}

function escAttr(text) {
  return esc(text)
    .replace(/"/g, "&quot;")
    .replace(/'/g, "&#39;");
}

function isSafeImageUrl(url) {
  if (!url) return false;
  const value = url.trim();
  if (value.startsWith("data:image/")) return true;
  return value.startsWith("https://") || value.startsWith("http://");
}

function isLikelyImageUrl(url) {
  if (!isSafeImageUrl(url)) return false;
  if (url.startsWith("data:image/")) return true;
  return /\.(png|jpe?g|gif|webp|bmp|svg|avif)(\?|#|$)/i.test(url);
}

function extractImageUrls(text) {
  const images = [];
  const seen = new Set();
  const input = text || "";

  const markdownImageRegex = /!\[[^\]]*\]\(([^)\s]+)\)/g;
  let markdownMatch;
  while ((markdownMatch = markdownImageRegex.exec(input)) !== null) {
    const url = markdownMatch[1];
    if (isSafeImageUrl(url) && !seen.has(url)) {
      seen.add(url);
      images.push(url);
    }
  }

  const plainUrlRegex = /https?:\/\/[^\s<>")']+/g;
  let plainMatch;
  while ((plainMatch = plainUrlRegex.exec(input)) !== null) {
    const url = plainMatch[0];
    if (isLikelyImageUrl(url) && !seen.has(url)) {
      seen.add(url);
      images.push(url);
    }
  }

  return images;
}

function renderMessageContent(content) {
  const text = content || "";
  const imageUrls = extractImageUrls(text);
  const textHtml = esc(text).replace(/\n/g, "<br>");

  if (imageUrls.length === 0) {
    return textHtml;
  }

  const imagesHtml = imageUrls
    .map((url) => `<img class="chatImage" src="${escAttr(url)}" alt="Chat image" loading="lazy" />`)
    .join("");

  return `${textHtml}<div class="chatImageGroup">${imagesHtml}</div>`;
}

function showOverlay(id, show) {
  const el = document.getElementById(id);
  if (!el) return;
  el.classList.toggle("show", !!show);
}

function renderCommands() {
  dom.commandsList.innerHTML = "";
  for (const [name, desc] of COMMANDS) {
    const d = document.createElement("div");
    d.className = "cmd";
    d.innerHTML = `<strong>${esc(name)}</strong><div style="color:var(--muted);margin-top:4px;">${esc(desc)}</div>`;
    d.addEventListener("click", () => {
      dom.inputBox.value = name.split(" ")[0] + (name.includes("<") ? " " : "");
      showOverlay("commandsOverlay", false);
      dom.inputBox.focus();
    });
    dom.commandsList.appendChild(d);
  }
}

function getModelOwnerLabel(model) {
  if (!model) return "";
  const owner = (model.owner || "").trim();
  if (owner) {
    return owner;
  }
  if ((model.image || false) || (model.id || "").toLowerCase().includes("flux") || (model.id || "").toLowerCase().startsWith("hf/")) {
    return "Image";
  }
  if (model.tools) {
    return "Tooling";
  }
  return "";
}

function getModelBadges(model) {
  const badges = [];
  if (model.tools) badges.push("🔧");
  if (model.thinking) badges.push("🧠");
  if (model.image) badges.push("🖼️");
  return badges.join(" ");
}

function groupModelsForDropdown(models) {
  const imageModels = [];
  const textModels = [];

  for (const model of models) {
    if (model.image) {
      imageModels.push(model);
    } else {
      textModels.push(model);
    }
  }

  return { imageModels, textModels };
}

function renderModels() {
  dom.modelSelect.innerHTML = "";
  const { imageModels, textModels } = groupModelsForDropdown(state.models);

  const appendOption = (model) => {
    const option = document.createElement("option");
    option.value = model.id;
    const badges = getModelBadges(model);
    const owner = getModelOwnerLabel(model);
    const labelParts = [badges, model.name].filter(Boolean);
    if (owner) {
      labelParts.push(`• ${owner}`);
    }
    option.textContent = labelParts.join(" ").replace(/\s+/g, " ").trim();
    option.selected = model.id === state.selectedModelId;
    dom.modelSelect.appendChild(option);
  };

  if (imageModels.length > 0) {
    const imageGroup = document.createElement("optgroup");
    imageGroup.label = "Image Models";
    for (const model of imageModels) {
      const option = document.createElement("option");
      option.value = model.id;
      const badges = getModelBadges(model);
      const owner = getModelOwnerLabel(model);
      const labelParts = [badges, model.name].filter(Boolean);
      if (owner) {
        labelParts.push(`• ${owner}`);
      }
      option.textContent = labelParts.join(" ").replace(/\s+/g, " ").trim();
      option.selected = model.id === state.selectedModelId;
      imageGroup.appendChild(option);
    }
    dom.modelSelect.appendChild(imageGroup);
  }

  if (textModels.length > 0) {
    const textGroup = document.createElement("optgroup");
    textGroup.label = "Text Models";
    for (const model of textModels) {
      const option = document.createElement("option");
      option.value = model.id;
      const badges = getModelBadges(model);
      const owner = getModelOwnerLabel(model);
      const labelParts = [badges, model.name].filter(Boolean);
      if (owner) {
        labelParts.push(`• ${owner}`);
      }
      option.textContent = labelParts.join(" ").replace(/\s+/g, " ").trim();
      option.selected = model.id === state.selectedModelId;
      textGroup.appendChild(option);
    }
    dom.modelSelect.appendChild(textGroup);
  }

  dom.quickList.innerHTML = "";
  for (const model of state.models.slice(0, 16)) {
    const b = document.createElement("button");
    b.className = "ghost";
    const badges = getModelBadges(model);
    const owner = getModelOwnerLabel(model);
    b.innerHTML = `<strong>${badges ? esc(badges + " ") : ""}${esc(model.name)}</strong>${owner ? `<span>${esc(owner)}</span>` : ""}`;
    b.addEventListener("click", () => {
      state.selectedModelId = model.id;
      dom.modelSelect.value = model.id;
      sendHost("selectModel", { modelId: model.id });
    });
    dom.quickList.appendChild(b);
  }
}

function buildMessageHtml(msg) {
  const ts = msg.timestamp ? new Date(msg.timestamp).toLocaleTimeString() : "";
  const streamingBadge = msg.streaming ? '<span class="streamBadge">Streaming...</span>' : "";
  return `
      <div class="meta">${esc(msg.role || "Assistant")} ${ts ? "• " + esc(ts) : ""} ${streamingBadge}</div>
      ${msg.thinking ? `<div class="thinking">${esc(msg.thinking)}</div>` : ""}
      <div class="content">${renderMessageContent(msg.content || "")}</div>
    `;
}

function renderMessages() {
  const children = dom.messages.children;
  const canPatchInPlace = children.length === state.messages.length;

  if (!canPatchInPlace) {
    dom.messages.innerHTML = "";
    for (const msg of state.messages) {
      const role = (msg.role || "assistant").toLowerCase();
      const roleClass = role === "you" ? "you" : (role === "system" ? "system" : "assistant");
      const article = document.createElement("article");
      article.className = `msg ${roleClass}`;
      article.dataset.sig = "";
      article.innerHTML = buildMessageHtml(msg);
      article.dataset.sig = `${msg.role || ""}|${msg.timestamp || ""}|${msg.streaming ? "1" : "0"}|${msg.thinking || ""}|${msg.content || ""}`;
      dom.messages.appendChild(article);
    }
  } else {
    for (let i = 0; i < state.messages.length; i++) {
      const msg = state.messages[i];
      const role = (msg.role || "assistant").toLowerCase();
      const roleClass = role === "you" ? "you" : (role === "system" ? "system" : "assistant");
      const article = children[i];

      const nextSig = `${msg.role || ""}|${msg.timestamp || ""}|${msg.streaming ? "1" : "0"}|${msg.thinking || ""}|${msg.content || ""}`;
      if (article.className !== `msg ${roleClass}`) {
        article.className = `msg ${roleClass}`;
      }

      if (article.dataset.sig !== nextSig) {
        article.innerHTML = buildMessageHtml(msg);
        article.dataset.sig = nextSig;
      }
    }
  }

  if (state.settings.autoScroll !== false) {
    dom.messages.scrollTop = dom.messages.scrollHeight;
  }
}

function renderSettings() {
  const s = state.settings || {};
  dom.apiEndpoint.value = s.apiEndpoint || "https://integrate.api.nvidia.com/v1/";
  dom.whisperEndpoint.value = s.whisperEndpoint || "";
  dom.enableVoice.checked = !!s.enableVoice;
  dom.showTokens.checked = s.showTokens !== false;
  dom.showContext.checked = s.showContext !== false;
  dom.autoScroll.checked = s.autoScroll !== false;
  dom.showTimeAgo.checked = s.showTimeAgo !== false;
}

function renderMcpCategoryOptions() {
  const categories = [...new Set(state.mcpServers.map(s => s.category || "Other"))].sort();
  const current = dom.mcpCategory.value;
  dom.mcpCategory.innerHTML = "<option value=\"\">All</option>";
  for (const cat of categories) {
    const option = document.createElement("option");
    option.value = cat;
    option.textContent = cat;
    dom.mcpCategory.appendChild(option);
  }
  if (["", ...categories].includes(current)) {
    dom.mcpCategory.value = current;
  }
}

function renderMcpList() {
  const search = (dom.mcpSearch.value || "").trim().toLowerCase();
  const category = dom.mcpCategory.value || "";
  const filtered = state.mcpServers.filter(s => {
    if (category && (s.category || "") !== category) return false;
    if (!search) return true;
    return (s.name || "").toLowerCase().includes(search)
      || (s.description || "").toLowerCase().includes(search)
      || (s.author || "").toLowerCase().includes(search);
  });

  dom.mcpList.innerHTML = "";
  for (const server of filtered) {
    const row = document.createElement("div");
    row.className = "mcpItem";

    row.innerHTML = `
      <div>
        <strong>${esc(server.name)}</strong>
        <div>${esc(server.description || "")}</div>
        <div class="mcpMeta">${esc(server.category || "Other")} • ${esc(server.author || "Unknown")}</div>
      </div>
      <button class="${server.installed ? "ghost" : "accent"}">${server.installed ? "Remove" : "Install"}</button>
    `;

    row.querySelector("button").addEventListener("click", () => {
      sendHost("toggleMcpServer", { name: server.name });
    });

    dom.mcpList.appendChild(row);
  }
}

function applyState(payload) {
  state.busy = !!payload.busy;
  state.models = Array.isArray(payload.models) ? payload.models : [];
  state.selectedModelId = payload.selectedModelId || "";
  state.messages = Array.isArray(payload.messages) ? payload.messages : [];
  state.settings = payload.settings || {};

  dom.statusText.textContent = payload.status || (state.busy ? "Working..." : "Ready");
  dom.sendBtn.disabled = state.busy;
  dom.inputBox.disabled = state.busy;
  dom.refreshBtn.disabled = state.busy;
  dom.thinkingBubbles.classList.toggle("active", state.busy);

  const modelsKey = `${state.selectedModelId}|${state.models.map(m => `${m.id}|${m.name}|${m.owner}|${m.recommended ? 1 : 0}|${m.image ? 1 : 0}|${m.tools ? 1 : 0}|${m.thinking ? 1 : 0}`).join(";")}`;
  if (viewCache.modelsKey !== modelsKey) {
    viewCache.modelsKey = modelsKey;
    renderModels();
  }

  renderMessages();

  const settingsKey = JSON.stringify(state.settings || {});
  if (viewCache.settingsKey !== settingsKey) {
    viewCache.settingsKey = settingsKey;
    renderSettings();
  }
}

function applyMcpCatalog(payload) {
  state.mcpServers = Array.isArray(payload.servers) ? payload.servers : [];
  renderMcpCategoryOptions();
  renderMcpList();
}

function appendTerminalLine(text) {
  if (!text) {
    return;
  }

  state.terminal.lines.push(text);
  if (state.terminal.lines.length > 400) {
    state.terminal.lines = state.terminal.lines.slice(-400);
  }

  dom.terminalOutput.textContent = state.terminal.lines.join("\n");
  dom.terminalOutput.scrollTop = dom.terminalOutput.scrollHeight;
}

function setTerminalRunning(running) {
  state.terminal.running = running;
  dom.terminalRunBtn.disabled = running;
  dom.terminalInput.disabled = running;
}

function applyTerminalResult(payload) {
  setTerminalRunning(false);
  const header = payload.ok
    ? `> ${payload.command}\n[exit ${payload.exitCode}]`
    : `> ${payload.command}\n[error ${payload.exitCode}]`;
  appendTerminalLine(header);
  appendTerminalLine(payload.output || "");
  appendTerminalLine("----------------------------------------");
}

window.dispatchHostMessage = (message) => {
  if (!message || !message.type) return;
  if (message.type === "state") {
    applyState(message.payload || {});
  }
  if (message.type === "mcpCatalog") {
    applyMcpCatalog(message.payload || {});
  }
  if (message.type === "terminalResult") {
    applyTerminalResult(message.payload || {});
  }
};

dom.sendBtn.addEventListener("click", () => {
  const text = dom.inputBox.value.trim();
  if (!text) return;
  sendHost("sendMessage", { text, modelId: dom.modelSelect.value || state.selectedModelId });
  dom.inputBox.value = "";
});

dom.inputBox.addEventListener("keydown", (e) => {
  if (e.key === "Enter" && !e.shiftKey) {
    e.preventDefault();
    dom.sendBtn.click();
  }
});

dom.modelSelect.addEventListener("change", () => {
  sendHost("selectModel", { modelId: dom.modelSelect.value });
});

dom.refreshBtn.addEventListener("click", () => sendHost("refreshModels"));
dom.exportBtn.addEventListener("click", () => sendHost("exportChat"));
dom.newBtn.addEventListener("click", () => sendHost("newChat"));
dom.clearBtn.addEventListener("click", () => sendHost("clearChat"));

dom.commandsBtn.addEventListener("click", () => showOverlay("commandsOverlay", true));
dom.settingsBtn.addEventListener("click", () => {
  renderSettings();
  showOverlay("settingsOverlay", true);
});
dom.mcpBtn.addEventListener("click", () => {
  sendHost("getMcpCatalog");
  showOverlay("mcpOverlay", true);
});

dom.terminalBtn.addEventListener("click", () => {
  showOverlay("terminalOverlay", true);
  dom.terminalInput.focus();
});

document.querySelectorAll("[data-close]").forEach((btn) => {
  btn.addEventListener("click", () => {
    showOverlay(btn.getAttribute("data-close"), false);
  });
});

dom.saveSettingsBtn.addEventListener("click", () => {
  sendHost("saveSettings", {
    apiEndpoint: dom.apiEndpoint.value.trim(),
    whisperEndpoint: dom.whisperEndpoint.value.trim(),
    enableVoice: dom.enableVoice.checked,
    showTokens: dom.showTokens.checked,
    showContext: dom.showContext.checked,
    autoScroll: dom.autoScroll.checked,
    showTimeAgo: dom.showTimeAgo.checked
  });
  showOverlay("settingsOverlay", false);
});

dom.mcpSearch.addEventListener("input", renderMcpList);
dom.mcpCategory.addEventListener("change", renderMcpList);

dom.terminalRunBtn.addEventListener("click", () => {
  const command = dom.terminalInput.value.trim();
  if (!command || state.terminal.running) {
    return;
  }

  setTerminalRunning(true);
  appendTerminalLine(`$ ${command}`);
  sendHost("executeTerminalCommand", { command });
  dom.terminalInput.value = "";
});

dom.terminalInput.addEventListener("keydown", (e) => {
  if (e.key === "Enter") {
    e.preventDefault();
    dom.terminalRunBtn.click();
  }
});

appendTerminalLine("Local terminal ready.");
renderCommands();

window.__pendingAppReady = true;
