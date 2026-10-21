// Klock dial: a canvas port of the app's renderer. Same layout (units of the dial radius), same colours,
// same movement maths (damped-spring ticks), so the page shows the real thing rather than a mock-up.

export const DIALS = [
  // id, name, face centre, face edge, ink, hand/pill, cap dot, shadow, night dim
  d('white', 'White', '#E9E6DF', '#DDD9D0', '#2B2A28', '#FBFAF7', '#8C8579', .30, .86),
  d('turquoise', 'Turquoise', '#AEE9E3', '#93DAD3', '#0D5560', '#F2FFFD', '#0D5560', .28, .90),
  d('glacier', 'Glacier', '#C4D8E1', '#B1C9D4', '#1D4F63', '#F4FAFC', '#1D4F63', .28, .86),
  d('ocean', 'Ocean', '#2C6E9C', '#1D5280', '#E8F3FA', '#F5FAFD', '#E8F3FA', .40, .82),
  d('tennis', 'Tennis', '#DAEB6E', '#C5DA48', '#2C3A12', '#FBFFEA', '#2C3A12', .28, .90),
  d('signal-blue', 'Signal Blue', '#3862E8', '#2548C2', '#F4F7FF', '#FFFFFF', '#F4F7FF', .42, .84),
  d('salmon', 'Salmon', '#F5B6A8', '#EB9888', '#5A2418', '#FFF8F5', '#5A2418', .28, .90),
  d('yellow', 'Yellow', '#FFE26E', '#F7CE42', '#3D2E00', '#FFFDF2', '#3D2E00', .26, .92),
  d('beige', 'Beige', '#E9DCC5', '#D9C9AC', '#3F3225', '#FFFCF6', '#3F3225', .30, .86),
  d('pistachio', 'Pistachio', '#C7DEB3', '#B3CF9C', '#26401C', '#F9FFF4', '#26401C', .28, .86),
  d('lavender', 'Lavender', '#D0C7F1', '#BBAFE7', '#302760', '#FAF8FF', '#302760', .30, .86),
  d('rose', 'Rose', '#F7CBD6', '#ECB2C0', '#5A2236', '#FFF8FA', '#5A2236', .28, .86),
  d('sky-blue', 'Sky Blue', '#A7D7F4', '#8EC8EB', '#12405E', '#F5FBFF', '#12405E', .28, .86),
  d('cream', 'Cream', '#F7EDD5', '#ECDEBB', '#35302A', '#FFFEF9', '#35302A', .28, .86),
  d('slate', 'Slate', '#5E6973', '#4B555E', '#EEF2F5', '#F7F9FA', '#EEF2F5', .50, .72),
  d('noir', 'Noir', '#222225', '#161618', '#E6E6E8', '#F4F4F5', '#E6E6E8', .60, .35),
];

export const LUMES = [
  ['red', 'Red', '#FF3B30'], ['amber', 'Amber', '#FFB000'], ['lavender', 'Lavender', '#B79CFF'],
  ['rose', 'Rose', '#FF7AA8'], ['seafoam', 'Seafoam', '#6EE7B7'], ['sky', 'Sky', '#5CC8FF'],
  ['bgw9', 'BGW9', '#A8F5EA'], ['peach', 'Peach', '#FFB38A'], ['tritium', 'Tritium Green', '#9CFF6B'],
  ['aqua', 'Aqua', '#2DE2E6'], ['yellow', 'Yellow', '#FFE94D'], ['lime', 'Lime', '#B6FF2E'],
  ['ice-blue', 'Ice Blue', '#BEE6FF'], ['white', 'White', '#F4F7FF'],
].map(([id, name, hex]) => ({ id, name, hex }));

// Typefaces (self-hosted, unmodified). `scale` = optical size for [hour numerals, minute numerals].
export const FONTS = {
  panchang: { name: 'Panchang', css: '800 100px Panchang', family: 'Panchang', weight: 800, scale: [0.80, 0.74] },
  clash: { name: 'Clash Display', css: '600 100px "Clash Display"', family: '"Clash Display"', weight: 600, scale: [1.02, 1.0] },
  satoshi: { name: 'Satoshi', css: '900 100px Satoshi', family: 'Satoshi', weight: 900, scale: [1.00, 0.95] },
  melodrama: { name: 'Melodrama', css: '700 100px Melodrama', family: 'Melodrama', weight: 700, scale: [1.00, 1.02] },
};

function d(id, name, c1, c2, ink, hand, accent, shadow, dim) {
  return { id, name, c1: hex(c1), c2: hex(c2), ink: hex(ink), hand: hex(hand), accent: hex(accent), shadow, dim };
}

// ---------------------------------------------------------------- colour helpers

export function hex(h) { h = h.replace('#', ''); return [0, 2, 4].map(i => parseInt(h.slice(i, i + 2), 16)); }
export const mix = (a, b, t) => a.map((v, i) => v + (b[i] - v) * t);
export const rgb = (c, a = 1) => `rgba(${c.map(v => Math.round(Math.min(255, Math.max(0, v)))).join(',')},${a})`;
const BLACK = [0, 0, 0], WHITE = [255, 255, 255];
const lum = c => (0.2126 * c[0] + 0.7152 * c[1] + 0.0722 * c[2]) / 255;

/** The dial's day and night palettes (night: face dims toward black, ink becomes the lume colour). */
export function palette(dial, lumeHex) {
  const mid = mix(dial.c1, dial.c2, .5);
  const lume = hex(lumeHex);
  return {
    day: { bg1: dial.c1, bg2: dial.c2, ink: dial.ink, hand: dial.hand, mid },
    night: {
      bg1: mix(dial.c1, BLACK, dial.dim), bg2: mix(dial.c2, BLACK, Math.min(1, dial.dim + .05)),
      ink: lume, hand: mix(mix(mid, BLACK, dial.dim), BLACK, .08), mid: mix(mid, BLACK, dial.dim), lume,
    },
  };
}
export { lum };

// ---------------------------------------------------------------- movement (port of MovementEngine.cs)

const QUARTZ = { z: 0.55, w: 54 }, MECH = { z: 0.62, w: 110 };

export function stepResponse(t, z, w) {
  if (t <= 0) return 0;
  const wd = w * Math.sqrt(1 - z * z), e = Math.exp(-z * w * t);
  return 1 - e * (Math.cos(wd * t) + (z * w / wd) * Math.sin(wd * t));
}

/** Hand angles in degrees from a local time-of-day in seconds. type: 'quartz' | 'mechanical' | 'smooth'. */
export function handAngles(type, tod) {
  tod = ((tod % 86400) + 86400) % 86400;
  const sec = tod % 60;
  const hour = t => ((t / 3600) % 12) * 30, min = t => ((t / 60) % 60) * 6;
  if (type === 'quartz') {
    const whole = Math.floor(tod), n = Math.floor(sec), f = sec - n;
    return { h: hour(whole), m: min(whole), s: (n - 1) * 6 + 6 * stepResponse(f, QUARTZ.z, QUARTZ.w) };
  }
  if (type === 'mechanical') {
    const beat = Math.floor(sec * 4), elapsed = sec - beat / 4;
    return { h: hour(tod), m: min(tod), s: (beat - 1) * 1.5 + 1.5 * stepResponse(elapsed, MECH.z, MECH.w) };
  }
  return { h: hour(tod), m: min(tod), s: sec * 6 };
}

// ---------------------------------------------------------------- renderer

const TAU = Math.PI * 2, DEG = Math.PI / 180;

export class DialClock {
  /** @param state shared page state: { dial, lume, font, movement, night (0..1, animated) } */
  constructor(canvas, state, { interactiveLabel = true } = {}) {
    this.canvas = canvas;
    this.state = state;
    this.ctx = canvas.getContext('2d');
    this.key = '';
    this.layers = null;
    this.visible = true;
    this.label = interactiveLabel;
    this.lastLabel = '';
    this.resize();
    new ResizeObserver(() => this.resize()).observe(canvas);
    new IntersectionObserver(e => { this.visible = e[0].isIntersecting; }, { rootMargin: '100px' }).observe(canvas);
  }

  resize() {
    const r = this.canvas.getBoundingClientRect();
    const dpr = Math.min(window.devicePixelRatio || 1, 2);
    const size = Math.max(64, Math.round(Math.min(r.width, r.height) * dpr));
    if (size === this.size && dpr === this.dpr) return;
    this.dpr = dpr;
    this.size = size;
    this.canvas.width = this.canvas.height = size;
    this.R = (size / 2) * 0.93; // outer edge of the minute numerals ~ 0.99R, leaving room for shadows and glow
    this.key = '';              // layers need rebuilding
    this.geo = null;
  }

  // ---- static layers (face furniture), rebuilt only when the look changes

  ensureLayers() {
    const s = this.state, key = `${s.dial.id}|${s.lume.id}|${s.font}|${this.size}`;
    if (key === this.key && this.layers) return;
    this.key = key;
    const pal = palette(s.dial, s.lume.hex);
    this.pal = pal;
    this.layers = { day: this.buildLayer(pal, false), night: this.buildLayer(pal, true) };
  }

  buildLayer(pal, night) {
    const S = this.size, R = this.R, s = this.state, c = document.createElement('canvas');
    c.width = c.height = S;
    const g = c.getContext('2d');
    g.translate(S / 2, S / 2);
    const p = night ? pal.night : pal.day;
    const ink = night ? pal.night.lume : pal.day.ink;
    const glow = night ? pal.night.lume : null;

    // pills (the 5-minute indices): raised body, thin rim, inset groove
    for (let i = 0; i < 12; i++) {
      const q = i % 3 === 0 ? 1.10 : 1;
      const L = .180 * R * q, W = .064 * R * q, mid = .800 * R;
      g.save();
      g.rotate(i * 30 * DEG);
      const x = -W / 2, y = -(mid + L / 2);
      g.shadowColor = rgb(BLACK, night ? .5 : s.dial.shadow * .95);
      g.shadowBlur = R * .018; g.shadowOffsetX = R * .009; g.shadowOffsetY = R * .016;
      g.fillStyle = night ? rgb(mix(mix(p.mid, pal.night.lume, .10), WHITE, .0).map(v => v * 1.25)) : rgb(p.hand);
      round(g, x, y, W, L, W / 2); g.fill();
      g.shadowColor = 'transparent'; g.shadowBlur = 0; g.shadowOffsetX = g.shadowOffsetY = 0;
      g.lineWidth = R * .003;
      g.strokeStyle = night ? rgb(pal.night.lume, .3) : rgb(p.ink, .16);
      round(g, x, y, W, L, W / 2); g.stroke();

      const inset = W * .20, gx = x + inset, gy = y + inset * 1.15, gw = W - 2 * inset, gl = L - 2 * inset * 1.15;
      if (night) { g.shadowColor = rgb(glow, .95); g.shadowBlur = R * .035; }
      g.fillStyle = night ? rgb(pal.night.lume) : rgb(mix(p.hand, p.mid, .30));
      round(g, gx, gy, gw, gl, gw / 2); g.fill();
      g.shadowColor = 'transparent'; g.shadowBlur = 0;
      if (!night) { g.strokeStyle = 'rgba(0,0,0,.20)'; g.lineWidth = R * .0034; round(g, gx, gy, gw, gl, gw / 2); g.stroke(); }
      // upper-left specular lip (day only: it is a reflection, which lume does not have)
      if (!night) {
        const grad = g.createLinearGradient(x, y, x + W, y + L);
        grad.addColorStop(0, 'rgba(255,255,255,.9)'); grad.addColorStop(.45, 'rgba(255,255,255,0)');
        g.strokeStyle = grad; g.lineWidth = R * .0042; round(g, x + R * .001, y + R * .001, W - R * .002, L - R * .002, W / 2); g.stroke();
      }
      g.restore();
    }

    // minute dashes
    g.fillStyle = rgb(ink, night ? .5 : .8);
    if (night) { g.shadowColor = rgb(glow, .7); g.shadowBlur = R * .02; }
    for (let m = 0; m < 60; m++) {
      if (m % 5 === 0) continue;
      g.save(); g.rotate(m * 6 * DEG);
      const L = .056 * R, W = .0105 * R, mid = .852 * R;
      round(g, -W / 2, -(mid + L / 2), W, L, W / 2); g.fill();
      g.restore();
    }
    g.shadowColor = 'transparent'; g.shadowBlur = 0;

    // numerals
    const fx = FONTS[s.font];
    if (night) { g.shadowColor = rgb(glow, .9); g.shadowBlur = R * .03; }
    for (let h = 1; h <= 12; h++) {
      const a = h * 30 * DEG;
      numeral(g, String(h), Math.sin(a) * .59 * R, -Math.cos(a) * .59 * R, .126 * R * fx.scale[0], 0, fx, rgb(ink, night ? .95 : 1));
    }
    if (night) g.shadowBlur = R * .02;
    for (let i = 0; i < 12; i++) {
      const deg = i * 30, a = deg * DEG, rot = deg > 90 && deg < 270 ? deg + 180 : deg;
      numeral(g, i === 0 ? '60' : String(i * 5).padStart(2, '0'), Math.sin(a) * .972 * R, -Math.cos(a) * .972 * R,
        .056 * R * fx.scale[1], rot * DEG, fx, rgb(ink, night ? .62 : .88));
    }
    return c;
  }

  // ---- hand geometry for the current radius

  geometry() {
    if (this.geo) return this.geo;
    const R = this.R;
    const tapered = (tip, tail, hw, taper) => {
      const p = new Path2D(), w = hw * R, t = tip * R, tl = tail * R, tp = taper * R;
      p.moveTo(-w, tl - w); p.arc(0, tl - w, w, Math.PI, 0, true);
      p.lineTo(w, -t + tp); p.lineTo(0, -t); p.lineTo(-w, -t + tp); p.closePath();
      return p;
    };
    const slot = (tip, bottom, hw) => {
      const p = new Path2D(), w = hw * R, t = tip * R, b = bottom * R, pt = w * 3.6;
      p.moveTo(0, -t); p.lineTo(w, -t + pt); p.lineTo(w, b - w); p.arc(0, b - w, w, 0, Math.PI, false);
      p.lineTo(-w, -t + pt); p.closePath();
      return p;
    };
    const needle = (tip, tail, hw) => {
      const p = new Path2D(), w = Math.max(hw * R, .6), t = tip * R, tl = tail * R;
      p.moveTo(0, -t); p.lineTo(w, -t + w * 14); p.lineTo(w * 1.5, tl); p.lineTo(-w * 1.5, tl); p.lineTo(-w, -t + w * 14); p.closePath();
      return p;
    };
    this.geo = {
      hour: tapered(.560, .130, .027, .085), hourSlot: slot(.500, .030, .0115),
      minute: tapered(.865, .150, .0215, .105), minuteSlot: slot(.790, .035, .0088),
      second: needle(.905, .150, .0042),
    };
    return this.geo;
  }

  // ---- one frame

  draw(todSeconds) {
    if (!this.visible) return;
    this.ensureLayers();
    const { ctx, size: S, R, state: s } = this;
    const night = s.night, pal = this.pal;
    ctx.setTransform(1, 0, 0, 1, 0, 0);
    ctx.clearRect(0, 0, S, S);
    ctx.drawImage(this.layers.day, 0, 0);
    if (night > .002) { ctx.globalAlpha = night; ctx.drawImage(this.layers.night, 0, 0); ctx.globalAlpha = 1; }

    const a = handAngles(s.reduced ? 'quartz' : s.movement, s.reduced ? Math.floor(todSeconds) : todSeconds);
    const geo = this.geometry();
    const dial = s.dial, mid = pal.day.mid;
    const nightFace = pal.night.mid;
    const lume = pal.night.lume;
    const handFill = mix(dial.hand, mix(nightFace, BLACK, .08), night);
    const edge = mix(dial.ink, lume, night);
    const channel = mix(mix(dial.hand, mid, .50), lume, night);
    const secondFill = mix(dial.hand, lume.map(v => v * .62), night);
    const shadowA = dial.shadow * (1 - .55 * night);

    ctx.save();
    ctx.translate(S / 2, S / 2);

    const hand = (angle, body, slot, shDx, shDy, shBlur, shA) => {
      ctx.save();
      ctx.rotate(angle * DEG);
      ctx.shadowColor = rgb(BLACK, shA); ctx.shadowBlur = shBlur; ctx.shadowOffsetX = shDx; ctx.shadowOffsetY = shDy;
      ctx.fillStyle = rgb(handFill);
      ctx.fill(body); // canvas shadow offsets ignore the transform, so the light stays upper-left at any angle
      ctx.shadowColor = 'transparent';
      ctx.lineWidth = R * .0032; ctx.strokeStyle = rgb(edge, .22); ctx.stroke(body);
      if (slot) {
        ctx.lineWidth = R * .0042; ctx.strokeStyle = rgb(BLACK, .22 * (1 - .6 * night)); ctx.stroke(slot);
        if (night > .01) { ctx.shadowColor = rgb(lume, .95 * night); ctx.shadowBlur = R * .05; }
        ctx.fillStyle = rgb(channel); ctx.fill(slot);
        ctx.shadowColor = 'transparent';
        ctx.save(); ctx.translate(R * .0016, R * .0022);
        ctx.lineWidth = R * .0022; ctx.strokeStyle = rgb(WHITE, .55 * (1 - night)); ctx.stroke(slot); ctx.restore();
      }
      ctx.restore();
    };
    hand(a.h, geo.hour, geo.hourSlot, R * .016, R * .030, R * .022, shadowA);
    hand(a.m, geo.minute, geo.minuteSlot, R * .016, R * .030, R * .022, shadowA);

    // seconds hand rides highest
    ctx.save();
    ctx.rotate(a.s * DEG);
    ctx.shadowOffsetX = R * .026; ctx.shadowOffsetY = R * .052;
    ctx.shadowColor = rgb(BLACK, shadowA * .9); ctx.shadowBlur = R * .028;
    ctx.fillStyle = rgb(secondFill); ctx.fill(geo.second);
    ctx.beginPath(); ctx.arc(0, .150 * R, .0125 * R, 0, TAU); ctx.fill();
    ctx.shadowColor = 'transparent';
    ctx.lineWidth = R * .003; ctx.strokeStyle = rgb(edge, .18); ctx.stroke(geo.second);
    ctx.restore();

    // layered centre cap
    const capFace = mix(dial.hand, mix(nightFace, WHITE, .0).map(v => v * 1.4), night);
    const disc = (x, y, r, col, al = 1) => { ctx.fillStyle = rgb(col, al); ctx.beginPath(); ctx.arc(x, y, r, 0, TAU); ctx.fill(); };
    disc(R * .006, R * .012, R * .050, BLACK, .18 * (1 - .5 * night));
    disc(0, 0, R * .046, capFace);
    ctx.lineWidth = R * .0035; ctx.strokeStyle = rgb(edge, .28); ctx.beginPath(); ctx.arc(0, 0, R * .046, 0, TAU); ctx.stroke();
    disc(0, 0, R * .034, mix(capFace, dial.ink, .12));
    disc(0, 0, R * .026, capFace);
    disc(-R * .006, -R * .007, R * .012, WHITE, .35 * (1 - night));
    disc(0, 0, R * .011, mix(dial.accent, lume, night));
    ctx.restore();

    if (this.label) this.updateLabel(todSeconds);
  }

  updateLabel(tod) {
    const h = Math.floor(tod / 3600) % 24, m = Math.floor(tod / 60) % 60;
    const text = `Analog clock showing ${h % 12 || 12}:${String(m).padStart(2, '0')} ${h < 12 ? 'AM' : 'PM'}`;
    if (text !== this.lastLabel) { this.canvas.setAttribute('aria-label', text); this.lastLabel = text; }
  }
}

// ---------------------------------------------------------------- drawing helpers

function round(g, x, y, w, h, r) { g.beginPath(); g.roundRect(x, y, w, h, Math.min(r, w / 2, h / 2)); }

const inkCache = new Map();
/** Ink metrics of the digit "0" for a typeface, measured once at a large size. */
function inkOf(g, fx) {
  if (inkCache.has(fx.family)) return inkCache.get(fx.family);
  g.save();
  g.font = `${fx.weight} 200px ${fx.family}`;
  const m = g.measureText('0');
  g.restore();
  const v = { h: (m.actualBoundingBoxAscent + m.actualBoundingBoxDescent) / 200, up: m.actualBoundingBoxAscent / 200 };
  // Only trust (and cache) the measurement once the real face is loaded, not the fallback font.
  if (v.h > 0.2 && document.fonts.check(`${fx.weight} 20px ${fx.family}`)) inkCache.set(fx.family, v);
  return v;
}

/** Digits centred on (x, y) so their *ink* is `hpx` tall, optionally rotated about their centre. */
function numeral(g, text, x, y, hpx, rot, fx, fill) {
  const ink = inkOf(g, fx);
  const size = hpx / (ink.h || 0.7);
  g.save();
  g.translate(x, y); g.rotate(rot);
  g.font = `${fx.weight} ${size}px ${fx.family}, system-ui, sans-serif`;
  g.textAlign = 'center'; g.textBaseline = 'alphabetic';
  g.fillStyle = fill;
  g.fillText(text, 0, (ink.up - ink.h / 2) * size);   // baseline so the ink's centre lands on the origin
  g.restore();
}

/** Fine paper grain as a tiling data-URL (generated, not shipped). */
export function grainDataUrl() {
  const n = 192, c = document.createElement('canvas');
  c.width = c.height = n;
  const g = c.getContext('2d'), img = g.createImageData(n, n);
  let seed = 0x4B4C4F43;
  const rnd = () => ((seed = (seed * 1664525 + 1013904223) >>> 0) / 4294967296);
  const v = new Float32Array(n * n);
  for (let i = 0; i < v.length; i++) v[i] = rnd() - .5;
  for (let y = 0; y < n; y++) for (let x = 0; x < n; x++) {
    const i = y * n + x;
    const s = (v[i] * 2 + v[y * n + (x + 1) % n] + v[y * n + (x + n - 1) % n] + v[((y + 1) % n) * n + x] + v[((y + n - 1) % n) * n + x]) / 6 * 2.2;
    const a = Math.min(255, Math.abs(s) * 2 * 255), col = s > 0 ? 255 : 0;
    img.data.set([col, col, col, a], i * 4);
  }
  g.putImageData(img, 0, 0);
  return c.toDataURL('image/png');
}

