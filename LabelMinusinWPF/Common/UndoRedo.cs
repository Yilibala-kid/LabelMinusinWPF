using System;
using System.Collections.Generic;
using System.Windows;

namespace LabelMinusinWPF.Common
{
    #region 撤销重做功能

    // 撤销重做管理器：维护两个栈实现无限撤销/重做
    internal sealed class UndoRedoManager
    {
        private sealed record HistoryEntry(
            Action Redo,
            Action Undo,
            long BeforeStateId,
            long AfterStateId,
            object? CancellationKey)
        {
            public bool WasSaved { get; set; }
        }

        private readonly Stack<HistoryEntry> _undoStack = new();
        private readonly Stack<HistoryEntry> _redoStack = new();
        private long _nextStateId;
        private long _currentStateId;
        private long _savedStateId;

        public bool CanUndo => _undoStack.Count > 0;
        public bool CanRedo => _redoStack.Count > 0;
        public bool HasUnsavedChanges => _currentStateId != _savedStateId;

        public void Execute(Action redo, Action undo, object? cancellationKey = null)
        {
            redo();
            var entry = new HistoryEntry(redo, undo, _currentStateId, ++_nextStateId, cancellationKey);
            _undoStack.Push(entry);
            _redoStack.Clear();
            _currentStateId = entry.AfterStateId;
        }

        public bool Undo()
        {
            if (!_undoStack.TryPeek(out var entry)) return false;
            entry.Undo();
            _undoStack.Pop();
            _redoStack.Push(entry);
            _currentStateId = entry.BeforeStateId;
            return true;
        }

        public bool Redo()
        {
            if (!_redoStack.TryPeek(out var entry)) return false;
            entry.Redo();
            _redoStack.Pop();
            _undoStack.Push(entry);
            _currentStateId = entry.AfterStateId;
            return true;
        }

        public bool TryCancelLatest(object cancellationKey)
        {
            if (!_undoStack.TryPeek(out var entry)
                || entry.WasSaved
                || !ReferenceEquals(entry.CancellationKey, cancellationKey))
                return false;

            entry.Undo();
            _undoStack.Pop();
            _redoStack.Clear();
            _currentStateId = entry.BeforeStateId;
            return true;
        }

        public void MarkAsSaved()
        {
            _savedStateId = _currentStateId;
            foreach (var entry in _undoStack)
                entry.WasSaved = true;
        }
    }

    // 标注状态快照（记录 Text、Group、Position）
    public sealed record LabelSnapshot(string Text, string Group, Point Position)
    {
        public LabelSnapshot(OneLabel label)
            : this(label.Text, label.Group, label.Position)
        {
        }

        public bool Matches(OneLabel label) =>
            Text == label.Text && Group == label.Group && Position == label.Position;

        public void RestoreTo(OneLabel label)
        {
            label.Text = Text;
            label.Group = Group;
            label.Position = Position;
        }
    }

    #endregion
}
