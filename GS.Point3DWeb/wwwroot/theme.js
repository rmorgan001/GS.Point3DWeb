/* Copyright(C) 2026  Rob Morgan (robert.morgan.e@gmail.com)

    This program is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published
    by the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with this program.  If not, see <https://www.gnu.org/licenses/>.
 */

// The colour theme: dark (the default look, no data-theme), day, dusk, night, or custom (the user's own seven colours, see
// app.css and Components/Shared/ThemeEditor.razor). The browser keeps the choice and the custom colours. Loaded in the
// <head>, so the theme is on before anything is drawn and there is no flash of the wrong one.
(function () {
    const root = document.documentElement;
    const CUSTOM = ['bg', 'text', 'accent', 'warn', 'error', 'fail', 'info'];

    function stored(key) {
        try { return localStorage.getItem(key); } catch { return null; }
    }

    function getCustom() {
        try {
            const value = JSON.parse(stored('gsCustomTheme'));
            return value && CUSTOM.every(k => typeof value[k] === 'string') ? value : null;
        } catch { return null; }
    }

    // A light background wants the browser's own controls (scrollbars, inputs) light too.
    function isLight(hex) {
        const n = parseInt((hex || '#000000').slice(1), 16);
        const lin = v => { v /= 255; return v <= 0.03928 ? v / 12.92 : Math.pow((v + 0.055) / 1.055, 2.4); };
        return 0.2126 * lin((n >> 16) & 255) + 0.7152 * lin((n >> 8) & 255) + 0.0722 * lin(n & 255) > 0.4;
    }

    function applyCustom() {
        const colours = getCustom();
        if (!colours) return;
        for (const k of CUSTOM) root.style.setProperty('--c-' + k, colours[k]);
        root.style.colorScheme = isLight(colours.bg) ? 'light' : 'dark';
    }

    function clearCustom() {
        for (const k of CUSTOM) root.style.removeProperty('--c-' + k);
        root.style.removeProperty('color-scheme');
    }

    // The browser's own window colour (the title bar of an app-style window, a phone's address bar) follows the page's background.
    function syncWindowColour() {
        try {
            const colour = getComputedStyle(root).backgroundColor;
            let meta = document.querySelector('meta[name="theme-color"]');
            if (!meta) {
                meta = document.createElement('meta');
                meta.name = 'theme-color';
                document.head.appendChild(meta);
            }
            meta.content = colour;
        } catch { /* nothing to change */ }
    }

    let previewTimer = 0;

    // Tells what is drawn outside the page's CSS (the 3D view's canvas) to take the colours again.
    function changed() {
        window.dispatchEvent(new Event('gsthemechange'));
    }

    function get() {
        return stored('gsTheme') || (window.matchMedia && matchMedia('(prefers-color-scheme: light)').matches ? 'day' : 'dark');
    }

    function set(name) {
        // Custom with no colours to show (the site data was cleared) would leave every colour undefined: use the default look.
        if (name === 'custom' && !getCustom()) name = 'dark';
        // A name that isn't a theme, or Custom with no usable palette (cleared or hand-edited storage), would leave no colours at all.
        if (!['dark', 'day', 'dusk', 'night', 'custom'].includes(name) || (name === 'custom' && !getCustom())) name = 'dark';
        try { localStorage.setItem('gsTheme', name); } catch { /* not remembered */ }
        clearCustom();
        if (name === 'dark') delete root.dataset.theme;
        else root.dataset.theme = name;
        if (name === 'custom') applyCustom();
        syncWindowColour();
        changed();
    }

    window.gsTheme = {
        get,
        set,
        getCustom,
        // A colour being dragged in the editor's picker: shown now, in the page's CSS only. Nothing is kept or sent to the server
        // until the picker settles (its change event). The 3D view repaints a moment after the dragging pauses, not on every step.
        preview: (key, value) => {
            if (root.dataset.theme !== 'custom' || !CUSTOM.includes(key) || !/^#[0-9a-fA-F]{6}$/.test(value)) return;
            root.style.setProperty('--c-' + key, value);
            if (key === 'bg') root.style.colorScheme = isLight(value) ? 'light' : 'dark';
            syncWindowColour();
            clearTimeout(previewTimer);
            previewTimer = setTimeout(changed, 150);
        },
        // Keeps the colours and, if the custom theme is on, shows them now.
        setCustom: colours => {
            try { localStorage.setItem('gsCustomTheme', JSON.stringify(colours)); } catch { /* not remembered */ }
            if (root.dataset.theme === 'custom') applyCustom();
            syncWindowColour();
            changed();
        }
    };

    set(get());
})();

// Copy text to the clipboard (Export); falls back to a hidden textarea where the clipboard API isn't available (a page that
// isn't served over https or localhost).
window.gsCopy = async text => {
    try {
        await navigator.clipboard.writeText(text);
        return true;
    } catch {
        const box = document.createElement('textarea');
        box.value = text;
        box.style.position = 'fixed';
        box.style.opacity = '0';
        document.body.appendChild(box);
        box.select();
        let ok = false;
        try { ok = document.execCommand('copy'); } catch { /* nothing more to do */ }
        document.body.removeChild(box);
        return ok;
    }
};
