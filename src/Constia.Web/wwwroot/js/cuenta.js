window.constiaCuenta = (() => {
    // Retirar el fragmento antes de iniciar Blazor. El enlace se consume una sola vez en memoria.
    let enlace = null;
    const esConfirmacion = () => location.pathname.endsWith("/confirmar-correo");
    if (esConfirmacion() && location.hash) {
        const datos = new URLSearchParams(location.hash.slice(1));
        history.replaceState(history.state, "", location.pathname + location.search);

        const ids = datos.getAll("desafioId");
        const tokens = datos.getAll("token");
        if (ids.length === 1 && tokens.length === 1 &&
            /^[\da-f]{8}(?:-[\da-f]{4}){3}-[\da-f]{12}$/i.test(ids[0]) &&
            /^[\w-]{43}$/.test(tokens[0])) {
            enlace = [ids[0], tokens[0]];
        }
    }

    // Al volver a abrir un enlace en la misma pestaña, el navegador puede cambiar solo el fragmento.
    addEventListener("hashchange", () => {
        if (esConfirmacion() && /(?:^#|&)(?:desafioId|token)=/.test(location.hash))
            location.reload();
    });

    return {
        tomarEnlace() {
            const valor = enlace;
            enlace = null;
            return valor;
        },
        zonas() {
            if (typeof Intl.supportedValuesOf !== "function")
                return [];

            return [...new Set([...Intl.supportedValuesOf("timeZone"), "Etc/UTC"])].sort();
        }
    };
})();
