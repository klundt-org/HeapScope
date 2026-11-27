// HeapScope
// Copyright (C) 2026 Ray Klundt
// https://github.com/klundt-org/HeapScope
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU Affero General Public License as published
// by the Free Software Foundation, version 3 only, with additional terms
// under Section 7 of that license.
//
// Additional terms, including attribution requirements, apply.
// See the ADDITIONAL-TERMS.md file distributed with this program.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
// GNU Affero General Public License for more details.
//
// You should have received a copy of the GNU Affero General Public License
// along with this program (see the LICENSE file). If not, see
// <https://www.gnu.org/licenses/>.
//
// Commercial licenses are available. See COMMERCIAL.md.

namespace HeapScope.Core;

/// <summary>
/// What the logs may hold, by the user's settings: <c>settings.json</c> read once and again only when it
/// changes, checked at most once a second, so a change on the Settings page reaches a running worker and the next line of
/// every process without a restart. <see cref="Current"/> is the process's; tests make their own.
/// </summary>
public sealed class LogPrivacy
{
    private static LogPrivacy s_current = new(HeapScopeFolders.Default);

    private readonly Lock _gate = new();
    private readonly HeapScopeFolders _folders;
    private readonly TimeSpan _checkEvery;
    private DateTime _written;
    private long _checked;
    private bool _includeFilterText = new HeapScopeSettings().IncludeFilterTextInLogs;
    private bool _includeDumpValues;
    private bool _readOnce;

    /// <summary>Reads the settings of <paramref name="folders"/>, checking the file at most every <paramref name="checkEvery"/> (1 s by default).</summary>
    public LogPrivacy(HeapScopeFolders folders, TimeSpan? checkEvery = null)
    {
        _folders = folders ?? throw new ArgumentNullException(nameof(folders));
        _checkEvery = checkEvery ?? TimeSpan.FromSeconds(1);
    }

    /// <summary>The process's: HeapScope's data folder, or the one a process was given (<see cref="Use"/>).</summary>
    public static LogPrivacy Current => Volatile.Read(ref s_current);

    /// <summary>Reads the settings of <paramref name="folders"/> from now on (a worker given <c>--data-folder</c>).</summary>
    public static void Use(HeapScopeFolders folders) => Volatile.Write(ref s_current, new LogPrivacy(folders));

    /// <summary>
    /// Raised with the new values when a setting read changes (each process logs the privacy it applies).
    /// Raised under the instance's lock; a handler must not read <see cref="IncludeFilterText"/> or <see cref="IncludeDumpValues"/>.
    /// </summary>
    public event Action<LogPrivacyChange>? Changed;

    /// <summary>The settings file this instance follows, for a process's start line.</summary>
    public string SettingsFile => HeapScopeSettings.PathIn(_folders);

    /// <summary>Whether filter and search text may be written into a log line now.</summary>
    public bool IncludeFilterText
    {
        get
        {
            lock (_gate)
            {
                Refresh();
                return _includeFilterText;
            }
        }
    }

    /// <summary>
    /// Whether values read from a dump (field values, string text) may be written into a log line now: off by
    /// default, and off until the settings file has been read once.
    /// </summary>
    public bool IncludeDumpValues
    {
        get
        {
            lock (_gate)
            {
                Refresh();
                return _includeDumpValues;
            }
        }
    }

    /// <summary>Both settings in one read, so a log line never mixes two versions of the file.</summary>
    public LogPrivacyChange Read()
    {
        lock (_gate)
        {
            Refresh();
            return new LogPrivacyChange(_includeFilterText, _includeDumpValues);
        }
    }

    /// <summary>Both settings as one log text, for a process's start line: "filter text on, dump values off".</summary>
    public string Describe()
    {
        lock (_gate)
        {
            Refresh();
            return LogPrivacyChange.Describe(_includeFilterText, _includeDumpValues);
        }
    }

    private void Refresh()
    {
        var now = Environment.TickCount64;
        if (_checked != 0 && now - _checked < _checkEvery.TotalMilliseconds)
        {
            return;
        }

        _checked = now;
        var path = HeapScopeSettings.PathIn(_folders);
        DateTime written;
        try
        {
            written = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // As an unreadable file below: never the default "on" before the file was read once.
            SetUnreadable();

            return;
        }

        if (written == _written)
        {
            return;
        }

        if (!HeapScopeSettings.TryRead(_folders, out var settings, wait: false))
        {
            // There and unreadable now: never fall back to the default "on" (see SetUnreadable); the write
            // time is not recorded, so the next check reads again.
            SetUnreadable();
            return;
        }

        _written = written;
        _readOnce = true;
        Set(settings.IncludeFilterTextInLogs, settings.IncludeDumpValuesInLogs);
    }

    // A file that cannot be read now: filter text keeps what was read last, or is off until the file was read once; dump values are off whatever was read before, since they are on only by the user's confirmed choice
    // The write time is forgotten, so the next check that can read the file reads it again, even unchanged.
    private void SetUnreadable()
    {
        _written = default;
        Set(_readOnce && _includeFilterText, includeDumpValues: false);
    }

    private void Set(bool includeFilterText, bool includeDumpValues)
    {
        if (includeFilterText != _includeFilterText || includeDumpValues != _includeDumpValues)
        {
            _includeFilterText = includeFilterText;
            _includeDumpValues = includeDumpValues;
            Changed?.Invoke(new LogPrivacyChange(includeFilterText, includeDumpValues));
        }
    }
}
