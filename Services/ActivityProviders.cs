/**
 * geetRPCS - Activity providers
 * Resolves a foreground window through a local integration bridge, a small set
 * of app-aware providers, then a universal window-title fallback.
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
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using geetRPCS.Models;
using geetRPCS.Utils;

namespace geetRPCS.Services
{
    internal interface IActivityProvider
    {
        string Name { get; }
        bool CanHandle(ActivityContext context);
        ActivitySnapshot GetActivity(ActivityContext context);
    }

    internal sealed class ActivityProviderRegistry : IDisposable
    {
        private readonly IReadOnlyList<IActivityProvider> _providers;
        private readonly FileSystemWatcher _bridgeWatcher;
        private readonly Timer _bridgeDebounce;
        private string _pendingProcess;
        private bool _disposed;

        public event Action<string> ActivityChanged;

        public string BridgeDirectory { get; }

        internal ActivityProviderRegistry(IEnumerable<IActivityProvider> providers)
        {
            _providers = providers?.ToArray() ?? Array.Empty<IActivityProvider>();
        }

        private ActivityProviderRegistry(string bridgeDirectory, bool watchBridge)
        {
            BridgeDirectory = bridgeDirectory;
            _providers = new IActivityProvider[]
            {
                new LocalActivityBridgeProvider(bridgeDirectory),
                new AfterEffectsActivityProvider(),
                new GenericWindowActivityProvider()
            };

            if (!watchBridge) return;
            try
            {
                Directory.CreateDirectory(bridgeDirectory);
                _bridgeDebounce = new Timer(_ => PublishBridgeChange(), null, Timeout.Infinite, Timeout.Infinite);
                _bridgeWatcher = new FileSystemWatcher(bridgeDirectory, "*.json")
                {
                    IncludeSubdirectories = false,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size
                };
                _bridgeWatcher.Changed += OnBridgeFileChanged;
                _bridgeWatcher.Created += OnBridgeFileChanged;
                _bridgeWatcher.Deleted += OnBridgeFileChanged;
                _bridgeWatcher.Renamed += OnBridgeFileRenamed;
                _bridgeWatcher.EnableRaisingEvents = true;
            }
            catch (Exception ex)
            {
                _bridgeWatcher?.Dispose();
                _bridgeDebounce?.Dispose();
                _bridgeWatcher = null;
                _bridgeDebounce = null;
                LogService.Log($"Activity bridge watcher disabled: {ex.Message}", "WARNING", "ActivityProvider");
            }
        }

        public static ActivityProviderRegistry CreateDefault(bool watchBridge = false, string bridgeDirectory = null)
            => new ActivityProviderRegistry(bridgeDirectory ?? AppPaths.ActivityBridgeDir, watchBridge);

        public ActivitySnapshot Resolve(ActivityContext context)
        {
            if (context == null || string.IsNullOrWhiteSpace(context.ProcessName)) return null;
            foreach (var provider in _providers)
            {
                try
                {
                    if (!provider.CanHandle(context)) continue;
                    var result = provider.GetActivity(context);
                    if (result != null) return result;
                }
                catch (Exception ex)
                {
                    LogService.Log($"Provider '{provider.Name}' failed: {ex.Message}", "WARNING", "ActivityProvider");
                }
            }
            return null;
        }

        private void OnBridgeFileChanged(object sender, FileSystemEventArgs e)
            => QueueBridgeChange(e.FullPath);

        private void OnBridgeFileRenamed(object sender, RenamedEventArgs e)
            => QueueBridgeChange(e.FullPath);

        private void QueueBridgeChange(string fullPath)
        {
            if (_disposed) return;
            string process = Path.GetFileNameWithoutExtension(fullPath);
            if (string.IsNullOrWhiteSpace(process)) return;
            Interlocked.Exchange(ref _pendingProcess, process);
            _bridgeDebounce?.Change(150, Timeout.Infinite);
        }

        private void PublishBridgeChange()
        {
            if (_disposed) return;
            string process = Interlocked.Exchange(ref _pendingProcess, null);
            if (!string.IsNullOrWhiteSpace(process)) ActivityChanged?.Invoke(process);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_bridgeWatcher != null)
            {
                _bridgeWatcher.EnableRaisingEvents = false;
                _bridgeWatcher.Changed -= OnBridgeFileChanged;
                _bridgeWatcher.Created -= OnBridgeFileChanged;
                _bridgeWatcher.Deleted -= OnBridgeFileChanged;
                _bridgeWatcher.Renamed -= OnBridgeFileRenamed;
                _bridgeWatcher.Dispose();
            }
            _bridgeDebounce?.Dispose();
        }
    }

    internal sealed class LocalActivityBridgeProvider : IActivityProvider
    {
        private const long MaxDocumentBytes = 32 * 1024;
        private static readonly TimeSpan MaxAge = TimeSpan.FromSeconds(15);
        private readonly string _directory;

        public string Name => "local-bridge";

        public LocalActivityBridgeProvider(string directory)
        {
            _directory = directory ?? throw new ArgumentNullException(nameof(directory));
        }

        public bool CanHandle(ActivityContext context) => true;

        public ActivitySnapshot GetActivity(ActivityContext context)
        {
            string fileName = SafeProcessFileName(context.ProcessName) + ".json";
            string path = Path.Combine(_directory, fileName);
            if (!File.Exists(path)) return null;

            var info = new FileInfo(path);
            if (info.Length <= 0 || info.Length > MaxDocumentBytes) return null;

            string json;
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var reader = new StreamReader(stream))
                json = reader.ReadToEnd();

            var document = JsonSerializer.Deserialize(json, JsonContext.Default.LocalActivityDocument);
            if (document == null) return null;
            if (!string.IsNullOrWhiteSpace(document.Process)
                && !document.Process.Equals(context.ProcessName, StringComparison.OrdinalIgnoreCase))
                return null;

            DateTime updated = document.UpdatedAtUtc?.ToUniversalTime() ?? info.LastWriteTimeUtc;
            TimeSpan age = DateTime.UtcNow - updated;
            if (age > MaxAge || age < TimeSpan.FromMinutes(-5)) return null;

            string details = ActivityText.Normalize(document.Details);
            string state = ActivityText.Normalize(document.State);
            if (string.IsNullOrEmpty(details) && string.IsNullOrEmpty(state)) return null;
            return new ActivitySnapshot { Details = details, State = state, Provider = Name };
        }

        internal static string SafeProcessFileName(string processName)
        {
            string value = processName ?? "unknown";
            foreach (char invalid in Path.GetInvalidFileNameChars()) value = value.Replace(invalid, '_');
            return value.Trim().ToLowerInvariant();
        }
    }

    internal sealed class AfterEffectsActivityProvider : IActivityProvider
    {
        public string Name => "after-effects";

        public bool CanHandle(ActivityContext context)
            => context.ProcessName.Equals("AfterFX", StringComparison.OrdinalIgnoreCase);

        public ActivitySnapshot GetActivity(ActivityContext context)
        {
            string title = ActivityText.Normalize(context.WindowTitle);
            string project = ExtractProjectName(title);
            return new ActivitySnapshot
            {
                Details = "Editing in Adobe After Effects",
                State = !string.IsNullOrEmpty(project) ? $"Project: {project}" : title,
                Provider = Name
            };
        }

        internal static string ExtractProjectName(string title)
        {
            if (string.IsNullOrWhiteSpace(title)) return null;
            int extension = title.IndexOf(".aep", StringComparison.OrdinalIgnoreCase);
            if (extension < 0) return null;

            int end = extension + 4;
            string candidate = title.Substring(0, end);
            int start = 0;
            string[] separators = { " - ", " — ", " | ", " > " };
            foreach (string separator in separators)
            {
                int index = candidate.LastIndexOf(separator, StringComparison.Ordinal);
                if (index >= 0) start = Math.Max(start, index + separator.Length);
            }
            start = Math.Max(start, candidate.LastIndexOf('\\') + 1);
            start = Math.Max(start, candidate.LastIndexOf('/') + 1);
            string project = candidate.Substring(start).Trim();
            return string.IsNullOrWhiteSpace(project) ? null : project;
        }
    }

    internal sealed class GenericWindowActivityProvider : IActivityProvider
    {
        public string Name => "generic-window";
        public bool CanHandle(ActivityContext context) => true;

        public ActivitySnapshot GetActivity(ActivityContext context)
        {
            string appName = ActivityText.Normalize(context.AppName);
            if (string.IsNullOrEmpty(appName)) appName = context.ProcessName;
            if (string.Equals(context.ProcessName, Branding.LegacyProcessName, StringComparison.OrdinalIgnoreCase))
                appName = Branding.ProductName;
            return new ActivitySnapshot
            {
                Details = ActivityText.Normalize($"Using {appName}"),
                State = ActivityText.Normalize(context.WindowTitle),
                Provider = Name
            };
        }
    }

    internal static class ActivityText
    {
        public const int DiscordTextLimit = 128;

        public static string Normalize(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            string cleaned = Regex.Replace(value, @"[\r\n\t]+", " ").Trim();
            if (cleaned.Length <= DiscordTextLimit) return cleaned;
            int length = DiscordTextLimit;
            if (char.IsHighSurrogate(cleaned[length - 1])) length--;
            return cleaned.Substring(0, length).TrimEnd();
        }
    }
}
