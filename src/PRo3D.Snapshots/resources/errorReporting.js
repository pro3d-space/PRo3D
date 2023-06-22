function sendCrashDump() {
    $.get("./minilog.txt", function (data, status) {
        var email = 'pro3d-support@vrvis.at';
        var subject = 'PRo3D log';
        var emailBody =
            'Please send this as mail to pro3d-support@vrvis.at%0D%0APlease describe the issue here...%0D%0A%0D%0A%0D%0AThis log file should help us finding problems:%0D%0A' +
            (status == "success" ? data : 'log file could not be generated');
        var ref = "mailto:" + email + "?subject=" + subject + "&body=" + emailBody;
        var button = $(".invisibleCrashButton")[0];
        button.href = ref;
        button.click();
        button.href = "";
    });
}

function attachResize(id) {
    new ResizeSensor(jQuery('.mainrendercontrol'), function () {
        var elem = $('.mainrendercontrol');

        aardvark.processEvent(id, "resizeControl", elem.width().toFixed(), elem.height().toFixed());
    });
}