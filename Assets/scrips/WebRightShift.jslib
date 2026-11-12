mergeInto(LibraryManager.library, {
    $GolemRightShift: {
        pressed: false,
        onKeyDown: function (event) {
            var isRightShift = event.code === "ShiftRight" ||
                (event.key === "Shift" && event.location === 2);
            if (isRightShift && !event.repeat) GolemRightShift.pressed = true;
        },
        onBlur: function () {
            GolemRightShift.pressed = false;
        }
    },

    GolemListenForRightShift__deps: ["$GolemRightShift"],
    GolemListenForRightShift: function () {
        window.addEventListener("keydown", GolemRightShift.onKeyDown, { capture: true });
        window.addEventListener("blur", GolemRightShift.onBlur);
    },

    GolemStopListeningForRightShift__deps: ["$GolemRightShift"],
    GolemStopListeningForRightShift: function () {
        window.removeEventListener("keydown", GolemRightShift.onKeyDown, { capture: true });
        window.removeEventListener("blur", GolemRightShift.onBlur);
        GolemRightShift.pressed = false;
    },

    GolemConsumeRightShiftPress__deps: ["$GolemRightShift"],
    GolemConsumeRightShiftPress: function () {
        var pressed = GolemRightShift.pressed;
        GolemRightShift.pressed = false;
        return pressed ? 1 : 0;
    }
});
