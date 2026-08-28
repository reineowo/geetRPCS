/**
 * geetRPCS - Activity provider models
 * Local, provider-neutral data used to turn a foreground window into a richer
 * Discord presence without coupling providers to DiscordRPC types.
 */
/*
 * Copyright (c) 2026 geetRPCS contributors
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     http://www.apache.org/licenses/LICENSE-2.0
 */

using System;
using System.Text.Json.Serialization;

namespace geetRPCS.Models
{
    internal sealed class ActivityContext
    {
        public string ProcessName { get; init; }
        public string AppName { get; init; }
        public string WindowTitle { get; init; }
        public IntPtr WindowHandle { get; init; }
    }

    internal sealed class ActivitySnapshot
    {
        public string Details { get; init; }
        public string State { get; init; }
        public string Provider { get; init; }
        public bool DetailsOnly { get; init; }
    }

    /// <summary>
    /// File contract for app-specific integrations. A producer writes
    /// %LOCALAPPDATA%\geetRPCS\activity\&lt;process&gt;.json and refreshes the
    /// timestamp while the reported activity is valid.
    /// </summary>
    internal sealed class LocalActivityDocument
    {
        [JsonPropertyName("process")]
        public string Process { get; set; }

        [JsonPropertyName("details")]
        public string Details { get; set; }

        [JsonPropertyName("state")]
        public string State { get; set; }

        [JsonPropertyName("updatedAtUtc")]
        public DateTime? UpdatedAtUtc { get; set; }
    }
}
