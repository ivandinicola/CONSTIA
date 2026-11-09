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
Blazor.addEventListener("enhancedload", restoreHabits);
window.addEventListener("pageshow", restoreHabits);

document.addEventListener("submit", event => {
    const form = event.target;
    if (!form.matches("form[data-edit-habit]")) return;
    if (form.dataset.pending) {
        event.preventDefault();
        return;
    }
    form.dataset.pending = "true";
    const status = document.getElementById("edit-status");
    status.textContent = event.submitter?.value === "confirmar" || event.submitter?.value === "guardar"
        ? "Procesando los cambios…" : "Consultando el hábito…";
    form.setAttribute("aria-busy", "true");
});
window.addEventListener("pageshow", event => {
    if (!event.persisted) return;
    const form = document.querySelector("form[data-edit-habit]");
    if (!form) return;
    delete form.dataset.pending;
    form.removeAttribute("aria-busy");
    document.getElementById("edit-status").textContent = "";
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
