(() => {
    function reconcileProgressDates() {
        const streaks = document.getElementById("progress-streaks");
        const period = document.getElementById("progress-period");
        const streakDate = streaks?.dataset.progressCut;
        const periodDate = period?.dataset.progressCut;
        const mismatch = Boolean(streakDate && periodDate && streakDate !== periodDate);
        const note = streaks?.querySelector("[data-progress-cut-note]");
        if (note) note.hidden = !mismatch;
        const refreshStreaks = streaks?.querySelector("[data-progress-stale-action]");
        const refreshPeriod = period?.querySelector("[data-progress-stale-action]");
        if (refreshStreaks) refreshStreaks.hidden = !(mismatch && streakDate < periodDate);
        if (refreshPeriod) refreshPeriod.hidden = !(mismatch && periodDate < streakDate);
    }

    // Blazor completa ambas secciones por separado durante la carga inicial.
    const progressDatesObserver = new MutationObserver(reconcileProgressDates);
    function observeProgressDates() {
        progressDatesObserver.disconnect();
        const page = document.querySelector(".progress-page");
        if (!page) return;
        progressDatesObserver.observe(page, {
            childList: true, subtree: true, attributes: true, attributeFilter: ["data-progress-cut"]
        });
        reconcileProgressDates();
    }
    observeProgressDates();
    Blazor.addEventListener("enhancedload", observeProgressDates);
    document.addEventListener("click", async event => {
        const button = event.target.closest("[data-progress-retry]");
        if (!button || button.getAttribute("aria-disabled") === "true") return;

        const section = button.closest("section");
        const content = section.querySelector("[aria-busy]");
        const status = section.querySelector('[data-progress-message]');
        const spinner = section.querySelector('.progress-spinner');
        const previousStatus = status.textContent.trim();
        button.setAttribute("aria-disabled", "true");
        content.setAttribute("aria-busy", "true");
        spinner.hidden = false;
        status.classList.remove('visually-hidden');
        status.textContent = button.dataset.loading;
        try {
            const response = await fetch(button.dataset.progressRetry, { redirect: "manual", cache: "no-store" });
            if (response.type === "opaqueredirect" || response.status === 401) {
                location.assign("/login");
                return;
            }
            if (!response.ok) throw new Error("No se pudo cargar la sección.");
            const html = new DOMParser().parseFromString(await response.text(), "text/html");
            const updated = html.getElementById(section.id);
            if (!updated) throw new Error("No se recibió la sección.");

            const restoreFocus = document.activeElement === button;
            section.replaceWith(updated);
            if (updated.dataset.progressDate) {
                document.getElementById("progress-date").textContent = "Hasta hoy, " + updated.dataset.progressDate;
            }
            reconcileProgressDates();
            if (restoreFocus) {
                (updated.querySelector("[data-progress-retry]:not([hidden])") ??
                    updated.querySelector("h2")).focus();
            }
            document.getElementById("progress-announcement").textContent =
                updated.querySelector("[role=status]").textContent.trim();
        } catch {
            content.setAttribute("aria-busy", "false");
            spinner.hidden = true;
            status.classList.add('visually-hidden');
            status.textContent = previousStatus;
            button.removeAttribute("aria-disabled");
        }
    });
})();
