document.addEventListener("DOMContentLoaded", function () {
    var dataEl = document.getElementById("qrCodeData");
    if (!dataEl) return;

    var uri = dataEl.getAttribute("data-url");
    if (!uri) return;

    new QRCode(document.getElementById("qrCode"), {
        text: uri,
        width: 200,
        height: 200,
        colorDark: "#000000",
        colorLight: "#ffffff",
        correctLevel: QRCode.CorrectLevel.M
    });
});
