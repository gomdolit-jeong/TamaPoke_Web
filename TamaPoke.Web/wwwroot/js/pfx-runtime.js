// 판을 시작할 때 pixelfx(MIT, https://github.com/patch-gg/pixelfx, wwwroot/lib/pixelfx)로 효과 시트를 그 자리에서 그립니다.
// 미리 뽑아 둔 회색 시트(wwwroot/pfx)에 색을 입히는 대신, 기술 타입 색 8단계로 처음부터 그려서 색이 더 자연스럽습니다.
// 그린 시트는 미리 뽑아 둔 시트와 크기·기준점이 같게 잘라서(survival.js의 PFX 값), 그리는 코드는 그대로 씁니다.
// 실패하면 null을 돌려주고, 게임은 미리 뽑아 둔 시트에 색을 입혀 씁니다.

const BASE = new URL("../lib/pixelfx/src/", import.meta.url).href;

// 시트 이름 → [pixelfx 경로, 추가 옵션]. fps·프레임 수는 PFX 값을 씁니다. (tools/make_pfx_sheets.js와 같은 효과)
export const SPECS = {
    w_bolt: ["projectiles/plasma-shot"], w_spray: ["fire/fire-breath"], w_slash: ["melee/claw-swipe"],
    w_orbit: ["water/water-orb"], w_chain: ["lightning/zap-hit"], w_aura: ["arcane/rune-circle"], nova: ["arcane/mana-burst", { seed: 9 }],
    s_flame: ["fire/flamethrower"], s_jet: ["water/water-jet"], s_frost: ["ice/frost-breath"],
    b_fireball: ["fire/fireball"], b_ice: ["ice/ice-lance"], b_disc: ["projectiles/energy-disc"], b_laser: ["projectiles/laser-bolt"], b_arrow: ["projectiles/arrow"],
    sl_sword: ["melee/sword-slash"], sl_double: ["melee/double-slash"], sl_punch: ["melee/uppercut"], sl_whip: ["melee/whip-crack"],
    sl_thrust: ["melee/thrust"], sl_hammer: ["melee/hammer-smash"], sl_scythe: ["melee/scythe-reap"],
    sl_flurry: ["melee/dagger-flurry"], sl_spin: ["melee/spin-slash"], sl_cleave: ["melee/heavy-cleave"],
    n_slam: ["impacts/ground-slam"], n_frost: ["ice/frost-nova"], n_thunder: ["lightning/thunderclap"], n_fire: ["fire/fire-ring"],
    n_dark: ["shadow/dark-explosion"], n_plasma: ["explosions/plasma-explosion"], n_splash: ["water/splash"],
    c_strike: ["lightning/lightning-strike"],
    o_leaf: ["nature/leaf-storm"], o_flame: ["shadow/shadow-flames"], o_arcane: ["arcane/arcane-orb"],
    a_puddle: ["nature/toxic-puddle"], a_sun: ["holy/sunburst"], a_whirl: ["water/whirlpool"],
    a_heal: ["holy/heal-aoe"], a_shield: ["holy/divine-shield"], a_field: ["scifi/force-field"], a_hex: ["shadow/hex-sigil"], a_spore: ["nature/spore-burst"],
    h_spark: ["impacts/hit-spark"], h_burst: ["explosions/plasma-explosion"], h_blunt: ["impacts/blunt-hit"], h_parry: ["impacts/parry"],
    c_muzzle: ["projectiles/muzzle-pistol"],
};

// 미리 뽑아 둔 시트는 회색 8단계(아래 밝기)로 그렸습니다. 같은 자리에 타입 색을 넣으면 같은 모양이 됩니다.
const GRAY_LUM = [58, 90, 124, 160, 196, 224, 244, 255];

// 타입 색 8단계(어두운 색 → 흰색)를 게임의 색 입히기와 같은 밝기 곡선으로 옮긴 팔레트
export function paletteFor(ramp) {
    const rgb = ramp.map(h => { const v = parseInt(h.slice(1), 16); return [(v >> 16) & 255, (v >> 8) & 255, v & 255]; });
    return GRAY_LUM.map(lum => {
        const t = Math.pow(Math.max(0, Math.min(1, (lum - 40) / 215)), 1.5) * 5.4;
        const k = Math.min(6, Math.floor(t)), f = t - k, a = rgb[k], b = rgb[k + 1];
        return "#" + a.map((c, i) => Math.round(c + (b[i] - c) * f).toString(16).padStart(2, "0")).join("");
    });
}

let core = null;
const modules = {};
async function moduleOf(path) {
    core ??= await import(`${BASE}core/index.js`);
    modules[path] ??= await import(`${BASE}fx/${path}.js`);
    return modules[path];
}

// name 시트를 palette(8색)로 그려 info(fw·fh·frames·fps·ox·oy)와 같은 모양의 캔버스로 돌려줍니다.
export async function renderSheet(name, info, palette) {
    const spec = SPECS[name];
    if (!spec || !info) return null;
    const [path, extra = {}] = spec;
    const mod = await moduleOf(path);
    const [w, h] = mod.meta.size, [mx, my] = mod.meta.origin;
    const opts = { palette, sparks: [palette[7], palette[5], palette[4], palette[2]] };
    if (extra.seed != null) opts.seed = extra.seed;
    else if (!/^(w_|nova)/.test(name)) opts.seed = 7; // tools/make_pfx_sheets.js의 runVariants와 같은 seed
    const frame = core.createFrame(w, h, -mx, -my);
    const fx = core.createFx(mod, opts);
    const sheet = document.createElement("canvas");
    sheet.width = info.fw * info.frames;
    sheet.height = info.fh;
    const g = sheet.getContext("2d");
    const tmp = document.createElement("canvas");
    tmp.width = w;
    tmp.height = h;
    const tg = tmp.getContext("2d");
    const rgba = new Uint8ClampedArray(w * h * 4);
    // 미리 뽑은 시트의 기준점(info.ox, oy)에 효과 기준점(mx, my)이 오도록 잘라 붙입니다.
    const sx = mx - info.ox, sy = my - info.oy;
    for (let i = 0; i < info.frames; i++) {
        core.clearFrame(frame);
        fx.draw(frame);
        rgba.fill(0);
        core.composite(frame, rgba, null);
        tg.putImageData(new ImageData(rgba, w, h), 0, 0);
        g.drawImage(tmp, sx, sy, info.fw, info.fh, i * info.fw, 0, info.fw, info.fh);
        fx.update(1 / info.fps);
        // 오래 걸리는 시트도 화면(준비 화면의 진행 막대)이 멈추지 않게 6장마다 쉬어 갑니다.
        if (i % 6 === 5) await new Promise(resolve => setTimeout(resolve));
    }
    return sheet;
}
