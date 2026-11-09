function focusCalendar() {
    const title = document.getElementById("calendar-title");
    if (title) requestAnimationFrame(() => title.focus());
}
Blazor.addEventListener("enhancedload", focusCalendar);
window.addEventListener("pageshow", focusCalendar);
