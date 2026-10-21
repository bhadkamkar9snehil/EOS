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
window.eosGanttFit = (id) => {
  const el = document.getElementById(id);
  if (!el) return;
  const main = el.closest("main");
  const run = () => {
    if (!el.isConnected) return;
    const bottom = Math.min(main ? main.getBoundingClientRect().bottom : innerHeight, innerHeight);
    const pad = main ? parseFloat(getComputedStyle(main).paddingBottom) || 0 : 0;
    const legend = el.nextElementSibling;
    const reserve = (legend ? legend.offsetHeight + 10 : 0) + Math.min(pad, 24);
    el.style.maxHeight = Math.max(260, bottom - el.getBoundingClientRect().top - reserve) + "px";
  };
  run();
  if (el.__fit) return;
  el.__fit = true;
  addEventListener("resize", run);
  if (main && window.ResizeObserver) new ResizeObserver(run).observe(main);
};
window.eosGanttScroll =(id, x) => {
  const el = document.getElementById(id);
  if (el) el.scrollLeft = x;
};

window.__bindPlannerInteractions = () => {
  if (window.__plannerDndBound) return;
  window.__plannerDndBound = true;
  let person = null, hot = null;
  const ref = () => window.__plannerRef;
  const mark = (task) => {
    if (hot === task) return;
    document.querySelectorAll('[data-dnd="over"]').forEach((n) => n.removeAttribute("data-dnd"));
    hot = task;
    if (!task) return;
    const el = document.querySelector('.plgx .gl[data-task="' + task + '"]');
    const group = el && el.classList.contains("sel") ? document.querySelectorAll(".plgx .sel[data-task]") : document.querySelectorAll('.plgx [data-task="' + task + '"]');
    group.forEach((n) => n.setAttribute("data-dnd", "over"));
  };
  const end = () => {
    person = null; mark(null);
    document.body.classList.remove("dnd");
    document.querySelectorAll(".pcard[data-dragging]").forEach((n) => n.removeAttribute("data-dragging"));
  };
  document.addEventListener("dragstart", (e) => {
    const card = e.target.closest && e.target.closest(".pcard[data-person]");
    if (!card || !ref()) return;
    person = card.dataset.person;
    e.dataTransfer.effectAllowed = "copy";
    e.dataTransfer.setData("text/plain", person);
    const ghost = document.createElement("div");
    ghost.className = "dnd-ghost";
    ghost.textContent = card.dataset.name;
    document.body.appendChild(ghost);
    e.dataTransfer.setDragImage(ghost, 14, 14);
    setTimeout(() => ghost.remove(), 0);
    setTimeout(() => { card.setAttribute("data-dragging", ""); document.body.classList.add("dnd"); }, 0);
  });
  document.addEventListener("dragover", (e) => {
    if (person === null) return;
    const row = e.target.closest && e.target.closest(".plgx [data-task]");
    if (row) { e.preventDefault(); e.dataTransfer.dropEffect = "copy"; mark(row.dataset.task); }
    else mark(null);
    const box = document.getElementById("plgx");
    if (box) {
      const r = box.getBoundingClientRect();
      if (e.clientY > r.top && e.clientY < r.bottom && e.clientX > r.left && e.clientX < r.right) {
        if (e.clientY < r.top + 90) box.scrollTop -= 14;
        else if (e.clientY > r.bottom - 50) box.scrollTop += 14;
      }
    }
  });
  document.addEventListener("drop", (e) => {
    if (person === null) return;
    const row = e.target.closest && e.target.closest(".plgx [data-task]");
    const who = person;
    if (row && ref()) { e.preventDefault(); ref().invokeMethodAsync("DropPerson", parseInt(who, 10), parseInt(row.dataset.task, 10)); }
    end();
  });
  document.addEventListener("dragend", end);

  document.addEventListener("keydown", (e) => {
    if (!ref()) return;
    const tag = e.target.tagName;
    const typing = tag === "INPUT" || tag === "SELECT" || tag === "TEXTAREA" || e.target.isContentEditable;
    const mod = e.ctrlKey || e.metaKey;
    if (!typing && mod && !e.shiftKey && e.key.toLowerCase() === "z") { e.preventDefault(); ref().invokeMethodAsync("UndoKey"); return; }
    if (!typing && mod && (e.key.toLowerCase() === "y" || (e.shiftKey && e.key.toLowerCase() === "z"))) { e.preventDefault(); ref().invokeMethodAsync("RedoKey"); return; }
    if (e.key === "Escape" && !typing && !document.querySelector("details[open]")) { ref().invokeMethodAsync("ClearSelection"); return; }
    const row = e.target.closest && e.target.closest(".plgx .gl[data-task]:not(.lane-label)");
    if (row && !typing && e.target === row) {
      if (e.key === "ArrowDown" || e.key === "ArrowUp") {
        e.preventDefault();
        const rows = [...document.querySelectorAll(".plgx .gl[data-task]:not(.lane-label)")];
        const next = rows[rows.indexOf(row) + (e.key === "ArrowDown" ? 1 : -1)];
        if (next) next.focus();
      } else if (e.key === "Enter" || e.key === " ") {
        e.preventDefault();
        row.dispatchEvent(new MouseEvent("click", { bubbles: true, ctrlKey: e.ctrlKey || e.metaKey, shiftKey: e.shiftKey }));
      }
    }
    const card = e.target.closest && e.target.closest(".pcard[data-person]");
    if (card && !typing && e.target === card && (e.key === "Enter" || e.key === " ")) {
      e.preventDefault();
      ref().invokeMethodAsync("AddPerson", parseInt(card.dataset.person, 10));
    }
  });
};

window.eosPlannerInit = (ref) => {
  window.__plannerRef = ref;
  window.__bindPlannerInteractions();
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

// Accessibility: tie each ".field" caption label to its control (pages write <div class="field"><label>Name</label><input/>),
// and give label-less file pickers a name. Runs on every Blazor render via a MutationObserver.
(function () {
  let n = 0;
  function link(root) {
    for (const lab of root.querySelectorAll(".field > label:not([for])")) {
      const ctl = lab.parentElement.querySelector("input, select, textarea");
      if (!ctl || ctl.closest("label")) continue;
      if (!ctl.id) ctl.id = "eosf" + (++n);
      lab.htmlFor = ctl.id;
    }
    for (const f of root.querySelectorAll("input[type=file]:not([aria-label])")) f.setAttribute("aria-label", "Choose file");
  }
  const run = () => link(document);
  new MutationObserver(run).observe(document.documentElement, { childList: true, subtree: true });
  document.addEventListener("DOMContentLoaded", run);
})();
