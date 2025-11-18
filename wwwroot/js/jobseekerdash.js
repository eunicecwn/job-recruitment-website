// wwwroot/js/jobseekerdash.js
(function () {
    // ==== CONFIG ====
    const HUB_URL = '/hubs/notifications';     // must match MapHub<NotificationsHub>(...)
    const BADGE_ID = 'notifCount';             // <span id="notifCount">0</span>
    const LIST_ID = 'notifList';               // container for list items
    const EMPTY_ID = 'notifEmpty';             // "no items" hint element (optional)
    const BELL_BTN_ID = 'notifBtn';            // button that opens the dropdown

    // ==== UTILITIES ====
    function onReady(fn) { if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', fn); else fn(); }
    const $ = (sel) => document.querySelector(sel);

    function timeAgo(date) {
        try {
            const d = (date instanceof Date) ? date : new Date(date);
            const diff = Math.floor((Date.now() - d.getTime()) / 1000);
            if (diff < 60) return 'just now';
            const m = Math.floor(diff / 60); if (m < 60) return `${m} min${m > 1 ? 's' : ''} ago`;
            const h = Math.floor(m / 60); if (h < 24) return `${h} hour${h > 1 ? 's' : ''} ago`;
            const d2 = Math.floor(h / 24); return `${d2} day${d2 > 1 ? 's' : ''} ago`;
        } catch { return ''; }
    }

    function statusToBadge(s) {
        switch ((s || '').toLowerCase()) {
            case 'hired':
            case 'offersent': return 'badge bg-success';
            case 'rejected': return 'badge bg-danger';
            case 'interviewscheduled': return 'badge bg-info text-dark';
            case 'shortlisted': return 'badge bg-primary';
            case 'pending':
            default: return 'badge bg-secondary';
        }
    }

    function escapeHtml(s) {
        return (s ?? '').toString()
            .replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;').replace(/'/g, '&#039;');
    }

    // tiny CSS for the blue "unread" ping on the bell
    function ensurePingCss() {
        if (document.getElementById('notifPingStyle')) return;
        const st = document.createElement('style');
        st.id = 'notifPingStyle';
        st.textContent = `
      #${CSS.escape(BELL_BTN_ID)}.notif-has-unread::after{
        content:""; position:absolute; top:6px; right:6px;
        width:10px; height:10px; border-radius:50%;
        background:#3b82f6; box-shadow:0 0 0 2px #fff;
      }`;
        document.head.appendChild(st);
    }

    // ==== RENDER ====
    function makeNotifItem(p) {
        const div = document.createElement('div');
        div.className = 'd-flex align-items-start gap-2 p-2 border-bottom';
        div.innerHTML = `
      <div>
        <div class="small">
          <span class="${statusToBadge(p.status)}">${escapeHtml(p.status ?? '')}</span>
        </div>
        <div class="fw-semibold small mt-1">${escapeHtml(p.title ?? '')}</div>
        <div class="text-muted small">${escapeHtml(p.company ?? '')}</div>
        <div class="text-muted small">${timeAgo(p.whenUtc)}</div>
        ${p.link ? `<a class="small" href="${escapeHtml(p.link)}">View</a>` : ''}
      </div>`;
        return div;
    }

    function incrementBadge() {
        const badge = document.getElementById(BADGE_ID);
        if (!badge) return;
        const n = parseInt(badge.textContent || '0', 10) || 0;
        const next = n + 1;
        badge.textContent = String(next);
        badge.style.display = '';
        document.getElementById(BELL_BTN_ID)?.classList.add('notif-has-unread');
    }

    // PREPEND new item to the **top** (newest first)
    function prependNotif(p) {
        const list = document.getElementById(LIST_ID);
        const empty = document.getElementById(EMPTY_ID);
        if (!list) return;
        const el = makeNotifItem(p);
        // remove placeholder row if present
        if (list.firstElementChild && list.firstElementChild.classList.contains('text-muted')) list.innerHTML = '';
        list.prepend(el);
        if (empty) empty.classList.add('d-none');
    }

    function updateRow(p) {
        if (!p.applicationId) return;
        const row = document.querySelector(`[data-application-id="${p.applicationId}"]`);
        if (!row) return;
        const badge = row.querySelector('.status-badge');
        if (badge) {
            badge.textContent = p.status ?? '';
            badge.className = `status-badge ${statusToBadge(p.status)}`;
        }
    }

    function normalizePayload(p) {
        return {
            status: (p.status ?? p.newStatus ?? p.applicationStatus ?? '').toString(),
            title: p.title ?? p.jobTitle ?? p.position ?? '',
            company: p.company ?? p.employerName ?? '',
            whenUtc: p.whenUtc ?? p.updatedAtUtc ?? p.timestampUtc ?? new Date().toISOString(),
            link: p.link ?? p.url ?? '',
            applicationId: p.applicationId ?? p.id ?? p.applicationID ?? null
        };
    }

    // ==== SIGNALR ====
    function connect() {
        try {
            if (!window.signalR || !window.signalR.HubConnectionBuilder) {
                console.warn('[jobseekerdash] SignalR library missing');
                dbg.setConn('SignalR library missing', false);
                return;
            }

            const conn = new signalR.HubConnectionBuilder()
                .withUrl(HUB_URL)
                .withAutomaticReconnect()
                .configureLogging(signalR.LogLevel.Information)
                .build();

            conn.onclose(err => { console.warn('[jobseekerdash] connection closed', err); dbg.setConn('Closed' + (err ? (': ' + (err.message || String(err))) : ''), false); });
            conn.onreconnecting(err => { console.warn('[jobseekerdash] reconnecting', err); dbg.setConn('Reconnecting…', false); });
            conn.onreconnected(id => { console.info('[jobseekerdash] reconnected', id); dbg.setConn('Reconnected', true); });

            conn.on('applicationStatus', (raw) => {
                try {
                    const payload = normalizePayload(raw);
                    dbg.log({ event: 'applicationStatus', payload });
                    prependNotif(payload);   // NEWEST → TOP
                    incrementBadge();
                    updateRow(payload);
                } catch (e) {
                    console.error('applicationStatus handler error:', e);
                    dbg.log('handler error: ' + (e && e.message ? e.message : String(e)));
                }
            });

            conn.start()
                .then(() => { console.info('[jobseekerdash] connected'); dbg.setConn('Connected', true); })
                .catch(err => { console.error('SignalR connection error:', err); dbg.setConn('Connect error: ' + (err && err.message ? err.message : err), false); });
        } catch (e) {
            console.error('[jobseekerdash] connect() fatal', e);
            dbg.setConn('Fatal: ' + (e && e.message ? e.message : e), false);
        }
    }

    // ==== BOOT ====
    onReady(() => {
        ensurePingCss();

        // Clear ping when bell opened
        const bell = document.getElementById(BELL_BTN_ID);
        bell?.addEventListener('click', () => {
            bell.classList.remove('notif-has-unread');
            const badge = document.getElementById(BADGE_ID);
            if (badge && (badge.dataset.count === '0')) badge.style.display = 'none';
        });

        waitForSignalR().then(connect).catch(err => {
            console.warn('[jobseekerdash] ' + err.message);
            dbg.setConn(err.message, false);
        });
    });
})();
