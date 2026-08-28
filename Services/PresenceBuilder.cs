/**
 * geetRPCS - Presence Builder
 * Builds RichPresence payloads (idle/active) from the loaded config, the app
 * database, placeholder expansion, narrative texts and mouse-energy state.
 * Kept UI-free so RPC payload assembly is testable and decoupled from the host.
 */
/*
 * Copyright (c) 2026 geetcr4ck
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     http://www.apache.org/licenses/LICENSE-2.0
 */

using System;
using System.Collections.Generic;
using System.Linq;
using DiscordRPC;
using geetRPCS.Models;
using geetRPCS.Utils;

namespace geetRPCS.Services
{
    internal sealed class PresenceBuilder
    {
        // Language-neutral redaction shown instead of the window title whenever
        // private mode hides it (manual toggle or auto-detected browser window).
        private const string HiddenTitle = "**********";

        public Config Config { get; set; }
        public bool PrivateMode { get; set; }
        private readonly ActivityProviderRegistry _activityProviders;

        public PresenceBuilder(Config config)
            : this(config, ActivityProviderRegistry.CreateDefault())
        {
        }

        internal PresenceBuilder(Config config, ActivityProviderRegistry activityProviders)
        {
            Config = config;
            _activityProviders = activityProviders ?? throw new ArgumentNullException(nameof(activityProviders));
        }

        /// <summary>Builds the idle (no app active) presence from config.json.</summary>
        public RichPresence BuildIdlePresence(string energyState = null)
        {
            string details = string.IsNullOrWhiteSpace(Config.Discord?.Details) ? LanguageManager.Current.Idling : Config.Discord.Details;
            string state = string.IsNullOrWhiteSpace(Config.Discord?.State) ? LanguageManager.Current.Ready : Config.Discord.State;
            if (!string.IsNullOrEmpty(energyState)) state = $"{state} | {energyState}";
            var presence = new RichPresence
            {
                Details = details,
                State = state,
                Assets = GetDefaultAssets()
            };
            var buttons = BuildButtons(Config.Discord?.Buttons?.Select(b => (b.Label, b.Url)) ?? Enumerable.Empty<(string, string)>());
            if (buttons != null && buttons.Length > 0) presence.Buttons = buttons;
            return presence;
        }

        /// <summary>Builds the active presence for a detected application.</summary>
        public RichPresence BuildAppPresence(string processName, IntPtr hWnd, DateTime started, string energyState = null)
        {
            string visibleTitle = GetVisibleWindowTitle(processName, hWnd);
            var activityContext = new ActivityContext
            {
                ProcessName = processName,
                AppName = Placeholders.GetAppName(processName),
                WindowTitle = visibleTitle,
                WindowHandle = hWnd
            };
            // Private mode must cover every provider, including local bridge
            // documents that may contain a project, composition, or layer name.
            var activity = visibleTitle == HiddenTitle
                ? new GenericWindowActivityProvider().GetActivity(activityContext)
                : _activityProviders.Resolve(activityContext);

            bool hasStateOverride = SettingsService.Instance.AppOverrides.TryGetValue(processName, out var stateOverride)
                && !string.IsNullOrWhiteSpace(stateOverride.State);
            bool detailsOnly = activity?.DetailsOnly == true && !hasStateOverride;
            string detailsTemplate = GetProviderAwareDetails(processName, activity);
            string stateTemplate = detailsOnly ? "" : GetProviderAwareState(processName, activity);
            string details = ActivityText.Normalize(ReplacePlaceholders(detailsTemplate, processName, hWnd, visibleTitle)) ?? "";
            string state = ActivityText.Normalize(ReplacePlaceholders(stateTemplate, processName, hWnd, visibleTitle)) ?? "";
            if (!detailsOnly && !string.IsNullOrEmpty(energyState))
                state = ActivityText.Normalize($"{state} | {energyState}") ?? "";

            var presence = new RichPresence
            {
                Details = details,
                State = state,
                Assets = PresenceAssets.ForApp(processName, GetDefaultAssets())
            };

            // Effective entry = apps.json (or a custom app) with the user's
            // override applied: timestamps/buttons respect the override here.
            var appConfig = AppConfigManager.GetEffectiveApp(processName);
            if (appConfig?.ShowTimestamps ?? Config.Discord?.ShowTimestamps ?? true)
                presence.Timestamps = new Timestamps { Start = started };

            var appButtons = BuildButtons(appConfig?.Buttons?.Select(b => (b.Label, b.Url)) ?? Enumerable.Empty<(string, string)>());
            if (appButtons != null && appButtons.Length > 0) presence.Buttons = appButtons;
            return presence;
        }

        private string GetProviderAwareDetails(string processName, ActivitySnapshot activity)
        {
            if (SettingsService.Instance.AppOverrides.TryGetValue(processName, out var ov)
                && !string.IsNullOrWhiteSpace(ov.Details))
                return ov.Details;
            // A deep/app-aware provider beats the bundled witty template. The
            // generic provider is only a fallback, so known apps keep their
            // existing customDetails behavior.
            if (activity?.Provider != "generic-window" && !string.IsNullOrWhiteSpace(activity?.Details))
                return activity.Details;
            var app = AppConfigManager.FindExact(processName);
            if (!string.IsNullOrWhiteSpace(app?.CustomDetails)) return app.CustomDetails;
            if (!string.IsNullOrWhiteSpace(activity?.Details)) return activity.Details;
            return Config.Discord?.ActiveDetails ?? "";
        }

        private string GetProviderAwareState(string processName, ActivitySnapshot activity)
        {
            if (SettingsService.Instance.AppOverrides.TryGetValue(processName, out var ov)
                && !string.IsNullOrWhiteSpace(ov.State))
                return ov.State;
            if (!string.IsNullOrWhiteSpace(activity?.State)) return activity.State;
            return Config.Discord?.ActiveState ?? "";
        }

        /// <summary>Template resolution for the detail line (override &gt; app &gt; active).</summary>
        public string GetCustomDetailsForApp(string processName)
        {
            if (SettingsService.Instance.AppOverrides.TryGetValue(processName, out var ov) && !string.IsNullOrWhiteSpace(ov.Details))
                return ov.Details;
            var app = AppConfigManager.FindExact(processName);
            if (!string.IsNullOrWhiteSpace(app?.CustomDetails)) return app.CustomDetails;
            return Config.Discord?.ActiveDetails ?? "";
        }

        /// <summary>Trampled state line (override &gt; config active state).</summary>
        public string GetCustomStateForApp(string processName)
        {
            if (SettingsService.Instance.AppOverrides.TryGetValue(processName, out var ov) && !string.IsNullOrWhiteSpace(ov.State))
                return ov.State;
            return Config.Discord?.ActiveState ?? "";
        }

        public Assets GetDefaultAssets() => new Assets
        {
            LargeImageKey = Config.Discord?.Assets?.LargeImageKey ?? "",
            LargeImageText = Config.Discord?.Assets?.LargeImageText ?? "",
            SmallImageKey = Config.Discord?.Assets?.SmallImageKey ?? "",
            SmallImageText = Config.Discord?.Assets?.SmallImageText ?? ""
        };

        /// <summary>Validates and caps buttons (Discord allows at most 2, label &lt;= 32 chars, https only).</summary>
        private DiscordRPC.Button[] BuildButtons(IEnumerable<(string Label, string Url)> source)
        {
            var valid = source
                .Where(b => !string.IsNullOrEmpty(b.Label)
                            && !string.IsNullOrEmpty(b.Url)
                            && IsValidUrl(b.Url)
                            && b.Label.Length <= 32)
                .Take(2)
                .Select(b => new DiscordRPC.Button { Label = b.Label, Url = b.Url })
                .ToArray();
            return valid.Length > 0 ? valid : null;
        }

        public static bool IsValidUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return false;
            if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                && !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                return false;
            return Uri.TryCreate(url, UriKind.Absolute, out var uri)
                && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
        }

        public string ReplacePlaceholders(string format, string processName, IntPtr hWnd)
            => ReplacePlaceholders(format, processName, hWnd, GetVisibleWindowTitle(processName, hWnd));

        private string ReplacePlaceholders(string format, string processName, IntPtr hWnd, string visibleTitle)
        {
            if (string.IsNullOrEmpty(format)) return format ?? "";
            try
            {
                bool hasProcessName = format.IndexOf("{process_name}", StringComparison.Ordinal) >= 0;
                bool hasAppName = format.IndexOf("{app_name}", StringComparison.Ordinal) >= 0;
                bool hasWindowTitle = format.IndexOf("{window_title}", StringComparison.Ordinal) >= 0;
                bool hasWittyText = format.IndexOf("{witty_text}", StringComparison.Ordinal) >= 0;

                string appName = hasAppName ? Placeholders.GetAppName(processName) : null;
                string title = null;
                if (hasWindowTitle)
                {
                    title = string.IsNullOrEmpty(visibleTitle) ? LanguageManager.Current.Working : visibleTitle;
                }

                string wittyText = hasWittyText ? NarrativeService.GetForApp(processName) : null;
                return format.Replace("{process_name}", hasProcessName ? processName ?? "" : "")
                    .Replace("{app_name}", hasAppName ? appName ?? processName ?? "" : "")
                    .Replace("{window_title}", hasWindowTitle ? title : "")
                    .Replace("{witty_text}", hasWittyText ? wittyText : "");
            }
            catch (Exception ex)
            {
                LogService.Log($"ReplacePlaceholders error: {ex.Message}", "ERROR", "PresenceBuilder");
                return format;
            }
        }

        private string GetVisibleWindowTitle(string processName, IntPtr hWnd)
        {
            if (PrivateMode) return HiddenTitle;
            string title = Placeholders.GetWindowTitle(hWnd);
            string accessibleWindowName = PrivateBrowsingDetector.IsSupportedBrowser(processName)
                ? Placeholders.GetAccessibleWindowName(hWnd, title)
                : "";
            if (PrivateBrowsingDetector.IsPrivateWindow(processName, title, accessibleWindowName))
                return HiddenTitle;
            return title;
        }
    }
}
