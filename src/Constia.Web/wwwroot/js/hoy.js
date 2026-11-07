document.addEventListener("submit", event => {
    const form = event.target;
    if (!form.matches("form[data-completion]"))
        return;
    if (form.dataset.pending === "true") {
        event.preventDefault();
        event.stopImmediatePropagation();
        return;
    }
    const button = event.submitter;
    if (!button)
        return;
    const selection = document.createElement("input");
    selection.type = "hidden";
    selection.name = button.name;
    selection.value = button.value;
    selection.dataset.completionSelection = "";
    form.append(selection);
    form.dataset.pending = "true";
    form.setAttribute("aria-busy", "true");
    form.querySelectorAll('button[type="submit"]').forEach(control => control.disabled = true);
    document.getElementById("completion-status").textContent = "Registrando o comprobando el cumplimiento…";
}, true);

Blazor.addEventListener("enhancedload", () => {
    const status = document.getElementById("completion-status");
    if (status?.textContent.trim())
        requestAnimationFrame(() => status.focus());
});

window.addEventListener("pageshow", () => {
    const form = document.querySelector("form[data-completion]");
    if (!form)
        return;
    delete form.dataset.pending;
    form.removeAttribute("aria-busy");
    form.querySelector("[data-completion-selection]")?.remove();
    form.querySelectorAll('button[type="submit"]').forEach(control => control.disabled = false);
    const status = document.getElementById("completion-status");
    if (status?.textContent.trim())
        requestAnimationFrame(() => status.focus());
});
