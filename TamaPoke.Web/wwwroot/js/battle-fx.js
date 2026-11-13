// 배틀 화면의 기술 효과 시트: 서바이벌과 같은 pixelfx 효과를 기술 타입 색으로 그려서 그림 주소(blob URL)로 돌려줍니다.
// 그리는 데 실패하면 미리 뽑아 둔 회색 시트(wwwroot/pfx)에 타입 색을 입힙니다. st_로 시작하는 그림은 원래 색 그대로 씁니다.
const urls = new Map();       // "이름|타입" → Promise<string|null>
let runtime;                  // pfx-runtime.js (한 번만 불러옴)

// 서바이벌의 효과 시트 표(PFX)와 타입 색(rampHex). 이 파일과 같은 버전 꼬리(?v=)를 붙여서 옛 파일이 캐시에서 오지 않게 합니다.
let shared;
function sharedModule() {
    shared ??= import(`./survival.js${new URL(import.meta.url).search}`);
    return shared;
}

function runtimeModule() {
    runtime ??= import("./pfx-runtime.js").catch(() => null);
    return runtime;
}

function loadImage(src) {
    return new Promise((resolve, reject) => {
        const img = new Image();
        img.onload = () => resolve(img);
        img.onerror = reject;
        img.src = src;
    });
}

function toUrl(canvas) {
    return new Promise(resolve => canvas.toBlob(blob => resolve(blob ? URL.createObjectURL(blob) : null), "image/png"));
}

// 회색 시트의 밝기를 타입 색 8단계에 이어서 칠합니다. (survival.js의 tinted와 같은 곡선)
async function tintGray(name, ramp) {
    const img = await loadImage(`pfx/${name}.png`);
    const canvas = document.createElement("canvas");
    canvas.width = img.width;
    canvas.height = img.height;
    const g = canvas.getContext("2d", { willReadFrequently: true });
    g.drawImage(img, 0, 0);
    const rgb = ramp.map(h => { const v = parseInt(h.slice(1), 16); return [(v >> 16) & 255, (v >> 8) & 255, v & 255]; });
    const data = g.getImageData(0, 0, canvas.width, canvas.height);
    const px = data.data;
    for (let i = 0; i < px.length; i += 4) {
        if (px[i + 3] === 0) continue;
        const lum = 0.3 * px[i] + 0.59 * px[i + 1] + 0.11 * px[i + 2];
        const t = Math.pow(Math.max(0, Math.min(1, (lum - 40) / 215)), 1.5) * 5.4;
        const k = Math.min(6, Math.floor(t)), f = t - k, a = rgb[k], b = rgb[k + 1];
        px[i] = a[0] + (b[0] - a[0]) * f;
        px[i + 1] = a[1] + (b[1] - a[1]) * f;
        px[i + 2] = a[2] + (b[2] - a[2]) * f;
    }
    g.putImageData(data, 0, 0);
    return toUrl(canvas);
}

async function make(name, type, color) {
    const { PFX, rampHex } = await sharedModule();
    if (!PFX[name]) return null;
    if (name.startsWith("st_")) return `pfx/${name}.png`;
    const ramp = rampHex(type, color);
    const rt = await runtimeModule();
    if (rt?.SPECS?.[name]) {
        try {
            const sheet = await rt.renderSheet(name, PFX[name], rt.paletteFor(ramp));
            if (sheet) return await toUrl(sheet);
        } catch { /* 회색 시트에 색을 입혀 씁니다 */ }
    }
    try { return await tintGray(name, ramp); } catch { return null; }
}

// 효과 시트 그림 주소. 같은 이름과 타입은 한 번만 그립니다. 없는 효과면 null
export function sheet(name, type, color) {
    const key = `${name}|${type}`;
    if (!urls.has(key)) urls.set(key, make(name, type, color));
    return urls.get(key);
}

// 미리 그려 두기 (배틀이 시작될 때 양쪽 기술 효과). [[이름, 타입, 색], ...]
export async function preload(list) {
    for (const [name, type, color] of list) await sheet(name, type, color);
}

// --- 머리 위치: 스프라이트의 Offsets 그림(검은 점 = 머리)에서 지금 프레임의 머리를 찾아 배틀 화면 좌표로 돌려줍니다. ---
const offsetImages = new Map(); // 주소 → Promise<ImageData|null>

function offsetData(url) {
    if (!offsetImages.has(url)) {
        offsetImages.set(url, (async () => {
            const img = new Image();
            img.crossOrigin = "anonymous";
            img.src = url;
            await img.decode();
            const canvas = document.createElement("canvas");
            canvas.width = img.width;
            canvas.height = img.height;
            const g = canvas.getContext("2d", { willReadFrequently: true });
            g.drawImage(img, 0, 0);
            return g.getImageData(0, 0, img.width, img.height);
        })().catch(() => null));
    }
    return offsetImages.get(url);
}

// side: "player" 또는 "enemy". 머리를 못 찾으면 null
export async function head(side) {
    const el = document.querySelector(`.battle-sprite-${side} .sprite`);
    const field = el?.closest(".battle-field");
    if (!el?.dataset.offsets || !field) return null;
    const { frame, row, fw, fh, flip } = Object.fromEntries(Object.entries(el.dataset).map(([k, v]) => [k, k === "offsets" ? v : Number(v)]));
    const data = await offsetData(el.dataset.offsets);
    if (!data) return null;
    let hx = -1, hy = -1;
    for (let y = 0; y < fh && hx < 0; y++) {
        for (let x = 0; x < fw; x++) {
            const i = ((row * fh + y) * data.width + frame * fw + x) * 4;
            if (data.data[i + 3] > 0 && data.data[i] < 40 && data.data[i + 1] < 40 && data.data[i + 2] < 40) { hx = x; hy = y; break; }
        }
    }
    if (hx < 0) return null;
    // 화면에 그려진 크기(배율·페이지 확대 포함)를 배틀 화면 좌표(CSS px)로 바꿉니다.
    const r = el.getBoundingClientRect(), f = field.getBoundingClientRect();
    const lx = (flip ? fw - hx - 0.5 : hx + 0.5) / fw, ly = (hy + 0.5) / fh;
    const k = field.offsetWidth / f.width;
    return [(r.left + lx * r.width - f.left) * k, (r.top + ly * r.height - f.top) * k];
}
