/*
 * geetRPCS Adobe After Effects activity bridge
 * Writes project/composition/layer state to a local JSON file once per second.
 */
(function discordActivityBridge(thisObject) {
    var globalName = "__geetActivityBridge";
    var bridge = $.global[globalName] || {};
    $.global[globalName] = bridge;

    function quote(value) {
        var text = String(value === undefined || value === null ? "" : value);
        return '"' + text
            .replace(/\\/g, "\\\\")
            .replace(/"/g, '\\"')
            .replace(/\r/g, "\\r")
            .replace(/\n/g, "\\n")
            .replace(/\t/g, "\\t") + '"';
    }

    function pad(value) {
        return value < 10 ? "0" + value : String(value);
    }

    function isoUtc(date) {
        return date.getUTCFullYear() + "-" + pad(date.getUTCMonth() + 1) + "-" +
            pad(date.getUTCDate()) + "T" + pad(date.getUTCHours()) + ":" +
            pad(date.getUTCMinutes()) + ":" + pad(date.getUTCSeconds()) + ".000Z";
    }

    function bridgeFile() {
        var localAppData = $.getenv("LOCALAPPDATA");
        if (!localAppData) return null;
        var directory = new Folder(localAppData + "/geetRPCS/activity");
        if (!directory.exists && !directory.create()) return null;
        return new File(directory.fsName + "/afterfx.json");
    }

    bridge.write = function () {
        try {
            var file = bridgeFile();
            if (!file || !app.project) return;

            var projectName = app.project.file ? app.project.file.name : "Untitled project";
            var details = "Editing " + projectName;
            var parts = [];
            var activeItem = app.project.activeItem;

            if (activeItem && activeItem instanceof CompItem) {
                parts.push("Composition: " + activeItem.name);
                var selected = activeItem.selectedLayers;
                if (selected && selected.length === 1) {
                    parts.push("Layer: " + selected[0].name);
                } else if (selected && selected.length > 1) {
                    parts.push("Selected layers: " + selected.length);
                }
            } else if (activeItem && activeItem.name) {
                parts.push("Item: " + activeItem.name);
            }

            var state = parts.length ? parts.join(" / ") : "Project open";
            var json = "{" +
                "\n  \"process\": " + quote("AfterFX") + "," +
                "\n  \"details\": " + quote(details) + "," +
                "\n  \"state\": " + quote(state) + "," +
                "\n  \"updatedAtUtc\": " + quote(isoUtc(new Date())) +
                "\n}";

            file.encoding = "UTF-8";
            if (file.open("w")) {
                file.write(json);
                file.close();
            }
        } catch (error) {
            // Keep the scheduled task alive; the panel status remains usable.
        }
    };

    bridge.start = function () {
        if (bridge.taskId) return;
        bridge.write();
        bridge.taskId = app.scheduleTask("$.global.__geetActivityBridge.write()", 1000, true);
    };

    bridge.stop = function () {
        if (bridge.taskId) {
            try { app.cancelTask(bridge.taskId); } catch (error) {}
            bridge.taskId = null;
        }
        try {
            var file = bridgeFile();
            if (file && file.exists) file.remove();
        } catch (error) {}
    };

    function buildPanel(owner) {
        var panel = owner instanceof Panel ? owner : new Window("palette", "Discord Activity Bridge");
        panel.orientation = "column";
        panel.alignChildren = ["fill", "top"];
        var status = panel.add("statictext", undefined, "Reports project, composition, and layer locally.");
        var buttons = panel.add("group");
        var startButton = buttons.add("button", undefined, "Start");
        var stopButton = buttons.add("button", undefined, "Stop");
        startButton.onClick = function () { bridge.start(); status.text = "Running"; };
        stopButton.onClick = function () { bridge.stop(); status.text = "Stopped"; };
        panel.layout.layout(true);
        return panel;
    }

    var panel = buildPanel(thisObject);
    bridge.start();
    if (panel instanceof Window) {
        panel.center();
        panel.show();
    }
})(this);
