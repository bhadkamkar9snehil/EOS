// Sign-in helpers for the static login page and the sign-out button. No framework needed.
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
  } catch { /* keep hidden */ }
});

// Download helper for exports invoked from Blazor.
window.eosDownload = (name, mime, base64) => {
  const a = document.createElement("a");
  a.href = "data:" + mime + ";base64," + base64;
  a.download = name;
  document.body.appendChild(a); a.click(); a.remove();
};
