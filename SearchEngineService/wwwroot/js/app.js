const qEl = document.getElementById('q');
const typeEl = document.getElementById('type');
const sortEl = document.getElementById('sort');
const sizeEl = document.getElementById('size');
const searchBtn = document.getElementById('searchBtn');
const resultsEl = document.getElementById('results');
const metaEl = document.getElementById('meta');
const statusEl = document.getElementById('status');
const prevBtn = document.getElementById('prev');
const nextBtn = document.getElementById('next');

let page = 1;

function fmtDate(iso) { try { return new Date(iso).toLocaleString(); } catch { return iso; } }
function escapeHtml(s) { return String(s).replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c])); }

function setLoading(on) {
    statusEl.textContent = on ? 'Yükleniyor…' : '';
    searchBtn.disabled = on; prevBtn && (prevBtn.disabled = on); nextBtn && (nextBtn.disabled = on);
}

async function runSearch() {
    const query = (qEl.value || '').trim();
    if (query.length < 1) { statusEl.textContent = 'Lütfen bir sorgu yazın.'; return; }
    setLoading(true); resultsEl.innerHTML = '';

    const params = new URLSearchParams({
        query, type: typeEl.value, sort: sortEl.value, page: page, size: sizeEl.value
    });

    try {
        const res = await fetch(`/api/Search?${params}`);

        // --- 429: Rate limit aşıldı ---
        if (res.status === 429) {
            const retry = res.headers.get('Retry-After') ?? '60';
            let extra = '';
            // Sunucu ProblemDetails gönderdiyse detayını kullan
            try {
                const pr = await res.json();
                if (pr?.detail) extra = ` (${pr.detail})`;
            } catch { /* gövde yoksa sorun değil */ }

            statusEl.textContent = '';
            resultsEl.innerHTML =
                `<div class="card" style="border-color:#ef4444;background:#1f2937">
           <strong>İstek limiti aşıldı.</strong> ${retry} saniye sonra tekrar deneyin.${extra}
         </div>`;
            return;
        }

        // --- Diğer hata durumları (400/500 vs) ---
        if (!res.ok) {
            let msg = `Hata: ${res.status}`;
            try {
                const p = await res.json();
                if (p?.title || p?.detail) msg = `${p.title ?? ''} ${p.detail ?? ''}`.trim();
            } catch {
                // text gövde varsa onu yaz
                try { msg = await res.text(); } catch { }
            }
            statusEl.textContent = '';
            resultsEl.innerHTML =
                `<div class="card" style="border-color:#ef4444;background:#1f2937">${escapeHtml(msg)}</div>`;
            return;
        }

        // --- Başarılı yanıt ---
        const data = await res.json();

        const totalPages = Math.max(1, Math.ceil(data.meta.total / data.meta.size));
        metaEl.textContent = `Sayfa ${data.meta.page} / ${totalPages} — Toplam ${data.meta.total} sonuç`;

        if (!data.results || data.results.length === 0) {
            resultsEl.innerHTML = `<div class="muted">Sonuç bulunamadı.</div>`;
            return;
        }

        for (const it of data.results) {
            const card = document.createElement('div'); card.className = 'card';
            card.innerHTML = `
        <div class="header">${escapeHtml(it.title)}</div>
        <div class="row" style="gap:8px;margin:6px 0 8px 0;">
          <span class="badge">${it.type}</span>
          <span class="muted">sağlayıcı:</span><span>${escapeHtml(it.provider)}</span>
          <span class="muted">yayın:</span><span>${fmtDate(it.published_at)}</span>
        </div>
        <div class="row" style="justify-content:space-between;margin:6px 0;">
          <div>puan: <span class="score">${it.score}</span></div>
          <a href="${it.url}" target="_blank" rel="noopener">kaynağa git ↗</a>
        </div>
        <div class="divider"></div>
        <details>
          <summary>puan breakdown</summary>
          <div class="muted">
            base: ${it.score_breakdown.baseScore},
            typeWeight: ${it.score_breakdown.typeWeight},
            recency: ${it.score_breakdown.recency},
            engagement: ${it.score_breakdown.engagement}
          </div>
        </details>`;
            resultsEl.appendChild(card);
        }
    } catch (e) {
        resultsEl.innerHTML =
            `<div class="card" style="border-color:#ef4444;background:#1f2937">
         Hata: <pre style="white-space:pre-wrap">${escapeHtml(String(e.message))}</pre>
       </div>`;
    } finally {
        setLoading(false);
    }
}


searchBtn.addEventListener('click', e => { e.preventDefault(); page = 1; runSearch(); });
document.getElementById('next').addEventListener('click', e => { e.preventDefault(); page++; runSearch(); });
document.getElementById('prev').addEventListener('click', e => { e.preventDefault(); page = Math.max(1, page - 1); runSearch(); });
qEl.addEventListener('keydown', e => { if (e.key === 'Enter') { page = 1; runSearch(); } });
