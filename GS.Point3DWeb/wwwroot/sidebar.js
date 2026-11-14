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

// Collapsible server list (the sidebar on the home page). Loaded in <head>, before the body is drawn, so a list
// this browser collapsed is collapsed from the first paint instead of opening and then closing.
//
// The state is <html data-sidebar="collapsed">, which the page's CSS follows. Blazor never re-renders <html>, so
// nothing on the server needs to know, and the choice is kept per browser (localStorage), as the language is.
(() => {
    const key = 'gs-point3dweb.sidebar';
    const root = document.documentElement;
    const isCollapsed = () => root.dataset.sidebar === 'collapsed';

    try {
        if (localStorage.getItem(key) === 'collapsed') root.dataset.sidebar = 'collapsed';
    } catch {
        // Storage blocked (private window, site data off): the list starts open.
    }

    // The two toggle buttons (Home.razor): "collapse" shows while open, "expand" while collapsed.
    document.addEventListener('click', e => {
        const button = e.target.closest && e.target.closest('[data-sidebar-toggle]');
        if (!button) return;
        const collapse = button.dataset.sidebarToggle === 'collapse';
        if (collapse) root.dataset.sidebar = 'collapsed';
        else delete root.dataset.sidebar;
        try {
            if (isCollapsed()) localStorage.setItem(key, 'collapsed');
            else localStorage.removeItem(key);
        } catch {
            // Not remembered; it still works for this page.
        }
        // The clicked button is now hidden: keep the keyboard focus on the one that replaced it.
        document.querySelector(`[data-sidebar-toggle="${collapse ? 'expand' : 'collapse'}"]`)?.focus();
    });
})();
