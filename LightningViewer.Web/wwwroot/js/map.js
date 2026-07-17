/**
 * map.js — Leaflet map initialization and flash rendering
 * Handles unit selection, radius circles, and flash point display.
 */

'use strict';

// ── State ────────────────────────────────────────────────────────────────────
const state = {
    map:          null,
    unitMarker:   null,
    circles:      [],       // [30km, 50km, 100km, 200km] circle layers
    flashLayer:   null,     // current flash point layer group
    allUnitsLayer:null,     // layer group for all units in Visão Geral
    selectedUnit: null,
    selectedRadius: 200,    // km — default to outermost
    unidades:     [],
};

// Radius config: colour and opacity per ring
const RADII = [
    { km: 30,  color: '#ffe066', fill: 0.05, stroke: 0.7, weight: 1.5 },
    { km: 50,  color: '#ffa040', fill: 0.04, stroke: 0.6, weight: 1.5 },
    { km: 100, color: '#ff6030', fill: 0.03, stroke: 0.55, weight: 1.5 },
    { km: 200, color: '#cc2040', fill: 0.02, stroke: 0.5, weight: 1.5 },
];

// ── Initialise ────────────────────────────────────────────────────────────────
document.addEventListener('DOMContentLoaded', () => {
    state.unidades = (window.APP_CONFIG?.unidades ?? []).map(u => ({
        id: u.Id || u.id,
        nome: u.Nome || u.nome || '',
        municipio: u.Municipio || u.municipio || '',
        latitude: u.Latitude || u.latitude,
        longitude: u.Longitude || u.longitude
    }));
    
    // Add virtual unit for South America
    state.unidades.unshift({
        id: 0,
        nome: 'América do Sul (Visão Geral)',
        municipio: 'Continente',
        latitude: -15.0,
        longitude: -55.0,
        numero: 0
    });
    
    initMap();
    initUnitSelector();
    startClock();

    const geoModal = document.getElementById('geo-modal');
    const btnGeoYes = document.getElementById('btn-geo-yes');
    const btnGeoNo = document.getElementById('btn-geo-no');

    const saUnit = state.unidades.find(u => u.id === 0);

    if (geoModal && btnGeoYes && btnGeoNo) {
        geoModal.classList.remove('hidden');

        btnGeoYes.addEventListener('click', () => {
            geoModal.classList.add('hidden');
            if (navigator.geolocation) {
                navigator.geolocation.getCurrentPosition(
                    pos => selectNearestUnit(pos.coords.latitude, pos.coords.longitude),
                    _err => selectUnit(saUnit)
                );
            } else {
                selectUnit(saUnit);
            }
        });

        btnGeoNo.addEventListener('click', () => {
            geoModal.classList.add('hidden');
            selectUnit(saUnit);
        });
    } else {
        selectUnit(saUnit);
    }
});

// ── Map Setup ─────────────────────────────────────────────────────────────────
function initMap() {
    state.map = L.map('map', {
        zoomControl: false,
        center: [-15.0, -60.0],
        zoom: 4,
        attributionControl: true,
        preferCanvas: true
    });

    // CartoDB Dark Matter (free, no API key required)
    L.tileLayer('https://{s}.basemaps.cartocdn.com/dark_all/{z}/{x}/{y}{r}.png', {
        attribution: '© <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> contributors &copy; <a href="https://carto.com/attributions">CARTO</a> | MTG-LI © EUMETSAT',
        maxZoom: 19,
        subdomains: 'abcd',
    }).addTo(state.map);

    L.control.zoom({ position: 'bottomright' }).addTo(state.map);
}

// ── Unit Selector ─────────────────────────────────────────────────────────────
function initUnitSelector() {
    const searchInput = document.getElementById('unit-search');
    const dropdown    = document.getElementById('unit-dropdown');

    searchInput.addEventListener('input', () => {
        const q = searchInput.value.trim().toLowerCase();
        renderDropdown(q ? state.unidades.filter(u =>
            u.nome.toLowerCase().includes(q) || u.municipio.toLowerCase().includes(q)
        ) : state.unidades);
        dropdown.classList.remove('hidden');
    });

    searchInput.addEventListener('focus', () => {
        renderDropdown(state.unidades);
        dropdown.classList.remove('hidden');
    });

    document.addEventListener('click', e => {
        if (!e.target.closest('.select-wrapper'))
            dropdown.classList.add('hidden');
    });
}

function renderDropdown(units) {
    const dropdown = document.getElementById('unit-dropdown');
    dropdown.innerHTML = units.map(u => `
        <div class="dropdown-item ${state.selectedUnit?.id === u.id ? 'active' : ''}"
             data-id="${u.id}">
            <div class="dropdown-item-name">${u.nome}</div>
            <div class="dropdown-item-loc">${u.municipio}</div>
        </div>
    `).join('');

    dropdown.querySelectorAll('.dropdown-item').forEach(el => {
        el.addEventListener('click', () => {
            const unit = state.unidades.find(u => u.id === parseInt(el.dataset.id));
            if (unit) {
                selectUnit(unit);
                document.getElementById('unit-dropdown').classList.add('hidden');
                document.getElementById('unit-search').value = unit.nome;
            }
        });
    });
}

// ── Select Unit ───────────────────────────────────────────────────────────────
function selectUnit(unit) {
    state.selectedUnit = unit;
    updateUnitInfo(unit);
    updateMapForUnit(unit);
    // Notify player to reload frames
    window.dispatchEvent(new CustomEvent('unit-changed', { detail: { unit, radius: state.selectedRadius } }));
}

function updateUnitInfo(unit) {
    document.getElementById('unit-info-name').textContent     = unit.nome;
    document.getElementById('unit-info-location').textContent = unit.municipio;
    
    if (unit.id === 0) {
        document.getElementById('unit-info-coords').textContent = "";
    } else {
        document.getElementById('unit-info-coords').textContent =
            `${unit.latitude.toFixed(5)}°, ${unit.longitude.toFixed(5)}°`;
    }
    
    document.getElementById('unit-info').classList.remove('hidden');
    document.getElementById('unit-search').value = unit.nome;
}

function updateMapForUnit(unit) {
    const latlng = [unit.latitude, unit.longitude];

    if (state.unitMarker) { state.map.removeLayer(state.unitMarker); state.unitMarker = null; }
    if (state.allUnitsLayer) { state.map.removeLayer(state.allUnitsLayer); state.allUnitsLayer = null; }

    if (unit.id === 0) {
        // Zoom out to see whole South America
        state.map.setView(latlng, 4, { animate: true, duration: 0.6 });
        
        // Remove existing radius circles
        state.circles.forEach(c => state.map.removeLayer(c));
        state.circles = [];
        
        // Draw all units
        const layers = [];
        state.unidades.forEach(u => {
            if (u.id === 0) return;
            
            // X marker
            const xIcon = L.divIcon({
                className: 'unit-x-marker',
                html: '<div class="x-icon">✖</div>',
                iconSize: [24, 24],
                iconAnchor: [12, 12]
            });
            const marker = L.marker([u.latitude, u.longitude], { icon: xIcon })
                .bindTooltip(u.nome, { direction: 'top' })
                .on('click', () => selectUnit(u));
            layers.push(marker);

            // Draw circles for this unit
            RADII.forEach(r => {
                layers.push(L.circle([u.latitude, u.longitude], {
                    radius:       r.km * 1000,
                    color:        r.color,
                    weight:       1.5,
                    opacity:      r.stroke,
                    fill:         false,
                    dashArray:    '4 6',
                    interactive:  false
                }));
            });
        });
        state.allUnitsLayer = L.layerGroup(layers).addTo(state.map);
    } else {
        // Pan / zoom map to the specific unit
        state.map.setView(latlng, 8, { animate: true, duration: 0.6 });

        // Unit marker (glowing pin)
        state.unitMarker = L.circleMarker(latlng, {
            radius: 8, fillColor: '#ffe066', color: '#fff',
            weight: 2, opacity: 1, fillOpacity: 0.9,
        }).addTo(state.map)
          .bindPopup(`<strong>${unit.nome}</strong><br/>${unit.municipio}`);

        // Draw the 4 radius circles
        drawRadiusCircles(unit);
    }
}

function drawRadiusCircles(unit) {
    // Remove existing circles
    state.circles.forEach(c => state.map.removeLayer(c));
    state.circles = [];

    RADII.forEach(r => {
        const circle = L.circle([unit.latitude, unit.longitude], {
            radius:       r.km * 1000,
            color:        r.color,
            weight:       r.weight,
            opacity:      r.stroke,
            fillColor:    r.color,
            fillOpacity:  r.fill,
            dashArray:    '4 6',
        }).addTo(state.map)
          .bindTooltip(unit.nome, { permanent: false, direction: 'top' });

        state.circles.push(circle);
    });
}



/**
 * Renders a set of flash points on the map.
 * Points are coloured by intensity (time based).
 */
window.renderFlashes = function renderFlashes(points) {
    if (state.flashLayer) {
        state.map.removeLayer(state.flashLayer);
        state.flashLayer = null;
    }

    if (!points || points.length === 0) return;

    const markers = points.map(p => {
        const color = intensityColor(p.intensity); // 0 (blue) to 1 (red)

        return L.circleMarker([p.lat, p.lon], {
            radius:      3, // slightly smaller
            fillColor:   color, // filled with jet color
            color:       color,
            weight:      1, // thin border
            opacity:     0.8,
            fillOpacity: 0.8,
            className:   'flash-marker',
        });
    });

    state.flashLayer = L.layerGroup(markers).addTo(state.map);
};

/** Maps an intensity value (0–1) to an RGB colour (blue → cyan → yellow → orange → red). */
function intensityColor(t) {
    // Blue (low) → Cyan → Yellow → Orange → Red (high)
    const stops = [
        [0.0,  [0, 0, 255]],      // Blue
        [0.25, [0, 255, 255]],    // Cyan
        [0.5,  [0, 255, 0]],      // Green
        [0.75, [255, 255, 0]],    // Yellow
        [1.0,  [255, 0, 0]],      // Red
    ];
    for (let i = 0; i < stops.length - 1; i++) {
        const [t0, c0] = stops[i];
        const [t1, c1] = stops[i + 1];
        if (t >= t0 && t <= t1) {
            const f = (t - t0) / (t1 - t0);
            const r = Math.round(c0[0] + f * (c1[0] - c0[0]));
            const g = Math.round(c0[1] + f * (c1[1] - c0[1]));
            const b = Math.round(c0[2] + f * (c1[2] - c0[2]));
            return `rgb(${r},${g},${b})`;
        }
    }
    return '#ff4060';
}

// ── Helper Functions ──────────────────────────────────────────────────────────
function selectNearestUnit(lat, lon) {
    fetch(`/api/geo/nearest?lat=${lat}&lon=${lon}`)
        .then(r => r.json())
        .then(unit => selectUnit(unit))
        .catch(() => selectFirstUnit());
}

function selectFirstUnit() {
    if (state.unidades.length > 0) selectUnit(state.unidades[0]);
}

function startClock() {
    function tick() {
        const now = new Date();
        document.getElementById('clock-local').textContent =
            now.toLocaleTimeString('pt-BR', { hour: '2-digit', minute: '2-digit', second: '2-digit', timeZone: 'America/Sao_Paulo' });
    }
    tick();
    setInterval(tick, 1000);
}
