using System;
using System.ComponentModel;

namespace LabelMinusinWPF.Common
{
    #region 撤销重做功能

    // 撤销/重做命令接口
    public interface IUndoCommand
    {
        void Execute();
        void Undo();
    }

    // 撤销重做管理器：维护两个栈实现无限撤销/重做
    public class UndoRedoManager
    {
        private readonly Stack<IUndoCommand> _undoStack = new();
        private readonly Stack<IUndoCommand> _redoStack = new();

        public bool CanUndo => _undoStack.Count > 0;
        public bool CanRedo => _redoStack.Count > 0;
        public int UndoCount => _undoStack.Count;
        public int RedoCount => _redoStack.Count;

        // 执行命令并压入撤销栈（清空重做栈）
        public void Execute(IUndoCommand command)
        {
            command.Execute();
            _undoStack.Push(command);
            _redoStack.Clear();
        }

        public void Undo()
        {
            if (!CanUndo) return;
            var command = _undoStack.Pop();
            command.Undo();
            _redoStack.Push(command);
        }

        public void Redo()
        {
            if (!CanRedo) return;
            var command = _redoStack.Pop();
            command.Execute();
            _undoStack.Push(command);
        }

        public void Clear()
        {
            _undoStack.Clear();
            _redoStack.Clear();
        }
    }

    // 新增标注命令
    public class AddCommand(BindingList<OneLabel> list, OneLabel label) : IUndoCommand
    {
        public void Execute() { label.IsDeleted = false; list.Add(label); }
        public void Undo() { label.IsDeleted = true; list.Remove(label); }
    }

    // 删除标注命令（软删除）
    public class DeleteCommand(OneLabel label) : IUndoCommand
    {
        public void Execute() { label.IsDeleted = true; }
        public void Undo() { label.IsDeleted = false; }
    }

    // 标注属性修改命令（基于快照的撤销/重做）
    public class UpdateLabelCommand : IUndoCommand
    {
        private readonly OneLabel _target;
        private readonly LabelSnapshot _oldState;
        private readonly LabelSnapshot _newState;
        private readonly Action _refreshAction;

        public UpdateLabelCommand(OneLabel target, LabelSnapshot oldState, Action refresh)
        {
            _target = target;
            _oldState = oldState;
            _newState = new LabelSnapshot(target);
            _refreshAction = refresh;
        }

        public void Execute() { _newState.RestoreTo(_target); _refreshAction(); }
        public void Undo() { _oldState.RestoreTo(_target); _refreshAction(); }
    }

    // 标注状态快照（记录 Text、Group、Position）
    public class LabelSnapshot
    {
        public string Text { get; }
        public string Group { get; }
        public System.Windows.Point Position { get; }

        public LabelSnapshot(OneLabel label)
        {
            Text = label.Text;
            Group = label.Group;
            Position = label.Position;
        }

        public void RestoreTo(OneLabel label)
        {
            label.Text = Text;
            label.Group = Group;
            label.Position = Position;
        }
    }

    #endregion
}
