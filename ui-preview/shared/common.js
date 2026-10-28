/* NekoStrap UI preview — shared pages & interactions.
   Each variant (a-mono/b-frame/c-touch) only owns styles + chrome.
   To add a page: push to PAGES and add renderers[<id>] below. */
(function () {
  const PAGES = [
    { id: "home",     icon: "⌂", ru: "Главная",  en: "Home" },
    { id: "mods",     icon: "▦", ru: "Моды",     en: "Mods" },
    { id: "flags",    icon: "⚙", ru: "FastFlags", en: "FastFlags" },
    { id: "versions", icon: "≡", ru: "Версии",   en: "Versions" },
    { id: "history",  icon: "◷", ru: "История",  en: "History" },
    { id: "chat",     icon: "✎", ru: "Чат",      en: "Chat" },
    { id: "settings", icon: "⚒", ru: "Настройки", en: "Settings" },
    { id: "about",    icon: "?", ru: "О программе", en: "About" },
  ];

  const T = {
    ru: {
      ready: "Готов к запуску", play: "Играть", launching: "Запуск…",
      version: "Версия клиента", account: "Аккаунт: гость",
      stats_mods: "Активных модов", stats_flags: "Флагов включено", stats_hours: "Часов в игре",
      inv_title: "Код приглашения", inv_sub: "Создай код на своём сервере — друг вводит его и заходит к тебе.",
      inv_create: "Создать код", inv_join: "Войти по коду друга", inv_btn: "Создать",
      inv_uses: "Входов", inv_ttl: "Срок", inv_code_ok: "Код готов:",
      search_ph: "Поиск игр…", fav_title: "Избранные плейсы", last_title: "Последняя сессия",
      play_again: "Играть снова", server_card: "Карточка сервера", clean_run: "Чистый запуск",
      mods_sub: "Профили модов: быстрая замена звуков, шрифтов, текстур.",
      mods_profiles: "Профили", mods_new: "Новый профиль", mods_export: "Экспорт zip", mods_import: "Импорт zip",
      mods_enable: "Включить", mods_disable: "Выключить", mods_delete: "Удалить",
      flags_sub: "Таблица флагов активной версии с живым поиском и пресетами.",
      flags_presets: "Пресеты", flags_profiles: "Профили флагов", flags_search: "Поиск по имени/значению…",
      flags_import: "Импорт JSON", flags_export: "Экспорт JSON", flags_name: "Имя", flags_val: "Значение",
      ver_sub: "Установленные клиенты: активация, откат, ремонт.",
      ver_install: "Установить версию", ver_check: "Проверить целостность", ver_active: "Активна",
      ver_rollback: "Откатить", ver_remove: "Удалить", ver_studio: "Roblox Studio",
      hist_sub: "Наиграно по плейсам и лог заходов.",
      hist_time: "Наиграно", hist_log: "Лог заходов", hist_hop: "Сервер-хоп", hist_same: "На тот же сервер",
      chat_sub: "Две комнаты: «Все» и «Сервер». Оверлей поверх игры.",
      chat_on: "Чат включён", chat_ls: "ЛС", chat_overlay: "Оверлей в игре", chat_send: "Отправить",
      chat_all: "Все", chat_server: "Сервер",
      set_sub: "Внешний вид, звуки, трей, музыка, Discord, система.",
      set_theme: "Тема", set_lang: "Язык", set_wall: "Обои", set_sounds: "Звуки интерфейса",
      set_vol: "Громкость", set_tray: "Сворачивать в трей", set_music: "Музыка (папка Music)",
      set_shuffle: "Шаффл", set_discord: "Discord Rich Presence", set_priority: "Приоритет процесса",
      set_cdn: "CDN-фикс для РФ", set_color: "Цветокоррекция экрана", set_only_game: "Только в игре",
      about_sub: "Версия лаунчера, автообновление, ссылки.",
      about_check: "Проверить обновления", about_latest: "У вас последняя версия",
      about_tg: "Telegram", about_gh: "GitHub", about_license: "MIT License",
      toast_ok: "Готово", toast_play: "Запуск Roblox…", toast_upd: "Обновлений нет",
      toast_cp: "Код приглашения скопирован", toast_mod: "Профиль применён",
      palette_ph: "Командная палитра… (пример: «играть», «флаги»)",
      search_none: "Ничего не найдено",
    },
    en: {
      ready: "Ready to launch", play: "Play", launching: "Launching…",
      version: "Client version", account: "Account: guest",
      stats_mods: "Active mods", stats_flags: "Flags on", stats_hours: "Hours in game",
      inv_title: "Invite code", inv_sub: "Create a code on your server — a friend enters it and joins you.",
      inv_create: "Create code", inv_join: "Join by friend's code", inv_btn: "Create",
      inv_uses: "Uses", inv_ttl: "TTL", inv_code_ok: "Code ready:",
      search_ph: "Search games…", fav_title: "Favorite places", last_title: "Last session",
      play_again: "Play again", server_card: "Server card", clean_run: "Clean launch",
      mods_sub: "Mod profiles: quick swap of sounds, fonts, textures.",
      mods_profiles: "Profiles", mods_new: "New profile", mods_export: "Export zip", mods_import: "Import zip",
      mods_enable: "Enable", mods_disable: "Disable", mods_delete: "Delete",
      flags_sub: "Flags table for the active version with live search and presets.",
      flags_presets: "Presets", flags_profiles: "Flag profiles", flags_search: "Search name/value…",
      flags_import: "Import JSON", flags_export: "Export JSON", flags_name: "Name", flags_val: "Value",
      ver_sub: "Installed clients: activate, rollback, repair.",
      ver_install: "Install version", ver_check: "Check integrity", ver_active: "Active",
      ver_rollback: "Rollback", ver_remove: "Remove", ver_studio: "Roblox Studio",
      hist_sub: "Playtime by place and join log.",
      hist_time: "Playtime", hist_log: "Join log", hist_hop: "Server hop", hist_same: "Same server",
      chat_sub: "Two rooms: All and Server. In-game overlay.",
      chat_on: "Chat enabled", chat_ls: "DMs", chat_overlay: "In-game overlay", chat_send: "Send",
      chat_all: "All", chat_server: "Server",
      set_sub: "Appearance, sounds, tray, music, Discord, system.",
      set_theme: "Theme", set_lang: "Language", set_wall: "Wallpaper", set_sounds: "UI sounds",
      set_vol: "Volume", set_tray: "Minimize to tray", set_music: "Music (Music folder)",
      set_shuffle: "Shuffle", set_discord: "Discord Rich Presence", set_priority: "Process priority",
      set_cdn: "CDN fix for RU", set_color: "Screen color correction", set_only_game: "Only in game",
      about_sub: "Launcher version, self-update, links.",
      about_check: "Check for updates", about_latest: "You are on the latest version",
      about_tg: "Telegram", about_gh: "GitHub", about_license: "MIT License",
      toast_ok: "Done", toast_play: "Launching Roblox…", toast_upd: "No updates",
      toast_cp: "Invite code copied", toast_mod: "Profile applied",
      palette_ph: "Command palette… (try: play, flags)",
      search_none: "Nothing found",
    },
  };

  const state = {
    lang: "ru",
    page: "home",
    playing: false,
    activeMod: "Без модов",
    activeProfile: "FPS 240",
    inviteCode: null,
    flags: [
      { name: "DFIntTaskSchedulerTargetFps", val: "240", on: true },
      { name: "FFlagDebugGraphicsPreferredAdapter", val: "Vulkan", on: false },
      { name: "DFFlagTextureQualityOverrideEnabled", val: "true", on: true },
      { name: "FIntRenderShadowIntensity", val: "0", on: true },
      { name: "DFIntMSAASamples", val: "4", on: true },
      { name: "FFlagDisablePostFx", val: "true", on: false },
      { name: "FIntDebugForceMSAASamples", val: "2", on: false },
      { name: "DFFlagEnablePreferredTextureSize", val: "true", on: true },
    ],
  };

  function t(k) { return (T[state.lang] && T[state.lang][k]) || T.ru[k] || k; }
  function esc(s) { return String(s).replace(/[&<>"]/g, c => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;" }[c])); }

  /* Функция с раскрывающимися настройками: включил тумблер — под строкой
     выезжают её опции. Клик по заголовку (или «⚙») тоже открывает/закрывает. */
  function feature(name, optsHtml, o) {
    o = o || {};
    const on = o.on !== false;
    const hint = o.hint ? `<span class="feature-hint">${o.hint}</span>` : "";
    return `
    <div class="feature ${on ? "open" : ""}" data-feature>
      <div class="feature-head" data-feature-head>
        <span class="feature-titles">
          <span class="feature-name">${name}</span>
          ${hint}
        </span>
        <button type="button" class="feature-gear" title="Настройки" tabindex="-1">⚙</button>
        <label class="switch" onclick="event.stopPropagation()">
          <input type="checkbox" ${on ? "checked" : ""} data-feature-toggle><span></span>
        </label>
      </div>
      <div class="feature-body"><div class="feature-opts">${optsHtml}</div></div>
    </div>`;
  }

  function optRow(label, controlHtml) {
    return `<div class="opt-row"><span class="opt-label">${label}</span><span class="opt-ctl">${controlHtml}</span></div>`;
  }
  function optSwitch(label, on, attrs) {
    return `<div class="opt-row"><span class="opt-label">${label}</span>
      <label class="switch sm"><input type="checkbox" ${on ? "checked" : ""} ${attrs || ""}><span></span></label></div>`;
  }
  function optSelect(options, sel, attrs) {
    const opts = options.map(x => {
      const v = Array.isArray(x) ? x[0] : x;
      const l = Array.isArray(x) ? x[1] : x;
      return `<option ${v === sel ? "selected" : ""}>${l}</option>`;
    }).join("");
    return `<select ${attrs || ""}>${opts}</select>`;
  }
  function optRange(val, attrs) {
    return `<input type="range" min="0" max="100" value="${val}" ${attrs || ""}>`;
  }
  function optInput(placeholder, attrs) {
    return `<input class="input opt-input" placeholder="${esc(placeholder)}" ${attrs || ""}>`;
  }

  /* ---------- page templates ---------- */
  const games = [
    { id: "6516141723", name: "Blox Fruits", players: "412K" },
    { id: "286090429", name: "Adopt Me!", players: "88K" },
    { id: "920587237", name: "Adopt Me Trading", players: "12K" },
    { id: "606849621", name: "Jailbreak", players: "54K" },
    { id: "2753915549", name: "Blox Fruits 2", players: "9K" },
  ];

  const renderers = {
    home() {
      const favs = games.slice(0, 3).map(g => `
        <button class="tile" data-act="play-game" data-name="${esc(g.name)}">
          <span class="tile-art"></span>
          <span class="tile-name">${esc(g.name)}</span>
          <span class="tile-meta">${g.players} online</span>
        </button>`).join("");
      return `
      <section class="hero card">
        <h2>${t("play")}</h2>
        <p class="statusline"><span class="dot good"></span> ${t("ready")}</p>
        <p class="mono dim">${t("account")}</p>
        <button class="btn primary big" data-act="play">${t("play")}</button>
        <div class="track" data-role="play-progress" hidden><div class="fill" data-role="play-fill"></div></div>
        <hr>
        <p class="mono dim">${t("version")}: version-0e2c6c4a8f7d4b1e · live</p>
      </section>

      <div class="stats">
        <div class="stat card"><b data-role="stat-mods">3</b><span>${t("stats_mods")}</span></div>
        <div class="stat card"><b data-role="stat-flags">5</b><span>${t("stats_flags")}</span></div>
        <div class="stat card"><b>47ч 12м</b><span>${t("stats_hours")}</span></div>
      </div>

      <section class="card">
        <h3>${t("inv_title")}</h3>
        <p class="dim">${t("inv_sub")}</p>
        <div class="invite-grid">
          <div>
            <label class="mono dim">${t("inv_create")}</label>
            <div class="row">
              <select data-role="inv-uses"><option>1</option><option selected>3</option><option>∞</option></select>
              <select data-role="inv-ttl"><option>10 мин</option><option selected>1 час</option><option>24 часа</option></select>
            </div>
            <button class="btn" data-act="invite-create">${t("inv_btn")}</button>
            <p class="mono" data-role="inv-status">${t("inv_join")}: нужно быть в игре</p>
          </div>
          <div class="vsep"></div>
          <div>
            <label class="mono dim">${t("inv_join")}</label>
            <input class="input code-input" maxlength="4" placeholder="XXXX" data-role="inv-join">
            <button class="btn" data-act="invite-join">${t("play")}</button>
          </div>
        </div>
      </section>

      <section class="card">
        <input class="input" placeholder="${t("search_ph")}" data-role="game-search">
        <div class="tiles" data-role="game-results">${favs}</div>
      </section>

      <div class="cols">
        <section class="card">
          <h3>${t("fav_title")}</h3>
          <ul class="list">
            <li><span>Побег из тюрьмы</span><button class="btn sm" data-act="play-game" data-name="Побег из тюрьмы">${t("play")}</button></li>
            <li><span>Slap Battles</span><button class="btn sm" data-act="play-game" data-name="Slap Battles">${t("play")}</button></li>
          </ul>
        </section>
        <section class="card">
          <h3>${t("last_title")}</h3>
          <p class="mono dim">Blox Fruits · PlaceId 6516141723<br>JobId f3a1…c2 · 2ч 14м назад</p>
          <button class="btn" data-act="play-game" data-name="Blox Fruits">${t("play_again")}</button>
          <hr>
          <h4 class="mono dim">${t("server_card")}</h4>
          <p class="mono">ip 128.116.45.12 · RU Москва · ping 24ms</p>
          <div class="row">
            <button class="btn sm" data-act="toast" data-msg="Сервер-хоп…">${t("hist_hop")}</button>
            <button class="btn sm ghost" data-act="toast" data-msg="Чистый запуск (без модов)">${t("clean_run")}</button>
          </div>
        </section>
      </div>`;
    },

    mods() {
      const profiles = [
        { name: "Без модов", files: 0, on: true },
        { name: "Sprint + FOV", files: 12, on: false },
        { name: "Тихий шаг", files: 3, on: false },
        { name: "Пак текстур HD", files: 240, on: false },
        { name: "Fleasion (авто)", files: 8, on: false },
      ];
      const rows = profiles.map(p => `
        <li class="${p.on ? "active" : ""}">
          <span>${esc(p.name)} <span class="mono dim">${p.files} файлов</span></span>
          <span class="row">
            <button class="btn sm" data-act="mod-apply" data-name="${esc(p.name)}">${p.on ? t("mods_disable") : t("mods_enable")}</button>
            <button class="btn sm ghost" data-act="toast" data-msg="Профиль удалён">${t("mods_delete")}</button>
          </span>
        </li>`).join("");
      return `
      <h1>${t("mods")}</h1>
      <p class="dim">${t("mods_sub")}</p>
      <div class="row wrap gap">
        <button class="btn" data-act="toast" data-msg="Новый профиль создан">${t("mods_new")}</button>
        <button class="btn ghost" data-act="toast" data-msg="Экспорт: profile.zip">${t("mods_export")}</button>
        <button class="btn ghost" data-act="toast" data-msg="Импорт zip…">${t("mods_import")}</button>
      </div>
      <section class="card">
        <h3>${t("mods_profiles")}</h3>
        <ul class="list" data-role="mod-list">${rows}</ul>
      </section>
      <section class="card">
        <h3>Быстрая замена</h3>
        <div class="chips">
          <button class="chip" data-act="toast" data-msg="Звук смерти: выбери wav">Звук смерти</button>
          <button class="chip" data-act="toast" data-msg="Шрифт: выбери ttf">Шрифт</button>
          <button class="chip" data-act="toast" data-msg="Курсор: выбери png">Курсор</button>
          <button class="chip" data-act="toast" data-msg="Текстуры: выбери папку">Текстуры</button>
        </div>
      </section>`;
    },

    flags() {
      const presets = ["FPS 60", "FPS 144", "FPS 240", "Свет", "MSAA", "Vulkan", "Без пост-эффектов", "Мобильный"];
      const chips = presets.map(p => `
        <button class="chip ${p === state.activeProfile ? "on" : ""}" data-act="flag-preset" data-name="${esc(p)}">${esc(p)}</button>`).join("");
      const rows = state.flags.map((f, i) => `
        <tr data-flag-row data-name="${esc(f.name.toLowerCase())}" data-val="${esc(f.val.toLowerCase())}">
          <td><label class="switch"><input type="checkbox" ${f.on ? "checked" : ""} data-act="flag-toggle" data-i="${i}"><span></span></label></td>
          <td class="mono">${esc(f.name)}</td>
          <td class="mono">${esc(f.val)}</td>
          <td><button class="btn sm ghost" data-act="toast" data-msg="Удалить флаг">✕</button></td>
        </tr>`).join("");
      return `
      <h1>FastFlags</h1>
      <p class="dim">${t("flags_sub")}</p>
      <section class="card">
        <h3>${t("flags_presets")}</h3>
        <div class="chips">${chips}</div>
      </section>
      <section class="card">
        <div class="row wrap gap">
          <input class="input grow" placeholder="${t("flags_search")}" data-role="flag-search">
          <button class="btn ghost" data-act="toast" data-msg="Импорт JSON…">${t("flags_import")}</button>
          <button class="btn ghost" data-act="toast" data-msg="Экспорт: flags.json">${t("flags_export")}</button>
        </div>
        <table class="table" data-role="flag-table">
          <thead><tr><th></th><th>${t("flags_name")}</th><th>${t("flags_val")}</th><th></th></tr></thead>
          <tbody>${rows}</tbody>
        </table>
      </section>
      <section class="card">
        <h3>${t("flags_profiles")}</h3>
        <ul class="list">
          <li><span>Мой профиль</span><button class="btn sm" data-act="toast" data-msg="Профиль флагов применён">${t("mods_enable")}</button></li>
          <li><span>Ваниль+</span><button class="btn sm" data-act="toast" data-msg="Профиль флагов применён">${t("mods_enable")}</button></li>
        </ul>
      </section>`;
    },

    versions() {
      const vers = [
        { v: "version-0e2c6c4a8f7d4b1e", date: "28.10.2026", active: true },
        { v: "version-15e8a91c2b3d4f00", date: "12.10.2026", active: false },
        { v: "version-91aa77c0ffee1234", date: "01.10.2026", active: false },
      ];
      const rows = vers.map(v => `
        <li class="${v.active ? "active" : ""}">
          <span class="mono">${esc(v.v)}<br><span class="dim">${v.date}${v.active ? " · " + t("ver_active") : ""}</span></span>
          <span class="row">
            ${v.active ? "" : `<button class="btn sm" data-act="toast" data-msg="Активирована ${esc(v.v)}">${t("ver_active")}</button>`}
            <button class="btn sm ghost" data-act="toast" data-msg="Откат на предыдущую…">${t("ver_rollback")}</button>
            <button class="btn sm ghost" data-act="toast" data-msg="Удаление версии…">${t("ver_remove")}</button>
          </span>
        </li>`).join("");
      return `
      <h1>${t("versions")}</h1>
      <p class="dim">${t("ver_sub")}</p>
      <div class="row wrap gap">
        <button class="btn" data-act="install-ver">${t("ver_install")}</button>
        <button class="btn ghost" data-act="toast" data-msg="Проверка целостности… OK">${t("ver_check")}</button>
        <button class="btn ghost" data-act="toast" data-msg="Roblox Studio: та же версия">${t("ver_studio")}</button>
      </div>
      <section class="card">
        <ul class="list">${rows}</ul>
        <div class="track" data-role="install-progress" hidden><div class="fill" data-role="install-fill"></div></div>
      </section>`;
    },

    history() {
      const places = [
        { name: "Blox Fruits", time: "31ч 04м" },
        { name: "Jailbreak", time: "9ч 41м" },
        { name: "Slap Battles", time: "4ч 55м" },
        { name: "Adopt Me!", time: "1ч 32м" },
      ];
      const rows = places.map(p => `
        <li><span>${esc(p.name)}</span>
          <span class="row"><span class="mono dim">${p.time}</span>
          <button class="btn sm" data-act="play-game" data-name="${esc(p.name)}">${t("play")}</button></span>
        </li>`).join("");
      const log = [
        ["28.10 21:14", "6516141723", "f3a1…c2", "2ч 14м", "128.116.45.12"],
        ["28.10 18:02", "606849621", "9bb0…11", "0ч 48м", "178.44.12.9"],
        ["27.10 23:40", "6516141723", "aa01…7e", "3ч 02м", "128.116.45.12"],
      ].map(r => `<tr class="mono"><td>${r[0]}</td><td>${r[1]}</td><td>${r[2]}</td><td>${r[3]}</td><td>${r[4]}</td></tr>`).join("");
      return `
      <h1>${t("history")}</h1>
      <p class="dim">${t("hist_sub")}</p>
      <div class="cols">
        <section class="card">
          <h3>${t("hist_time")}</h3>
          <ul class="list">${rows}</ul>
        </section>
        <section class="card">
          <h3>${t("server_card")}</h3>
          <p class="mono">Blox Fruits · 128.116.45.12 · ping 24ms</p>
          <div class="row">
            <button class="btn sm" data-act="toast" data-msg="Вход на тот же сервер…">${t("hist_same")}</button>
            <button class="btn sm ghost" data-act="toast" data-msg="Случайный сервер…">${t("hist_hop")}</button>
          </div>
        </section>
      </div>
      <section class="card">
        <h3>${t("hist_log")}</h3>
        <div class="scroll-x">
          <table class="table"><thead><tr><th>Время</th><th>PlaceId</th><th>JobId</th><th>Длит.</th><th>IP</th></tr></thead>
          <tbody>${log}</tbody></table>
        </div>
      </section>`;
    },

    chat() {
      const msgs = [
        { u: "kot", m: "го в блюкс фрутс", me: false },
        { u: "nyan", m: "уже на сервере, входим по коду", me: false },
        { u: "вы", m: "сейчас соберу инвайт", me: true },
        { u: "kot", m: "ок ждём", me: false },
      ];
      const bubbles = msgs.map(x => `
        <div class="msg ${x.me ? "me" : ""}">
          <b class="mono">${esc(x.u)}</b>
          <span>${esc(x.m)}</span>
        </div>`).join("");
      return `
      <h1>${t("chat")}</h1>
      <p class="dim">${t("chat_sub")}</p>
      <div class="row wrap gap">
        <button class="chip on" data-act="chat-room" data-room="all">${t("chat_all")}</button>
        <button class="chip" data-act="chat-room" data-room="server">${t("chat_server")} <span class="mono dim">· 4</span></button>
      </div>
      <div class="cols chat-cols">
        <section class="card chat-card">
          <div class="chat-log" data-role="chat-log">${bubbles}</div>
          <div class="row gap">
            <input class="input grow" placeholder="Сообщение…" data-role="chat-input">
            <button class="btn" data-act="chat-send">${t("chat_send")}</button>
          </div>
        </section>
        <section class="card">
          <h3>${t("chat")}</h3>
          <div class="feature-list">
            ${feature(t("chat_on"), `
              ${optSwitch(t("chat_ls"), true)}
              ${optSwitch("Звуки уведомлений", true)}
              ${optRow("Комната по умолчанию", optSelect([["all", t("chat_all")], ["server", t("chat_server")]], "all"))}
            `, { on: true, hint: "две комнаты + ЛС" })}
            ${feature(t("chat_overlay"), `
              ${optSwitch("Показывать при входе в игру", true)}
              ${optSwitch("Прозрачный фон", false)}
              ${optRow("Позиция", optSelect(["Слева сверху", "Справа сверху", "Слева снизу", "Справа снизу"], "Справа сверху"))}
              ${optRow("Прозрачность", optRange(85))}
              ${optRow("Размер", optSelect(["Мелкий", "Средний", "Крупный"], "Средний"))}
              ${`<p class="mono dim opt-note">Перетаскивается за шапку, сворачивается в полоску, «×» прячет без разрыва.</p>`}
            `, { on: true, hint: "поверх игры" })}
          </div>
        </section>
      </div>`;
    },

    settings() {
      return `
      <h1>${t("settings")}</h1>
      <p class="dim">${t("set_sub")}</p>
      <div class="cols">
        <section class="card">
          <h3>Внешний вид</h3>
          <div class="feature-list">
            ${optRow(t("set_lang"), optSelect([["ru", "Русский"], ["en", "English"]], state.lang, 'data-role="lang"'))}
            ${optRow(t("set_theme"), optSelect(["Тёмная", "Светлая", "Как в системе"], "Тёмная", 'data-act="toast" data-msg="Тема переключена"'))}
            ${feature(t("set_wall"), `
              ${optRow("Режим", optSelect(["Картинка", "GIF", "Цвет"], "GIF"))}
              ${optSwitch("Блюр обоев", true)}
              ${optRow("Затемнение", optRange(78))}
              ${optRow("Своя папка", optInput("C:\\Wallpapers\\…", 'data-act="toast" data-msg="Выбор папки…"'))}
            `, { on: true, hint: "GIF · блюр · затемнение" })}
            ${feature(t("set_sounds"), `
              ${optRow(t("set_vol"), optRange(40))}
              ${optSwitch("Свой wav на каждый звук", false)}
              ${optRow("Клик", `<button class="btn sm ghost" data-act="toast" data-msg="Выбор click.wav">выбрать wav</button>`)}
              ${optRow("Транзишн", `<button class="btn sm ghost" data-act="toast" data-msg="Выбор transition.wav">выбрать wav</button>`)}
            `, { on: true, hint: "громкость · свои wav" })}
          </div>
        </section>

        <section class="card">
          <h3>${t("set_music")}</h3>
          <div class="feature-list">
            ${feature(t("set_music"), `
              ${optSwitch(t("set_shuffle"), true)}
              ${optRow(t("set_vol"), optRange(70))}
              ${optSwitch("Автозапуск при старте лаунчера", false)}
              ${optSwitch("Пауза в игре", true)}
              <div class="row gap opt-note">
                <button class="btn sm" data-act="toast" data-msg="▶ track_01.mp3">▶</button>
                <button class="btn sm ghost" data-act="toast" data-msg="Shuffle">⇄</button>
                <span class="mono dim">track_01.mp3 · 2:14 / 3:40</span>
              </div>
            `, { on: true, hint: "папка Music · шаффл" })}
          </div>
        </section>

        <section class="card">
          <h3>Discord</h3>
          <div class="feature-list">
            ${feature(t("set_discord"), `
              ${optSwitch("Название игры", true)}
              ${optSwitch("Сервер (IP/гео)", true)}
              ${optSwitch("Иконка плейса", true)}
              ${optSwitch("Время сессии", true)}
              <div class="feature nested">
                <div class="feature-head" data-feature-head>
                  <span class="feature-titles">
                    <span class="feature-name">Кнопка «Открыть игру»</span>
                    <span class="feature-hint">на страницу плейса / к другу</span>
                  </span>
                  <button type="button" class="feature-gear" tabindex="-1">⚙</button>
                  <label class="switch sm" onclick="event.stopPropagation()">
                    <input type="checkbox" checked data-feature-toggle><span></span>
                  </label>
                </div>
                <div class="feature-body"><div class="feature-opts">
                  ${optRow("Текст кнопки", optInput("Открыть игру"))}
                  ${optRow("Ссылка", optSelect(["Страница плейса", "Вход к другу на сервер"], "Страница плейса"))}
                </div></div>
              </div>
            `, { on: true, hint: "статус · сервер · кнопки" })}
          </div>
        </section>

        <section class="card">
          <h3>Система</h3>
          <div class="feature-list">
            ${feature(t("set_tray"), `
              ${optSwitch("Сворачивать при закрытии (×)", true)}
              ${optSwitch("Уведомление при сворачивании", false)}
              ${optRow("Действие по клику на иконку", optSelect(["Менюю трей", "Показать окно"], "Меню трей"))}
            `, { on: true, hint: "меню · перезаход · медиа" })}
            ${optRow(t("set_priority"), optSelect(["Низкий", "Обычный", "Выше среднего", "Высокий"], "Высокий", 'data-act="toast" data-msg="Приоритет применится при запуске"'))}
            ${feature(t("set_cdn"), `
              ${optSwitch("Применять при запуске игры", true)}
              ${optSwitch("Авто-ремонт hosts при сбое", false)}
              ${`<p class="mono dim opt-note">Нужны права администратора. Пишет в системный hosts.</p>`}
            `, { on: false, hint: "hosts · нужен админ" })}
            ${feature(t("set_color"), `
              ${optRow("Пресет", optSelect(["Ночной тёплый", "Холодный контраст", "Чёрно-белый", "Свой RGB"], "Ночной тёплый"))}
              ${optSwitch(t("set_only_game"), true, 'data-act="toast" data-msg="Цветокор: только в игре"')}
              <div class="feature nested">
                <div class="feature-head" data-feature-head>
                  <span class="feature-titles">
                    <span class="feature-name">Только в определённой игре</span>
                    <span class="feature-hint">применять при входе в плейс</span>
                  </span>
                  <button type="button" class="feature-gear" tabindex="-1">⚙</button>
                  <label class="switch sm" onclick="event.stopPropagation()">
                    <input type="checkbox" data-feature-toggle><span></span>
                  </label>
                </div>
                <div class="feature-body"><div class="feature-opts">
                  ${optRow("PlaceId", optInput("6516141723…"))}
                  ${optRow("Или выбрать", `<button class="btn sm ghost" data-act="toast" data-msg="Поиск игр…">избранные плейсы</button>`)}
                </div></div>
              </div>
              ${optRow("Яркость", optRange(50))}
              ${optRow("Насыщенность", optRange(60))}
              ${optRow("Температура", optRange(40))}
            `, { on: false, hint: "Magnification API · пресеты" })}
          </div>
        </section>
      </div>`;
    },

    about() {
      return `
      <h1>${t("about")}</h1>
      <p class="dim">${t("about_sub")}</p>
      <section class="hero card">
        <h2>NekoStrap 1.3.4</h2>
        <p class="mono dim">.NET 8 · WPF · MIT · portable</p>
        <div class="row gap">
          <button class="btn primary" data-act="check-upd">${t("about_check")}</button>
          <span class="mono dim" data-role="upd-status"></span>
        </div>
        <hr>
        <div class="row gap">
          <a class="btn ghost" href="https://t.me/mesorgdev" target="_blank" rel="noopener">${t("about_tg")}</a>
          <a class="btn ghost" href="https://github.com/mesorg-dev/NekoStrap" target="_blank" rel="noopener">${t("about_gh")}</a>
          <span class="mono dim">${t("about_license")}</span>
        </div>
      </section>
      <section class="card">
        <h3>Что нового 1.3.4</h3>
        <ul class="list">
          <li><span>Hotfix автообновления: версия, батник</span></li>
          <li><span>Петля версий и запуск после установки</span></li>
        </ul>
      </section>`;
    },
  };

  /* ---------- interactions ---------- */
  function toast(msg) {
    const host = document.querySelector("[data-role=toasts]");
    if (!host) return;
    const el = document.createElement("div");
    el.className = "toast";
    el.textContent = msg;
    host.appendChild(el);
    setTimeout(() => el.classList.add("out"), 2200);
    setTimeout(() => el.remove(), 2600);
  }

  function renderPage(id) {
    const host = document.querySelector("[data-role=page]");
    if (!host) return;
    host.innerHTML = (renderers[id] || renderers.home)();
    host.scrollTop = 0;
    document.querySelectorAll("[data-nav]").forEach(b => b.classList.toggle("on", b.dataset.nav === id));
    const pg = PAGES.find(p => p.id === id);
    const label = document.querySelector("[data-role=page-title]");
    if (label && pg) label.textContent = state.lang === "ru" ? pg.ru : pg.en;
  }

  function simulatePlay(name) {
    if (state.playing) return;
    state.playing = true;
    const track = document.querySelector("[data-role=play-progress]");
    const fill = document.querySelector("[data-role=play-fill]");
    toast(t("toast_play") + (name ? " · " + name : ""));
    if (!track || !fill) { state.playing = false; return; }
    track.hidden = false;
    let p = 0;
    fill.style.width = "0%";
    const iv = setInterval(() => {
      p += 8 + Math.random() * 14;
      if (p >= 100) {
        p = 100; clearInterval(iv);
        toast("Игра запущена ✓");
        setTimeout(() => { track.hidden = true; state.playing = false; }, 600);
      }
      fill.style.width = p + "%";
    }, 180);
  }

  function bind(root) {
    root.addEventListener("click", e => {
      /* раскрытие настроек фичи: по заголовку / шестерёнке (не по тумблеру) */
      const head = e.target.closest("[data-feature-head]");
      if (head && !e.target.closest(".switch")) {
        const feat = head.closest("[data-feature]");
        const tog = head.querySelector("[data-feature-toggle]");
        if (feat && tog && tog.checked) feat.classList.toggle("open");
        return;
      }

      const nav = e.target.closest("[data-nav]");
      if (nav) { state.page = nav.dataset.nav; renderPage(state.page); return; }
      const el = e.target.closest("[data-act]");
      if (!el) return;
      const act = el.dataset.act;
      if (act === "toast") toast(el.dataset.msg || t("toast_ok"));
      else if (act === "play") simulatePlay();
      else if (act === "play-game") simulatePlay(el.dataset.name);
      else if (act === "install-ver") {
        const track = document.querySelector("[data-role=install-progress]");
        const fill = document.querySelector("[data-role=install-fill]");
        if (track && fill) {
          track.hidden = false; let p = 0;
          const iv = setInterval(() => {
            p += 10; fill.style.width = Math.min(p, 100) + "%";
            if (p >= 100) { clearInterval(iv); toast("Версия установлена ✓"); setTimeout(() => track.hidden = true, 500); }
          }, 120);
        }
      } else if (act === "invite-create") {
        const chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        state.inviteCode = Array.from({ length: 4 }, () => chars[Math.floor(Math.random() * chars.length)]).join("");
        const st = document.querySelector("[data-role=inv-status]");
        if (st) st.textContent = `${t("inv_code_ok")} ${state.inviteCode} (копируется)`;
        toast(t("toast_cp") + ": " + state.inviteCode);
      } else if (act === "invite-join") {
        const box = document.querySelector("[data-role=inv-join]");
        const code = (box && box.value || "").toUpperCase();
        if (code.length === 4) toast("Вход по коду " + code + "…");
        else toast("Введите 4 символа");
      } else if (act === "mod-apply") {
        state.activeMod = el.dataset.name;
        document.querySelectorAll("[data-role=mod-list] li").forEach(li => {
          const isIt = li.textContent.indexOf(state.activeMod) === 0;
          li.classList.toggle("active", isIt);
        });
        const s = document.querySelector("[data-role=stat-mods]");
        if (s) s.textContent = state.activeMod === "Без модов" ? "0" : "1";
        toast(t("toast_mod") + ": " + state.activeMod);
      } else if (act === "flag-preset") {
        state.activeProfile = el.dataset.name;
        document.querySelectorAll("[data-act=flag-preset]").forEach(c => c.classList.toggle("on", c === el));
        toast("Пресет: " + state.activeProfile);
      } else if (act === "chat-send") {
        const inp = document.querySelector("[data-role=chat-input]");
        const log = document.querySelector("[data-role=chat-log]");
        if (inp && inp.value.trim() && log) {
          const d = document.createElement("div");
          d.className = "msg me";
          d.innerHTML = `<b class="mono">вы</b><span>${esc(inp.value)}</span>`;
          log.appendChild(d); log.scrollTop = log.scrollHeight; inp.value = "";
        }
      } else if (act === "chat-room") {
        document.querySelectorAll("[data-act=chat-room]").forEach(c => c.classList.toggle("on", c === el));
        toast("Комната: " + el.dataset.room);
      } else if (act === "check-upd") {
        const st = document.querySelector("[data-role=upd-status]");
        if (st) st.textContent = t("toast_upd") + " (1.3.4)";
        toast(t("toast_upd"));
      }
    });

    root.addEventListener("change", e => {
      const el = e.target;
      if (el.matches("[data-role=lang]")) {
        state.lang = el.value;
        renderPage(state.page);
        toast(state.lang === "ru" ? "Язык: RU" : "Language: EN");
      }
      if (el.matches("[data-feature-toggle]")) {
        const feat = el.closest("[data-feature]");
        if (feat) {
          feat.classList.toggle("open", el.checked);
          const name = feat.querySelector(".feature-name");
          toast((name ? name.textContent : "Функция") + (el.checked ? ": вкл" : ": выкл"));
        }
      }
      if (el.matches("[data-act=flag-toggle]")) {
        const i = +el.dataset.i;
        if (state.flags[i]) state.flags[i].on = el.checked;
        const s = document.querySelector("[data-role=stat-flags]");
        if (s) s.textContent = String(state.flags.filter(f => f.on).length);
      }
      if (el.dataset.act === "toast") toast(el.dataset.msg);
    });

    root.addEventListener("input", e => {
      const el = e.target;
      if (el.matches("[data-role=game-search]")) {
        const q = el.value.toLowerCase();
        const res = games.filter(g => g.name.toLowerCase().includes(q));
        const host = document.querySelector("[data-role=game-results]");
        if (!host) return;
        host.innerHTML = res.length
          ? res.map(g => `<button class="tile" data-act="play-game" data-name="${esc(g.name)}">
              <span class="tile-art"></span><span class="tile-name">${esc(g.name)}</span>
              <span class="tile-meta">${g.players}</span></button>`).join("")
          : `<p class="dim">${t("search_none")}</p>`;
      }
      if (el.matches("[data-role=flag-search]")) {
        const q = el.value.toLowerCase();
        document.querySelectorAll("[data-flag-row]").forEach(tr => {
          tr.style.display = !q || tr.dataset.name.includes(q) || tr.dataset.val.includes(q) ? "" : "none";
        });
      }
    });
  }

  /* palette (Ctrl+K) */
  function openPalette() {
    const host = document.querySelector("[data-role=palette]");
    if (!host) return;
    host.hidden = false;
    const inp = host.querySelector("input");
    if (inp) { inp.value = ""; inp.focus(); }
  }
  function closePalette() {
    const host = document.querySelector("[data-role=palette]");
    if (host) host.hidden = true;
  }

  function bindPalette() {
    const host = document.querySelector("[data-role=palette]");
    if (!host) return;
    host.addEventListener("click", e => {
      if (e.target === host) closePalette();
      const it = e.target.closest("[data-page]");
      if (it) { closePalette(); renderPage(it.dataset.page); }
    });
    const inp = host.querySelector("input");
    if (inp) inp.addEventListener("input", () => {
      const q = inp.value.toLowerCase();
      host.querySelectorAll("[data-page]").forEach(el => {
        el.style.display = el.textContent.toLowerCase().includes(q) ? "" : "none";
      });
    });
  }

  function boot(variantRoot) {
    document.querySelectorAll("[data-nav]").forEach(b => {
      const pg = PAGES.find(p => p.id === b.dataset.nav);
      if (pg) b.setAttribute("title", state.lang === "ru" ? pg.ru : pg.en);
    });
    bind(variantRoot);
    bindPalette();
    renderPage("home");
    document.addEventListener("keydown", e => {
      if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === "k") { e.preventDefault(); openPalette(); }
      if (e.key === "Escape") closePalette();
    });
  }

  window.NekoUI = { PAGES, t, esc, boot, toast, state };
})();
