/**
 * player.js — Animation player for lightning frame playback
 * Controls: Play/Pause, Next/Prev frame, Speed, Timeline slider
 */

'use strict';

// ── Player State ──────────────────────────────────────────────────────────────
const player = {
    frames:       [],       // array of FrameMetaDto from API
    currentIndex: 0,
    isPlaying:    false,
    speed:        1,        // multiplier: 0.25, 0.5, 1, 2, 4
    timer:        null,
    unit:         null,
    radius:       200,
    lastFetch:    null,
    loadingFrame: false,
};

// Speed steps (multiplier → interval between frames in ms)
const SPEEDS = [0.25, 0.5, 1, 2, 4, 8];
const BASE_INTERVAL_MS = 800; // base ms per frame at 1×

// ── Init ──────────────────────────────────────────────────────────────────────
document.addEventListener('DOMContentLoaded', () => {
    document.getElementById('btn-play').addEventListener('click', togglePlay);
    document.getElementById('btn-prev').addEventListener('click', prevFrame);
    document.getElementById('btn-next').addEventListener('click', nextFrame);
    document.getElementById('btn-slower').addEventListener('click', decreaseSpeed);
    document.getElementById('btn-faster').addEventListener('click', increaseSpeed);
    document.getElementById('timeline-slider').addEventListener('input', onSliderInput);

    // Listen for unit/radius changes from map.js
    window.addEventListener('unit-changed',   e => loadFrames(e.detail.unit, e.detail.radius));
    window.addEventListener('radius-changed', e => loadFrames(e.detail.unit, e.detail.radius));

    // Auto-refresh new frames every minute
    setInterval(refreshLatestFrame, 60_000);
});

// ── Load Frames ────────────────────────────────────────────────────────────────
async function loadFrames(unit, radius) {
    player.unit   = unit;
    player.radius = radius;
    player.frames = [];
    player.currentIndex = 0;

    updateTimeDisplay('--:--');

    try {
        const url = `/api/lightning/frames?unidadeId=${unit.id}&raio=${radius}`;
        const res = await fetch(url);
        if (!res.ok) throw new Error(`HTTP ${res.status}`);

        player.frames = await res.json();

        if (player.frames.length === 0) {
            updateDataBadge('Sem dados', true);
            window.renderFlashes([]);
            return;
        }

        // Start at the most recent frame
        player.currentIndex = player.frames.length - 1;

        // Update slider
        const slider = document.getElementById('timeline-slider');
        slider.max   = player.frames.length - 1;
        slider.value = player.currentIndex;

        updateDataBadge(`${player.frames.length} frames`, false);

        if (!player.isPlaying) {
            await loadAndRenderComposite();
        } else {
            await loadAndRenderFrame(player.currentIndex);
        }
    } catch (err) {
        console.error('Failed to load frames:', err);
        updateDataBadge('Erro ao carregar', true);
    }
}

// ── Frame Rendering ────────────────────────────────────────────────────────────
async function loadAndRenderFrame(index) {
    if (!player.unit || player.loadingFrame) return;
    if (index < 0 || index >= player.frames.length) return;

    player.loadingFrame = true;
    const meta = player.frames[index];

    try {
        const url = `/api/lightning/frame?unidadeId=${player.unit.id}&raio=${player.radius}&ts=${meta.frameTime}`;
        const res = await fetch(url);
        if (!res.ok) throw new Error(`HTTP ${res.status}`);

        const frame = await res.json();
        window.renderFlashes(frame.points);

        // Update time displays
        const dt = new Date(frame.frameTime);
        updateTimeDisplay(formatLocalTime(dt));
        document.getElementById('timeline-slider').value = index;

        updateDataBadge('Ao vivo', false);
        player.lastFetch = new Date();
    } catch (err) {
        console.error(`Failed to load frame ${index}:`, err);
    } finally {
        player.loadingFrame = false;
    }
}

async function loadAndRenderComposite() {
    if (!player.unit) return;
    try {
        const url = `/api/lightning/composite?unidadeId=${player.unit.id}&raio=${player.radius}`;
        const res = await fetch(url);
        if (!res.ok) throw new Error(`HTTP ${res.status}`);

        const frame = await res.json();
        window.renderFlashes(frame.points);
        updateTimeDisplay('Últimas 3h');
        
        updateDataBadge('Visão Geral 3h', false);
    } catch (err) {
        console.error('Failed to load composite:', err);
    }
}

// ── Playback Controls ─────────────────────────────────────────────────────────
function togglePlay() {
    player.isPlaying ? pause() : play();
}

function play() {
    if (player.frames.length === 0) return;
    player.isPlaying = true;
    updatePlayButton(true);
    scheduleNextTick();
}

function pause() {
    player.isPlaying = false;
    if (player.timer) { clearTimeout(player.timer); player.timer = null; }
    updatePlayButton(false);
}

function scheduleNextTick() {
    if (!player.isPlaying) return;
    const intervalMs = BASE_INTERVAL_MS / player.speed;
    player.timer = setTimeout(async () => {
        if (!player.isPlaying) return;

        let next = player.currentIndex + 1;
        if (next >= player.frames.length) next = 0; // loop

        player.currentIndex = next;
        await loadAndRenderFrame(next);
        scheduleNextTick();
    }, intervalMs);
}

async function prevFrame() {
    pause();
    player.currentIndex = Math.max(0, player.currentIndex - 1);
    await loadAndRenderFrame(player.currentIndex);
}

async function nextFrame() {
    pause();
    player.currentIndex = Math.min(player.frames.length - 1, player.currentIndex + 1);
    await loadAndRenderFrame(player.currentIndex);
}

async function onSliderInput(e) {
    pause();
    player.currentIndex = parseInt(e.target.value);
    await loadAndRenderFrame(player.currentIndex);
}

// ── Speed Control ─────────────────────────────────────────────────────────────
function decreaseSpeed() {
    const i = SPEEDS.indexOf(player.speed);
    if (i > 0) setSpeed(SPEEDS[i - 1]);
}

function increaseSpeed() {
    const i = SPEEDS.indexOf(player.speed);
    if (i < SPEEDS.length - 1) setSpeed(SPEEDS[i + 1]);
}

function setSpeed(s) {
    player.speed = s;
    document.getElementById('speed-label').textContent = `${s}×`;
}

// ── Auto-refresh latest frame ─────────────────────────────────────────────────
async function refreshLatestFrame() {
    if (!player.unit || player.isPlaying) return;

    try {
        const url = `/api/lightning/frames?unidadeId=${player.unit.id}&raio=${player.radius}`;
        const res = await fetch(url);
        if (!res.ok) return;

        const frames = await res.json();
        if (frames.length === 0) return;

        // Check if there are new frames
        const lastKnown = player.frames.length > 0
            ? player.frames[player.frames.length - 1].frameTime
            : null;
        const lastNew   = frames[frames.length - 1].frameTime;

        player.frames = frames;
        document.getElementById('timeline-slider').max   = frames.length - 1;

        // If there's a new frame and we're at the last frame, advance to it
        if (lastNew !== lastKnown && player.currentIndex === player.frames.length - 2) {
            player.currentIndex = frames.length - 1;
            await loadAndRenderFrame(player.currentIndex);
        }
    } catch (err) {
        console.warn('Auto-refresh failed:', err);
    }
}

// ── UI Updates ────────────────────────────────────────────────────────────────
function updatePlayButton(playing) {
    const btn  = document.getElementById('btn-play');
    const icon = document.getElementById('play-icon');
    icon.textContent = playing ? '⏸' : '▶';
    btn.classList.toggle('playing', playing);
}

function updateTimeDisplay(text) {
    document.getElementById('player-time-display').textContent = text;
}

function updateDataBadge(label, isStale) {
    const badge = document.getElementById('update-badge');
    document.getElementById('update-label').textContent = label;
    badge.classList.toggle('stale', isStale);
}

function formatLocalTime(date) {
    return date.toLocaleString('pt-BR', { dateStyle: 'short', timeStyle: 'short', timeZone: 'America/Sao_Paulo' });
}
