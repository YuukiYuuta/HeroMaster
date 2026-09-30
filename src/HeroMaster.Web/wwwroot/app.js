// Панель мастера: показывает состояние партии и собирает решения мастера на день.
// Вся игровая логика — на сервере (ядро HeroMaster.Core); здесь только отображение.

const STATUS = {
  normal: "в порядке",
  discontented: "недоволен",
  onEdge: "на грани",
  boycott: "бойкот",
};
const PHASE = {
  morningReport: "Утро",
  masterDecisions: "Решения мастера",
  expedition: "Вылазка",
  baseLife: "Жизнь на базе",
  nightReflection: "Ночь",
};
const HINTS = [
  ["", "намёк: —"],
  ["rest", "отдохнуть"],
  ["train", "тренироваться"],
  ["socialize", "пообщаться"],
  ["work", "поработать"],
];
const TRAITS = {
  courage: "смелость", pride: "гордость", empathy: "эмпатия",
  discipline: "дисциплина", ambition: "амбиции", pragmatism: "прагматизм",
};

let state = null;
let decisions = freshDecisions();
let showingFullLog = false;

function freshDecisions() {
  return { team: new Set(), loot: {}, keepLoot: false, gifts: {}, hints: {} };
}

const $ = (id) => document.getElementById(id);

async function api(path, body) {
  const res = await fetch(path, body === undefined ? {} : {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(body),
  });
  const data = await res.json().catch(() => ({}));
  return { ok: res.ok, data };
}

async function load() {
  const { data } = await api("/api/state");
  state = data;
  render();
}

function render() {
  const has = state && state.hasGame;
  $("app").hidden = !has;
  $("start").hidden = has;
  if (!has) {
    $("stats").innerHTML = "";
    return;
  }
  $("stats").innerHTML =
    `<span>День ${state.day}</span><span>Золото: ${state.gold}</span><span class="muted">зерно ${state.seed}</span>`;
  renderLoot();
  renderHeroes();
  if (!showingFullLog) renderJournal(state.lastDay, state.day === 0 ? "Начало партии" : `Журнал дня ${state.day}`);
  updateTeamInfo();
}

function heroName(id) {
  const h = state.heroes.find((x) => x.id === id);
  return h ? h.name : id;
}

function renderLoot() {
  const pool = state.pendingLoot;
  $("lootPanel").hidden = !pool;
  if (!pool) return;

  $("lootInfo").textContent =
    `${pool.amount} золота. Ходили: ${pool.participants.map(heroName).join(", ")}. ` +
    "Обделённые обидятся, гордые ждут доли побольше. Остаток уходит мастеру.";
  if (Object.keys(decisions.loot).length === 0 && !decisions.keepLoot) splitEqually();

  $("keepLoot").checked = decisions.keepLoot;
  $("lootRows").innerHTML = state.heroes.map((h) => `
    <label class="loot-row">
      <span>${h.name}${pool.participants.includes(h.id) ? "" : ' <span class="muted">(не ходил)</span>'}</span>
      <input type="number" min="0" data-loot="${h.id}" value="${decisions.loot[h.id] ?? 0}" ${decisions.keepLoot ? "disabled" : ""}>
    </label>`).join("");
  $("lootRows").querySelectorAll("input").forEach((input) => {
    input.addEventListener("input", () => {
      decisions.loot[input.dataset.loot] = Math.max(0, parseInt(input.value || "0", 10));
      updateLootRemain();
    });
  });
  updateLootRemain();
}

function splitEqually() {
  const pool = state.pendingLoot;
  decisions.loot = {};
  const each = Math.floor(pool.amount / pool.participants.length);
  pool.participants.forEach((id) => (decisions.loot[id] = each));
}

function updateLootRemain() {
  const pool = state.pendingLoot;
  if (!pool) return;
  if (decisions.keepLoot) {
    $("lootRemain").textContent = `Всё (${pool.amount}) — мастеру.`;
    return;
  }
  const given = Object.values(decisions.loot).reduce((a, b) => a + b, 0);
  const left = pool.amount - given;
  $("lootRemain").textContent = left >= 0 ? `Роздано ${given}, мастеру останется ${left}.` : `Роздано больше, чем есть, на ${-left}!`;
}

function bar(label, value, color) {
  return `<div class="bar"><span>${label}</span>
    <div class="bar-track"><div class="bar-fill" style="width:${value}%;background:var(${color})"></div></div>
    <span class="num">${value}</span></div>`;
}

function renderHeroes() {
  const giftOptions = [["", "подарок: —"], ...state.giftKinds.map((g) => [g.id, `${g.name} (${g.cost} зол.)`])];
  $("heroes").innerHTML = state.heroes.map((h) => {
    const selected = decisions.team.has(h.id);
    return `
      <div class="hero ${selected ? "selected" : ""}">
        <div class="hero-head">
          <div>
            <div><span class="stars">${"★".repeat(h.stars)}</span> <span class="hero-name" data-hero="${h.id}">${h.name}</span></div>
            <div class="prof">${h.profession} · сила ${h.power}${h.foundPurpose ? " · нашёл цель" : ""}</div>
          </div>
          <div style="display:flex;flex-direction:column;gap:4px;align-items:flex-end">
            <span class="badge ${h.status}">${STATUS[h.status] ?? h.status}</span>
          </div>
        </div>
        <div class="bars">
          ${bar("Доверие", h.trust, "--trust")}
          ${bar("Решимость", h.resolve, "--resolve")}
          ${bar("Усталость", h.fatigue, "--fatigue")}
          ${bar("Стресс", h.stress, "--stress")}
        </div>
        <div class="hero-controls">
          <label class="check"><input type="checkbox" data-team="${h.id}" ${selected ? "checked" : ""}> в вылазку</label>
          <select data-hint="${h.id}">${HINTS.map(([v, t]) => `<option value="${v}" ${decisions.hints[h.id] === v ? "selected" : ""}>${t}</option>`).join("")}</select>
          <select data-gift="${h.id}">${giftOptions.map(([v, t]) => `<option value="${v}" ${decisions.gifts[h.id] === v ? "selected" : ""}>${t}</option>`).join("")}</select>
        </div>
      </div>`;
  }).join("");

  $("heroes").querySelectorAll("[data-team]").forEach((box) =>
    box.addEventListener("change", () => {
      box.checked ? decisions.team.add(box.dataset.team) : decisions.team.delete(box.dataset.team);
      box.closest(".hero").classList.toggle("selected", box.checked);
      updateTeamInfo();
    }));
  $("heroes").querySelectorAll("[data-hint]").forEach((sel) =>
    sel.addEventListener("change", () => {
      sel.value ? (decisions.hints[sel.dataset.hint] = sel.value) : delete decisions.hints[sel.dataset.hint];
    }));
  $("heroes").querySelectorAll("[data-gift]").forEach((sel) =>
    sel.addEventListener("change", () => {
      sel.value ? (decisions.gifts[sel.dataset.gift] = sel.value) : delete decisions.gifts[sel.dataset.gift];
      updateTeamInfo();
    }));
  $("heroes").querySelectorAll("[data-hero]").forEach((el) =>
    el.addEventListener("click", () => openHero(el.dataset.hero)));
}

function updateTeamInfo() {
  const n = decisions.team.size;
  const cost = Object.values(decisions.gifts).reduce((sum, kind) => {
    const g = state.giftKinds.find((k) => k.id === kind);
    return sum + (g ? g.cost : 0);
  }, 0);
  const team = n === 0 ? "вылазки не будет" : `в вылазке ${n} (нужно ${state.teamMin}–${state.teamMax})`;
  $("teamInfo").textContent = team + (cost ? ` · подарки на ${cost} зол.` : "");
}

function renderJournal(events, title) {
  $("journalTitle").textContent = title;
  const hideMinor = $("hideMinor").checked;
  let html = "";
  let day = null;
  let phase = null;
  for (const e of events) {
    if (hideMinor && e.importance <= 2) continue;
    if (showingFullLog && e.day !== day) {
      day = e.day;
      phase = null;
      html += `<h3 class="day">День ${e.day}</h3>`;
    }
    if (e.phase !== phase) {
      phase = e.phase;
      html += `<h3>${PHASE[e.phase] ?? e.phase}</h3>`;
    }
    const cls = [
      e.importance <= 2 ? "minor" : "",
      e.importance >= 7 ? "major" : "",
      e.importance >= 8 ? "big" : "",
      e.emotion < 0 ? "neg" : e.emotion > 0 ? "pos" : "",
    ].join(" ");
    html += `<div class="event ${cls}">${escapeHtml(e.summary)}</div>`;
  }
  $("journal").innerHTML = html || '<p class="muted">Пока ничего не произошло.</p>';
}

function showErrors(list) {
  $("errors").hidden = !list || list.length === 0;
  $("errors").innerHTML = (list || []).map((e) => `<div>• ${escapeHtml(e)}</div>`).join("");
}

async function runDay() {
  const body = {
    team: [...decisions.team],
    keepLoot: decisions.keepLoot,
    lootShares: state.pendingLoot && !decisions.keepLoot ? decisions.loot : null,
    gifts: Object.entries(decisions.gifts).map(([heroId, kindId]) => ({ heroId, kindId })),
    hints: decisions.hints,
  };
  $("runDay").disabled = true;
  const { ok, data } = await api("/api/day", body);
  $("runDay").disabled = false;
  if (!ok) {
    showErrors(data.errors || ["Не удалось прожить день."]);
    return;
  }
  showErrors(null);
  state = data;
  decisions = freshDecisions();
  showingFullLog = false;
  $("fullLog").textContent = "Весь журнал";
  render();
  $("journal").scrollTop = 0;
}

async function newGame() {
  if (state && state.hasGame && !confirm("Начать новую партию? Текущая будет перезаписана.")) return;
  const seedText = $("seed").value.trim();
  const { data } = await api("/api/new", { seed: seedText === "" ? null : Number(seedText) });
  state = data;
  decisions = freshDecisions();
  showingFullLog = false;
  showErrors(null);
  render();
}

async function toggleFullLog() {
  showingFullLog = !showingFullLog;
  $("fullLog").textContent = showingFullLog ? "Только этот день" : "Весь журнал";
  if (showingFullLog) {
    const { data } = await api("/api/log");
    renderJournal(data, "Весь журнал");
    $("journal").scrollTop = $("journal").scrollHeight;
  } else {
    render();
  }
}

async function openHero(id) {
  const { ok, data: h } = await api(`/api/hero/${id}`);
  if (!ok) return;
  const rel = h.relations.map((r) => `
    <tr><td>${r.name}</td><td class="n">${r.trust}</td><td class="n">${r.affection}</td><td class="n">${r.respect}</td><td class="n">${r.rivalry}</td></tr>`).join("");
  $("heroDetails").innerHTML = `
    <h2><span class="stars">${"★".repeat(h.stars)}</span> ${h.name}</h2>
    <div class="prof">${h.profession} · сила ${h.power} · опыт ${h.experience} · кошелёк ${h.purse} зол.</div>
    <p>${escapeHtml(h.bio)}</p>
    <p><b>Мечта:</b> ${escapeHtml(h.dream)}<br><b>Ценности:</b> ${h.values.map(escapeHtml).join("; ")}</p>
    <div class="detail-grid">${h.traits.map((t) => `<span>${TRAITS[t.id] ?? t.id}</span><b>${t.value}</b>`).join("")}</div>
    <p><b>К мастеру:</b> доверие ${h.master.trust}, уважение ${h.master.respect}, привязанность ${h.master.affection}, страх ${h.master.fear}</p>
    <b>Отношения к остальным</b>
    <table class="rel"><tr><th>Кому</th><th>Доверие</th><th>Привяз.</th><th>Уважение</th><th>Соперн.</th></tr>${rel}</table>
    <b>Последние события</b>
    ${h.recent.map((e) => `<div class="event ${e.emotion < 0 ? "neg" : e.emotion > 0 ? "pos" : ""}">День ${e.day}: ${escapeHtml(e.summary)}</div>`).join("")}`;
  $("heroDialog").showModal();
}

function escapeHtml(s) {
  return String(s).replace(/[&<>"]/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;" }[c]));
}

$("runDay").addEventListener("click", runDay);
$("newGame").addEventListener("click", newGame);
$("fullLog").addEventListener("click", toggleFullLog);
$("hideMinor").addEventListener("change", () => (showingFullLog ? toggleFullLog().then(toggleFullLog) : render()));
$("lootEqual").addEventListener("click", () => { decisions.keepLoot = false; splitEqually(); renderLoot(); });
$("keepLoot").addEventListener("change", (e) => { decisions.keepLoot = e.target.checked; renderLoot(); });

load();
