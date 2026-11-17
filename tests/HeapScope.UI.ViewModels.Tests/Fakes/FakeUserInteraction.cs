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

using System.Globalization;
using System.IO;
using HeapScope.Application.Workers;
using HeapScope.Contracts;

namespace HeapScope.UI.ViewModels.Tests;

/// <summary>Scripted dialogs.</summary>
internal sealed class FakeUserInteraction : HeapScope.UI.Services.IUserInteraction
{
    public string? NextFile { get; set; }

    public bool Answer { get; set; } = true;

    public List<string> Questions { get; } = [];

    public Task<string?> PickDumpFileAsync() => Task.FromResult(NextFile);

    public Task<string?> PickTraceFileAsync() => Task.FromResult(NextFile);

    /// <summary>Where a save picker answers (sprint 2.7); null cancels.</summary>
    public string? SaveFile { get; set; }

    public List<string> Messages { get; } = [];

    public Task<string?> PickSaveFileAsync(string title, string suggestedName)
    {
        SuggestedNames.Add(suggestedName);
        return Task.FromResult(SaveFile);
    }

    public List<string> SuggestedNames { get; } = [];

    public Task ShowMessageAsync(string title, string message)
    {
        Messages.Add(message);
        return Task.CompletedTask;
    }

    /// <summary>Answers given in turn before <see cref="Answer"/>; the titles of the questions asked.</summary>
    public Queue<bool> Answers { get; } = new();

    public List<string> Titles { get; } = [];

    public Task<bool> ConfirmAsync(string title, string message)
    {
        Titles.Add(title);
        Questions.Add(message);
        return Task.FromResult(Answers.Count > 0 ? Answers.Dequeue() : Answer);
    }

    /// <summary>Files asked to be opened or shown in their folder (the Live timeline).</summary>
    public List<string> Opened { get; } = [];

    public List<string> Shown { get; } = [];

    public Task<bool> OpenFileAsync(string path)
    {
        Opened.Add(path);
        return Task.FromResult(true);
    }

    public Task<bool> ShowInFolderAsync(string path)
    {
        Shown.Add(path);
        return Task.FromResult(true);
    }
}
