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
    const confirmation = document.getElementById("habits-confirm");
    const status = document.getElementById("habits-status");
    const returnTarget = document.getElementById(document.getElementById("habits-return")?.dataset.target);
    const target = confirmation || returnTarget || (status?.textContent.trim() ? status : document.getElementById(location.hash.slice(1)));
    if(target) requestAnimationFrame(() => target.focus());
}
Blazor.addEventListener("enhancedload", restoreHabits);
window.addEventListener("pageshow", restoreHabits);
