// pixelfx(MIT) 효과를 스프라이트 시트(PNG)와 정보(JSON)로 뽑습니다. (생성용 도구, 게임에는 들어가지 않음)
// 쓰는 법: python tools/pfx_save_server.py 를 켜 두고, 아무 http://localhost 페이지의 개발자 도구에서
//   (await import("/tools/make_pfx_sheets.js")).run()   ← 이 파일을 그 서버에서 열 수 있는 곳에 두고 실행
// tools/fx_out 에 pfx_*.png/json 이 생깁니다. png는 wwwroot/pfx 로(앞의 pfx_ 빼고) 옮기고,
// json 값들은 wwwroot/js/survival.js 의 PFX 상수에 넣습니다.
// 기술별 효과(VARIANTS)만 다시 뽑을 때는 run() 대신 runVariants()를 부릅니다.
const REV = "044e12a45dc9cc312cec50ee606dfc98afe5adc2";
const BASE = `https://cdn.jsdelivr.net/gh/patch-gg/pixelfx@${REV}/src`;

const TYPES = {
    normal: "#9e9e7e", fire: "#e65100", water: "#1e78d6", electric: "#e0a800", grass: "#388e3c", ice: "#00acc1",
    fighting: "#c62828", poison: "#8e24aa", ground: "#a1742a", flying: "#5c6bc0", psychic: "#d81b60", bug: "#7c8a0e",
    rock: "#8d7a3a", ghost: "#5e4b8b", dragon: "#4527a0", dark: "#4e3c30", steel: "#607d8b", fairy: "#c2588f",
};

function mix(hex, to, k) {
    const a = parseInt(hex.slice(1), 16), b = parseInt(to.slice(1), 16);
    const ch = (v, s) => (v >> s) & 255;
    const m = (s) => Math.round(ch(a, s) * (1 - k) + ch(b, s) * k);
    return "#" + [m(16), m(8), m(0)].map(v => v.toString(16).padStart(2, "0")).join("");
}

// 어두운 색에서 흰색까지 8단계
function ramp(hex) {
    return [mix(hex, "#000000", 0.75), mix(hex, "#000000", 0.5), mix(hex, "#000000", 0.25), hex,
        mix(hex, "#ffffff", 0.3), mix(hex, "#ffffff", 0.55), mix(hex, "#ffffff", 0.8), "#ffffff"];
}

async function render(core, mod, opts, fps) {
    const meta = mod.meta;
    const [w, h] = meta.size, [ox, oy] = meta.origin;
    const frame = core.createFrame(w, h, -ox, -oy);
    const fx = core.createFx(mod, opts);
    const count = Math.max(1, Math.round(meta.duration * fps));
    const step = 1 / fps;
    const images = [];
    let minX = w, minY = h, maxX = -1, maxY = -1;
    for (let i = 0; i < count; i++) {
        core.clearFrame(frame);
        fx.draw(frame);
        const rgba = new Uint8ClampedArray(w * h * 4);
        core.composite(frame, rgba, null);
        for (let y = 0; y < h; y++) for (let x = 0; x < w; x++) {
            if (rgba[(y * w + x) * 4 + 3] > 8) {
                if (x < minX) minX = x; if (x > maxX) maxX = x;
                if (y < minY) minY = y; if (y > maxY) maxY = y;
            }
        }
        images.push(new ImageData(rgba, w, h));
        fx.update(step);
    }
    if (maxX < 0) { minX = 0; minY = 0; maxX = 0; maxY = 0; }
    const fw = maxX - minX + 1, fh = maxY - minY + 1;
    const sheet = document.createElement("canvas");
    sheet.width = fw * count;
    sheet.height = fh;
    const g = sheet.getContext("2d");
    const tmp = document.createElement("canvas");
    tmp.width = w; tmp.height = h;
    const tg = tmp.getContext("2d");
    images.forEach((img, i) => {
        tg.clearRect(0, 0, w, h);
        tg.putImageData(img, 0, 0);
        g.drawImage(tmp, minX, minY, fw, fh, i * fw, 0, fw, fh);
    });
    return { sheet, info: { fw, fh, frames: count, fps, ox: ox - minX, oy: oy - minY } };
}

async function save(name, sheet, info) {
    const blob = await new Promise(r => sheet.toBlob(r, "image/png"));
    await fetch(`http://127.0.0.1:5192/${name}.png`, { method: "POST", body: blob });
    await fetch(`http://127.0.0.1:5192/${name}.json`, { method: "POST", body: JSON.stringify(info) });
}

// 같은 공격 모양이라도 기술마다 다른 효과를 쓰도록 더 뽑는 회색 효과 시트 (게임에서 타입 색을 곱함)
// [시트 이름, pixelfx 경로, fps]. 시트를 어떤 기술에 쓰는지는 SurvivalRules.FxFor에서 정합니다.
export const VARIANTS = [
    ["s_flame", "fire/flamethrower", 15], ["s_jet", "water/water-jet", 15], ["s_frost", "ice/frost-breath", 15],
    ["b_fireball", "fire/fireball", 15], ["b_ice", "ice/ice-lance", 20], ["b_disc", "projectiles/energy-disc", 20],
    ["b_laser", "projectiles/laser-bolt", 20], ["b_arrow", "projectiles/arrow", 20],
    ["sl_sword", "melee/sword-slash", 20], ["sl_double", "melee/double-slash", 20], ["sl_punch", "melee/uppercut", 20],
    ["sl_whip", "melee/whip-crack", 20], ["sl_thrust", "melee/thrust", 20], ["sl_hammer", "melee/hammer-smash", 12],
    ["sl_scythe", "melee/scythe-reap", 12],
    ["n_slam", "impacts/ground-slam", 12], ["n_frost", "ice/frost-nova", 12], ["n_thunder", "lightning/thunderclap", 15],
    ["n_fire", "fire/fire-ring", 10], ["n_dark", "shadow/dark-explosion", 10], ["n_plasma", "explosions/plasma-explosion", 15],
    ["n_splash", "water/splash", 12],
    ["c_strike", "lightning/lightning-strike", 12],
    ["o_leaf", "nature/leaf-storm", 12], ["o_flame", "shadow/shadow-flames", 12], ["o_arcane", "arcane/arcane-orb", 12],
    ["a_puddle", "nature/toxic-puddle", 10], ["a_sun", "holy/sunburst", 10], ["a_whirl", "water/whirlpool", 12],
    // 베기 더: 여러 번 베기, 회전 베기, 큰 내려베기
    ["sl_flurry", "melee/dagger-flurry", 20], ["sl_spin", "melee/spin-slash", 20], ["sl_cleave", "melee/heavy-cleave", 12],
    // 맞을 때 효과 (공격 모양마다): 불꽃 튀김, 작은 폭발, 둔탁한 충격, 반짝임
    ["h_spark", "impacts/hit-spark", 30], ["h_burst", "explosions/plasma-explosion", 20], ["h_blunt", "impacts/blunt-hit", 20],
    ["h_parry", "impacts/parry", 20],
    // 쏘는 순간 (탄환만, 게임에서는 앞부분 번쩍임만)
    ["c_muzzle", "projectiles/muzzle-pistol", 15],
    // 변화 기술 오라 (발밑): 회복, 방어막, 보호막, 저주 문양, 포자
    ["a_heal", "holy/heal-aoe", 10], ["a_shield", "holy/divine-shield", 12], ["a_field", "scifi/force-field", 12],
    ["a_hex", "shadow/hex-sigil", 10], ["a_spore", "nature/spore-burst", 10],
    // 변화 기술 오라 (머리 위, 원래 색 그대로): 분노, 가속, 잠, 혼란, 마비, 헤롱헤롱, 화상, 자라기
    ["st_rage", "status/rage", 12, true], ["st_haste", "status/haste", 12, true], ["st_sleep", "status/sleep", 10, true],
    ["st_confused", "status/confused", 10, true], ["st_stunned", "status/stunned", 10, true], ["st_charmed", "status/charmed", 10, true],
    ["st_burning", "status/burning", 12, true], ["st_bloom", "nature/healing-bloom", 10, true],
];

export async function runVariants(only) {
    const core = await import(`${BASE}/core/index.js`);
    const GRAY = ["#3a3a3a", "#5a5a5a", "#7c7c7c", "#a0a0a0", "#c4c4c4", "#e0e0e0", "#f4f4f4", "#ffffff"];
    const log = [];
    for (const [name, path, fps, native] of VARIANTS) {
        if (only && !only.includes(name)) continue;
        const mod = await import(`${BASE}/fx/${path}.js`);
        // native: 원래 색 그대로 (머리 위 상태 표시). 나머지는 회색으로 뽑고 게임에서 타입 색 단계를 입힙니다.
        const r = await render(core, mod, native ? { seed: 7 } : { palette: GRAY, sparks: ["#ffffff", "#e0e0e0", "#c4c4c4", "#7c7c7c"], seed: 7 }, fps);
        await save(`pfx_${name}`, r.sheet, r.info);
        log.push(`${name} ${JSON.stringify(r.info)}`);
    }
    return log.join("\n");
}

export async function run() {
    const core = await import(`${BASE}/core/index.js`);
    const hit = await import(`${BASE}/fx/impacts/hit-spark.js`);
    const poof = await import(`${BASE}/fx/spawn/poof.js`);
    const burst = await import(`${BASE}/fx/pickups/collect-burst.js`);
    const mana = await import(`${BASE}/fx/arcane/mana-burst.js`);
    const log = [];

    for (const [type, color] of Object.entries(TYPES)) {
        let r = await render(core, hit, { palette: ramp(color), height: 0, seed: 4 }, 30);
        await save(`pfx_hit_${type}`, r.sheet, r.info);
        log.push(`hit_${type} ${r.info.fw}x${r.info.fh}x${r.info.frames}`);
    }
    // 충격파는 회색 한 장만 만들고, 게임에서 타입 색을 곱해서 씁니다.
    let r = await render(core, mana, { palette: ["#3a3a3a", "#5a5a5a", "#7c7c7c", "#a0a0a0", "#c4c4c4", "#e0e0e0", "#f4f4f4", "#ffffff"], seed: 9 }, 15);
    await save("pfx_nova", r.sheet, r.info);
    log.push(`nova ${r.info.fw}x${r.info.fh}x${r.info.frames}`);
    r = await render(core, poof, {}, 15);
    await save("pfx_poof", r.sheet, r.info);
    log.push(`poof ${r.info.fw}x${r.info.fh}x${r.info.frames}`);
    // 서바이벌 기술 효과: 회색으로 뽑고 게임에서 기술 타입 색을 곱합니다.
    const GRAY = ["#3a3a3a", "#5a5a5a", "#7c7c7c", "#a0a0a0", "#c4c4c4", "#e0e0e0", "#f4f4f4", "#ffffff"];
    const weapons = [
        ["bolt", "projectiles/plasma-shot", {}, 20],
        ["spray", "fire/fire-breath", {}, 15],
        ["slash", "melee/claw-swipe", { sparks: ["#ffffff", "#e0e0e0", "#c4c4c4", "#7c7c7c"] }, 20],
        ["orbit", "water/water-orb", {}, 12],
        ["chain", "lightning/zap-hit", {}, 20],
        ["aura", "arcane/rune-circle", {}, 12],
    ];
    for (const [kind, path, extra, fps] of weapons) {
        const mod = await import(`${BASE}/fx/${path}.js`);
        const w = await render(core, mod, { palette: GRAY, ...extra }, fps);
        await save(`pfx_w_${kind}`, w.sheet, w.info);
        log.push(`w_${kind} ${w.info.fw}x${w.info.fh}x${w.info.frames}`);
    }
    const crit = await import(`${BASE}/fx/impacts/crit-hit.js`);
    r = await render(core, crit, { height: 0 }, 30);
    await save("pfx_crit", r.sheet, r.info);
    log.push(`crit ${r.info.fw}x${r.info.fh}x${r.info.frames}`);
    r = await render(core, burst, { palette: ramp("#f5b92c") }, 20);
    await save("pfx_levelup", r.sheet, r.info);
    log.push(`levelup ${r.info.fw}x${r.info.fh}x${r.info.frames}`);
    return log.join("\n");
}
