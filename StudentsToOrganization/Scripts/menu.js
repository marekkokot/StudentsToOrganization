$(function () {
    $("ul#nav li a").each(function (i, e) {
        if (e.toString() == location.href) {
            $(e).parent().addClass("active");//= "active";
        }
    });
});