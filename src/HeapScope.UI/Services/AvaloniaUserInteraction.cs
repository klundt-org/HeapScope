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


using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using HeapScope.UI.Views;
using Microsoft.Extensions.Logging;

namespace HeapScope.UI.Services;

/// <summary>
/// The system's file picker and HeapScope's own question window (Avalonia has no message box). Both are
/// asynchronous: the UI thread keeps running while they are open.
/// </summary>
public sealed class AvaloniaUserInteraction(Func<Window?> owner, ILogger<AvaloniaUserInteraction>? logger = null) : IUserInteraction
{
    private static readonly FilePickerFileType[] s_dumpTypes =
    [
        new("Memory dumps") { Patterns = ["*.dmp", "*.gcdump", "core*"] },
        new("All files") { Patterns = ["*"] },
    ];

    private static readonly FilePickerFileType[] s_traceTypes =
    [
        new("Allocation traces") { Patterns = ["*.nettrace"] },
        new("All files") { Patterns = ["*"] },
    ];

    private static readonly FilePickerFileType[] s_csvTypes =
    [
        new("CSV files") { Patterns = ["*.csv"] },
    ];

    public async Task<string?> PickDumpFileAsync()
    {
        if (owner() is not { } window)
        {
            return null;
        }

        var files = await window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open a memory dump",
            AllowMultiple = false,
            FileTypeFilter = s_dumpTypes,
        });
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    public async Task<string?> PickTraceFileAsync()
    {
        if (owner() is not { } window)
        {
            return null;
        }

        var files = await window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open an allocation trace",
            AllowMultiple = false,
            FileTypeFilter = s_traceTypes,
        });
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    public async Task<string?> PickFolderAsync(string title)
    {
        if (owner() is not { } window)
        {
            return null;
        }

        var folders = await window.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
        });
        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }

    public Task<bool> ConfirmAsync(string title, string message) =>
        MessageWindow.ShowAsync(owner(), title, message, MessageButtons.YesNo);

    public Task<bool> ConfirmAsync(string title, string message, string confirm, string cancel) =>
        MessageWindow.ShowAsync(owner(), title, message, MessageButtons.YesNo, (confirm, cancel));

    public async Task<string?> PickSaveFileAsync(string title, string suggestedName)
    {
        if (owner() is not { } window)
        {
            return null;
        }

        var file = await window.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggestedName,
            DefaultExtension = "csv",
            ShowOverwritePrompt = true,
            FileTypeChoices = s_csvTypes,
        });
        return file?.TryGetLocalPath();
    }

    public Task ShowMessageAsync(string title, string message) =>
        MessageWindow.ShowAsync(owner(), title, message, MessageButtons.Ok);

    public async Task<bool> OpenFileAsync(string path)
    {
        if (owner() is not { } window || !File.Exists(path))
        {
            return false;
        }

        return await window.Launcher.LaunchFileInfoAsync(new FileInfo(path));
    }

    public async Task<bool> ShowInFolderAsync(string path)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            // Explorer and Finder select the file; elsewhere the folder opens. Explorer's exit code means nothing, so it
            // is not awaited. The path is one argument (Windows paths hold no quotes). Both programs by their full paths: a bare name
            // is looked for in the current folder first, where a file of that name may lie beside a dump.
            // Without a Windows folder the path would be the bare name.
            if (OperatingSystem.IsWindows() && Environment.GetFolderPath(Environment.SpecialFolder.Windows) is { Length: > 0 } windows)
            {
                using var _ = Process.Start(new ProcessStartInfo(Path.Combine(windows, "explorer.exe"), "/select,\"" + path + "\"") { UseShellExecute = false });
                return true;
            }

            if (OperatingSystem.IsMacOS())
            {
                using var _ = Process.Start(new ProcessStartInfo("/usr/bin/open") { ArgumentList = { "-R", path }, UseShellExecute = false });
                return true;
            }
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            logger?.LogWarning("The folder of a file could not be shown ({Error}): {Message}", ex.NativeErrorCode, ex.Message);
            return false;
        }

        return owner() is { } window && Path.GetDirectoryName(path) is { } folder
            && await window.Launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(folder));
    }

    public async Task CopyTextAsync(string text)
    {
        if (owner()?.Clipboard is { } clipboard)
        {
            await Avalonia.Input.Platform.ClipboardExtensions.SetTextAsync(clipboard, text);
        }
    }
}
