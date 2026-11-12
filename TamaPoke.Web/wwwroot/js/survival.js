// 서바이벌: 몰려오는 야생 포켓몬 사이에서 10분 동안 버티는 게임. 캔버스 한 장에 모두 그립니다.
// C#(Components/SurvivalLayer.razor)이 내 포켓몬의 능력치와 기술(무기), 적 포켓몬을 넘겨 주고,
// 레벨업 고르기·일시정지·결과 화면은 C#이 그 위에 보여 줍니다.

const RUN_SECONDS = 600;

// 난이도 (m = 지난 시간(분), t = 지난 시간(초))
const TUNE = {
    // 세대별 스타팅 포켓몬 9마리와 잉어킹을 봇으로 돌려 맞춘 값: 잘 풀린 판은 체력 절반쯤까지 밀리며 10분, 안 풀린 판은 4~8분.
    spawnBase: 1.8, spawnPerSec: 1 / 40, spawnPerMin2: 0.18, spawnMax: 34, // 초당 나오는 적: min(spawnMax, spawnBase + t × spawnPerSec + m² × spawnPerMin2)
    hpLinear: 0.45, hpSquare: 0.27,                      // 적 체력 배수: 1 + m × hpLinear + m² × hpSquare
    damageBase: 1.5, damagePerMin: 0.42,                 // 적 공격 배수: damageBase + m × damagePerMin
    speedBase: 1.15, speedPerMin: 0.07,                  // 적 속도 배수: speedBase + m × speedPerMin
    expBase: 4, expLinear: 2.5, expPow: 1.4,             // 레벨업에 필요한 사탕: expBase + Lv × expLinear + Lv^expPow
    expLateFrom: 15, expLate: 0.25,                      //   + (Lv - expLateFrom)² × expLate (후반 레벨업만 느리게)
    startLevel: 2,                                       // 처음부터 쓰는 기술의 레벨
    rushFrom: 240, rushChance: 0.32, rushSpeed: 1.8, rushHp: 0.6, // 돌격형 적: rushFrom초부터 rushChance 확률로 (빠르고 약함)
};
const ELITE_EVERY = 45;
const MAX_ENEMIES = 320;
const SWARM_AT = [150, 300, 420, 540]; // 사방에서 둘러싸는 습격 (초)
const SWARM_SIZE = 36;
const FLYBY_AT = [90, 210, 330, 450]; // 무리가 화면을 가로질러 지나가는 때 (초)
const FLYBY_SIZE = 22, FLYBY_SPEED = 115;
const OPENING_SIZE = 12; // 시작하자마자 화면 안에 나와 있는 약한 무리
const OPENING_MIN_DISTANCE = 160; // 시작 무리는 적어도 이만큼 떨어져 있습니다.
const OPENING_RATE = 3;   // 처음 30초쯤은 초당 이만큼은 나옵니다.
const FREEZE_SECONDS = 4;
const ITEM_ICONS = { magnet: "🧲", ice: "❄️", bomb: "💥" };
// 사탕은 바로 옆(이 거리 안)에 떨어지면 따로 생기지 않고 그 사탕에 더해집니다. (적이 몰린 곳에서 바닥이 사탕으로 덮이지 않도록)
const CANDY_MERGE_RADIUS = 22;
const MAX_CANDIES = 150; // 그래도 이만큼 넘게 깔리면, 나에게서 가장 먼 사탕을 그 옆 사탕에 합칩니다.
// 사탕은 담긴 양에 따라 3단계로 커지고 색이 바뀝니다. (from: 이 값부터 그 단계, size 0은 양에 따라 작게)
const CANDY_STAGES = [
    { from: 0, size: 0, tint: null, glow: null },                   // 1단계: 원래 분홍 사탕
    { from: 20, size: 15, tint: "#4aa8ff", glow: "110 180 255" },  // 2단계: 파랑, 빛나는 고리
    { from: 60, size: 20, tint: "#ffd23c", glow: "255 240 150" },  // 3단계: 금색, 반짝임
];
function candyStage(value) {
    let stage = 0;
    while (stage + 1 < CANDY_STAGES.length && value >= CANDY_STAGES[stage + 1].from) stage++;
    return stage;
}
const MAX_NUMBERS = 40;
const MINIMAP_RANGE = 420; // 동그란 지도에 보이는 거리 (게임 좌표, 화면 반쯤 바깥까지)
const WEAPON_MAX = 5;
const PASSIVE_MAX = 5;
const OWNED_WEAPON_MAX = 4;
const PLAYER_RADIUS = 7;
const INVULN_SECONDS = 0.8;
const EVOLVE_SECONDS = 2.6;      // 진화 장면 길이 (그동안 게임이 멈춥니다)
const EVOLVE_PUSH_RADIUS = 90;   // 진화가 끝날 때 이 안의 적을 밀어냅니다.
const PICKUP_RADIUS = 38;
const CELL = 32;
const DECOR_CHUNK = 160;

// 기술 종류별 기본 재사용 시간(ms)
const COOLDOWN = { bolt: 1100, spray: 1300, slash: 950, nova: 2400, orbit: 0, chain: 1500, aura: 0 };

// pixelfx(MIT, https://github.com/patch-gg/pixelfx)로 미리 그려 둔 효과 시트 (wwwroot/pfx).
// fw x fh 프레임이 가로로 이어져 있고, (ox, oy)가 효과의 기준점입니다.
const PFX = {"w_bolt":{"fw":114,"fh":51,"frames":16,"fps":20,"ox":91,"oy":25},"w_spray":{"fw":146,"fh":101,"frames":23,"fps":15,"ox":12,"oy":66},"w_slash":{"fw":66,"fh":56,"frames":18,"fps":20,"ox":5,"oy":46},"w_orbit":{"fw":78,"fh":100,"frames":36,"fps":12,"ox":39,"oy":92},"w_chain":{"fw":114,"fh":76,"frames":18,"fps":20,"ox":76,"oy":38},"w_aura":{"fw":112,"fh":81,"frames":48,"fps":12,"ox":56,"oy":61},"nova":{"fw":158,"fh":144,"frames":27,"fps":15,"ox":83,"oy":118},"poof":{"fw":82,"fh":74,"frames":24,"fps":15,"ox":37,"oy":62},"levelup":{"fw":73,"fh":81,"frames":27,"fps":20,"ox":34,"oy":78},"hit_normal":{"fw":101,"fh":51,"frames":15,"fps":30,"ox":25,"oy":35},"hit_fire":{"fw":102,"fh":51,"frames":15,"fps":30,"ox":26,"oy":35},"hit_water":{"fw":101,"fh":51,"frames":15,"fps":30,"ox":25,"oy":35},"hit_electric":{"fw":102,"fh":51,"frames":15,"fps":30,"ox":26,"oy":35},"hit_grass":{"fw":101,"fh":51,"frames":15,"fps":30,"ox":25,"oy":35},"hit_ice":{"fw":101,"fh":51,"frames":15,"fps":30,"ox":25,"oy":35},"hit_fighting":{"fw":101,"fh":51,"frames":15,"fps":30,"ox":25,"oy":35},"hit_poison":{"fw":101,"fh":51,"frames":15,"fps":30,"ox":25,"oy":35},"hit_ground":{"fw":101,"fh":51,"frames":15,"fps":30,"ox":25,"oy":35},"hit_flying":{"fw":101,"fh":51,"frames":15,"fps":30,"ox":25,"oy":35},"hit_psychic":{"fw":101,"fh":51,"frames":15,"fps":30,"ox":25,"oy":35},"hit_bug":{"fw":100,"fh":51,"frames":15,"fps":30,"ox":24,"oy":35},"hit_rock":{"fw":101,"fh":51,"frames":15,"fps":30,"ox":25,"oy":35},"hit_ghost":{"fw":100,"fh":51,"frames":15,"fps":30,"ox":24,"oy":35},"hit_dragon":{"fw":101,"fh":51,"frames":15,"fps":30,"ox":25,"oy":35},"hit_dark":{"fw":99,"fh":51,"frames":15,"fps":30,"ox":23,"oy":35},"hit_steel":{"fw":100,"fh":51,"frames":15,"fps":30,"ox":24,"oy":35},"hit_fairy":{"fw":101,"fh":51,"frames":15,"fps":30,"ox":25,"oy":35}};
const MAX_PFX = { hit: 24, poof: 40, nova: 8, levelup: 2, spray: 6, slash: 8, chain: 24 };

// 기술 모양별 효과 시트 (회색, 게임에서 기술 타입 색을 곱함). 충격파는 nova를 씁니다.
// 오라: 범위 안의 모든 적을 계속 때리므로 한 번의 데미지는 작게 둡니다.
const AURA_TICK = 0.8;      // 몇 초마다 때리는지
const AURA_SCALE = 0.3;     // 기술 데미지의 몇 배인지
const AURA_MAX_POWER = 60;  // 독 공격 기술처럼 위력이 큰 기술도 이 이상은 세지지 않습니다.

const WEAPON_PFX = { bolt: "w_bolt", spray: "w_spray", slash: "w_slash", orbit: "w_orbit", chain: "w_chain", aura: "w_aura" };

const PASSIVES = ["might", "swift", "vital", "haste", "magnet", "armor"];

// 강화 아이콘 (레벨업 카드, 일시정지 화면과 같은 그림 문자)
const PASSIVE_ICONS = { might: "💪", swift: "👟", vital: "❤️", haste: "⏱️", magnet: "🧲", armor: "🛡️" };

// 공격 모양 아이콘 (화면 오른쪽 기술 목록, SurvivalRules.KindIcon과 같은 그림 문자)
const KIND_ICONS = { bolt: "🎯", spray: "🌊", slash: "⚔️", nova: "💥", orbit: "🌀", chain: "⚡", aura: "🔮" };

// 방향 줄: 0 아래, 1 오른쪽 아래, 2 오른쪽, 3 오른쪽 위, 4 위, 5 왼쪽 위, 6 왼쪽, 7 왼쪽 아래 (PMD 시트 순서)
const SECTOR_TO_ROW = [2, 1, 0, 7, 6, 5, 4, 3];

const DECOR = ["grass", "grass", "grass", "fern", "flowers_yellow", "flowers_pink", "flowers_white", "flowers_blue",
    "rock_small", "mushroom", "tulip_red", "tulip_yellow", "bush", "rock", "sunflower"];

let game = null;

export async function start(canvas, setupJson, dotnet) {
    stop();
    const next = new Game(canvas, JSON.parse(setupJson), dotnet);
    game = next;
    await next.load();
    if (game === next) next.begin();
}

export function stop() {
    game?.destroy();
    game = null;
}

export function choose(id) { game?.choose(id); }
export function setPaused(paused) { game?.setPaused(paused, false); }
export function quit() { game?.finish(false); }
export function setMuted(muted) { if (game) game.muted = muted; }

// 일시정지 화면에 보여 줄 지금 상태 (JSON)
export function getState() {
    if (!game?.player) return "";
    const g = game, p = g.player, base = g.forms[0];
    return JSON.stringify({
        speciesId: g.form.speciesId,
        hp: Math.ceil(p.hp), maxHp: Math.round(p.maxHp), level: g.level, exp: g.exp, expNext: g.expNext,
        timeSec: Math.floor(g.time), kills: g.kills,
        weapons: g.weapons.map(w => w.level),
        weaponDamage: g.weapons.map(w => Math.round(w.dealt)),
        weaponKills: g.weapons.map(w => w.ko),
        passives: PASSIVES.map(name => ({ name, level: g.passives[name] })),
        power: Math.round(g.powerMul / base.power * 100),
        speed: Math.round(g.speed / base.speed * 100),
        guard: Math.round(g.guardMul / base.guard * 100),
        cooldown: Math.round(g.cooldownMul * 100),
        pickup: Math.round(g.pickupRadius / PICKUP_RADIUS * 100),
    });
}

function loadImage(src) {
    return new Promise((resolve) => {
        const image = new Image();
        image.onload = () => resolve(image);
        image.onerror = () => resolve(null);
        image.src = src;
    });
}

// 스프라이트 시트: 가로는 프레임, 세로는 방향. 프레임 시간은 1/60초 단위입니다.
function makeSprite(data, image) {
    const durations = data.durations.length === data.cols ? data.durations.map(d => Math.max(1, d) * 1000 / 60) : Array(data.cols).fill(200);
    return { image, fw: data.fw, fh: data.fh, cols: data.cols, rows: data.rows, durations, total: durations.reduce((a, b) => a + b, 0) };
}

function frameAt(sprite, ms) {
    let t = ms % sprite.total;
    for (let i = 0; i < sprite.durations.length; i++) {
        if (t < sprite.durations[i]) return i;
        t -= sprite.durations[i];
    }
    return 0;
}

function rowFor(dx, dy) {
    const sector = Math.round(Math.atan2(dy, dx) / (Math.PI / 4));
    return SECTOR_TO_ROW[(sector + 8) % 8];
}

// 같은 칸에는 늘 같은 소품이 나오도록 좌표로 정하는 난수
function hash(x, y, salt) {
    let h = (x * 374761393 + y * 668265263 + salt * 2147483647) | 0;
    h = Math.imul(h ^ (h >>> 13), 1274126177);
    return ((h ^ (h >>> 16)) >>> 0) / 4294967296;
}

function pickWeighted(items, weightOf) {
    const total = items.reduce((sum, item) => sum + weightOf(item), 0);
    let roll = Math.random() * total;
    for (const item of items) {
        roll -= weightOf(item);
        if (roll < 0) return item;
    }
    return items[items.length - 1];
}

class Game {
    constructor(canvas, setup, dotnet) {
        this.canvas = canvas;
        this.ctx = canvas.getContext("2d");
        this.setup = setup;
        this.dotnet = dotnet;
        this.muted = setup.muted;
        this.images = {};
        this.running = false;
        this.finished = false;
        this.raf = 0;
        this.listeners = [];
    }

    async load() {
        const setup = this.setup;
        const jobs = [];
        const sprite = (data) => {
            const holder = { sprite: null };
            if (data) jobs.push(loadImage(data.url).then(image => { if (image) holder.sprite = makeSprite(data, image); }));
            return holder;
        };

        // 진화할 모습까지 모두 미리 받아 둡니다.
        this.forms = setup.forms.map(form => ({ ...form, walkH: sprite(form.walk), idleH: sprite(form.idle) }));
        this.form = this.forms[0];
        this.roster = setup.enemies.map(enemy => ({ ...enemy, holder: sprite(enemy.sprite) }));
        this.bosses = setup.bosses.map(boss => ({ ...boss, holder: sprite(boss.sprite) }));

        // 왼쪽 위 얼굴 그림: 없는 그림이 있어서 앞에서부터 차례로 받아 봅니다.
        for (const form of this.forms) {
            jobs.push((async () => {
                for (const url of form.portraits ?? []) {
                    const image = await loadImage(url);
                    if (image) { form.portrait = image; return; }
                }
            })());
        }

        const files = new Set(["ui/icon_candy.png", "ui/icon_berry_red.png", "ui/icon_ball.png"]);
        for (const weapon of setup.weapons) {
            files.add(`ui/fx_shot_${weapon.type}.png`);
            files.add(`ui/fx_skill_${weapon.type}.png`);
        }
        for (const name of new Set(DECOR)) files.add(`walk/${name}.png`);
        for (const name of ["nova", "poof", "levelup"]) files.add(`pfx/${name}.png`);
        for (const weapon of setup.weapons) if (WEAPON_PFX[weapon.kind]) files.add(`pfx/${WEAPON_PFX[weapon.kind]}.png`);
        for (const weapon of setup.weapons) files.add(`pfx/hit_${weapon.type}.png`);
        for (const file of files) jobs.push(loadImage(file).then(image => { this.images[file] = image; }));

        await Promise.all(jobs);
        this.groundPattern = this.makeGroundPattern();
    }

    makeGroundPattern() {
        const tile = document.createElement("canvas");
        tile.width = 48;
        tile.height = 48;
        const g = tile.getContext("2d");
        g.fillStyle = "#7fc65e";
        g.fillRect(0, 0, 48, 48);
        const specks = ["#74ba54", "#8ad06a", "#6cb14d"];
        for (let i = 0; i < 26; i++) {
            g.fillStyle = specks[i % specks.length];
            const x = Math.floor(hash(i, 3, 7) * 48), y = Math.floor(hash(i, 5, 11) * 48);
            g.fillRect(x, y, i % 4 === 0 ? 2 : 1, i % 3 === 0 ? 2 : 1);
        }
        return this.ctx.createPattern(tile, "repeat");
    }

    begin() {
        const s = this.setup;
        this.player = {
            x: 0, y: 0, hp: this.form.maxHp, maxHp: this.form.maxHp, row: 0, moving: false, animMs: 0,
            invuln: 0, faceX: 0, faceY: 1, dead: false,
        };
        this.weapons = s.weapons.map((w, index) => ({ ...w, index, level: index === 0 ? TUNE.startLevel : 0, cd: 400 + index * 150, angle: 0, tick: 0, dealt: 0, ko: 0 }));
        // 대폭발 아이템의 기록 (기술처럼 데미지를 주지만 기술 기록과 따로 셉니다)
        this.bombSource = { ...this.weapons[0], index: -1, kind: "bomb", slow: false, dealt: 0, ko: 0 };
        this.passives = Object.fromEntries(PASSIVES.map(p => [p, 0]));
        this.enemies = [];
        this.gems = [];
        this.shots = [];
        this.effects = [];
        this.pfx = [];
        this.novaTints = {};
        this.numbers = [];
        this.time = 0;
        this.kills = 0;
        this.level = 1;
        this.exp = 0;
        this.expNext = this.needFor(1);
        this.pendingLevels = 0;
        this.awaitingChoice = false;
        this.spawnAcc = 0;
        this.nextElite = ELITE_EVERY;
        this.nextSwarm = 0;
        this.nextFlyby = 0;
        this.freezeUntil = 0;
        this.banner = null;
        this.nextBoss = 0;
        this.bossRef = null;
        this.paused = false;
        this.keys = new Set();
        this.stick = null;
        this.lastPickupSound = 0;

        this.resize();
        this.spawnOpening();
        this.listen(window, "resize", () => this.resize());
        this.listen(window, "keydown", (e) => this.onKey(e, true));
        this.listen(window, "keyup", (e) => this.onKey(e, false));
        this.listen(window, "blur", () => this.keys.clear());
        this.listen(document, "visibilitychange", () => { if (document.hidden) this.setPaused(true, true); });
        this.listen(this.canvas, "pointerdown", (e) => this.onPointerDown(e));
        this.listen(this.canvas, "pointermove", (e) => this.onPointerMove(e));
        this.listen(this.canvas, "pointerup", () => { this.stick = null; });
        this.listen(this.canvas, "pointercancel", () => { this.stick = null; });
        this.listen(this.canvas, "contextmenu", (e) => e.preventDefault());

        this.running = true;
        this.last = performance.now();
        const loop = (now) => {
            if (!this.running) return;
            const dt = Math.min(now - this.last, 50) / 1000;
            this.last = now;
            if (!this.paused && !this.awaitingChoice) this.update(dt);
            this.render();
            this.raf = requestAnimationFrame(loop);
        };
        this.raf = requestAnimationFrame(loop);
    }

    listen(target, type, handler) {
        target.addEventListener(type, handler);
        this.listeners.push(() => target.removeEventListener(type, handler));
    }

    destroy() {
        this.running = false;
        cancelAnimationFrame(this.raf);
        for (const remove of this.listeners) remove();
        this.listeners = [];
    }

    resize() {
        const rect = this.canvas.getBoundingClientRect();
        this.dpr = Math.min(window.devicePixelRatio || 1, 2);
        this.viewW = Math.max(1, rect.width);
        this.viewH = Math.max(1, rect.height);
        this.canvas.width = Math.round(this.viewW * this.dpr);
        this.canvas.height = Math.round(this.viewH * this.dpr);
        // 화면이 작아도 주변이 충분히 보이도록 배율을 정합니다.
        this.scale = Math.min(2.6, Math.max(1.2, Math.min(this.viewW, this.viewH) / 300));
        this.halfW = this.viewW / 2 / this.scale;
        this.halfH = this.viewH / 2 / this.scale;
    }

    // --- 입력 ---

    onKey(e, down) {
        const key = e.key.toLowerCase();
        if (down && (key === "escape" || key === "p")) {
            if (!this.awaitingChoice && !this.finished) this.setPaused(!this.paused, true);
            e.preventDefault();
            return;
        }
        if (["w", "a", "s", "d", "arrowup", "arrowdown", "arrowleft", "arrowright"].includes(key)) {
            if (down) this.keys.add(key); else this.keys.delete(key);
            e.preventDefault();
        }
    }

    // 누른 곳을 중심으로 끄는 방향으로 움직이는 가상 조이스틱 (마우스도 같음)
    onPointerDown(e) {
        if (this.finished) return;
        this.canvas.setPointerCapture?.(e.pointerId);
        const rect = this.canvas.getBoundingClientRect();
        const x = e.clientX - rect.left, y = e.clientY - rect.top;
        this.stick = { ox: x, oy: y, x, y };
    }

    onPointerMove(e) {
        if (!this.stick) return;
        const rect = this.canvas.getBoundingClientRect();
        this.stick.x = e.clientX - rect.left;
        this.stick.y = e.clientY - rect.top;
        const dx = this.stick.x - this.stick.ox, dy = this.stick.y - this.stick.oy;
        const dist = Math.hypot(dx, dy);
        const max = 46;
        if (dist > max) {
            this.stick.ox = this.stick.x - dx / dist * max;
            this.stick.oy = this.stick.y - dy / dist * max;
        }
    }

    inputDirection() {
        let dx = 0, dy = 0;
        if (this.keys.has("a") || this.keys.has("arrowleft")) dx -= 1;
        if (this.keys.has("d") || this.keys.has("arrowright")) dx += 1;
        if (this.keys.has("w") || this.keys.has("arrowup")) dy -= 1;
        if (this.keys.has("s") || this.keys.has("arrowdown")) dy += 1;
        if (dx === 0 && dy === 0 && this.stick) {
            const sx = this.stick.x - this.stick.ox, sy = this.stick.y - this.stick.oy;
            if (Math.hypot(sx, sy) > 6) { dx = sx; dy = sy; }
        }
        const length = Math.hypot(dx, dy);
        return length > 0 ? [dx / length, dy / length] : [0, 0];
    }

    setPaused(paused, notify) {
        if (this.finished || this.paused === paused) return;
        this.paused = paused;
        this.keys.clear();
        this.stick = null;
        if (notify) this.dotnet.invokeMethodAsync("OnPauseChanged", paused);
    }

    sound(notes) {
        if (!this.muted) window.tamaPoke?.playNotes(notes);
    }

    // --- 능력치 ---

    get powerMul() { return this.form.power * (1 + 0.12 * this.passives.might); }
    get speed() { return this.form.speed * (1 + 0.08 * this.passives.swift); }
    get cooldownMul() { return Math.pow(0.92, this.passives.haste); }
    get pickupRadius() { return PICKUP_RADIUS * (1 + 0.4 * this.passives.magnet); }
    get guardMul() { return this.form.guard * Math.pow(0.92, this.passives.armor); }

    // --- 진화: 게임 안 레벨이 진화 레벨(도감 기준, C#에서 계산)이 되면 다음 모습으로 (갈래가 여럿이면 고르기) ---

    checkEvolve() {
        const need = this.form.evolveLevel;
        if (this.form.next.length > 0 && need && this.level >= need) this.evolveQueued = true;
    }

    offerEvolve() {
        const options = this.form.next.map(index => ({ id: `e${index}`, kind: "evolve", index, level: 0 }));
        this.awaitingChoice = true;
        this.lastOptions = options;
        this.keys.clear();
        this.stick = null;
        this.dotnet.invokeMethodAsync("OnLevelUp", JSON.stringify(options));
    }

    // 진화 장면 시작: 게임이 멈추고, 포켓몬이 빛나며 원래 모습과 진화한 모습이 점점 빠르게 번갈아 보입니다.
    evolve(index) {
        const next = this.forms[index];
        if (!next) return;
        this.evolving = { from: this.form, to: next, t: 0 };
        this.keys.clear();
        this.stick = null;
        this.sound([[392, 140], [440, 140], [494, 140], [523, 140], [587, 140], [659, 140], [740, 140], [784, 140], [880, 200]]);
    }

    finishEvolve() {
        const next = this.evolving.to;
        this.evolving = null;
        const p = this.player;
        p.invuln = Math.max(p.invuln, 1.2);
        // 진화의 빛이 가까운 적을 밀어냅니다.
        for (const e of this.enemies) {
            if (e.dying || e.rank === "boss") continue;
            const dx = e.x - p.x, dy = e.y - p.y, d = Math.hypot(dx, dy) || 1;
            if (d > EVOLVE_PUSH_RADIUS) continue;
            const k = 260 * (1 - d / EVOLVE_PUSH_RADIUS) / e.size;
            e.kbx += dx / d * k;
            e.kby += dy / d * k;
        }
        const ratio = p.hp / p.maxHp;
        this.form = next;
        p.maxHp = next.maxHp * (1 + 0.15 * this.passives.vital);
        p.hp = Math.min(p.maxHp, ratio * p.maxHp + p.maxHp * 0.25);
        this.evolveBanner = 3;
        this.addPfx("levelup", "levelup", p.x, p.y + 7, { scale: 1.4, speed: 0.9, follow: true });
        this.sound([[523, 90], [659, 90], [784, 90], [1047, 90], [1319, 260]]);
        this.checkEvolve();
    }

    needFor(level) {
        const late = Math.max(0, level - TUNE.expLateFrom);
        return Math.floor(TUNE.expBase + level * TUNE.expLinear + Math.pow(level, TUNE.expPow) + late * late * TUNE.expLate);
    }

    // --- 진행 ---

    update(dt) {
        if (this.player.dead) {
            this.player.deadTimer -= dt;
            if (this.player.deadTimer <= 0) this.sendResult(false);
            this.updateEffects(dt);
            return;
        }

        if (this.evolving) {
            this.evolving.t += dt;
            this.player.animMs += dt * 1000;
            this.updateEffects(dt);
            if (this.evolving.t >= EVOLVE_SECONDS) this.finishEvolve();
            return;
        }

        this.time += dt;
        if (this.time >= RUN_SECONDS) {
            this.time = RUN_SECONDS;
            this.finish(true);
            return;
        }

        this.movePlayer(dt);
        this.spawn(dt);
        this.buildGrid();
        this.moveEnemies(dt);
        this.fireWeapons(dt);
        this.moveShots(dt);
        this.updateGems(dt);
        this.updateEffects(dt);
        if (this.swarmBanner > 0) this.swarmBanner -= dt;
        if (this.evolveBanner > 0) this.evolveBanner -= dt;
        if (this.bossBanner > 0) this.bossBanner -= dt;
        if (this.banner) {
            this.banner.t -= dt;
            if (this.banner.t <= 0) this.banner = null;
        }
        this.enemies = this.enemies.filter(e => !e.removed);

        if (this.evolveQueued && !this.awaitingChoice) {
            this.evolveQueued = false;
            if (this.form.next.length === 1) this.evolve(this.form.next[0]);
            else if (this.form.next.length > 1) this.offerEvolve();
        }
        if (this.pendingLevels > 0 && !this.awaitingChoice) this.offerLevelUp();
    }

    movePlayer(dt) {
        const p = this.player;
        const [dx, dy] = this.inputDirection();
        p.moving = dx !== 0 || dy !== 0;
        if (p.moving) {
            p.x += dx * this.speed * dt;
            p.y += dy * this.speed * dt;
            p.faceX = dx;
            p.faceY = dy;
            p.row = rowFor(dx, dy);
        }
        p.animMs += dt * 1000;
        if (p.invuln > 0) p.invuln -= dt;
    }

    spawn(dt) {
        const t = this.time;
        const m = t / 60;
        this.spawnAcc += dt * Math.min(TUNE.spawnMax, Math.max(OPENING_RATE, TUNE.spawnBase + t * TUNE.spawnPerSec + m * m * TUNE.spawnPerMin2));
        while (this.spawnAcc >= 1) {
            this.spawnAcc -= 1;
            if (this.enemies.length < MAX_ENEMIES) this.spawnEnemy(this.pickTier(), "normal");
            else this.recycleFarthest();
        }
        if (this.nextFlyby < FLYBY_AT.length && t >= FLYBY_AT[this.nextFlyby]) {
            this.nextFlyby++;
            this.spawnFlyby();
        }
        if (this.nextSwarm < SWARM_AT.length && t >= SWARM_AT[this.nextSwarm]) {
            this.nextSwarm++;
            this.spawnSwarm();
        }
        if (t >= this.nextElite) {
            this.nextElite += ELITE_EVERY;
            this.spawnEnemy(this.pickTier(), "elite");
        }
        // 정기 보스: 정해진 시간마다 전설 포켓몬이 하나씩
        if (this.nextBoss < this.setup.bossTimes.length && t >= this.setup.bossTimes[this.nextBoss]) {
            this.bossRef = this.spawnEnemy(-1 - this.nextBoss, "boss");
            this.nextBoss++;
            this.bossBanner = 2.5;
            this.sound([[220, 120], [0, 40], [196, 120], [0, 40], [165, 260]]);
        }
    }

    // 시간이 지날수록 강한 포켓몬이 나옵니다. (가장 최근에 나온 3~4종 중에서)
    pickTier() {
        const top = Math.min(this.roster.length - 1, Math.floor(this.time / 65));
        const bottom = Math.max(0, top - 3);
        const tiers = [];
        for (let i = bottom; i <= top; i++) tiers.push(i);
        return pickWeighted(tiers, i => 1 + (i - bottom));
    }

    // 화면 밖 둘레의 한 점. 절반은 움직이는 방향 앞쪽에 나와서, 도망만 다녀서는 피할 수 없습니다.
    spawnPoint() {
        const p = this.player;
        const angle = p.moving && Math.random() < 0.5
            ? Math.atan2(p.faceY, p.faceX) + (Math.random() - 0.5) * 1.8
            : Math.random() * Math.PI * 2;
        const distance = Math.hypot(this.halfW, this.halfH) + 24;
        return [p.x + Math.cos(angle) * distance, p.y + Math.sin(angle) * distance];
    }

    // 적이 꽉 찼으면 새로 만들지 않고, 가장 멀리 뒤처진 적을 다시 앞쪽으로 데려옵니다.
    recycleFarthest() {
        let far = null, farD = 0;
        for (const e of this.enemies) {
            if (e.dying || e.rank !== "normal") continue;
            const d = (e.x - this.player.x) ** 2 + (e.y - this.player.y) ** 2;
            if (d > farD) { farD = d; far = e; }
        }
        if (!far) return;
        [far.x, far.y] = this.spawnPoint();
        far.kbx = far.kby = 0;
    }

    // 습격: 지금 나오는 포켓몬들이 화면 밖에서 원을 그리며 한꺼번에 몰려옵니다.
    spawnSwarm() {
        const p = this.player;
        const distance = Math.hypot(this.halfW, this.halfH) + 10;
        for (let i = 0; i < SWARM_SIZE; i++) {
            const enemy = this.spawnEnemy(this.pickTier(), "normal");
            const angle = i / SWARM_SIZE * Math.PI * 2;
            enemy.x = p.x + Math.cos(angle) * distance;
            enemy.y = p.y + Math.sin(angle) * distance;
        }
        this.swarmBanner = 2.5;
        this.sound([[330, 80], [0, 30], [330, 80], [0, 30], [440, 160]]);
    }

    // 지나가는 떼: 한 종류가 줄지어 화면을 한쪽에서 반대쪽으로 곧게 가로지릅니다. (쫓아오지 않음)
    spawnFlyby() {
        const p = this.player;
        const angle = Math.random() * Math.PI * 2;
        const dx = Math.cos(angle), dy = Math.sin(angle);
        const distance = Math.hypot(this.halfW, this.halfH) + 30;
        const tier = this.pickTier();
        for (let i = 0; i < FLYBY_SIZE; i++) {
            const e = this.spawnEnemy(tier, "normal");
            const side = (i - (FLYBY_SIZE - 1) / 2) * 13 + (Math.random() - 0.5) * 8;
            const back = (i % 3) * 12;
            e.rank = "flyby";
            e.x = p.x - dx * (distance + back) - dy * side;
            e.y = p.y - dy * (distance + back) + dx * side;
            e.vx = dx * FLYBY_SPEED;
            e.vy = dy * FLYBY_SPEED;
            e.life = (distance * 2 + 60) / FLYBY_SPEED;
            e.row = rowFor(dx, dy);
        }
        this.showBanner(this.setup.texts?.flyby ?? "!", "#ffb74d");
        this.sound([[660, 60], [0, 20], [660, 60], [0, 20], [880, 120]]);
    }

    // 시작 무리: 원조 장르처럼 시작하자마자 화면 가장자리 안쪽에 가장 약한 포켓몬들이 둘러서 있습니다.
    spawnOpening() {
        const p = this.player;
        // 화면 모양(가로로 긴 화면이면 가로로 긴 타원)을 따라 화면 가장자리 근처에 둡니다. 가까워도 OPENING_MIN_DISTANCE보다는 멀리.
        for (let i = 0; i < OPENING_SIZE; i++) {
            const e = this.spawnEnemy(0, "normal");
            const angle = (i + Math.random() * 0.8) / OPENING_SIZE * Math.PI * 2;
            const edge = 0.9 + Math.random() * 0.2;
            let dx = Math.cos(angle) * this.halfW * edge, dy = Math.sin(angle) * this.halfH * edge;
            const d = Math.hypot(dx, dy) || 1;
            if (d < OPENING_MIN_DISTANCE) { dx *= OPENING_MIN_DISTANCE / d; dy *= OPENING_MIN_DISTANCE / d; }
            e.x = p.x + dx;
            e.y = p.y + dy;
        }
    }

    showBanner(text, color) {
        this.banner = { text, color, t: 2.5 };
    }

    spawnEnemy(tier, rank) {
        const bossIndex = tier < 0 ? -1 - tier : 0;
        const def = tier < 0 ? this.bosses[bossIndex] : this.roster[tier];
        const minutes = this.time / 60;
        const growth = 1 + minutes * TUNE.hpLinear + minutes * minutes * TUNE.hpSquare;
        const rush = rank === "normal" && this.time >= TUNE.rushFrom && Math.random() < TUNE.rushChance;
        const rankHp = rank === "boss" ? 25 + 15 * bossIndex : rank === "elite" ? 12 : 1;
        const [x, y] = this.spawnPoint();
        const enemy = {
            def, rank, tier, x, y,
            hp: def.hp * growth * rankHp * (rush ? TUNE.rushHp : 1),
            maxHp: def.hp * growth * rankHp * (rush ? TUNE.rushHp : 1),
            speed: def.speed * (TUNE.speedBase + minutes * TUNE.speedPerMin) * (rank === "boss" ? 0.85 : rank === "elite" ? 0.9 : 1) * (rush ? TUNE.rushSpeed : 1),
            damage: def.damage * (TUNE.damageBase + minutes * TUNE.damagePerMin) * (rank === "boss" ? 2 : rank === "elite" ? 1.5 : 1),
            rush,
            size: rank === "boss" ? 2.2 : rank === "elite" ? 1.5 : 1,
            animMs: Math.random() * 1000,
            row: 0, hitMs: 0, slow: 0, kbx: 0, kby: 0, hitBy: {},
        };
        enemy.radius = 7 * enemy.size;
        this.enemies.push(enemy);
        return enemy;
    }

    buildGrid() {
        const grid = new Map();
        for (const e of this.enemies) {
            if (e.dying) continue;
            const key = this.cellKey(Math.floor(e.x / CELL), Math.floor(e.y / CELL));
            let list = grid.get(key);
            if (!list) grid.set(key, list = []);
            list.push(e);
        }
        this.grid = grid;
    }

    cellKey(cx, cy) { return (cx + 100000) * 200000 + (cy + 100000); }

    // (x, y)에서 radius 안의 적마다 visit을 부릅니다.
    near(x, y, radius, visit) {
        const x0 = Math.floor((x - radius - 20) / CELL), x1 = Math.floor((x + radius + 20) / CELL);
        const y0 = Math.floor((y - radius - 20) / CELL), y1 = Math.floor((y + radius + 20) / CELL);
        for (let cx = x0; cx <= x1; cx++) {
            for (let cy = y0; cy <= y1; cy++) {
                const list = this.grid.get(this.cellKey(cx, cy));
                if (!list) continue;
                for (const e of list) {
                    if (e.dying) continue;
                    const r = radius + e.radius;
                    const dx = e.x - x, dy = e.y - y;
                    if (dx * dx + dy * dy <= r * r) visit(e);
                }
            }
        }
    }

    nearest(x, y, maxDistance, exclude) {
        let best = null, bestD = maxDistance * maxDistance;
        for (const e of this.enemies) {
            if (e.dying || exclude?.has(e)) continue;
            const dx = e.x - x, dy = e.y - y, d = dx * dx + dy * dy;
            if (d < bestD) { bestD = d; best = e; }
        }
        return best;
    }

    moveEnemies(dt) {
        const p = this.player;
        const far = Math.hypot(this.halfW, this.halfH) * 1.6;
        for (const e of this.enemies) {
            e.animMs += dt * 1000;
            if (e.hitMs > 0) e.hitMs -= dt * 1000;
            if (e.dying) {
                e.dieMs -= dt * 1000;
                if (e.dieMs <= 0) e.removed = true;
                continue;
            }
            if (e.slow > 0) e.slow -= dt;

            const dx = p.x - e.x, dy = p.y - e.y;
            const dist = Math.hypot(dx, dy) || 1;

            const frozen = this.time < this.freezeUntil && e.rank !== "boss";
            if (e.rank === "flyby") {
                if (!frozen) {
                    e.x += e.vx * dt;
                    e.y += e.vy * dt;
                    e.life -= dt;
                    if (e.life <= 0) e.removed = true;
                }
                if (!frozen && p.invuln <= 0 && dist < e.radius + PLAYER_RADIUS) this.hurtPlayer(e.damage);
                continue;
            }
            if (frozen) continue;

            // 너무 멀어진 적은 내 앞쪽으로 다시 데려옵니다. (보스 제외)
            if (dist > far && e.rank !== "boss") {
                [e.x, e.y] = this.spawnPoint();
                continue;
            }

            const speed = e.speed * (e.slow > 0 ? 0.55 : 1);
            e.x += (dx / dist * speed + e.kbx) * dt;
            e.y += (dy / dist * speed + e.kby) * dt;
            e.kbx *= Math.pow(0.002, dt);
            e.kby *= Math.pow(0.002, dt);
            e.row = rowFor(dx, dy);

            // 겹치지 않게 살짝 밀어냅니다.
            const list = this.grid.get(this.cellKey(Math.floor(e.x / CELL), Math.floor(e.y / CELL)));
            if (list) {
                for (const o of list) {
                    if (o === e) continue;
                    const ox = e.x - o.x, oy = e.y - o.y;
                    const d = Math.hypot(ox, oy);
                    const min = (e.radius + o.radius) * 0.8;
                    if (d > 0 && d < min) {
                        const push = (min - d) * 0.5;
                        e.x += ox / d * push;
                        e.y += oy / d * push;
                    }
                }
            }

            if (p.invuln <= 0 && dist < e.radius + PLAYER_RADIUS) this.hurtPlayer(e.damage);
        }
    }

    hurtPlayer(amount) {
        const p = this.player;
        const damage = Math.max(1, amount * this.guardMul);
        p.hp -= damage;
        p.invuln = INVULN_SECONDS;
        this.addNumber(p.x, p.y - 14, Math.round(damage), "#ff5a5a");
        this.sound([[180, 50]]);
        if (p.hp <= 0) {
            p.hp = 0;
            p.dead = true;
            p.deadTimer = 1.2;
            this.sound([[392, 120], [330, 120], [262, 260]]);
        }
    }

    // --- 무기 ---

    weaponDamage(w, e) {
        const levelMul = 1 + 0.3 * (w.level - 1);
        const eff = e.def.eff?.[w.index] ?? 1;
        return w.power / 4 * this.powerMul * levelMul * eff * (0.9 + Math.random() * 0.2);
    }

    fireWeapons(dt) {
        const p = this.player;
        for (const w of this.weapons) {
            if (w.level === 0) continue;

            if (w.kind === "orbit") { this.updateOrbit(w, dt); continue; }
            if (w.kind === "aura") { this.updateAura(w, dt); continue; }

            w.cd -= dt * 1000;
            if (w.cd > 0) continue;
            const base = COOLDOWN[w.kind] * w.cooldown * this.cooldownMul;

            const target = this.nearest(p.x, p.y, w.kind === "slash" ? 46 + 7 * w.level : 220);
            if (!target && w.kind !== "nova") { w.cd = 120; continue; }
            w.cd = base;
            w.cdMax = base; // 화면 오른쪽 재사용 시간 표시용

            const aimX = target ? target.x - p.x : p.faceX, aimY = target ? target.y - p.y : p.faceY;
            const aim = Math.atan2(aimY, aimX);
            switch (w.kind) {
                case "bolt": this.fireBolt(w, aim); break;
                case "spray": this.fireSpray(w, aim); break;
                case "slash": this.fireSlash(w, aim); break;
                case "nova": this.fireNova(w); break;
                case "chain": this.fireChain(w, target); break;
            }
        }
    }

    fireBolt(w, aim) {
        const count = 1 + Math.floor((w.level - 1) / 2) + w.extra;
        const pierce = w.level >= 4 ? 2 : w.level >= 2 ? 1 : 0;
        for (let i = 0; i < count; i++) {
            const angle = aim + (i - (count - 1) / 2) * 0.16;
            this.shots.push({ w, x: this.player.x, y: this.player.y, vx: Math.cos(angle) * 230, vy: Math.sin(angle) * 230, life: 1.3, pierce, radius: 5, hit: new Set(), size: 16 });
        }
    }

    fireSpray(w, aim) {
        const count = 3 + w.level + w.extra;
        const spread = 0.9;
        for (let i = 0; i < count; i++) {
            const angle = aim + (i / (count - 1) - 0.5) * spread + (Math.random() - 0.5) * 0.1;
            const speed = 170 + Math.random() * 40;
            // 불꽃·물줄기는 앞의 적을 뚫고 지나갑니다. (Lv.3부터 2마리)
            this.shots.push({ w, x: this.player.x, y: this.player.y, vx: Math.cos(angle) * speed, vy: Math.sin(angle) * speed, life: 0.42 + w.level * 0.04, pierce: w.level >= 3 ? 2 : 1, radius: 5, hit: new Set(), size: 12, hidden: true });
        }
        const reach = 190 * (0.42 + w.level * 0.04);
        // 포켓몬 몸과 겹치지 않게, 쏘는 방향으로 몸 바깥(16px 앞)에서 시작합니다.
        const mouth = 16;
        this.addPfx("w_spray", "spray", this.player.x + Math.cos(aim) * mouth, this.player.y + Math.sin(aim) * mouth,
            { angle: aim, scale: (reach - mouth) / 120, speed: 2, tint: w.type, color: w.color, follow: true });
    }

    fireSlash(w, aim) {
        const radius = 42 + 7 * w.level;
        const half = 1.05;
        const swing = (angle) => {
            this.near(this.player.x, this.player.y, radius, (e) => {
                let diff = Math.atan2(e.y - this.player.y, e.x - this.player.x) - angle;
                diff = Math.atan2(Math.sin(diff), Math.cos(diff));
                if (Math.abs(diff) <= half) this.damage(e, this.weaponDamage(w, e), w, 70);
            });
            const left = Math.cos(angle) < 0;
            this.addPfx("w_slash", "slash", this.player.x, this.player.y + 4, {
                angle: left ? angle - Math.PI : angle, flip: left, scale: radius / 52, speed: 1.4, tint: w.type, color: w.color, follow: true,
            });
        };
        swing(aim);
        if (w.level >= 3) swing(aim + Math.PI);
        for (let i = 0; i < w.extra; i++) setTimeout(() => this.running && !this.paused && swing(aim), 120 * (i + 1));
    }

    fireNova(w) {
        // 화면 전체를 쓸지 않도록 범위와 데미지를 조금 낮게 둡니다. (Lv.5 반경 98)
        const radius = 53 + 9 * w.level;
        this.near(this.player.x, this.player.y, radius, (e) => this.damage(e, this.weaponDamage(w, e) * 0.8 * (1 + 0.3 * w.extra), w, 160));
        this.effects.push({ type: "nova", w, x: this.player.x, y: this.player.y, radius, life: 0.3, max: 0.3 });
        this.addPfx("nova", "nova", this.player.x, this.player.y + 7, { scale: radius / 70, speed: 2, tint: w.type, color: w.color, follow: true });
    }

    fireChain(w, first) {
        if (!first) return;
        const jumps = 2 + w.level + w.extra;
        const visited = new Set([first]);
        const points = [[this.player.x, this.player.y], [first.x, first.y]];
        this.damage(first, this.weaponDamage(w, first), w, 20);
        this.addPfx("w_chain", "chain", first.x, first.y, { scale: 0.45, speed: 1.5, tint: w.type, color: w.color });
        let current = first;
        for (let i = 0; i < jumps; i++) {
            const next = this.nearest(current.x, current.y, 85, visited);
            if (!next) break;
            visited.add(next);
            points.push([next.x, next.y]);
            this.damage(next, this.weaponDamage(w, next) * 0.85, w, 20);
            this.addPfx("w_chain", "chain", next.x, next.y, { scale: 0.45, speed: 1.5, tint: w.type, color: w.color });
            current = next;
        }
        this.effects.push({ type: "chain", w, points, life: 0.16, max: 0.16 });
    }

    updateOrbit(w, dt) {
        const count = 2 + Math.floor(w.level / 2) + w.extra;
        // 회전 기술이 여러 개면 서로 다른 거리와 방향에서 돕니다.
        const radius = 36 + 6 * w.level + (w.index % 2) * 12;
        w.angle += dt * (2.6 + 0.15 * w.level) * (w.index % 2 ? -1 : 1);
        w.orbs = [];
        for (let i = 0; i < count; i++) {
            const angle = w.angle + w.index * 0.9 + i * Math.PI * 2 / count;
            const x = this.player.x + Math.cos(angle) * radius, y = this.player.y + Math.sin(angle) * radius;
            w.orbs.push([x, y]);
            this.near(x, y, 10, (e) => {
                const until = e.hitBy[w.index] ?? 0;
                if (this.time < until) return;
                e.hitBy[w.index] = this.time + 0.4;
                this.damage(e, this.weaponDamage(w, e), w, 90, x, y);
            });
        }
    }

    updateAura(w, dt) {
        w.radius = 30 + 7 * w.level;
        w.tick -= dt;
        if (w.tick > 0) return;
        w.tick = AURA_TICK * w.cooldown * this.cooldownMul;
        const scale = AURA_SCALE * Math.min(1, AURA_MAX_POWER / w.power);
        this.near(this.player.x, this.player.y, w.radius, (e) => {
            this.damage(e, this.weaponDamage(w, e) * scale, w, 0);
            e.slow = Math.max(e.slow, w.slow ? 1.2 : 0.4);
        });
        // 때릴 때마다 바깥으로 퍼지는 고리와 위로 떠오르는 빛 알갱이
        this.effects.push({ type: "auraPulse", w, radius: w.radius, life: 0.45, max: 0.45 });
        for (let i = 0; i < 3 + w.level; i++) {
            const angle = Math.random() * Math.PI * 2, distance = Math.sqrt(Math.random()) * w.radius;
            this.effects.push({
                type: "mote", w, x: this.player.x + Math.cos(angle) * distance, y: this.player.y + Math.sin(angle) * distance * 0.8,
                size: Math.random() < 0.3 ? 3 : 2, rise: 18 + Math.random() * 22, life: 0.7 + Math.random() * 0.4, max: 1.1,
            });
        }
    }

    damage(e, amount, w, knockback, fromX, fromY) {
        if (e.dying) return;
        w.dealt = (w.dealt ?? 0) + Math.max(0, Math.min(amount, e.hp));
        e.hp -= amount;
        e.hitMs = 90;
        if (w.slow) e.slow = Math.max(e.slow, 1);
        if (knockback > 0 && e.rank !== "boss") {
            const sx = fromX ?? this.player.x, sy = fromY ?? this.player.y;
            const dx = e.x - sx, dy = e.y - sy, d = Math.hypot(dx, dy) || 1;
            const k = knockback / e.size;
            e.kbx += dx / d * k;
            e.kby += dy / d * k;
        }
        this.addNumber(e.x, e.y - 10 * e.size, Math.max(1, Math.round(amount)), "#ffffff");
        if (!(e.sparkUntil > this.time)) {
            e.sparkUntil = this.time + 0.15;
            const sx = fromX ?? this.player.x, sy = fromY ?? this.player.y;
            this.addPfx(`hit_${w.type}`, "hit", e.x, e.y - 2 * e.size, { angle: Math.atan2(e.y - sy, e.x - sx), scale: 0.55 * Math.sqrt(e.size), speed: 1.2 });
        }
        if (e.hp <= 0) this.kill(e, w);
    }

    kill(e, w) {
        e.dying = true;
        e.dieMs = 220;
        this.kills++;
        if (w) w.ko = (w.ko ?? 0) + 1;
        this.addPfx("poof", "poof", e.x, e.y + 7 * e.size, { scale: 0.45 * e.size, speed: 1.8 });

        if (e.rank === "boss") {
            this.dropGem(e.x, e.y, "chest", 0);
            this.bossRef = null;
            return;
        }
        const value = e.rank === "elite" ? 20 : e.tier >= 6 ? 3 : e.tier >= 3 ? 2 : 1;
        this.dropGem(e.x, e.y, "candy", value);
        if (e.rank === "elite" || Math.random() < 0.006) this.dropGem(e.x + 6, e.y, "berry", 0);
        if (e.rank === "elite" && Math.random() < 0.35) this.dropGem(e.x - 6, e.y, ["magnet", "ice", "bomb"][Math.floor(Math.random() * 3)], 0);
    }

    dropGem(x, y, kind, value) {
        if (kind !== "candy") {
            this.gems.push({ x, y, kind, value, pulled: false });
            return;
        }
        // 바닥에 놓인 사탕 (나에게 끌려오는 중인 사탕은 빼고)
        const candies = this.gems.filter(g => g.kind === "candy" && !g.taken && !g.pulled);
        const near = this.nearestCandy(candies, x, y, null);
        if (near && Math.hypot(near.x - x, near.y - y) <= CANDY_MERGE_RADIUS) {
            this.addToCandy(near, value);
            return;
        }
        if (candies.length >= MAX_CANDIES) {
            // 너무 많으면 나에게서 가장 먼 사탕을 그 옆 사탕에 합칩니다.
            const p = this.player;
            const far = candies.reduce((a, b) => Math.hypot(b.x - p.x, b.y - p.y) > Math.hypot(a.x - p.x, a.y - p.y) ? b : a);
            const target = this.nearestCandy(candies, far.x, far.y, far);
            if (target) {
                far.taken = true;
                this.gems.splice(this.gems.indexOf(far), 1);
                this.addToCandy(target, far.value);
            }
        }
        this.gems.push({ x, y, kind, value, pulled: false });
    }

    nearestCandy(candies, x, y, except) {
        let best = null, bestD = Infinity;
        for (const g of candies) {
            if (g === except) continue;
            const d = Math.hypot(g.x - x, g.y - y);
            if (d < bestD) { best = g; bestD = d; }
        }
        return best;
    }

    addToCandy(g, value) {
        const before = candyStage(g.value);
        g.value += value;
        g.bumpAt = this.time; // 합쳐질 때 살짝 부풀고
        if (candyStage(g.value) > before) g.grownAt = this.time; // 한 단계 커질 때는 톡 튀어 오릅니다.
    }

    moveShots(dt) {
        for (const s of this.shots) {
            s.x += s.vx * dt;
            s.y += s.vy * dt;
            s.life -= dt;
            if (s.life <= 0) continue;
            this.near(s.x, s.y, s.radius, (e) => {
                if (s.life <= 0 || s.hit.has(e)) return;
                s.hit.add(e);
                this.damage(e, this.weaponDamage(s.w, e), s.w, 60, s.x - s.vx * 0.05, s.y - s.vy * 0.05);
                if (s.pierce-- <= 0) s.life = 0;
            });
        }
        this.shots = this.shots.filter(s => s.life > 0);
    }

    updateGems(dt) {
        const p = this.player;
        const radius = this.pickupRadius;
        for (const g of this.gems) {
            const dx = p.x - g.x, dy = p.y - g.y;
            const d = Math.hypot(dx, dy);
            if (!g.pulled && d < radius) g.pulled = true;
            if (!g.pulled) continue;
            const speed = 260 + (radius - Math.min(d, radius)) * 2;
            g.x += dx / (d || 1) * speed * dt;
            g.y += dy / (d || 1) * speed * dt;
            if (d < 8) {
                g.taken = true;
                this.collect(g);
            }
        }
        this.gems = this.gems.filter(g => !g.taken);
    }

    collect(g) {
        const p = this.player;
        if (g.kind === "berry") {
            p.hp = Math.min(p.maxHp, p.hp + p.maxHp * 0.25);
            this.sound([[988, 40], [1319, 70]]);
            return;
        }
        if (g.kind === "magnet") {
            for (const other of this.gems) if (other.kind === "candy") other.pulled = true;
            this.showBanner(this.setup.texts?.magnet ?? "🧲", "#ffffff");
            this.sound([[523, 50], [784, 50], [1047, 90]]);
            return;
        }
        if (g.kind === "ice") {
            this.freezeUntil = this.time + FREEZE_SECONDS;
            this.showBanner(this.setup.texts?.ice ?? "❄️", "#9fe3ff");
            this.sound([[1568, 40], [1319, 40], [1047, 120]]);
            return;
        }
        if (g.kind === "bomb") {
            // 화면 안의 일반 적은 쓰러지고, 엘리트와 보스는 최대 체력의 25%를 잃습니다.
            const reach = Math.hypot(this.halfW, this.halfH);
            const w = this.bombSource;
            for (const e of this.enemies) {
                if (e.dying || Math.hypot(e.x - p.x, e.y - p.y) > reach) continue;
                this.damage(e, e.rank === "elite" || e.rank === "boss" ? e.maxHp * 0.25 : e.hp + 1, w, 160);
            }
            this.addPfx("nova", "nova", p.x, p.y + 7, { scale: 3.2, speed: 1.6, follow: true });
            this.showBanner(this.setup.texts?.bomb ?? "💥", "#ffca28");
            this.sound([[196, 80], [147, 120], [98, 220]]);
            return;
        }
        if (g.kind === "chest") {
            p.hp = Math.min(p.maxHp, p.hp + p.maxHp * 0.3);
            this.pendingLevels += 2;
            this.sound([[784, 60], [988, 60], [1175, 60], [1568, 160]]);
            return;
        }
        this.exp += g.value;
        while (this.exp >= this.expNext) {
            this.exp -= this.expNext;
            this.level++;
            this.expNext = this.needFor(this.level);
            this.pendingLevels++;
            this.addPfx("levelup", "levelup", this.player.x, this.player.y + 7, { scale: 0.8, speed: 1.2, follow: true });
            this.checkEvolve();
        }
        if (this.time - this.lastPickupSound > 0.12) {
            this.lastPickupSound = this.time;
            this.sound([[1400, 12]]);
        }
    }

    updateEffects(dt) {
        for (const fx of this.pfx) fx.t += dt * fx.speed;
        this.pfx = this.pfx.filter(fx => fx.t * PFX[fx.name].fps < PFX[fx.name].frames);
        for (const fx of this.effects) fx.life -= dt;
        this.effects = this.effects.filter(fx => fx.life > 0);
        for (const n of this.numbers) { n.life -= dt; n.y -= dt * 18; }
        this.numbers = this.numbers.filter(n => n.life > 0);
    }

    // 미리 그린 효과 하나를 띄웁니다. 같은 종류가 너무 많으면 건너뜁니다.
    // o: angle(회전), scale, speed(재생 배속), tint(충격파 색 타입), follow(내 포켓몬을 따라감)
    addPfx(name, group, x, y, o = {}) {
        if (!PFX[name] || !this.images[`pfx/${name}.png`]) return;
        let count = 0;
        for (const fx of this.pfx) if (fx.group === group) count++;
        if (count >= (MAX_PFX[group] ?? 20)) return;
        const p = this.player;
        this.pfx.push({ name, group, x, y, dx: x - p.x, dy: y - p.y, t: 0, angle: o.angle ?? 0, scale: o.scale ?? 1, speed: o.speed ?? 1,
            tint: o.tint, color: o.color, follow: !!o.follow, flip: !!o.flip });
    }

    // 회색 효과 시트에 기술 타입 색을 곱한 그림 (효과·타입마다 한 번만 만듭니다)
    // 사탕 그림의 밝기는 그대로 두고 색만 바꾼 사본 (큰 사탕 단계별 색)
    candyTint(base, color) {
        const key = `candy|${color}`;
        if (this.novaTints[key]) return this.novaTints[key];
        const canvas = document.createElement("canvas");
        canvas.width = base.width;
        canvas.height = base.height;
        const g = canvas.getContext("2d");
        g.drawImage(base, 0, 0);
        g.globalCompositeOperation = "color";
        g.fillStyle = color;
        g.fillRect(0, 0, canvas.width, canvas.height);
        g.globalCompositeOperation = "destination-in";
        g.drawImage(base, 0, 0);
        return (this.novaTints[key] = canvas);
    }

    tinted(name, type, color) {
        const base = this.images[`pfx/${name}.png`];
        if (!type || !base) return base;
        const key = `${name}|${type}`;
        if (this.novaTints[key]) return this.novaTints[key];
        const canvas = document.createElement("canvas");
        canvas.width = base.width;
        canvas.height = base.height;
        const g = canvas.getContext("2d");
        g.drawImage(base, 0, 0);
        g.globalCompositeOperation = "multiply";
        g.fillStyle = color;
        g.fillRect(0, 0, canvas.width, canvas.height);
        g.globalCompositeOperation = "destination-in";
        g.drawImage(base, 0, 0);
        return (this.novaTints[key] = canvas);
    }

    drawPfx() {
        const ctx = this.ctx;
        const p = this.player;
        for (const fx of this.pfx) {
            const info = PFX[fx.name];
            const image = fx.tint ? this.tinted(fx.name, fx.tint, fx.color) : this.images[`pfx/${fx.name}.png`];
            if (!image) continue;
            const frame = Math.min(info.frames - 1, Math.floor(fx.t * info.fps));
            const x = fx.follow ? p.x + fx.dx : fx.x, y = fx.follow ? p.y + fx.dy : fx.y;
            ctx.save();
            ctx.translate(x, y);
            if (fx.angle) ctx.rotate(fx.angle);
            ctx.scale(fx.flip ? -fx.scale : fx.scale, fx.scale);
            ctx.drawImage(image, frame * info.fw, 0, info.fw, info.fh, -info.ox, -info.oy, info.fw, info.fh);
            ctx.restore();
        }
    }

    addNumber(x, y, value, color) {
        if (this.numbers.length >= MAX_NUMBERS) this.numbers.shift();
        this.numbers.push({ x: x + (Math.random() - 0.5) * 6, y, value, color, life: 0.6 });
    }

    // --- 레벨업 ---

    offerLevelUp() {
        const pool = [];
        const owned = this.weapons.filter(w => w.level > 0).length;
        for (const w of this.weapons) {
            if (w.level === 0 && owned < OWNED_WEAPON_MAX) pool.push({ id: `w${w.index}`, kind: "new", index: w.index, level: 1, weight: 2.2 });
            else if (w.level > 0 && w.level < WEAPON_MAX) pool.push({ id: `w${w.index}`, kind: "up", index: w.index, level: w.level + 1, weight: 3 });
        }
        for (const name of PASSIVES) {
            if (this.passives[name] < PASSIVE_MAX) pool.push({ id: `p_${name}`, kind: "passive", name, level: this.passives[name] + 1, weight: 1.4 });
        }

        const options = [];
        while (options.length < 3 && pool.length > 0) {
            const pick = pickWeighted(pool, o => o.weight);
            options.push(pick);
            pool.splice(pool.indexOf(pick), 1);
        }
        if (options.length === 0) options.push({ id: "heal", kind: "heal", level: 0 });

        this.awaitingChoice = true;
        this.keys.clear();
        this.stick = null;
        this.sound([[784, 60], [988, 60], [1319, 120]]);
        this.dotnet.invokeMethodAsync("OnLevelUp", JSON.stringify(options.map(({ weight, ...o }) => o)));
    }

    choose(id) {
        if (!this.awaitingChoice) return;
        if (id.startsWith("e")) {
            this.awaitingChoice = false;
            this.evolve(Number(id.slice(1)));
            this.last = performance.now();
            return;
        }
        if (id === "heal") {
            this.player.hp = Math.min(this.player.maxHp, this.player.hp + this.player.maxHp * 0.35);
        } else if (id.startsWith("w")) {
            const w = this.weapons[Number(id.slice(1))];
            if (w && w.level < WEAPON_MAX) w.level++;
        } else if (id.startsWith("p_")) {
            const name = id.slice(2);
            if (this.passives[name] < PASSIVE_MAX) {
                this.passives[name]++;
                if (name === "vital") {
                    const gain = this.form.maxHp * 0.15;
                    this.player.maxHp += gain;
                    this.player.hp += gain;
                }
            }
        }
        this.pendingLevels = Math.max(0, this.pendingLevels - 1);
        this.awaitingChoice = false;
        this.last = performance.now();
    }

    // --- 끝 ---

    finish(cleared) {
        if (this.finished) return;
        if (cleared) {
            this.sound([[784, 90], [988, 90], [1175, 90], [1568, 260]]);
            this.sendResult(true);
        } else {
            this.sendResult(false);
        }
    }

    sendResult(cleared) {
        if (this.finished) return;
        this.finished = true;
        this.stick = null;
        this.dotnet.invokeMethodAsync("OnGameOver", JSON.stringify({
            timeSec: Math.floor(this.time), kills: this.kills, level: this.level, cleared, finalSpeciesId: this.form.speciesId,
            weaponSkills: this.weapons.map(w => w.skillId),
            weaponLevels: this.weapons.map(w => w.level),
            weaponDamage: this.weapons.map(w => Math.round(w.dealt)),
            weaponKills: this.weapons.map(w => w.ko),
            itemDamage: Math.round(this.bombSource.dealt), itemKills: this.bombSource.ko,
        }));
    }

    // --- 그리기 ---

    render() {
        const ctx = this.ctx;
        const p = this.player;
        const s = this.scale * this.dpr;
        ctx.setTransform(1, 0, 0, 1, 0, 0);
        ctx.imageSmoothingEnabled = false;

        // 카메라는 포켓몬을 따라갑니다. 픽셀이 흔들리지 않도록 화면 픽셀 단위로 맞춥니다.
        const camX = Math.round(p.x * s) / s, camY = Math.round(p.y * s) / s;
        ctx.setTransform(s, 0, 0, s, this.canvas.width / 2 - camX * s, this.canvas.height / 2 - camY * s);
        const left = camX - this.halfW - 2, top = camY - this.halfH - 2;
        const width = this.halfW * 2 + 4, height = this.halfH * 2 + 4;

        ctx.fillStyle = this.groundPattern;
        ctx.fillRect(left, top, width, height);
        this.drawDecor(left, top, width, height);
        this.drawGems();

        this.drawAuras();

        // 아래에 있는 것부터 그립니다.
        const actors = this.enemies.slice();
        actors.push(p);
        actors.sort((a, b) => a.y - b.y);
        for (const a of actors) {
            if (a === p) this.drawPlayer();
            else this.drawEnemy(a);
        }

        this.drawWeapons();
        this.drawShots();
        this.drawEffects();
        this.drawPfx();
        this.drawNumbers();
        this.drawEvolution(left, top, width, height);
        this.drawHud();
    }

    // 진화 장면: 화면이 어두워지고, 빛줄기 속에서 하얀 실루엣이 원래 모습 ↔ 진화한 모습으로 점점 빠르게 바뀝니다.
    drawEvolution(left, top, width, height) {
        const ev = this.evolving;
        if (!ev) return;
        const ctx = this.ctx, p = this.player;
        const t = Math.min(1, ev.t / EVOLVE_SECONDS);

        ctx.fillStyle = `rgb(10 12 30 / ${Math.round(Math.min(1, ev.t / 0.35) * 62)}%)`;
        ctx.fillRect(left, top, width, height);

        // 빛줄기와 빛무리
        const glow = 26 + 34 * t;
        const gradient = ctx.createRadialGradient(p.x, p.y, 0, p.x, p.y, glow);
        gradient.addColorStop(0, "rgb(255 210 110 / 40%)");
        gradient.addColorStop(1, "rgb(255 210 110 / 0%)");
        ctx.fillStyle = gradient;
        ctx.beginPath();
        ctx.arc(p.x, p.y, glow, 0, Math.PI * 2);
        ctx.fill();
        ctx.save();
        ctx.translate(p.x, p.y);
        ctx.rotate(ev.t * 0.8);
        ctx.fillStyle = `rgb(255 235 160 / ${Math.round(8 + 18 * t)}%)`;
        const rays = 10, reach = 60 + 140 * t;
        for (let i = 0; i < rays; i++) {
            const a = i / rays * Math.PI * 2;
            ctx.beginPath();
            ctx.moveTo(0, 0);
            ctx.lineTo(Math.cos(a - 0.09) * reach, Math.sin(a - 0.09) * reach);
            ctx.lineTo(Math.cos(a + 0.09) * reach, Math.sin(a + 0.09) * reach);
            ctx.fill();
        }
        ctx.restore();

        // 원래 모습 ↔ 진화한 모습 (처음엔 느리게, 끝으로 갈수록 빠르게)
        const flips = Math.floor(18 * t * t);
        const form = t > 0.88 || flips % 2 === 1 ? ev.to : ev.from;
        const sprite = form.idleH.sprite ?? form.walkH.sprite;
        if (sprite) this.drawSilhouette(sprite, p.x, p.y, p.row, p.animMs);

        // 반짝이는 빛 알갱이가 안으로 모입니다.
        ctx.fillStyle = "#fffbe0";
        for (let i = 0; i < 14; i++) {
            const a = i * 2.39996 + ev.t * 1.5;
            const r = (1 - ((ev.t * 0.9 + i * 0.137) % 1)) * 70;
            ctx.fillRect(Math.round(p.x + Math.cos(a) * r) - 1, Math.round(p.y + Math.sin(a) * r * 0.8) - 1, 2, 2);
        }

        // 끝날 때 하얗게 번쩍
        if (t > 0.9) {
            ctx.fillStyle = `rgb(255 255 255 / ${Math.round((t - 0.9) / 0.1 * 90)}%)`;
            ctx.fillRect(left, top, width, height);
        }
    }

    // 스프라이트 한 장면을 하얀 실루엣으로 그립니다.
    drawSilhouette(sprite, x, y, row, ms) {
        const frame = frameAt(sprite, ms);
        const r = sprite.rows > row ? row : 0;
        const canvas = this.silhouetteCanvas ??= document.createElement("canvas");
        if (canvas.width !== sprite.fw || canvas.height !== sprite.fh) {
            canvas.width = sprite.fw;
            canvas.height = sprite.fh;
        }
        const c = canvas.getContext("2d");
        c.globalCompositeOperation = "source-over";
        c.clearRect(0, 0, sprite.fw, sprite.fh);
        c.drawImage(sprite.image, frame * sprite.fw, r * sprite.fh, sprite.fw, sprite.fh, 0, 0, sprite.fw, sprite.fh);
        c.globalCompositeOperation = "source-atop";
        c.fillStyle = "#ffffff";
        c.fillRect(0, 0, sprite.fw, sprite.fh);
        this.ctx.drawImage(canvas, Math.round(x - sprite.fw / 2), Math.round(y - sprite.fh / 2));
    }

    drawDecor(left, top, width, height) {
        const ctx = this.ctx;
        const x0 = Math.floor(left / DECOR_CHUNK), x1 = Math.floor((left + width) / DECOR_CHUNK);
        const y0 = Math.floor((top - 40) / DECOR_CHUNK), y1 = Math.floor((top + height + 40) / DECOR_CHUNK);
        for (let cx = x0; cx <= x1; cx++) {
            for (let cy = y0; cy <= y1; cy++) {
                const count = 2 + Math.floor(hash(cx, cy, 1) * 4);
                for (let i = 0; i < count; i++) {
                    const name = DECOR[Math.floor(hash(cx, cy, 10 + i) * DECOR.length)];
                    const image = this.images[`walk/${name}.png`];
                    if (!image) continue;
                    const x = (cx + hash(cx, cy, 20 + i)) * DECOR_CHUNK, y = (cy + hash(cx, cy, 30 + i)) * DECOR_CHUNK;
                    const w = image.width * 0.6, h = image.height * 0.6;
                    ctx.drawImage(image, Math.round(x - w / 2), Math.round(y - h), w, h);
                }
            }
        }
    }

    drawGems() {
        const ctx = this.ctx;
        const candy = this.images["ui/icon_candy.png"], berry = this.images["ui/icon_berry_red.png"], ball = this.images["ui/icon_ball.png"];
        const bob = Math.sin(this.time * 6) * 1;
        for (const g of this.gems) {
            if (g.kind === "berry" && berry) ctx.drawImage(berry, g.x - 5, g.y - 5 + bob, 10, 10);
            else if (g.kind === "chest" && ball) ctx.drawImage(ball, g.x - 9, g.y - 9 + bob, 18, 18);
            else if (ITEM_ICONS[g.kind]) {
                ctx.font = "12px sans-serif";
                ctx.textAlign = "center";
                ctx.textBaseline = "middle";
                ctx.fillStyle = "#ffffff";
                ctx.fillText(ITEM_ICONS[g.kind], g.x, g.y + bob);
            }
            else if (candy && candyStage(g.value) > 0) {
                // 많이 모인 사탕: 단계별로 커지고 색이 바뀝니다.
                const index = candyStage(g.value), stage = CANDY_STAGES[index];
                const pop = 1 + 0.5 * Math.max(0, 1 - (this.time - (g.grownAt ?? -9)) / 0.35);
                const size = stage.size * pop, pulse = 1 + Math.sin(this.time * 5) * 0.12;
                ctx.fillStyle = `rgb(${stage.glow} / 35%)`;
                ctx.beginPath();
                ctx.arc(g.x, g.y + bob, size * 0.68 * pulse, 0, Math.PI * 2);
                ctx.fill();
                const image = stage.tint ? this.candyTint(candy, stage.tint) : candy;
                ctx.drawImage(image, g.x - size / 2, g.y - size / 2 + bob, size, size);
                if (index === CANDY_STAGES.length - 1) {
                    // 마지막 단계: 둘레를 도는 반짝임
                    ctx.fillStyle = "#ffffff";
                    for (let i = 0; i < 3; i++) {
                        const angle = this.time * 2.5 + i * Math.PI * 2 / 3, twinkle = Math.sin(this.time * 9 + i * 2) > 0 ? 2 : 1;
                        const sx = Math.round(g.x + Math.cos(angle) * size * 0.62), sy = Math.round(g.y + bob + Math.sin(angle) * size * 0.62);
                        ctx.fillRect(sx - twinkle, sy, twinkle * 2, 1);
                        ctx.fillRect(sx, sy - twinkle, 1, twinkle * 2);
                    }
                }
            }
            else if (candy) {
                const pop = 1 + 0.3 * Math.max(0, 1 - (this.time - (g.bumpAt ?? -9)) / 0.2);
                const size = (g.value >= 10 ? 13 : g.value >= 3 ? 10 : g.value >= 2 ? 8 : 7) * pop;
                ctx.drawImage(candy, g.x - size / 2, g.y - size / 2 + bob, size, size);
            }
        }
    }

    drawShadow(x, y, width) {
        const ctx = this.ctx;
        ctx.fillStyle = "rgb(0 0 0 / 22%)";
        ctx.beginPath();
        ctx.ellipse(x, y, width / 2, width / 6, 0, 0, Math.PI * 2);
        ctx.fill();
    }

    drawSprite(sprite, x, y, row, ms, size, still) {
        const frame = still ? 0 : frameAt(sprite, ms);
        const r = sprite.rows > row ? row : 0;
        const w = sprite.fw * size, h = sprite.fh * size;
        this.ctx.drawImage(sprite.image, frame * sprite.fw, r * sprite.fh, sprite.fw, sprite.fh, Math.round(x - w / 2), Math.round(y - h / 2), w, h);
    }

    drawPlayer() {
        const ctx = this.ctx;
        const p = this.player;
        this.drawShadow(p.x, p.y + 7, 18);
        if (this.evolving) return; // 진화 장면에서는 drawEvolution이 실루엣으로 그립니다.
        const sprite = (p.moving || !this.form.idleH.sprite) ? this.form.walkH.sprite : this.form.idleH.sprite;
        if (p.invuln > 0 && Math.floor(p.invuln * 20) % 2 === 0) ctx.globalAlpha = 0.45;
        if (p.dead) ctx.globalAlpha = Math.max(0, p.deadTimer / 1.2);
        if (sprite) this.drawSprite(sprite, p.x, p.y, p.row, p.animMs, 1, false);
        ctx.globalAlpha = 1;

        // 체력 바
        const barW = 26;
        ctx.fillStyle = "rgb(20 24 40 / 80%)";
        ctx.fillRect(p.x - barW / 2 - 1, p.y + 12, barW + 2, 4);
        ctx.fillStyle = p.hp / p.maxHp > 0.3 ? "#5fe06b" : "#ff5a5a";
        ctx.fillRect(p.x - barW / 2, p.y + 13, barW * Math.max(0, p.hp / p.maxHp), 2);
    }

    drawEnemy(e) {
        const ctx = this.ctx;
        const sprite = e.def.holder.sprite;
        if ((e.rank !== "normal" || e.rush) && !e.dying) {
            ctx.fillStyle = e.rank === "boss" ? "rgb(255 60 60 / 22%)" : e.rush ? "rgb(255 120 30 / 30%)" : "rgb(255 200 60 / 22%)";
            ctx.beginPath();
            ctx.ellipse(e.x, e.y + 7 * e.size, 13 * e.size, 5 * e.size, 0, 0, Math.PI * 2);
            ctx.fill();
        }
        this.drawShadow(e.x, e.y + 7 * e.size, 16 * e.size);
        if (e.dying) ctx.globalAlpha = Math.max(0, e.dieMs / 220);
        else if (e.hitMs > 0) ctx.globalAlpha = 0.55;
        const frozen = this.time < this.freezeUntil && e.rank !== "boss" && !e.dying;
        if (sprite) this.drawSprite(sprite, e.x, e.y, e.row, frozen ? 0 : e.animMs, e.size, frozen);
        else {
            ctx.fillStyle = "#c04040";
            ctx.fillRect(e.x - 6, e.y - 6, 12, 12);
        }
        ctx.globalAlpha = 1;
        if (frozen) {
            ctx.fillStyle = "rgb(160 225 255 / 45%)";
            ctx.beginPath();
            ctx.ellipse(e.x, e.y, 9 * e.size, 11 * e.size, 0, 0, Math.PI * 2);
            ctx.fill();
        }

        if (e.rank === "elite" && !e.dying) {
            const barW = 22;
            ctx.fillStyle = "rgb(20 24 40 / 80%)";
            ctx.fillRect(e.x - barW / 2, e.y - 12 * e.size - 3, barW, 3);
            ctx.fillStyle = "#ffcf3f";
            ctx.fillRect(e.x - barW / 2, e.y - 12 * e.size - 3, barW * Math.max(0, e.hp / e.maxHp), 3);
        }
    }

    drawWeapons() {
        const ctx = this.ctx;
        for (const w of this.weapons) {
            if (w.level === 0) continue;
            if (w.kind === "orbit" && w.orbs) {
                // 물 구슬: 시트의 구슬 중심(기준점에서 52px 위)이 궤도 위에 오도록 그립니다.
                const info = PFX.w_orbit, image = this.tinted("w_orbit", w.type, w.color);
                const frame = Math.floor(this.time * info.fps) % info.frames, s = 0.38;
                for (const [x, y] of w.orbs) {
                    if (image) ctx.drawImage(image, frame * info.fw, 0, info.fw, info.fh, x - info.ox * s, y - (info.oy - 52) * s, info.fw * s, info.fh * s);
                    else { ctx.fillStyle = w.color; ctx.beginPath(); ctx.arc(x, y, 5, 0, Math.PI * 2); ctx.fill(); }
                }
            }
        }
    }

    // 오라: 내 발밑에 기술 색 마법진이 오라 범위만큼 돕니다.
    drawAuras() {
        const ctx = this.ctx;
        const info = PFX.w_aura;
        for (const w of this.weapons) {
            if (w.level === 0 || w.kind !== "aura" || !w.radius) continue;
            const image = this.tinted("w_aura", w.type, w.color);
            const glow = ctx.createRadialGradient(this.player.x, this.player.y, 0, this.player.x, this.player.y, w.radius);
            glow.addColorStop(0, w.color + "00");
            glow.addColorStop(0.7, w.color + "18");
            glow.addColorStop(1, w.color + "40");
            ctx.fillStyle = glow;
            ctx.beginPath();
            ctx.ellipse(this.player.x, this.player.y + 4, w.radius, w.radius * 0.8, 0, 0, Math.PI * 2);
            ctx.fill();
            ctx.globalAlpha = 0.35 + 0.25 * Math.sin(this.time * 5);
            ctx.strokeStyle = w.color;
            ctx.lineWidth = 1.5;
            ctx.setLineDash([4, 4]);
            ctx.lineDashOffset = -this.time * 12;
            ctx.stroke();
            ctx.setLineDash([]);
            ctx.globalAlpha = 1;
            const s = w.radius / (info.fw / 2);
            const frame = Math.floor(this.time * info.fps) % info.frames;
            if (image) ctx.drawImage(image, frame * info.fw, 0, info.fw, info.fh, this.player.x - info.ox * s, this.player.y + 7 - info.oy * s, info.fw * s, info.fh * s);
            else {
                ctx.fillStyle = w.color + "2e";
                ctx.beginPath();
                ctx.arc(this.player.x, this.player.y, w.radius, 0, Math.PI * 2);
                ctx.fill();
            }
        }
    }

    drawShots() {
        const ctx = this.ctx;
        const frame = Math.floor(this.time * 10) % 2;
        const bolt = PFX.w_bolt;
        for (const s of this.shots) {
            if (s.hidden) continue;
            // 탄환: 플라스마 탄 (머리가 날아가는 방향)
            const plasma = s.w.kind === "bolt" ? this.tinted("w_bolt", s.w.type, s.w.color) : null;
            if (plasma) {
                const f = Math.floor(this.time * bolt.fps) % bolt.frames, k = 0.42;
                ctx.save();
                ctx.translate(s.x, s.y);
                ctx.rotate(Math.atan2(s.vy, s.vx));
                ctx.drawImage(plasma, f * bolt.fw, 0, bolt.fw, bolt.fh, -bolt.ox * k, -bolt.oy * k, bolt.fw * k, bolt.fh * k);
                ctx.restore();
                continue;
            }
            const image = this.images[`ui/fx_shot_${s.w.type}.png`];
            const size = s.size;
            if (image) {
                ctx.save();
                ctx.translate(s.x, s.y);
                ctx.rotate(Math.atan2(s.vy, s.vx));
                ctx.drawImage(image, frame * 16, 0, 16, 16, -size / 2, -size / 2, size, size);
                ctx.restore();
            } else {
                ctx.fillStyle = s.w.color;
                ctx.beginPath();
                ctx.arc(s.x, s.y, 4, 0, Math.PI * 2);
                ctx.fill();
            }
        }
    }

    drawEffects() {
        const ctx = this.ctx;
        for (const fx of this.effects) {
            const t = 1 - fx.life / fx.max;
            switch (fx.type) {
                case "slash": {
                    ctx.strokeStyle = fx.w.color;
                    ctx.globalAlpha = 1 - t;
                    ctx.lineWidth = 6 * (1 - t) + 1;
                    ctx.beginPath();
                    ctx.arc(fx.x, fx.y, fx.radius * (0.7 + 0.3 * t), fx.angle - fx.half, fx.angle + fx.half);
                    ctx.stroke();
                    ctx.strokeStyle = "#ffffff";
                    ctx.lineWidth = 1.5;
                    ctx.beginPath();
                    ctx.arc(fx.x, fx.y, fx.radius * (0.7 + 0.3 * t), fx.angle - fx.half * (1 - t), fx.angle + fx.half * (1 - t));
                    ctx.stroke();
                    break;
                }
                case "auraPulse": {
                    ctx.globalAlpha = 0.8 * (1 - t);
                    ctx.strokeStyle = fx.w.color;
                    ctx.lineWidth = 3 * (1 - t) + 1;
                    ctx.beginPath();
                    ctx.ellipse(this.player.x, this.player.y + 4, fx.radius * (0.5 + 0.5 * t), fx.radius * 0.8 * (0.5 + 0.5 * t), 0, 0, Math.PI * 2);
                    ctx.stroke();
                    break;
                }
                case "mote": {
                    // 도트 느낌이 나도록 네모난 빛 알갱이 (가운데는 흰색)
                    const y = fx.y - fx.rise * t;
                    ctx.globalAlpha = Math.min(1, fx.life * 3);
                    ctx.fillStyle = fx.w.color;
                    ctx.fillRect(Math.round(fx.x - fx.size), Math.round(y - fx.size), fx.size * 2, fx.size * 2);
                    ctx.fillStyle = "#ffffff";
                    ctx.fillRect(Math.round(fx.x - 1), Math.round(y - 1), 2, 2);
                    break;
                }
                case "nova": {
                    ctx.globalAlpha = 1 - t;
                    ctx.strokeStyle = fx.w.color;
                    ctx.lineWidth = 5 * (1 - t) + 1;
                    ctx.beginPath();
                    ctx.arc(fx.x, fx.y, fx.radius * (0.25 + 0.75 * t), 0, Math.PI * 2);
                    ctx.stroke();
                    ctx.fillStyle = fx.w.color + "22";
                    ctx.fill();
                    break;
                }
                case "chain": {
                    ctx.globalAlpha = 1 - t;
                    ctx.strokeStyle = fx.w.color;
                    ctx.lineWidth = 2.5;
                    ctx.beginPath();
                    fx.points.forEach(([x, y], i) => {
                        if (i === 0) { ctx.moveTo(x, y); return; }
                        const [px, py] = fx.points[i - 1];
                        ctx.lineTo((px + x) / 2 + (Math.random() - 0.5) * 8, (py + y) / 2 + (Math.random() - 0.5) * 8);
                        ctx.lineTo(x, y);
                    });
                    ctx.stroke();
                    ctx.strokeStyle = "#ffffff";
                    ctx.lineWidth = 1;
                    ctx.stroke();
                    break;
                }
                case "burst": {
                    const image = fx.w ? this.images[`ui/fx_skill_${fx.w.type}.png`] : null;
                    const frame = Math.min(3, Math.floor(t * 4));
                    if (image) ctx.drawImage(image, frame * 32, 0, 32, 32, fx.x - fx.size / 2, fx.y - fx.size / 2, fx.size, fx.size);
                    break;
                }
            }
            ctx.globalAlpha = 1;
        }
    }

    drawNumbers() {
        const ctx = this.ctx;
        ctx.font = "bold 7px sans-serif";
        ctx.textAlign = "center";
        ctx.lineWidth = 2;
        ctx.strokeStyle = "rgb(0 0 0 / 70%)";
        for (const n of this.numbers) {
            ctx.globalAlpha = Math.min(1, n.life * 3);
            ctx.strokeText(n.value, n.x, n.y);
            ctx.fillStyle = n.color;
            ctx.fillText(n.value, n.x, n.y);
        }
        ctx.globalAlpha = 1;
    }

    // --- 화면 위 표시 (HUD) ---

    // 반투명 둥근 상자
    panel(x, y, w, h, r) {
        const ctx = this.ctx;
        ctx.beginPath();
        if (ctx.roundRect) ctx.roundRect(x, y, w, h, r); else ctx.rect(x, y, w, h);
        ctx.fillStyle = "rgb(18 22 40 / 82%)";
        ctx.fill();
        ctx.strokeStyle = "rgb(255 255 255 / 18%)";
        ctx.lineWidth = 1.5;
        ctx.stroke();
    }

    // 글자가 넓이를 넘으면 뒤를 …로 줄입니다.
    fitText(text, maxWidth) {
        const ctx = this.ctx;
        if (ctx.measureText(text).width <= maxWidth) return text;
        let cut = text;
        while (cut.length > 1 && ctx.measureText(cut + "…").width > maxWidth) cut = cut.slice(0, -1);
        return cut + "…";
    }

    // 글자 테두리를 둘러 어느 바탕에서도 읽히게 씁니다.
    outlinedText(text, x, y, color, width = 3) {
        const ctx = this.ctx;
        ctx.lineWidth = width;
        ctx.strokeStyle = "rgb(10 12 24 / 90%)";
        ctx.strokeText(text, x, y);
        ctx.fillStyle = color;
        ctx.fillText(text, x, y);
    }

    // 이름표가 붙은 막대 하나 (체력, 경험치)
    labeledBar(label, value, max, x, y, w, h, labelW, colors, fontSize) {
        const ctx = this.ctx;
        ctx.beginPath();
        if (ctx.roundRect) ctx.roundRect(x, y, labelW, h, 3); else ctx.rect(x, y, labelW, h);
        ctx.fillStyle = colors.label;
        ctx.fill();
        ctx.font = `bold ${fontSize}px sans-serif`;
        ctx.textAlign = "center";
        ctx.fillStyle = "#ffffff";
        ctx.fillText(label, x + labelW / 2, y + h / 2 + 0.5);

        const bx = x + labelW + 4, bw = w - labelW - 4;
        ctx.fillStyle = "rgb(0 0 0 / 60%)";
        ctx.fillRect(bx, y, bw, h);
        const ratio = Math.max(0, Math.min(1, value / max));
        if (ratio > 0) {
            const gradient = ctx.createLinearGradient(0, y, 0, y + h);
            gradient.addColorStop(0, colors.top);
            gradient.addColorStop(1, colors.bottom);
            ctx.fillStyle = gradient;
            ctx.fillRect(bx + 1, y + 1, (bw - 2) * ratio, h - 2);
            ctx.fillStyle = "rgb(255 255 255 / 30%)"; // 위쪽 빛
            ctx.fillRect(bx + 1, y + 1, (bw - 2) * ratio, Math.max(1, Math.floor(h / 4)));
        }
        ctx.strokeStyle = "rgb(255 255 255 / 25%)";
        ctx.lineWidth = 1;
        ctx.strokeRect(bx + 0.5, y + 0.5, bw - 1, h - 1);
        this.outlinedText(`${Math.ceil(value).toLocaleString()} / ${Math.round(max).toLocaleString()}`, bx + bw / 2, y + h / 2 + 0.5, "#ffffff");
    }

    // 왼쪽 위: 얼굴 그림, 레벨, 이름과 KO, 체력·경험치 막대
    drawProfile(wide, mapR) {
        const ctx = this.ctx, p = this.player;
        const x = 8, y = 8;
        const face = wide ? 58 : 44;
        // 휴대폰은 오른쪽 위 지도(일시정지 단추 왼쪽)와 겹치지 않을 만큼만
        const w = wide ? 280 : Math.min(220, this.viewW - 54 - mapR * 2 - 24);
        const h = face + 14;
        this.panel(x, y, w, h, 12);

        // 얼굴: 둥근 틀 안에 초상화 (없으면 몬스터볼)
        const cx = x + 8 + face / 2, cy = y + 6 + face / 2;
        ctx.beginPath();
        ctx.arc(cx, cy, face / 2, 0, Math.PI * 2);
        ctx.fillStyle = "#2b3358";
        ctx.fill();
        ctx.save();
        ctx.beginPath();
        ctx.arc(cx, cy, face / 2 - 2, 0, Math.PI * 2);
        ctx.clip();
        const portrait = this.form.portrait ?? this.images["ui/icon_ball.png"];
        if (portrait) ctx.drawImage(portrait, cx - face / 2 + 2, cy - face / 2 + 2, face - 4, face - 4);
        // 맞으면 잠깐 붉게
        if (p.invuln > INVULN_SECONDS - 0.25) {
            ctx.fillStyle = "rgb(255 60 60 / 40%)";
            ctx.fillRect(cx - face / 2, cy - face / 2, face, face);
        }
        ctx.restore();
        ctx.beginPath();
        ctx.arc(cx, cy, face / 2 - 1, 0, Math.PI * 2);
        ctx.strokeStyle = "#ffd54f";
        ctx.lineWidth = 2.5;
        ctx.stroke();

        // 레벨 표 (얼굴 아래에 걸침)
        const levelText = `Lv ${this.level}`;
        ctx.font = `bold ${wide ? 12 : 10}px sans-serif`;
        const lw = ctx.measureText(levelText).width + 12, lh = wide ? 16 : 14, ly = cy + face / 2 - lh / 2 - 1;
        ctx.beginPath();
        if (ctx.roundRect) ctx.roundRect(cx - lw / 2, ly, lw, lh, lh / 2); else ctx.rect(cx - lw / 2, ly, lw, lh);
        ctx.fillStyle = "#1c2242";
        ctx.fill();
        ctx.strokeStyle = "#ffd54f";
        ctx.lineWidth = 1.5;
        ctx.stroke();
        ctx.textAlign = "center";
        ctx.fillStyle = "#ffd54f";
        ctx.fillText(levelText, cx, ly + lh / 2 + 0.5);

        // 이름과 KO
        const tx = x + 8 + face + 10, tw = x + w - 10 - tx;
        const nameY = y + (wide ? 15 : 12);
        // KO는 이름 옆에 자리가 있을 때만 (이름이 6글자쯤 들어갈 자리), 없으면 drawHud가 시간 옆에 씁니다.
        ctx.font = "bold 11px sans-serif";
        const ko = `KO ${this.kills.toLocaleString()}`;
        const koW = ctx.measureText(ko).width;
        const koShown = tw - koW - 8 >= 72;
        if (koShown) {
            ctx.textAlign = "right";
            ctx.fillStyle = "#ffcf6e";
            ctx.fillText(ko, tx + tw, nameY);
        }
        ctx.font = `bold ${wide ? 15 : 12}px sans-serif`;
        ctx.textAlign = "left";
        this.outlinedText(this.fitText(this.form.name, koShown ? tw - koW - 8 : tw), tx, nameY, "#ffffff", 2);

        // 체력, 경험치
        const texts = this.setup.texts ?? {};
        const rowH = wide ? 15 : 12, labelW = wide ? 44 : 38, font = wide ? 10 : 9;
        const y1 = y + (wide ? 29 : 22), y2 = y1 + rowH + (wide ? 5 : 4);
        const low = p.hp / p.maxHp <= 0.3 && Math.floor(this.time * 4) % 2 === 0;
        this.labeledBar(texts.hp ?? "HP", p.hp, p.maxHp, tx, y1, tw, rowH, labelW,
            { label: "#c62828", top: low ? "#ffb4b4" : "#ff7b7b", bottom: low ? "#ff5252" : "#d32f2f" }, font);
        this.labeledBar(texts.exp ?? "EXP", this.exp, this.expNext, tx, y2, tw, rowH, labelW,
            { label: "#1f6fc4", top: "#8fd6ff", bottom: "#2f7fe0" }, font);
        return { right: x + w, bottom: y + h, koShown };
    }

    // 기술의 남은 재사용 시간 (0~1). 계속 도는 기술(orbit)은 null
    cooldownOf(w) {
        if (w.kind === "orbit") return null;
        if (w.kind === "aura") return Math.max(0, Math.min(1, w.tick / (AURA_TICK * w.cooldown * this.cooldownMul)));
        return w.cdMax ? Math.max(0, Math.min(1, w.cd / w.cdMax)) : 0;
    }

    // 기술 그림 하나와 재사용 시간 표시 (시계 방향으로 어둠이 걷힘)
    drawSkillIcon(w, x, y, size) {
        const ctx = this.ctx;
        const owned = w.level > 0;
        const r = 7;
        ctx.save();
        ctx.beginPath();
        if (ctx.roundRect) ctx.roundRect(x, y, size, size, r); else ctx.rect(x, y, size, size);
        ctx.fillStyle = owned ? "#2b3358" : "#22263a";
        ctx.fill();
        ctx.clip();
        const image = this.images[`ui/fx_shot_${w.type}.png`];
        ctx.globalAlpha = owned ? 1 : 0.3;
        const s = size * 0.72;
        if (image) ctx.drawImage(image, 0, 0, 16, 16, x + (size - s) / 2, y + (size - s) / 2, s, s);
        else { ctx.fillStyle = w.color; ctx.fillRect(x + size / 4, y + size / 4, size / 2, size / 2); }
        ctx.globalAlpha = 1;

        const left = owned ? this.cooldownOf(w) : 0;
        const cx = x + size / 2, cy = y + size / 2;
        if (left > 0) {
            const start = -Math.PI / 2 + (1 - left) * Math.PI * 2;
            ctx.beginPath();
            ctx.moveTo(cx, cy);
            ctx.arc(cx, cy, size, start, Math.PI * 1.5);
            ctx.closePath();
            ctx.fillStyle = "rgb(8 10 20 / 62%)";
            ctx.fill();
            const seconds = w.kind === "aura" ? left * AURA_TICK * w.cooldown * this.cooldownMul : w.cd / 1000;
            if (seconds >= 0.3 && (w.kind === "aura" ? AURA_TICK : (w.cdMax ?? 0) / 1000) >= 1.2) {
                ctx.font = `bold ${Math.round(size * 0.32)}px sans-serif`;
                ctx.textAlign = "center";
                this.outlinedText(seconds.toFixed(1), cx, cy + 1, "#ffffff");
            }
        }
        ctx.restore();

        // 테두리: 쓸 수 있을 때 기술 색으로 밝게
        ctx.beginPath();
        if (ctx.roundRect) ctx.roundRect(x + 1, y + 1, size - 2, size - 2, r - 1); else ctx.rect(x + 1, y + 1, size - 2, size - 2);
        ctx.strokeStyle = !owned ? "rgb(255 255 255 / 15%)" : left > 0 ? "rgb(255 255 255 / 35%)" : w.color;
        ctx.lineWidth = owned && left === 0 ? 2.5 : 1.5;
        ctx.stroke();

        // 계속 도는 기술은 테두리를 도는 빛으로 표시
        if (owned && left === null) {
            const a = this.time * 5;
            ctx.beginPath();
            ctx.arc(cx, cy, size / 2 - 1, a, a + 1.3);
            ctx.strokeStyle = "#ffffff";
            ctx.lineWidth = 2.5;
            ctx.stroke();
        }
    }

    // 오른쪽 아래: 기술 목록 (넓은 화면은 이름과 레벨까지, 휴대폰은 그림만. 휴대폰 가로는 가로로 한 줄)
    drawSkills(wide, row) {
        const ctx = this.ctx;
        const size = wide ? 36 : row ? 34 : 30, gap = wide ? 8 : 6;
        const count = this.weapons.length;
        const span = 6 + (count - 1) * (size + gap) + size + 6;
        const w = wide ? 196 : row ? span : size + 12;
        const h = row ? size + 12 : span;
        const x = this.viewW - w - 8, y = this.viewH - h - 8;
        this.panel(x, y, w, h, 12);
        this.weapons.forEach((wp, i) => {
            const ix = row ? x + 6 + i * (size + gap) : x + 6, iy = row ? y + 6 : y + 6 + i * (size + gap);
            this.drawSkillIcon(wp, ix, iy, size);
            const owned = wp.level > 0;
            if (wide) {
                const tx = ix + size + 8, tw = x + w - 8 - tx;
                ctx.textAlign = "left";
                ctx.font = "bold 12px sans-serif";
                ctx.fillStyle = owned ? "#ffffff" : "rgb(255 255 255 / 40%)";
                ctx.fillText(this.fitText(wp.name ?? "", tw - 18), tx, iy + 10);
                ctx.font = "11px sans-serif";
                ctx.textAlign = "right";
                ctx.globalAlpha = owned ? 1 : 0.4;
                ctx.fillText(KIND_ICONS[wp.kind] ?? "", tx + tw, iy + 10);
                ctx.globalAlpha = 1;
                // 레벨 칸 5개
                for (let k = 0; k < WEAPON_MAX; k++) {
                    ctx.fillStyle = k < wp.level ? wp.color : "rgb(255 255 255 / 14%)";
                    ctx.fillRect(tx + k * 12, iy + size - 11, 9, 6);
                    if (k < wp.level) {
                        ctx.fillStyle = "rgb(255 255 255 / 35%)";
                        ctx.fillRect(tx + k * 12, iy + size - 11, 9, 2);
                    }
                }
                if (wp.level >= WEAPON_MAX) {
                    ctx.font = "bold 9px sans-serif";
                    ctx.textAlign = "left";
                    ctx.fillStyle = "#ffd54f";
                    ctx.fillText("MAX", tx + WEAPON_MAX * 12 + 2, iy + size - 8);
                }
            } else if (owned) {
                ctx.font = "bold 10px sans-serif";
                ctx.textAlign = "right";
                this.outlinedText(`${wp.level}`, ix + size - 2, iy + size - 5, "#ffffff");
            }
        });
        return { left: x, right: x + w, top: y, bottom: y + h };
    }

    // 강화(패시브) 그림을 한 줄로 늘어놓습니다. (아래 기준, 넘치면 위로 한 줄 더)
    drawPassives(x, bottom, width) {
        const ctx = this.ctx;
        const list = PASSIVES.filter(name => this.passives[name] > 0);
        if (list.length === 0) return;
        const size = 24, gap = 4, perRow = Math.max(1, Math.floor((width + gap) / (size + gap)));
        list.forEach((name, i) => {
            const bx = x + (i % perRow) * (size + gap), by = bottom - size - Math.floor(i / perRow) * (size + gap);
            ctx.beginPath();
            if (ctx.roundRect) ctx.roundRect(bx, by, size, size, 6); else ctx.rect(bx, by, size, size);
            ctx.fillStyle = "rgb(18 22 40 / 82%)";
            ctx.fill();
            ctx.strokeStyle = this.passives[name] >= PASSIVE_MAX ? "#ffd54f" : "rgb(255 202 40 / 60%)";
            ctx.lineWidth = 1.5;
            ctx.stroke();
            ctx.font = "14px sans-serif";
            ctx.textAlign = "center";
            ctx.fillStyle = "#ffffff";
            ctx.fillText(PASSIVE_ICONS[name], bx + size / 2, by + size / 2 + 1);
            ctx.font = "bold 9px sans-serif";
            ctx.textAlign = "right";
            this.outlinedText(`${this.passives[name]}`, bx + size - 2, by + size - 5, "#ffffff", 2.5);
        });
    }

    // 오른쪽 위: 동그란 지도. 내 포켓몬이 가운데이고, 주변 MINIMAP_RANGE 안의 적과 아이템을 점으로 보여 줍니다.
    // 보스가 지도 밖에 있으면 테두리에 방향 표시를 띄웁니다. 일시정지 단추(SurvivalLayer) 왼쪽에 둡니다.
    drawMinimap(r) {
        const ctx = this.ctx, p = this.player;
        const cx = this.viewW - 54 - r, cy = 8 + r;
        const range = r >= 60 ? MINIMAP_RANGE : MINIMAP_RANGE * 0.8;
        const k = (r - 3) / range;

        ctx.save();
        ctx.beginPath();
        ctx.arc(cx, cy, r, 0, Math.PI * 2);
        ctx.fillStyle = "rgb(18 22 40 / 88%)";
        ctx.fill();
        ctx.clip();

        // 거리 고리와 십자선
        ctx.strokeStyle = "rgb(255 255 255 / 10%)";
        ctx.lineWidth = 1;
        for (const ring of [0.33, 0.66]) {
            ctx.beginPath();
            ctx.arc(cx, cy, r * ring, 0, Math.PI * 2);
            ctx.stroke();
        }
        ctx.beginPath();
        ctx.moveTo(cx - r, cy); ctx.lineTo(cx + r, cy);
        ctx.moveTo(cx, cy - r); ctx.lineTo(cx, cy + r);
        ctx.stroke();

        // 지금 화면에 보이는 넓이
        ctx.strokeStyle = "rgb(255 255 255 / 28%)";
        ctx.strokeRect(cx - this.halfW * k, cy - this.halfH * k, this.halfW * 2 * k, this.halfH * 2 * k);

        const dot = (x, y, size, color) => {
            const dx = (x - p.x) * k, dy = (y - p.y) * k;
            if (dx * dx + dy * dy > (r - 2) * (r - 2)) return;
            ctx.fillStyle = color;
            ctx.fillRect(cx + dx - size / 2, cy + dy - size / 2, size, size);
        };

        // 아이템: 큰 사탕, 열매, 상자, 자석·얼음·폭탄
        for (const g of this.gems) {
            if (g.kind === "candy") { if (candyStage(g.value) > 0) dot(g.x, g.y, 2.5, CANDY_STAGES[candyStage(g.value)].tint); }
            else if (g.kind === "berry") dot(g.x, g.y, 3, "#ff5c8a");
            else if (g.kind === "chest") dot(g.x, g.y, 5, "#ffd54f");
            else dot(g.x, g.y, 4, "#9fe3ff");
        }
        // 적: 보통은 작은 빨간 점, 엘리트는 주황, 보스는 크게
        let boss = null;
        for (const e of this.enemies) {
            if (e.dying) continue;
            if (e.rank === "boss") { boss = e; continue; }
            if (e.rank === "elite") dot(e.x, e.y, 5, "#ffa726");
            else dot(e.x, e.y, 3, e.rush ? "#ff9b5c" : "#ff5a5a");
        }
        if (boss) {
            const dx = (boss.x - p.x) * k, dy = (boss.y - p.y) * k, d = Math.hypot(dx, dy);
            const pulse = 1 + 0.25 * Math.sin(this.time * 8);
            if (d <= r - 6) {
                ctx.beginPath();
                ctx.arc(cx + dx, cy + dy, 4.5 * pulse, 0, Math.PI * 2);
                ctx.fillStyle = "#ff2d2d";
                ctx.fill();
                ctx.strokeStyle = "#ffffff";
                ctx.lineWidth = 1.5;
                ctx.stroke();
            } else {
                // 지도 밖: 테두리 안쪽에 보스 쪽을 가리키는 삼각형
                const a = Math.atan2(dy, dx), ex = cx + Math.cos(a) * (r - 8), ey = cy + Math.sin(a) * (r - 8);
                ctx.save();
                ctx.translate(ex, ey);
                ctx.rotate(a);
                ctx.beginPath();
                ctx.moveTo(6 * pulse, 0); ctx.lineTo(-4, -5); ctx.lineTo(-4, 5);
                ctx.closePath();
                ctx.fillStyle = "#ff2d2d";
                ctx.fill();
                ctx.restore();
            }
        }

        // 나: 바라보는 쪽을 가리키는 흰 삼각형
        const face = Math.atan2(p.faceY, p.faceX);
        ctx.save();
        ctx.translate(cx, cy);
        ctx.rotate(face);
        ctx.beginPath();
        ctx.moveTo(6, 0); ctx.lineTo(-4, -4.5); ctx.lineTo(-2, 0); ctx.lineTo(-4, 4.5);
        ctx.closePath();
        ctx.fillStyle = "#ffffff";
        ctx.fill();
        ctx.strokeStyle = "#4fa3ff";
        ctx.lineWidth = 1.2;
        ctx.stroke();
        ctx.restore();
        ctx.restore();

        // 테두리: 보스가 있으면 붉게
        ctx.beginPath();
        ctx.arc(cx, cy, r - 1, 0, Math.PI * 2);
        ctx.strokeStyle = boss ? "#ff4d4d" : "rgb(255 255 255 / 45%)";
        ctx.lineWidth = boss ? 2.5 : 2;
        ctx.stroke();
        return { left: cx - r, bottom: cy + r };
    }

    drawHud() {
        const ctx = this.ctx;
        const W = this.viewW, H = this.viewH;
        ctx.setTransform(this.dpr, 0, 0, this.dpr, 0, 0);

        ctx.imageSmoothingEnabled = false;
        ctx.textBaseline = "middle";

        // 화면 모양에 따라 세 가지로 늘어놓습니다. (CSS 픽셀 기준. 휴대폰은 해상도 설정을 바꿔도 이 크기는 같습니다)
        // - 넓은 화면(PC·태블릿): 크게, 기술 목록에 이름까지
        // - 휴대폰 세로: 작게, 기술은 그림만 세로로
        // - 휴대폰 가로(높이가 낮음): 작게, 기술은 그림만 가로로 (세로로 쌓으면 지도·강화와 겹침)
        const wide = W >= 700 && H >= 500;
        const landscape = !wide && W > H;
        const mapR = wide ? 62 : W < 360 ? 38 : 46;
        const profile = this.drawProfile(wide, mapR);
        const skills = this.drawSkills(wide, landscape);
        const map = this.drawMinimap(mapR);

        // 지난 시간: 얼굴 상자와 지도 사이가 넉넉하면 그 가운데 위에, 아니면 얼굴 상자 아래 왼쪽에 (KO가 상자에 안 들어갔으면 그 오른쪽에)
        const remain = Math.max(0, RUN_SECONDS - this.time);
        const clock = `${String(Math.floor(this.time / 60)).padStart(2, "0")}:${String(Math.floor(this.time % 60)).padStart(2, "0")}`;
        const gap = map.left - profile.right;
        const top = gap >= 96;
        const clockX = top ? (profile.right + map.left) / 2 : 14;
        const clockY = top ? (wide ? 26 : 22) : profile.bottom + 14;
        ctx.textAlign = top ? "center" : "left";
        ctx.font = `bold ${wide ? 22 : 18}px sans-serif`;
        this.outlinedText(clock, clockX, clockY, remain < 60 ? "#ffcf3f" : "#ffffff", 4);
        if (!profile.koShown) {
            ctx.font = "bold 12px sans-serif";
            ctx.textAlign = "right";
            this.outlinedText(`KO ${this.kills.toLocaleString()}`, profile.right - 4, clockY, "#ffcf6e");
        }

        // 보스 체력 (시간 아래)
        const boss = this.bossRef;
        if (boss && !boss.dying) {
            const barW = top ? Math.min(320, gap - 24) : profile.right - 8;
            const bx = top ? clockX - barW / 2 : 8, by = clockY + (top ? 16 : 13);
            ctx.fillStyle = "rgb(20 24 40 / 85%)";
            ctx.fillRect(bx - 2, by, barW + 4, 10);
            ctx.fillStyle = "#ff4d4d";
            ctx.fillRect(bx, by + 2, barW * Math.max(0, boss.hp / boss.maxHp), 6);
        }

        // 강화: 넓은 화면은 기술 목록 바로 위, 휴대폰은 왼쪽 아래 (기술 목록 왼쪽까지)
        this.drawPassives(wide ? skills.left : 8, wide ? skills.top - 6 : H - 8, wide ? skills.right - skills.left : skills.left - 16);

        // 아이템·떼 알림
        if (this.banner) {
            ctx.globalAlpha = Math.min(1, this.banner.t);
            ctx.font = "bold 16px sans-serif";
            ctx.textAlign = "center";
            ctx.lineWidth = 4;
            ctx.strokeStyle = "rgb(20 24 40 / 90%)";
            ctx.strokeText(this.banner.text, W / 2, 152);
            ctx.fillStyle = this.banner.color;
            ctx.fillText(this.banner.text, W / 2, 152);
            ctx.globalAlpha = 1;
        }

        // 보스 등장 알림
        if (this.bossBanner > 0 && this.bossRef) {
            const text = (this.setup.bossText ?? "{0}").replace("{0}", this.bossRef.def.name ?? "");
            ctx.globalAlpha = Math.min(1, this.bossBanner);
            ctx.font = "bold 18px sans-serif";
            ctx.textAlign = "center";
            ctx.lineWidth = 4;
            ctx.strokeStyle = "rgb(20 24 40 / 90%)";
            ctx.strokeText(text, W / 2, 128);
            ctx.fillStyle = "#ff4d4d";
            ctx.fillText(text, W / 2, 128);
            ctx.globalAlpha = 1;
        }

        // 진화 알림
        if (this.evolveBanner > 0) {
            const text = (this.setup.evolveText ?? "{0}").replace("{0}", this.form.name);
            ctx.globalAlpha = Math.min(1, this.evolveBanner);
            ctx.font = "bold 20px sans-serif";
            ctx.textAlign = "center";
            ctx.lineWidth = 4;
            ctx.strokeStyle = "rgb(20 24 40 / 90%)";
            ctx.strokeText(text, W / 2, 104);
            ctx.fillStyle = "#ffca28";
            ctx.fillText(text, W / 2, 104);
            ctx.globalAlpha = 1;
        }

        // 습격 알림
        if (this.swarmBanner > 0) {
            ctx.globalAlpha = Math.min(1, this.swarmBanner);
            ctx.font = "bold 18px sans-serif";
            ctx.textAlign = "center";
            ctx.lineWidth = 4;
            ctx.strokeStyle = "rgb(20 24 40 / 90%)";
            ctx.strokeText(this.setup.swarmText ?? "!", W / 2, 76);
            ctx.fillStyle = "#ff6b4a";
            ctx.fillText(this.setup.swarmText ?? "!", W / 2, 76);
            ctx.globalAlpha = 1;
        }

        // 조이스틱
        if (this.stick && !this.finished) {
            ctx.globalAlpha = 0.35;
            ctx.fillStyle = "#ffffff";
            ctx.beginPath();
            ctx.arc(this.stick.ox, this.stick.oy, 40, 0, Math.PI * 2);
            ctx.fill();
            ctx.globalAlpha = 0.7;
            ctx.beginPath();
            ctx.arc(this.stick.x, this.stick.y, 16, 0, Math.PI * 2);
            ctx.fill();
            ctx.globalAlpha = 1;
        }
    }
}
