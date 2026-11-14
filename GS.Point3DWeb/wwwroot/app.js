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

// Page behaviour Blazor can't do from C#. A file rather than inline script, so it still works under a
// Content-Security-Policy without 'unsafe-inline'. Events are delegated from the document, so they also
// cover elements Blazor renders later.

// Firefox only starts a drag when data is set; Blazor can't set dataTransfer from C#.
document.addEventListener('dragstart', e => {
    const el = e.target.closest && e.target.closest('[data-server-id]');
    if (!el) return;
    e.dataTransfer.setData('text/plain', el.dataset.serverId);
    e.dataTransfer.effectAllowed = 'copy';
});

// Language menu (a <details> drop-down in the header).
const closeLanguageMenus = except => {
    document.querySelectorAll('details.lang-menu[open]').forEach(menu => {
        if (menu !== except) menu.open = false;
    });
};

document.addEventListener('click', e => {
    const menu = e.target.closest && e.target.closest('details.lang-menu');
    // A click anywhere else closes it, as a <select> would.
    closeLanguageMenus(menu);

    // The language already showing: just close the menu. Following the link would reload the page and
    // restart every 3D view. Modified clicks (new tab / window) are left to the browser.
    const current = e.target.closest && e.target.closest('details.lang-menu a.current');
    if (current && !e.ctrlKey && !e.metaKey && !e.shiftKey && !e.altKey && e.button === 0) {
        e.preventDefault();
        current.closest('details').open = false;
    }
});

// Links marked data-popup (Log, Help) open in their own sized window; if pop-ups are blocked they open as an
// ordinary tab. Modified clicks (new tab / window) are left to the browser.
document.addEventListener('click', e => {
    const link = e.target.closest && e.target.closest('a[data-popup]');
    if (!link || e.defaultPrevented || e.ctrlKey || e.metaKey || e.shiftKey || e.altKey || e.button !== 0) return;
    if (window.open(link.href, link.target || '_blank', 'popup,' + link.dataset.popup)) e.preventDefault();
});

document.addEventListener('keydown', e => {
    if (e.key !== 'Escape') return;
    const open = document.querySelector('details.lang-menu[open]');
    if (!open) return;
    open.open = false;
    open.querySelector('summary')?.focus();
});
