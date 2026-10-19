async function eosPost(url, body) {
  const res = await fetch(url, { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify(body) });
  const data = await res.json().catch(() => ({}));
  return { ok: res.ok, status: res.status, data };
}

async function eosLogin(e) {
  e.preventDefault();
  const token = document.getElementById("token").value.trim();
  const err = document.getElementById("login-error");
  err.hidden = true;
  const r = await eosPost("/auth/login", { token });
  if (r.ok) {
    const back = new URLSearchParams(location.search).get("returnUrl");
    location.href = back && back.startsWith("/") ? back : "/";
  } else {
    err.textContent = r.data.error || "Sign-in failed.";
    err.hidden = false;
  }
}

async function eosBootstrap(e) {
  e.preventDefault();
  const name = document.getElementById("owner").value.trim() || "owner";
  const out = document.getElementById("bootstrap-result");
  const r = await eosPost("/auth/bootstrap", { name });
  out.hidden = false;
  if (r.ok) {
    out.textContent = "Your token (copy it now, it is shown once): " + r.data.token;
    out.className = "ok mono";
    setTimeout(() => { location.href = "/"; }, 12000);
  } else {
    out.textContent = r.data.error || "Could not create a token.";
    out.className = "error";
  }
}

async function eosSignOut(e) {
  e.preventDefault();
  await fetch("/auth/logout", { method: "POST" });
  location.href = "/login";
}

document.addEventListener("DOMContentLoaded", async () => {
  const boot = document.getElementById("bootstrap-form");
  if (!boot) return;
  try {
    const s = await (await fetch("/auth/state")).json();
    boot.hidden = !s.needsBootstrap;
  } catch { }
});

window.eosDownload = (name, mime, base64) => {
  const a = document.createElement("a");
  a.href = "data:" + mime + ";base64," + base64;
  a.download = name;
  document.body.appendChild(a); a.click(); a.remove();
};

window.eosGanttWidth = (id) => {
  const el = document.getElementById(id);
  return el ? el.clientWidth : 0;
};
window.eosGanttScroll = (id, x) => {
  const el = document.getElementById(id);
  if (el) el.scrollLeft = x;
};

window.eosPlannerInit = (ref) => {
  window.__plannerRef = ref;
  if (window.__plannerBound) return;
  window.__plannerBound = true;
  document.addEventListener("pointerdown", (e) => {
    const h = e.target.closest && e.target.closest("[data-lane]");
    if (!h || !window.__plannerRef) return;
    const svg = h.ownerSVGElement;
    const dw = parseFloat(svg.dataset.dw);
    const g = h.closest("g.lane");
    const body = g.querySelector("[data-body]");
    const edge = h.dataset.edge;
    const x0 = e.clientX;
    const bx = parseFloat(body.getAttribute("x"));
    const bw = parseFloat(body.getAttribute("width"));
    let delta = 0;
    g.classList.add("dragging");
    h.setPointerCapture(e.pointerId);
    e.preventDefault();
    const move = (ev) => {
      delta = Math.round((ev.clientX - x0) / dw);
      let nx = bx, nw = bw;
      if (edge === "move") nx = bx + delta * dw;
      else if (edge === "start") { nx = bx + delta * dw; nw = bw - delta * dw; }
      else nw = bw + delta * dw;
      if (nw < dw) return;
      body.setAttribute("x", nx);
      body.setAttribute("width", nw);
    };
    const up = () => {
      h.removeEventListener("pointermove", move);
      h.removeEventListener("pointerup", up);
      g.classList.remove("dragging");
      if (delta !== 0 && window.__plannerRef) window.__plannerRef.invokeMethodAsync("LaneChanged", h.dataset.lane, edge, delta);
      else { body.setAttribute("x", bx); body.setAttribute("width", bw); }
    };
    h.addEventListener("pointermove", move);
    h.addEventListener("pointerup", up);
  });
};

window.eosPlannerDispose = () => { window.__plannerRef = null; };

window.eosStoreGet = (key) => {
  try { return localStorage.getItem(key); } catch { return null; }
};

window.eosStoreSet = (key, value) => {
  try { if (value === null) localStorage.removeItem(key); else localStorage.setItem(key, value); } catch { }
};

window.eosFocusRow = (id) => {
  requestAnimationFrame(() => {
    const row = document.querySelector('tr[data-row="' + id + '"]');
    if (row) row.focus();
  });
};

(function () {
  const selector = "tr.clickable, td.clickable";

  function arm(root) {
    root.querySelectorAll(selector).forEach((el) => {
      if (!el.hasAttribute("tabindex")) {
        el.setAttribute("tabindex", "0");
        el.setAttribute("role", el.tagName === "TR" ? "row" : "button");
      }
    });
  }

  document.addEventListener("keydown", (e) => {
    if (e.key !== "Enter" && e.key !== " ") return;
    const el = e.target.closest && e.target.closest(selector);
    if (!el || e.target !== el) return;
    e.preventDefault();
    el.click();
  });

  document.addEventListener("click", (e) => {
    const open = document.querySelectorAll("details.menu[open]");
    open.forEach((d) => { if (!d.contains(e.target) || e.target.closest("a")) d.removeAttribute("open"); });
  });

  document.addEventListener("keydown", (e) => {
    if (e.key === "Escape") document.querySelectorAll("details.menu[open]").forEach((d) => d.removeAttribute("open"));
  });

  new MutationObserver(() => arm(document)).observe(document.documentElement, { childList: true, subtree: true });
  document.addEventListener("DOMContentLoaded", () => arm(document));
})();

window.eosToggleTheme = () => {
  const next = document.documentElement.dataset.theme === "dark" ? "light" : "dark";
  document.documentElement.dataset.theme = next;
  try { localStorage.setItem("eos.theme", next); } catch { }
};
