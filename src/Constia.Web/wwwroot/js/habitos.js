document.addEventListener("submit", event => {
    const form = event.target;
    if (!form.matches("form[data-habits]")) return;
    if (form.dataset.pending === "true") { event.preventDefault(); event.stopImmediatePropagation(); return; }
    const button = event.submitter;
    if (!button) return;
    const input = document.createElement("input");
    input.type = "hidden"; input.name = button.name; input.value = button.value;
    input.dataset.habitsSelection = ""; form.append(input);
    form.dataset.pending = "true"; form.setAttribute("aria-busy", "true");
    form.querySelectorAll('button[type="submit"]').forEach(b => b.disabled = true);
    document.getElementById("habits-status").classList.remove("visually-hidden");
    document.getElementById("habits-status").textContent = "Consultando el estado de tus hábitos…";
}, true);
function restoreHabits() {
    const form = document.querySelector("form[data-habits]");
    if (!form) return;
    delete form.dataset.pending; form.removeAttribute("aria-busy");
    form.querySelector("[data-habits-selection]")?.remove();
    form.querySelectorAll('button[type="submit"]').forEach(b => b.disabled = false);
    const confirmation = form.querySelector("dialog.confirmation");
    if (confirmation) {
        if (!confirmation.matches(":modal")) {
            confirmation.close();
            confirmation.showModal();
        }
        confirmation.querySelector("[data-cancel-confirmation]").focus();
        return;
    }
    const status = document.getElementById("habits-status");
    const returnTarget = document.getElementById(document.getElementById("habits-return")?.dataset.target);
    const details = returnTarget?.closest("details");
    if (details) details.open = true;
    const switchTarget = document.getElementById("habits-return")?.hasAttribute("data-focus-card") ? null : details?.querySelector(".state-switch");
    const target = switchTarget || returnTarget || (status?.textContent.trim() ? status : document.getElementById(location.hash.slice(1)));
    if(target) requestAnimationFrame(() => target.focus());
}
document.addEventListener("cancel", event => {
    const dialog = event.target;
    if (!dialog.matches("form[data-habits] dialog.confirmation")) return;
    event.preventDefault();
    const form = dialog.closest("form");
    if (form.dataset.pending !== "true")
        form.requestSubmit(dialog.querySelector("[data-cancel-confirmation]"));
}, true);
function restoreHistoryLink() {
    document.querySelector("[data-history-return]")?.focus();
}
Blazor.addEventListener("enhancedload", restoreHistoryLink);
window.addEventListener("pageshow", restoreHistoryLink);

document.addEventListener("keydown", event => {
    const day = event.target.closest("[data-history-day]");
    const step = { ArrowLeft: -1, ArrowRight: 1, ArrowUp: -7, ArrowDown: 7 }[event.key];
    if (!day || typeof step !== "number") return;
    event.preventDefault();
    const next = day.closest("[data-history-grid]")
        .querySelector(`[data-history-day="${Number(day.dataset.historyDay) + step}"]`);
    if (!next) return;
    day.tabIndex = -1;
    next.tabIndex = 0;
    next.focus();
});

document.addEventListener("click", event => {
    const day = event.target.closest("[data-history-day]");
    if (!day) return;
    const grid = day.closest("[data-history-grid]");
    grid.querySelector('[aria-pressed="true"]').setAttribute("aria-pressed", "false");
    grid.querySelector('[tabindex="0"]')?.setAttribute("tabindex", "-1");
    day.setAttribute("aria-pressed", "true");
    day.tabIndex = 0;
    day.focus();
    document.getElementById("history-date").textContent = day.dataset.fecha;
    document.getElementById("history-opportunity").textContent = day.dataset.estado;
    document.getElementById("history-record").textContent = day.dataset.registro;
});

Blazor.addEventListener("enhancedload", restoreHabits);
window.addEventListener("pageshow", restoreHabits);

document.addEventListener("submit", event => {
    const form = event.target;
    if (!form.matches("form[data-edit-habit]")) return;
    if (form.dataset.pending) {
        event.preventDefault();
        return;
    }
    const action = event.submitter?.value;
    const saving = action === "confirmar" || action === "guardar";
    form.dataset.pending = saving ? "saving" : "request";
    const dialog = form.querySelector("dialog:modal");
    const busy = dialog?.querySelector(".edit-actions") ?? form;
    const status = document.getElementById(dialog ? "edit-dialog-status" : "edit-status");
    busy.setAttribute("aria-busy", "true");
    status.textContent = saving ? "Guardando cambios…" : "Consultando el hábito…";
    if (saving) (dialog ?? form).querySelector(".edit-saving")?.scrollIntoView({ block: "nearest" });
});
document.addEventListener("change", event => {
    const input = event.target;
    if (!input.matches('.edit-icon-picker input[name="Datos.IconoId"]:checked')) return;
    input.closest("details").querySelector("summary").textContent = input.value
        ? `${input.labels[0].textContent.trim()} · Cambiar`
        : `${input.labels[0].textContent.trim()} · Elegir`;
});
window.addEventListener("pageshow", event => {
    if (!event.persisted) return;
    const form = document.querySelector("form[data-edit-habit]");
    if (!form) return;
    delete form.dataset.pending;
    form.removeAttribute("aria-busy");
    form.querySelector(".edit-dialog .edit-actions")?.removeAttribute("aria-busy");
    document.getElementById("edit-status").textContent = "";
    const dialogStatus = document.getElementById("edit-dialog-status");
    if (dialogStatus) dialogStatus.textContent = "";
});

window.addEventListener("pageshow", () => {
    const dialog = document.querySelector("form[data-edit-habit] .edit-dialog");
    if (!dialog || dialog.matches(":modal")) return;
    dialog.close();
    dialog.showModal();
    dialog.addEventListener("cancel", event => {
        event.preventDefault();
        dialog.closest("form").requestSubmit(dialog.querySelector('button[value="volver"]'));
    });
});
