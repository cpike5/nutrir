(function () {
    function generateQR() {
        var dataEl = document.getElementById("qrCodeData");
        if (!dataEl) return;

        var qrEl = document.getElementById("qrCode");
        if (!qrEl || qrEl.childNodes.length > 0) return;

        var uri = dataEl.getAttribute("data-url");
        if (!uri) return;

        new QRCode(qrEl, {
            text: uri,
            width: 200,
            height: 200,
            colorDark: "#000000",
            colorLight: "#ffffff",
            correctLevel: QRCode.CorrectLevel.M
        });
    }

    // Run on initial full page load
    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", generateQR);
    } else {
        generateQR();
    }

    // Run after Blazor enhanced navigation patches the DOM
    document.addEventListener("blazor:enhancedload", generateQR);
})();
