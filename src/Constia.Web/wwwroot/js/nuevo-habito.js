document.addEventListener("submit", event => {
    const form = event.target;
    if (!form.matches("form[data-create]")) return;
    if (form.dataset.pending === "true") { event.preventDefault(); event.stopImmediatePropagation(); return; }
    const button = event.submitter || form.querySelector('button.primary[type="submit"]');
    if (!button) return;
    const input = document.createElement("input");
    input.type = "hidden"; input.name = "Accion"; input.value = button.value; input.dataset.createAction = ""; form.append(input);
    form.dataset.pending = "true"; form.setAttribute("aria-busy", "true");
    form.querySelectorAll('button[type="submit"]').forEach(b => b.disabled = true);
    const status = document.getElementById("create-status");
    status.textContent = button.value === "crear" ? "Creando tu hábito…" : "Preparando el siguiente paso…";
}, true);
document.addEventListener("focusin", event => {
    const option = event.target.closest(".create-step :is(.category, .icon-choice, .day-choice)");
    if (option) option.scrollIntoView({ block: "nearest", inline: "nearest" });
});

function restoreCreate() {
    const form = document.querySelector("form[data-create]");
    if (form) {
        delete form.dataset.pending; form.removeAttribute("aria-busy");
        form.querySelector("[data-create-action]")?.remove();
        form.querySelectorAll('button[type="submit"]').forEach(b => b.disabled = false);
    }
    const target = (document.getElementById("create-error")?.textContent.trim() ? document.getElementById("create-error") : null) || document.getElementById("create-title");
    if (target) requestAnimationFrame(() => target.focus());
}
Blazor.addEventListener("enhancedload", restoreCreate);
window.addEventListener("pageshow", restoreCreate);
