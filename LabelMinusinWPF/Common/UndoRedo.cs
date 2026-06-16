using System;
using System.Collections.Generic;
using System.Windows;

namespace LabelMinusinWPF.Common
{
    #region 撤销重做功能

    // 撤销重做管理器：维护线性历史和当前游标。
    internal sealed class UndoRedoManager
    {
        private sealed record HistoryEntry(
            Action Redo,
            Action Undo,
            object? CancellationKey);

        private readonly List<HistoryEntry> _entries = [];
        private int _cursor;
        private int _savedCursor;

        public bool CanUndo => _cursor > 0;
        public bool CanRedo => _cursor < _entries.Count;
        public bool HasUnsavedChanges => _cursor != _savedCursor;

        public void Execute(Action redo, Action undo, object? cancellationKey = null)
        {
            ClearRedoBranch();
            redo();
            _entries.Add(new HistoryEntry(redo, undo, cancellationKey));
            _cursor++;
        }

        public bool Undo()
        {
            if (!CanUndo) return false;

            var entry = _entries[_cursor - 1];
            entry.Undo();
            _cursor--;
            return true;
        }

        public bool Redo()
        {
            if (!CanRedo) return false;

            var entry = _entries[_cursor];
            entry.Redo();
            _cursor++;
            return true;
        }

        public bool TryCancelLatest(object cancellationKey)
        {
            if (!CanUndo || _cursor <= _savedCursor)
                return false;

            var entry = _entries[_cursor - 1];
            if (!ReferenceEquals(entry.CancellationKey, cancellationKey))
                return false;

            ClearRedoBranch();
            entry.Undo();
            _entries.RemoveAt(_cursor - 1);
            _cursor--;
            return true;
        }

        public void MarkAsSaved() => _savedCursor = _cursor;

        private void ClearRedoBranch()
        {
            if (_cursor >= _entries.Count)
                return;

            if (_savedCursor > _cursor)
                _savedCursor = -1;

            _entries.RemoveRange(_cursor, _entries.Count - _cursor);
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
