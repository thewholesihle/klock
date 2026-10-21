import { DIALS, LUMES, FONTS, DialClock, palette, mix, rgb, grainDataUrl } from './clock.js';

const $ = (s, el = document) => el.querySelector(s);
const root = document.documentElement;
const reduced = matchMedia('(prefers-reduced-motion: reduce)').matches;

// ---------------------------------------------------------------- state
// The hero clock is fixed (a calm beige dial in Satoshi). The "try it" clock is the interactive one.
const dial = id => DIALS.find(d => d.id === id);
const lume = LUMES.find(l => l.id === 'bgw9');
const heroState = { dial: dial('beige'), lume, font: 'satoshi', movement: 'quartz', night: 0, reduced };
const state = { dial: dial('glacier'), lume, font: 'satoshi', movement: 'quartz', theme: 'day', night: 0, reduced };
let progress = 0;      // linear day/night progress, eased into state.night (the app's ThemeFader)
let dirty = true;

root.style.setProperty('--grain', `url(${grainDataUrl()})`);

// ---------------------------------------------------------------- the interactive screen takes on the dial's colours
function applyTheme() {
  const pal = palette(state.dial, state.lume.hex), n = state.night;
  root.style.setProperty('--bg1', rgb(mix(pal.day.bg1, pal.night.bg1, n)));
  root.style.setProperty('--bg2', rgb(mix(pal.day.bg2, pal.night.bg2, n)));
  root.style.setProperty('--night', n.toFixed(3));
  dirty = false;
}

// ---------------------------------------------------------------- controls
function radios(container, items, isChecked, onPick, build) {
  container.replaceChildren();
  for (const it of items) {
    const b = document.createElement('button');
    b.type = 'button'; b.setAttribute('role', 'radio');
    build(b, it);
    b.addEventListener('click', () => { onPick(it); sync(); });
    container.append(b);
  }
  container.addEventListener('keydown', e => {
    if (!['ArrowRight', 'ArrowLeft', 'ArrowDown', 'ArrowUp'].includes(e.key)) return;
    const list = [...container.querySelectorAll('[role=radio]')];
    const i = list.indexOf(document.activeElement); if (i < 0) return;
    e.preventDefault();
    const next = list[(i + (e.key === 'ArrowRight' || e.key === 'ArrowDown' ? 1 : list.length - 1)) % list.length];
    next.focus(); next.click();
  });
  return () => container.querySelectorAll('[role=radio]').forEach((b, i) => {
    const on = isChecked(items[i]);
    b.setAttribute('aria-checked', on); b.tabIndex = on ? 0 : -1;
  });
}

const syncers = [
  radios($('#dial-swatches'), DIALS, d => d === state.dial, d => { state.dial = d; dirty = true; }, (b, d) => {
    b.className = 'swatch'; b.title = d.name; b.setAttribute('aria-label', d.name);
    b.style.setProperty('--s1', rgb(d.c1)); b.style.setProperty('--s2', rgb(d.c2));
  }),
  radios($('#theme-seg'), [['day', 'Day'], ['night', 'Night']], t => t[0] === state.theme,
    t => { state.theme = t[0]; }, (b, t) => { b.className = 'seg'; b.textContent = t[1]; }),
  radios($('#move-seg'), [['quartz', 'Quartz'], ['mechanical', 'Mechanical'], ['smooth', 'Smooth']], m => m[0] === state.movement,
    m => { state.movement = m[0]; }, (b, m) => { b.className = 'seg'; b.textContent = m[1]; }),
  radios($('#font-seg'), Object.entries(FONTS), f => f[0] === state.font,
    f => { state.font = f[0]; }, (b, f) => { b.className = `seg font-${f[0]}`; b.textContent = f[1].name; }),
];
const sync = () => syncers.forEach(s => s());

// "See it at night" jumps to the demo and switches it to Night.
document.querySelectorAll('[data-night]').forEach(a => a.addEventListener('click', () => { state.theme = 'night'; sync(); }));

// ---------------------------------------------------------------- page colour follows the section in view (Radial's signature)
const colours = { orange: '#F9E2BD', blue: '#C5D9F8', green: '#D3E6C7', purple: '#DDC8E2' };
const zones = [...document.querySelectorAll('[data-bg]')];
let tick = false;
function pickPageColour() {
  tick = false;
  const y = innerHeight * 0.5;
  const hit = zones.find(z => { const r = z.getBoundingClientRect(); return r.top <= y && r.bottom >= y; });
  if (hit) {
    const c = colours[hit.dataset.bg];
    if (c) { root.style.setProperty('--page', c); const m = $('meta[name="theme-color"]'); if (m) m.content = c; }
  }
}
addEventListener('scroll', () => { if (!tick) { tick = true; requestAnimationFrame(pickPageColour); } }, { passive: true });
addEventListener('resize', pickPageColour);

// ---------------------------------------------------------------- clocks and the frame loop
const clocks = [new DialClock($('#clock-hero'), heroState), new DialClock($('#clock-main'), state)];

Promise.all(Object.values(FONTS).map(f => document.fonts.load(f.css, '0123456789'))).then(() => {
  clocks.forEach(c => { c.key = ''; });
});

let last = performance.now();
function frame(now) {
  const dt = Math.min(0.25, (now - last) / 1000); last = now;
  const target = state.theme === 'night' ? 1 : 0;
  if (progress !== target) {
    progress = reduced ? target : target > progress ? Math.min(target, progress + dt / 1.5) : Math.max(target, progress - dt / 1.5);
    state.night = progress * progress * (3 - 2 * progress);
    dirty = true;
  }
  if (dirty) applyTheme();

  const d = new Date();
  const tod = d.getHours() * 3600 + d.getMinutes() * 60 + d.getSeconds() + d.getMilliseconds() / 1000;
  for (const c of clocks) c.draw(tod);
  requestAnimationFrame(frame);
}

sync();
applyTheme();
pickPageColour();
requestAnimationFrame(frame);
document.addEventListener('visibilitychange', () => { last = performance.now(); });
