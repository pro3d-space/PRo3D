function createCalendar(id, calendarType) {
    $('#' + id).calendar({
        type: calendarType,
            className: { table: 'ui inverted celled center aligned unstackable table' },
        onBeforeChange: function (newDate, text, mode) {
            var prevDate = $(this).calendar('get date');
            if (!prevDate || prevDate != newDate) {
                return true;
            }
            else {
                return false;
            }
        },
        onChange: function(newDate, text, mode) {
            console.log(newDate);
            let calendarId = $('#' + this.id).find('.calendar')[0].id;
            aardvark.processEvent(calendarId, 'ondatechange', newDate.toISOString());
        },
        onSelect: function (date, mode) {
            console.debug("[Debug] on select");
        },
        formatter: {
            cellTime: 'H:mm',
            date: 'YYYY-MM-DD',
            datetime: 'YYYY-MM-DD, H:mm',
            dayHeader: 'MMMM YYYY',
            hourHeader: 'MMMM D, YYYY',
            minuteHeader: 'MMMM D, YYYY',
            month: 'MMMM YYYY',
            monthHeader: 'YYYY',
            time: 'H:mm',
            year: 'YYYY'
        }
    });
}

function setCalendarDate(id, data) {
    let date = new Date(data.time);
    let prevDate = $('#' + id).calendar('get date');
    if (prevDate != date) {
        $('#' + id).calendar('set date', date, true, false);
    } else {
        console.debug("[Debug] preventing calendar update");
    }
}