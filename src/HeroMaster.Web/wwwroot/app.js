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
  if (state.battle) enterBattle(state.battle); // бой шёл — продолжаем показ
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
  const { ok, data } = await api("/api/day/begin", body);
  $("runDay").disabled = false;
  if (!ok) {
    showErrors(data.errors || ["Не удалось прожить день."]);
    return;
  }
  showErrors(null);
  decisions = freshDecisions();
  if (data.battle) {
    state = data;
    enterBattle(data.battle);
    return;
  }
  afterDay(data);
}

function afterDay(data) {
  state = data;
  showingFullLog = false;
  $("fullLog").textContent = "Весь журнал";
  $("battle").hidden = true;
  render();
  $("journal").scrollTop = 0;
}

// ---------------- Живой бой ----------------

const CONDUCT = {
  allIn: ["по полной", false],
  steady: ["честно", false],
  halfHearted: ["вполсилы", true],
  passive: ["не дерётся", true],
  abandoned: ["бойкот", true],
  selfDefense: ["отбивается", true],
};
const ROLE = { melee: "", ranged: "стрелок", healer: "лекарь" };

const battle = { snap: null, feedCount: 0, playing: true, speed: 1, busy: false, timer: null, selectedHero: null };

function enterBattle(snap) {
  battle.snap = null;
  battle.feedCount = 0;
  battle.playing = true;
  battle.selectedHero = null;
  $("bFeed").innerHTML = "";
  $("bResults").hidden = true;
  $("app").hidden = true;
  $("battle").hidden = false;
  setSpeed(battle.speed);
  applySnapshot(snap);
  if (!battle.timer) battle.timer = setInterval(battleLoop, 300);
  window.scrollTo(0, 0);
}

async function battleLoop() {
  if (!battle.snap || battle.busy || !battle.playing || battle.snap.outcome !== "running") return;
  await stepBattle(battle.speed);
}

async function stepBattle(ticks) {
  battle.busy = true;
  const { ok, data } = await api("/api/battle/step", { ticks, feedFrom: battle.feedCount });
  battle.busy = false;
  if (ok) applySnapshot(data);
}

async function skipToEnd() {
  battle.playing = false;
  updatePlayButton();
  while (battle.snap && battle.snap.outcome === "running") await stepBattle(50);
}

function applySnapshot(snap) {
  battle.snap = snap;
  appendFeed(snap.feed.slice(Math.max(0, battle.feedCount - snap.feedFrom)));
  battle.feedCount = snap.feedFrom + snap.feed.length;

  $("bTitle").textContent = snap.mission.name;
  $("bDesc").textContent = snap.mission.description;
  const alive = snap.heroes.filter((h) => !h.dead).length;
  const wave = snap.nextWaveTick ? `следующая волна через ${Math.max(0, snap.nextWaveTick - snap.tick)}` : "все волны вышли";
  $("bStats").innerHTML =
    `<span>Волна ${snap.wavesSpawned} из ${snap.totalWaves}</span><span class="muted">${wave}</span>` +
    `<span>Убито врагов: ${snap.monstersKilled}</span><span>Добыча: ${snap.loot}</span><span>В строю: ${alive}/${snap.heroes.length}</span>`;
  renderMap(snap);
  updateAdviceHint();
  updatePlayButton();
  if (snap.outcome !== "running") showResults(snap);
}

function updateAdviceHint() {
  const snap = battle.snap;
  if (!snap || snap.outcome !== "running") {
    $("bAdviceHint").textContent = "";
    return;
  }
  const sel = snap.heroes.find((h) => h.id === battle.selectedHero && !h.dead);
  if (!sel) battle.selectedHero = null;
  $("bAdviceHint").innerHTML = sel
    ? `Совет для <b>${escapeHtml(sel.nameGenitive || sel.name)}</b>: нажмите «Сюда» на нужной позиции. Нажмите на героя ещё раз, чтобы советовать всем.`
    : "Совет всему отряду: нажмите «Сюда» на нужной позиции. Нажмите на героя, чтобы советовать только ему. Слишком частые советы путают героев.";
}

async function advise(zone) {
  const { ok, data } = await api("/api/battle/advice", { zone, heroId: battle.selectedHero, feedFrom: battle.feedCount });
  if (!ok) {
    appendFeed((data.errors || ["Совет не прозвучал."]).map((t) => ({ tick: battle.snap.tick, kind: "error", text: t, importance: 5, emotion: -1 })));
    return;
  }
  applySnapshot(data);
}

$("bMap").addEventListener("click", (e) => {
  const go = e.target.closest("[data-advise]");
  if (go) {
    advise(go.dataset.advise);
    return;
  }
  const chip = e.target.closest("[data-chip]");
  if (chip && battle.snap && battle.snap.outcome === "running") {
    battle.selectedHero = battle.selectedHero === chip.dataset.chip ? null : chip.dataset.chip;
    renderMap(battle.snap);
    updateAdviceHint();
  }
});

// Пока кнопка мыши зажата над картой, не перерисовываем её — иначе нажатие на «Сюда!» потеряется.
let mapPointerDown = false;
$("bMap").addEventListener("pointerdown", () => (mapPointerDown = true));
window.addEventListener("pointerup", () => {
  if (!mapPointerDown) return;
  mapPointerDown = false;
  setTimeout(() => battle.snap && renderMap(battle.snap), 0);
});

function renderMap(snap) {
  if (mapPointerDown) return;
  // Зоны прижаты к краям с запасом, чтобы рамки не вылезали за карту.
  const pos = (z) => ({ x: Math.min(86, Math.max(14, z.x)), y: Math.min(78, Math.max(9, z.y)) });
  const byId = Object.fromEntries(snap.mission.zones.map((z) => [z.id, z]));
  const drawn = new Set();
  let lines = "";
  for (const z of snap.mission.zones) {
    for (const l of z.links) {
      const key = [z.id, l].sort().join("|");
      if (drawn.has(key) || !byId[l]) continue;
      drawn.add(key);
      const a = pos(z), b = pos(byId[l]);
      lines += `<line x1="${a.x}" y1="${a.y}" x2="${b.x}" y2="${b.y}" />`;
    }
  }

  const zonesHtml = snap.mission.zones.map((z) => {
    const p = pos(z);
    const heroes = snap.heroes.filter((h) => h.zone === z.id);
    const monsters = snap.monsters.filter((m) => m.zone === z.id);
    const tags = [];
    if (z.spawn) tags.push("отсюда идут враги");
    if (!z.spawn && z.width <= 2) tags.push("узкий проход");
    if (z.cover > 0) tags.push(`укрытие ${z.cover}%`);
    if (!z.spawn && z.width >= 5) tags.push("открытое место");
    const cls = ["zone", z.spawn ? "spawn" : "", heroes.some((h) => !h.dead) ? "heroes" : "", heroes.some((h) => !h.dead) && monsters.length ? "fight" : ""].join(" ");
    const mon = monsters.map((m) => `${m.name} ×${m.count}`).join(", ");
    const chips = heroes.map((h) => {
      const [label, bad] = CONDUCT[h.conduct] ?? [h.conduct, false];
      const hpPct = Math.max(0, Math.round((h.hp * 100) / h.maxHp));
      const role = ROLE[h.role] ? ` · ${ROLE[h.role]}` : "";
      const chipCls = ["chip", h.dead ? "dead" : "", h.injured || hpPct < 35 ? "hurt" : "", h.zone !== h.targetZone ? "moving" : "", h.id === battle.selectedHero ? "selected" : ""].join(" ");
      return `<div class="${chipCls}" data-chip="${h.dead ? "" : h.id}" title="${escapeHtml(h.name)}: ${h.hp}/${h.maxHp}">
          <span class="nm">${escapeHtml(h.name)}</span>
          <span class="cd ${bad ? "bad" : ""}">${h.dead ? "пал" : label}${role}</span>
          <div class="hp"><div style="width:${hpPct}%"></div></div>
        </div>`;
    }).join("");
    return `<div class="${cls}" style="left:${p.x}%;top:${p.y}%">
        <div class="zone-name">${escapeHtml(z.name)}</div>
        <div class="zone-tags">${tags.join(" · ")}</div>
        ${mon ? `<div class="zone-monsters">${escapeHtml(mon)}</div>` : ""}
        <div class="zone-heroes">${chips}</div>
        ${!z.spawn && snap.outcome === "running" ? `<button class="go" data-advise="${z.id}">Сюда!</button>` : ""}
      </div>`;
  }).join("");

  $("bMap").innerHTML = `<svg viewBox="0 0 100 100" preserveAspectRatio="none">${lines}</svg>${zonesHtml}`;
}

function appendFeed(entries) {
  const feed = $("bFeed");
  const hideKills = $("bHideKills").checked;
  const nearBottom = feed.scrollHeight - feed.scrollTop - feed.clientHeight < 60;
  for (const f of entries) {
    if (f.kind === "arrived") continue;
    const cls = [
      "event",
      f.kind === "kill" ? "kill" : "",
      f.importance <= 2 ? "minor" : "",
      f.importance >= 7 ? "major" : "",
      f.importance >= 9 ? "big" : "",
      f.emotion < 0 ? "neg" : f.emotion > 0 ? "pos" : "",
    ].join(" ");
    const div = document.createElement("div");
    div.className = cls;
    div.hidden = hideKills && f.kind === "kill";
    div.innerHTML = `<span class="tick">${f.tick}</span>${escapeHtml(f.text)}`;
    feed.appendChild(div);
  }
  if (nearBottom) feed.scrollTop = feed.scrollHeight;
}

function showResults(snap) {
  const win = snap.outcome === "victory";
  const rows = [...snap.heroes]
    .sort((a, b) => b.score - a.score)
    .map((h) => {
      const status = h.dead ? '<span class="verdict-lose">пал</span>' : h.injured ? "ранен" : "цел";
      const mvp = h.id === snap.mvp ? ' <span class="mvp">MVP</span>' : "";
      return `<tr><td>${escapeHtml(h.name)}${mvp}</td><td class="n">${h.kills}</td><td class="n">${h.damageDealt}</td><td class="n">${h.healed}</td><td>${status}</td></tr>`;
    }).join("");
  $("bResults").innerHTML = `
    <h2 class="${win ? "verdict-win" : "verdict-lose"}">${win ? "Оборона выдержала" : "Отряд погиб"}</h2>
    <p>Убито врагов: <b>${snap.monstersKilled}</b> · добыча: <b>${snap.loot}</b> золота · тиков боя: ${snap.tick}</p>
    <table><tr><th>Герой</th><th>Убил</th><th>Урон</th><th>Вылечил</th><th>Итог</th></tr>${rows}</table>
    <button id="bFinish" class="primary">Вернуться на базу</button>`;
  $("bResults").hidden = false;
  $("bFinish").addEventListener("click", finishDay);
}

async function finishDay() {
  $("bFinish").disabled = true;
  const { ok, data } = await api("/api/day/finish", {});
  if (!ok) {
    $("bFinish").disabled = false;
    alert((data.errors || ["Не удалось закончить день."]).join("\n"));
    return;
  }
  battle.snap = null;
  afterDay(data);
}

function setSpeed(speed) {
  battle.speed = speed;
  document.querySelectorAll("[data-speed]").forEach((b) => b.classList.toggle("active", Number(b.dataset.speed) === speed));
}

function updatePlayButton() {
  const running = battle.snap && battle.snap.outcome === "running";
  $("bPlay").disabled = !running;
  $("bSkip").disabled = !running;
  $("bPlay").textContent = battle.playing ? "Пауза" : "Продолжить";
}

$("bPlay").addEventListener("click", () => { battle.playing = !battle.playing; updatePlayButton(); });
$("bSkip").addEventListener("click", skipToEnd);
document.querySelectorAll("[data-speed]").forEach((b) => b.addEventListener("click", () => setSpeed(Number(b.dataset.speed))));
$("bHideKills").addEventListener("change", (e) => {
  document.querySelectorAll("#bFeed .kill").forEach((el) => (el.hidden = e.target.checked));
});

async function newGame() {
  if (state && state.hasGame && !confirm("Начать новую партию? Текущая будет перезаписана.")) return;
  const seedText = $("seed").value.trim();
  const { data } = await api("/api/new", { seed: seedText === "" ? null : Number(seedText) });
  state = data;
  decisions = freshDecisions();
  showingFullLog = false;
  battle.snap = null;
  $("battle").hidden = true;
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
    <b>Приёмы</b> <span class="muted">(чутьё на позиции: ${h.sense}/100)</span>
    ${h.techniques.length === 0
      ? '<p class="muted">Пока ничему не научился. Удачные советы мастера в бою и разговоры с опытными товарищами учат — медленно.</p>'
      : h.techniques.map((t) => `<div class="tech">
          <span>${escapeHtml(t.name)}${t.learned ? ' <span class="learned">усвоено</span>' : ""}<br><span class="muted">удачно ${t.successes}, неудачно ${t.failures}${t.from ? `, научил: ${escapeHtml(t.from)}` : ""}</span></span>
          <div class="bar-track"><div class="bar-fill" style="width:${t.strength}%;background:var(--resolve)"></div></div>
          <span class="num">${t.strength}</span></div>`).join("")}
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
